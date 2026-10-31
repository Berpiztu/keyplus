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
