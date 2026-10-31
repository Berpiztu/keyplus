using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Berpiztu.KeyPlus;

/// <summary>
/// A corrector that reads the whole sentence and writes it again mended
/// (spelling, accents, punctuation, capitals, a word that is one but means
/// another), Berpiztu/llm-create's: an encoder-decoder made from mT5-small,
/// with or without Virtus, in 8 or 32 bits. Each lives in a folder of its
/// own named as it is chosen (small-8, virtus-32…), as llm-create's
/// scripts/export.py writes it: encoder.onnx, decoder.onnx, spiece.model and
/// format.json (how a sentence is given, which ids are no text). It writes
/// its likeliest token each time, never sampling, as it was measured. Loaded
/// once, away from the window's thread; how long that took and how much
/// memory it took, written to the log.
/// </summary>
public sealed class ContextCorrector : IDisposable
{
    private const long Eos = 1;
    private const long Pad = 0;

    /// <summary>The longest a sentence it reads or writes, in tokens, as it was taught.</summary>
    private const int MostTokens = 128;

    private readonly InferenceSession encoder;
    private readonly InferenceSession decoder;
    private readonly SentencePieceTokenizer tokenizer;
    private readonly long[] prefix;
    private readonly HashSet<long> notText;
    private readonly Lock running = new();
    private bool disposed;

    public string Name { get; }

#if !ANDROID
    /// <summary>Where Windows' correctors are, each in a folder of its name: %LOCALAPPDATA%\Berpiztu\Keyboard\correctors.</summary>
    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Berpiztu", "Keyboard", "correctors");
#endif

    private ContextCorrector(string name, InferenceSession encoder, InferenceSession decoder, SentencePieceTokenizer tokenizer, long[] prefix, HashSet<long> notText)
    {
        Name = name;
        this.encoder = encoder;
        this.decoder = decoder;
        this.tokenizer = tokenizer;
        this.prefix = prefix;
        this.notText = notText;
    }

    /// <summary>
    /// The correctors in a folder, by their folders' names: those with all
    /// their files. None where it cannot be read (a folder put there by adb is
    /// adb's, not the app's, until it is opened to all), written to the log.
    /// </summary>
    public static IReadOnlyList<string> Names(string root)
    {
        try
        {
            return Directory.Exists(root)
                ? [.. Directory.GetDirectories(root).Where(Whole).Select(folder => Path.GetFileName(folder)!).Order(StringComparer.Ordinal)]
                : [];
        }
        catch (Exception failure) when (failure is UnauthorizedAccessException or IOException)
        {
            Log.Write($"Corrector: the correctors in {root} could not be read: {failure.Message}");
            return [];
        }
    }

    private static bool Whole(string folder) =>
        new[] { "encoder.onnx", "decoder.onnx", "spiece.model", "format.json" }.All(name => File.Exists(Path.Combine(folder, name)));

