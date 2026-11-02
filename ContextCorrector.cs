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

    /// <summary>How a suggestion is asked for: "sugerir: ", or, for a model taught so (its format.json's suggest_tag), "sugerir {lang}: ", the keyboard's language put in.</summary>
    private readonly string suggestTag;
    private readonly Lock running = new();
    private bool disposed;

    public string Name { get; }

#if !ANDROID
    /// <summary>Where Windows' correctors are, each in a folder of its name: %LOCALAPPDATA%\Berpiztu\Keyboard\correctors.</summary>
    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Berpiztu", "Keyboard", "correctors");
#endif

    private ContextCorrector(string name, InferenceSession encoder, InferenceSession decoder, SentencePieceTokenizer tokenizer, long[] prefix, HashSet<long> notText, string suggestTag)
    {
        Name = name;
        this.suggestTag = suggestTag;
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

            // Up to four of the device's cores: with two, the phone took some 0.4 s a letter for the words offered, behind a
            // hurried thumb (the owner, 2026-11-01).
            var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, IntraOpNumThreads = Math.Min(4, Environment.ProcessorCount) };
            var corrector = new ContextCorrector(name, new InferenceSession(Path.Combine(folder, "encoder.onnx"), options),
                new InferenceSession(Path.Combine(folder, "decoder.onnx"), options), tokenizer, prefix, notText,
                form.RootElement.TryGetProperty("suggest_tag", out var tag) && tag.GetString() is { Length: > 0 } told ? told : SuggestTag);
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

    /// <summary>The words offered, as llm-create measures them (common.suggest): the likeliest of a beam search of this many, unless Troubleshooting's Search beams says other…</summary>
    public const int BeamsUsual = 5;

    /// <summary>The most beams Search beams takes; the fewest are the words offered.</summary>
    public const int BeamsMost = 10;

    /// <summary>…written up to this many tokens, each a word…</summary>
    private const int WordTokens = 12;

    /// <summary>…of which the three likeliest are offered.</summary>
    public const int Offered = 3;

    /// <summary>The fewest beams Search beams takes: fewer could not give the words offered.</summary>
    public const int BeamsFewest = Offered;

    /// <summary>
    /// The words the model offers for the sentence typed so far, its last word being typed ("hola que t": "tal"), the
    /// likeliest first, as llm-create taught and measures it: a beam search of <see cref="BeamsUsual"/>, each hypothesis
    /// scored by its log-probability over its length, the <see cref="Offered"/> best of those ended. None where the
    /// sentence is empty or too long, the corrector was let go, or <paramref name="cancel"/> asked to stop (a newer letter).
    /// A model taught with the language in it is asked in the keyboard's (<paramref name="language"/>, es or en).
    /// </summary>
    public IReadOnlyList<string> Suggest(string typed, string language, CancellationToken cancel = default) =>
        [.. SuggestScored(typed, language, cancel).Select(offered => offered.Word)];

    /// <summary>
    /// What the debug window shows (the owner, 2026-11-01): the model chosen, the one really loaded, the language's Percentage for
    /// bold, the search's beams, what it was asked and the words it gave with their percentages, as it gave them; none loaded, that the words come from the list.
    /// </summary>
    public static string Said(string? chosen, string? loaded, string asked, IReadOnlyList<(string Word, double Probability)> words, int boldPercent = 0, int beams = BeamsUsual)
    {
        var said = new System.Text.StringBuilder();
        said.AppendLine($"Elegido: {chosen ?? "ninguno"}");
        said.AppendLine($"Cargado: {loaded ?? "ninguno: palabras de la lista"}");
        said.AppendLine($"Negrita desde: {(boldPercent > 0 ? $"{boldPercent} %" : "sin porcentaje")}");
        said.AppendLine($"Haces: {beams}");
        said.AppendLine($"Escrito: {asked}");
        foreach (var (word, probability) in words)
        {
            said.AppendLine($"{word}  {(probability * 100).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("es-ES"))} %");
        }

        return said.ToString().TrimEnd();
    }

    /// <summary>The words of <see cref="Suggest"/>, each with the probability the search gave to writing it whole, by a search of <paramref name="beamCount"/> beams.</summary>
    public IReadOnlyList<(string Word, double Probability)> SuggestScored(string typed, string language, CancellationToken cancel = default, int beamCount = BeamsUsual)
    {
        var width = Math.Clamp(beamCount, BeamsFewest, BeamsMost);
        var text = typed.TrimStart();
        if (text.Length == 0)
        {
            return [];
        }

        var started = Stopwatch.StartNew();
        long[] read = [.. prefix, .. tokenizer.EncodeToIds(suggestTag.Replace("{lang}", language) + text).Select(id => (long)id), Eos];
        if (read.Length > MostTokens)
        {
            return [];
        }

        var ended = new List<(List<long> Tokens, double Score, double LogProbability)>();
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
            var (length, heardWidth) = (hidden.Dimensions[1], hidden.Dimensions[2]);
            var sentence = hidden.ToArray();

            // The sentence read and its mask, once for each beam, made once for each number of beams and copied whole:
            // copied a number at a time through the tensor's indexer for every step, the phone took most of a second.
            var heardFor = new Dictionary<int, (DenseTensor<float> Heard, DenseTensor<long> Masks)>();
            (DenseTensor<float> Heard, DenseTensor<long> Masks) Read(int rows)
            {
                if (!heardFor.TryGetValue(rows, out var made))
                {
                    var heard = new DenseTensor<float>([rows, length, heardWidth]);
                    var masks = new DenseTensor<long>([rows, read.Length]);
                    for (var row = 0; row < rows; row++)
                    {
                        sentence.AsSpan().CopyTo(heard.Buffer.Span.Slice(row * sentence.Length, sentence.Length));
                    }

                    masks.Buffer.Span.Fill(1);
                    heardFor[rows] = made = (heard, masks);
                }

                return made;
            }

            var start = prefix.Length > 0 ? prefix[0] : Pad;
            var beams = new List<(List<long> Tokens, double LogProbability)> { ([start], 0) };
            for (var step = 0; step < WordTokens && beams.Count > 0 && !cancel.IsCancellationRequested; step++)
            {
                // Every beam in one run: the sentence read, and its mask, once for each.
                var rows = beams.Count;
                var written = new DenseTensor<long>([rows, beams[0].Tokens.Count]);
                for (var row = 0; row < rows; row++)
                {
                    beams[row].Tokens.ToArray().CopyTo(written.Buffer.Span.Slice(row * beams[row].Tokens.Count));
                }

                var (heard, masks) = Read(rows);

                using var next = decoder.Run([
                    NamedOnnxValue.CreateFromTensor("decoder_input_ids", written),
                    NamedOnnxValue.CreateFromTensor("encoder_hidden_states", heard),
                    NamedOnnxValue.CreateFromTensor("encoder_attention_mask", masks),
                ]);
                var output = next.First().AsTensor<float>();
                var vocabulary = output.Dimensions[^1];
                // Each row's numbers read in place, not through the tensor's indexer, which reckons each one's place anew.
                var logits = output is DenseTensor<float> dense ? dense.Buffer : output.ToArray().AsMemory();
                var grown = new List<(List<long> Tokens, double LogProbability)>();
                for (var row = 0; row < rows; row++)
                {
                    // Each beam's log-probabilities (a log-softmax of its row), its best few grown.
                    var scores = logits.Span.Slice(row * vocabulary, vocabulary);
                    var most = float.MinValue;
                    foreach (var score in scores)
                    {
                        most = Math.Max(most, score);
                    }

                    var sum = 0.0;
                    foreach (var score in scores)
                    {
                        sum += Math.Exp(score - most);
                    }

                    var logSum = most + Math.Log(sum);
                    // The 2 × width likeliest tokens, kept in a small heap whose least is let go: only a token above it goes in.
                    var best = new PriorityQueue<int, float>();
                    for (var token = 0; token < vocabulary; token++)
                    {
                        var score = scores[token];
                        if (best.Count < width * 2)
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
                foreach (var hypothesis in grown.OrderByDescending(hypothesis => hypothesis.LogProbability).Take(width * 2))
                {
                    if (hypothesis.Tokens[^1] == Eos)
                    {
                        // Ended: scored over its length, as the beam search llm-create measures with (length penalty 1).
                        ended.Add((hypothesis.Tokens, hypothesis.LogProbability / (hypothesis.Tokens.Count - 1), hypothesis.LogProbability));
                    }
                    else if (beams.Count < width)
                    {
                        beams.Add(hypothesis);
                    }
                }

                // Done once enough have ended and none still growing can beat the worst of the best ended.
                if (ended.Count >= width && beams.All(beam => beam.LogProbability / beam.Tokens.Count <= ended.OrderByDescending(end => end.Score).ElementAt(width - 1).Score))
                {
                    break;
                }
            }

            // Those still growing only where too few ended, as the beam search llm-create measures with: a long one written
            // with ease would else outscore a word that ended ("te").
            if (ended.Count < Offered)
            {
                ended.AddRange(beams.Select(beam => (beam.Tokens, beam.LogProbability / beam.Tokens.Count, beam.LogProbability)));
            }
        }

        if (cancel.IsCancellationRequested)
        {
            return [];
        }

        var words = ended.OrderByDescending(end => end.Score)
            .Select(end => (Written: tokenizer.Decode(end.Tokens.Skip(1).Where(id => !notText.Contains(id)).Select(id => (int)id))?.Trim() ?? "", end.LogProbability))
            // A word is what the toolbar offers: where the model wrote on after it ("corregiendo. ¿Quién…"), its first, its marks aside.
            .Select(end => (Word: end.Written.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim('.', ',', ';', ':', '?', '!', '¿', '¡', '…', '"') ?? "", Probability: Math.Exp(end.LogProbability)))
            .Where(offered => offered.Word.Length > 0).DistinctBy(offered => offered.Word, StringComparer.OrdinalIgnoreCase).Take(Offered).ToList();
        Log.Detail($"Corrector: {Name} offered {string.Join(" | ", words.Select(offered => offered.Word))} for «{text}» in {started.ElapsedMilliseconds} ms");
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