    /// <summary>The corrector in a folder; none where it is not whole or could not be read, written to the log.</summary>
    public static ContextCorrector? Load(string folder)
    {
        var name = Path.GetFileName(folder);
        if (!Whole(folder))
        {
            Log.Write($"Corrector: {name} is not whole in {folder}");
            return null;
        }

        try
        {
            var started = Stopwatch.StartNew();
            var before = MemoryMegabytes();
            using var form = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "format.json")));
            var prefix = form.RootElement.GetProperty("source_prefix").EnumerateArray().Select(id => id.GetInt64()).ToArray();
            var notText = form.RootElement.GetProperty("not_text").EnumerateArray().Select(id => id.GetInt64()).ToHashSet();
            SentencePieceTokenizer tokenizer;
            using (var pieces = File.OpenRead(Path.Combine(folder, "spiece.model")))
            {
                tokenizer = SentencePieceTokenizer.Create(pieces, false, false);
            }

            var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, IntraOpNumThreads = 2 };
            var corrector = new ContextCorrector(name, new InferenceSession(Path.Combine(folder, "encoder.onnx"), options),
                new InferenceSession(Path.Combine(folder, "decoder.onnx"), options), tokenizer, prefix, notText);
            Log.Write($"Corrector: {name} loaded in {started.ElapsedMilliseconds} ms, the app's memory {before} MB before and {MemoryMegabytes()} MB after");
            return corrector;
        }
        // Any failure: a model's files are put by hand, and the tokenizer reading one it does not expect throws what it
        // likes (IndexOutOfRangeException for a model without a start token), which took the keyboard down.
        catch (Exception failure)
        {
            Log.Write($"Corrector: {name} could not be loaded: {failure.Message}");
            return null;
        }
    }

    /// <summary>
    /// The sentence written again as the corrector reads it, the spaces around
    /// it kept; itself where it is empty, too long, or the corrector was let go.
    /// </summary>
    public string Correct(string sentence)
    {
        var text = sentence.Trim();
        if (text.Length == 0)
        {
            return sentence;
        }

        var started = Stopwatch.StartNew();
        long[] read = [.. prefix, .. tokenizer.EncodeToIds(text).Select(id => (long)id), Eos];
        if (read.Length > MostTokens)
        {
            return sentence;
        }

        List<long> written = [prefix.Length > 0 ? prefix[0] : Pad];
        lock (running)
        {
            if (disposed)
            {
                return sentence;
            }

            var ids = new DenseTensor<long>(read, [1, read.Length]);
            var mask = new DenseTensor<long>(Enumerable.Repeat(1L, read.Length).ToArray(), [1, read.Length]);
            using var encoded = encoder.Run([NamedOnnxValue.CreateFromTensor("input_ids", ids), NamedOnnxValue.CreateFromTensor("attention_mask", mask)]);
            var hidden = encoded.First().AsTensor<float>().Clone();
            while (written.Count < MostTokens)
            {
                var so = new DenseTensor<long>(written.ToArray(), [1, written.Count]);
                using var next = decoder.Run([
                    NamedOnnxValue.CreateFromTensor("decoder_input_ids", so),
                    NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hidden),
                    NamedOnnxValue.CreateFromTensor("encoder_attention_mask", mask),
                ]);
                var token = Likeliest(next.First().AsTensor<float>());
                if (token == Eos)
                {
                    break;
                }

                written.Add(token);
            }
        }

        var corrected = tokenizer.Decode(written.Skip(1).Where(id => !notText.Contains(id)).Select(id => (int)id)) ?? text;
        // What it read and what it wrote, in the detailed log only (turned on by the user), so who corrected what can be told.
        Log.Detail($"Corrector: {Name} read {read.Length} tokens and wrote {written.Count - 1} in {started.ElapsedMilliseconds} ms: "
            + (corrected == text ? $"«{text}» left as typed" : $"«{text}» → «{corrected}»"));
        var lead = sentence[..(sentence.Length - sentence.TrimStart().Length)];
        var trail = sentence[sentence.TrimEnd().Length..];
        return lead + corrected + trail;
    }

    /// <summary>What a sentence is given as to be asked for the words to offer, as llm-create taught it (suggest_pairs.TAG).</summary>
    private const string SuggestTag = "sugerir: ";

    /// <summary>The words offered, as llm-create measures them (common.suggest): the likeliest of a beam search of this many…</summary>
    private const int Beams = 5;

    /// <summary>…written up to this many tokens, each a word…</summary>
    private const int WordTokens = 12;

    /// <summary>…of which the three likeliest are offered.</summary>
    public const int Offered = 3;

    /// <summary>
    /// The words the model offers for the sentence typed so far, its last word being typed ("hola que t": "tal"), the
    /// likeliest first, as llm-create taught and measures it: a beam search of <see cref="Beams"/>, each hypothesis
    /// scored by its log-probability over its length, the <see cref="Offered"/> best of those ended. None where the
    /// sentence is empty or too long, the corrector was let go, or <paramref name="cancel"/> asked to stop (a newer letter).
    /// </summary>
    public IReadOnlyList<string> Suggest(string typed, CancellationToken cancel = default)
    {
        var text = typed.TrimStart();
        if (text.Length == 0)
        {
            return [];
        }

        var started = Stopwatch.StartNew();
        long[] read = [.. prefix, .. tokenizer.EncodeToIds(SuggestTag + text).Select(id => (long)id), Eos];
        if (read.Length > MostTokens)
        {
            return [];
        }

        var ended = new List<(List<long> Tokens, double Score)>();
        lock (running)
        {
            if (disposed || cancel.IsCancellationRequested)
            {
                return [];
            }

            var ids = new DenseTensor<long>(read, [1, read.Length]);
            var mask = new DenseTensor<long>(Enumerable.Repeat(1L, read.Length).ToArray(), [1, read.Length]);
            using var encoded = encoder.Run([NamedOnnxValue.CreateFromTensor("input_ids", ids), NamedOnnxValue.CreateFromTensor("attention_mask", mask)]);
            var hidden = encoded.First().AsTensor<float>();
            var (length, width) = (hidden.Dimensions[1], hidden.Dimensions[2]);
            var start = prefix.Length > 0 ? prefix[0] : Pad;
            var beams = new List<(List<long> Tokens, double LogProbability)> { ([start], 0) };
            for (var step = 0; step < WordTokens && beams.Count > 0 && !cancel.IsCancellationRequested; step++)
            {
                // Every beam in one run: the sentence read, and its mask, once for each.
                var rows = beams.Count;
                var written = new DenseTensor<long>([rows, beams[0].Tokens.Count]);
                var heard = new DenseTensor<float>([rows, length, width]);
                var masks = new DenseTensor<long>([rows, read.Length]);
                for (var row = 0; row < rows; row++)
                {
                    for (var at = 0; at < beams[row].Tokens.Count; at++)
                    {
                        written[row, at] = beams[row].Tokens[at];
                    }

                    for (var at = 0; at < length; at++)
                    {
                        for (var unit = 0; unit < width; unit++)
                        {
                            heard[row, at, unit] = hidden[0, at, unit];
                        }
                    }

                    for (var at = 0; at < read.Length; at++)
                    {
                        masks[row, at] = 1;
                    }
                }

                using var next = decoder.Run([
                    NamedOnnxValue.CreateFromTensor("decoder_input_ids", written),
                    NamedOnnxValue.CreateFromTensor("encoder_hidden_states", heard),
                    NamedOnnxValue.CreateFromTensor("encoder_attention_mask", masks),
                ]);
                var logits = next.First().AsTensor<float>();
                var vocabulary = logits.Dimensions[^1];
                var grown = new List<(List<long> Tokens, double LogProbability)>();
                for (var row = 0; row < rows; row++)
                {
                    // Each beam's log-probabilities (a log-softmax of its row), its best few grown.
                    var most = float.MinValue;
                    for (var token = 0; token < vocabulary; token++)
                    {
                        most = Math.Max(most, logits[row, token]);
                    }

                    var sum = 0.0;
                    for (var token = 0; token < vocabulary; token++)
                    {
                        sum += Math.Exp(logits[row, token] - most);
                    }

                    var logSum = most + Math.Log(sum);
                    // The 2 × Beams likeliest tokens, kept in a small heap whose least is let go: only a token above it goes in.
                    var best = new PriorityQueue<int, float>();
                    for (var token = 0; token < vocabulary; token++)
                    {
                        var score = logits[row, token];
                        if (best.Count < Beams * 2)
                        {
                            best.Enqueue(token, score);
                        }
                        else if (best.TryPeek(out _, out var least) && score > least)
                        {
                            best.DequeueEnqueue(token, score);
                        }
                    }

                    while (best.TryDequeue(out var token, out var logit))
                    {
                        grown.Add(([.. beams[row].Tokens, token], beams[row].LogProbability + logit - logSum));
                    }
                }

                beams = [];
                foreach (var hypothesis in grown.OrderByDescending(hypothesis => hypothesis.LogProbability).Take(Beams * 2))
                {
                    if (hypothesis.Tokens[^1] == Eos)
                    {
                        // Ended: scored over its length, as the beam search llm-create measures with (length penalty 1).
                        ended.Add((hypothesis.Tokens, hypothesis.LogProbability / (hypothesis.Tokens.Count - 1)));
                    }
                    else if (beams.Count < Beams)
                    {
                        beams.Add(hypothesis);
                    }
                }

                // Done once enough have ended and none still growing can beat the worst of the best ended.
                if (ended.Count >= Beams && beams.All(beam => beam.LogProbability / beam.Tokens.Count <= ended.OrderByDescending(end => end.Score).ElementAt(Beams - 1).Score))
                {
                    break;
                }
            }

            // Those still growing only where too few ended, as the beam search llm-create measures with: a long one written
            // with ease would else outscore a word that ended ("te").
            if (ended.Count < Offered)
            {
                ended.AddRange(beams.Select(beam => (beam.Tokens, beam.LogProbability / beam.Tokens.Count)));
            }
        }

        if (cancel.IsCancellationRequested)
        {
            return [];
        }

        var words = ended.OrderByDescending(end => end.Score)
            .Select(end => tokenizer.Decode(end.Tokens.Skip(1).Where(id => !notText.Contains(id)).Select(id => (int)id))?.Trim() ?? "")
            // A word is what the toolbar offers: where the model wrote on after it ("corregiendo. ¿Quién…"), its first, its marks aside.
            .Select(written => written.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim('.', ',', ';', ':', '?', '!', '¿', '¡', '…', '"') ?? "")
            .Where(word => word.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(Offered).ToList();
        Log.Detail($"Corrector: {Name} offered {string.Join(" | ", words)} for «{text}» in {started.ElapsedMilliseconds} ms");
        return words;
    }

    private static long Likeliest(Tensor<float> logits)
    {
        var best = 0;
        var most = float.MinValue;
        var count = (int)logits.Length;
        for (var i = 0; i < count; i++)
        {
            var score = logits.GetValue(i);
            if (score > most)
            {
                most = score;
                best = i;
            }
        }

        return best;
    }

    /// <summary>The app's memory now, in MB: on Android its native heap and Java's, where ONNX Runtime keeps a model; elsewhere its working set.</summary>
    private static long MemoryMegabytes()
    {
#if ANDROID
        var java = Java.Lang.Runtime.GetRuntime()!;
        return (Android.OS.Debug.NativeHeapAllocatedSize + java.TotalMemory() - java.FreeMemory()) / 1_000_000;
#else
        return Process.GetCurrentProcess().WorkingSet64 / 1_000_000;
#endif
    }

    /// <summary>Let go once no sentence is being read.</summary>
    public void Dispose()
    {
        lock (running)
        {
            disposed = true;
            encoder.Dispose();
            decoder.Dispose();
        }
    }
}
