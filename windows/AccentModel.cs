using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Spanish accents put back by diactag-1.0 (olaverse, Apache-2.0), on the
/// device with ONNX Runtime: a bidirectional character tagger that reads the
/// whole sentence, so "como estas hoy?" comes back "cómo estás hoy?". For
/// each character it chooses only the marks (no mark, an acute, a
/// diaeresis, the ñ's tilde) among those the language allows its letter
/// (labels.json's legal pairs), so it never adds or takes away a letter.
/// As olaverse's own decoder (<c>olaverse.nlp._diactag.infer</c>): the
/// calibrated temperature, a mark the user typed never taken away, and URLs,
/// addresses and handles left as they are. Loaded once, away from the
/// window's thread, from the folder it was put in; none, nothing is offered.
/// </summary>
public sealed class AccentModel : IDisposable
{
    /// <summary>The model's files: %LOCALAPPDATA%\Berpiztu\Keyboard\diactag (diactag.int8.onnx, labels.json, calibration.json).</summary>
    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Berpiztu", "Keyboard", "diactag");

    /// <summary>
    /// The model's files as Hugging Face holds them at one revision, each
    /// checked by its SHA-256 once downloaded: Spelling turns it on, and
    /// only then is it fetched (38 MB), never with the keyboard.
    /// </summary>
    private const string Revision = "101cef2c97c656b0392429275dc17fe5f357d82d";

    private static readonly Downloads.Item[] Files =
    [
        Hosted("diactag.int8.onnx", "1affa4c44610a5dbc3bcf0e24f8ab46c190b94c3b79cd28f49c06c5ff63e436f"),
        Hosted("labels.json", "fc35b9a3281bd857c063c16f10f853899592a4aa66cdbe27d6d124a9d0f67ae7"),
        Hosted("calibration.json", "51d30760b017db1590131ed6613bad6d18d353f9036146cfb5d5bbb8ff22e1cd"),
    ];

    private static Downloads.Item Hosted(string name, string sha256) =>
        new($"https://huggingface.co/olaverse/diactag-1.0/resolve/{Revision}/{name}", name, sha256);

    /// <summary>Whether the model's files are on this computer.</summary>
    public static bool Downloaded => Downloads.Present(Folder, Files);

    /// <summary>When the model's files were last written: downloaded again, the keyboard loads them again.</summary>
    public static DateTime Stamp => Downloads.Stamp(Folder, Files);

    /// <summary>The model's files fetched from Hugging Face at their revision (<see cref="Downloads"/>); how much of all is done, told as it goes.</summary>
    public static async Task DownloadAsync(IProgress<(long Done, long Total)> progress, CancellationToken cancel = default)
    {
        await Downloads.FilesAsync(Folder, Files, progress, cancel);
        Log.Write("Spelling: accent model downloaded");
    }

    private const string Language = "spa";
    private const int UnknownId = 1;

    /// <summary>The longest stretch the model reads at once (its training window); a longer sentence is read by its end.</summary>
    private const int Window = 384;

    /// <summary>The combining marks a shape names, as olaverse's unicode spec has them; a special letter's shape keeps the letter as it is.</summary>
    private static readonly Dictionary<string, string> ShapeMarks = new()
    {
        ["ACUTE"] = "́", ["GRAVE"] = "̀", ["CIRCUMFLEX"] = "̂", ["TILDE"] = "̃", ["MACRON"] = "̄",
        ["BREVE"] = "̆", ["DOT_ABOVE"] = "̇", ["DIAERESIS"] = "̈", ["HOOK_ABOVE"] = "̉",
        ["DOUBLE_ACUTE"] = "̋", ["CARON"] = "̌", ["HORN"] = "̛", ["DOT_BELOW"] = "̣",
        ["CEDILLA"] = "̧", ["OGONEK"] = "̨",
    };

    /// <summary>The marks Spanish writes: an acute, a diaeresis, the ñ's tilde; a letter with any other is a foreign word's, left as it is.</summary>
    private static readonly HashSet<char> SpanishMarks = ['́', '̈', '̃'];

    /// <summary>What is never natural language, as olaverse's PROTECTED_RE: a URL, an e-mail, a handle, inline code, a bare domain.</summary>
    private static readonly Regex Protected = new(
        @"(https?://\S+|www\.\S+|[\w.+-]+@[\w-]+\.[\w.]+|[@#][\w_]+|`[^`]*`|<[^>\s]{1,40}>|\b[A-Z]{2,}(?:_[A-Z0-9]+)+\b|\b\w+\.(?:com|org|net|io|ng|vn)\b)",
        RegexOptions.CultureInvariant);

    private readonly InferenceSession session;
    private readonly Dictionary<string, int> charIds;
    private readonly string[] shapes;
    private readonly string[] tones;
    private readonly long languageId;
    private readonly Dictionary<string, (int Shape, int Tone)[]> legal;
    private readonly float temperature;
    private readonly Lock running = new();

    private AccentModel(InferenceSession session, Labels labels, float temperature)
    {
        this.session = session;
        this.temperature = temperature;
        charIds = labels.Chars.Select((character, id) => (character, id)).GroupBy(entry => entry.character).ToDictionary(entry => entry.Key, entry => entry.First().id);
        shapes = [.. labels.Shapes];
        tones = [.. labels.Tones];
        languageId = Array.IndexOf(labels.Langs, Language);
        legal = labels.Legal.TryGetValue(Language, out var spanish)
            ? spanish.ToDictionary(entry => entry.Key, entry => entry.Value.Select(pair => (pair[0], pair[1])).ToArray())
            : [];
    }

    /// <summary>The model loaded from its folder; none where its files are not there or could not be read, written to the log.</summary>
    public static AccentModel? Load()
    {
        var onnx = Path.Combine(Folder, "diactag.int8.onnx");
        var labelsFile = Path.Combine(Folder, "labels.json");
        if (!File.Exists(onnx) || !File.Exists(labelsFile))
        {
            Log.Write($"Spelling: no accent model in {Folder}");
            return null;
        }

        try
        {
            var labels = JsonSerializer.Deserialize<Labels>(File.ReadAllText(labelsFile), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("labels.json is empty");
            var temperature = 1f;
            var calibration = Path.Combine(Folder, "calibration.json");
            if (File.Exists(calibration) && JsonDocument.Parse(File.ReadAllText(calibration)).RootElement.TryGetProperty("shared", out var shared))
            {
                temperature = shared.GetSingle();
            }

            var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, IntraOpNumThreads = 2 };
            var model = new AccentModel(new InferenceSession(onnx, options), labels, temperature);
            Log.Write("Spelling: accent model loaded");
            return model;
        }
        catch (Exception failure) when (failure is IOException or JsonException or InvalidDataException or OnnxRuntimeException)
        {
            Log.Write($"Spelling: the accent model could not be loaded: {failure.Message}");
            return null;
        }
    }

    /// <summary>
    /// The sentence with its Spanish accents as the model reads them; itself
    /// where nothing changes. A question or an exclamation written without
    /// its opening mark is read with it, as the model learnt Spanish ("como
    /// sería hoy?" it leaves as it is, "¿como sería hoy?" it reads "¿cómo
    /// sería hoy?"), and the mark taken off again: it never adds a letter.
    /// </summary>
    public string Restore(string text)
    {
        var closing = text.TrimEnd().LastOrDefault();
        var opening = closing switch { '?' => '¿', '!' => '¡', _ => '\0' };
        var start = text.Length - text.TrimStart().Length;
        if (opening == '\0' || text.Contains(opening) || start == text.Length)
        {
            return Read(text);
        }

        var read = Read(text.Insert(start, opening.ToString()));
        var at = read.IndexOf(opening);
        return at < 0 ? read : read.Remove(at, 1);
    }

    /// <summary>The sentence as the model reads it.</summary>
    private string Read(string text)
    {
        var graphemes = Graphemes(text.Normalize(NormalizationForm.FormC));
        if (graphemes.Count == 0)
        {
            return text;
        }

        // Only the end of a long sentence: the part being closed.
        var from = Math.Max(0, graphemes.Count - Window);
        var read = graphemes.Skip(from).ToList();
        if (Run(read) is not (var shapeLogits, var toneLogits))
        {
            return text;
        }

        var kept = ProtectedFlags(read);
        var written = new StringBuilder();
        foreach (var grapheme in graphemes.Take(from))
        {
            written.Append(grapheme);
        }

        for (var at = 0; at < read.Count; at++)
        {
            written.Append(kept[at] ? read[at] : Decoded(read[at], shapeLogits, toneLogits, at));
        }

        return written.ToString();
    }

    /// <summary>Let go once no sentence is being read: the keyboard loads the model again after it is downloaded again while a sentence may still be reading this one.</summary>
    public void Dispose()
    {
        lock (running)
        {
            disposed = true;
            session.Dispose();
        }
    }

    private bool disposed;

    /// <summary>The model run over the characters: each one's shape and tone scores; none once it was let go.</summary>
    private (float[,] Shape, float[,] Tone)? Run(List<string> graphemes)
    {
        var count = graphemes.Count;
        var ids = new DenseTensor<long>([1, count]);
        var attention = new DenseTensor<bool>([1, count]);
        for (var at = 0; at < count; at++)
        {
            ids[0, at] = charIds.TryGetValue(graphemes[at], out var id) ? id : UnknownId;
            attention[0, at] = true;
        }

        var feeds = new Dictionary<string, NamedOnnxValue>
        {
            ["ids"] = NamedOnnxValue.CreateFromTensor("ids", ids),
            ["lang"] = NamedOnnxValue.CreateFromTensor("lang", new DenseTensor<long>(new[] { languageId }, [1])),
            ["lang_known"] = NamedOnnxValue.CreateFromTensor("lang_known", new DenseTensor<long>(new[] { 1L }, [1])),
            ["attn"] = NamedOnnxValue.CreateFromTensor("attn", attention),
        };
        lock (running)
        {
            if (disposed)
            {
                return null;
            }

            using var outputs = session.Run([.. feeds.Where(feed => session.InputMetadata.ContainsKey(feed.Key)).Select(feed => feed.Value)]);
            var shape = outputs.First(output => output.Name == "shape_logits").AsTensor<float>();
            var tone = outputs.First(output => output.Name == "tone_logits").AsTensor<float>();
            return (LogSoftmax(shape, count, shapes.Length), LogSoftmax(tone, count, tones.Length));
        }
    }

    /// <summary>A head's scores as log probabilities, at the calibrated temperature.</summary>
    private float[,] LogSoftmax(Tensor<float> logits, int count, int classes)
    {
        var result = new float[count, classes];
        for (var at = 0; at < count; at++)
        {
            var most = float.MinValue;
            for (var c = 0; c < classes; c++)
            {
                most = Math.Max(most, logits[0, at, c] / temperature);
            }

            var sum = 0.0;
            for (var c = 0; c < classes; c++)
            {
                sum += Math.Exp((logits[0, at, c] / temperature) - most);
            }

            var log = most + (float)Math.Log(sum);
            for (var c = 0; c < classes; c++)
            {
                result[at, c] = (logits[0, at, c] / temperature) - log;
            }
        }

        return result;
    }

    /// <summary>
    /// A character as the model writes it: the likeliest of the marks its
    /// letter may take in Spanish; a mark the user typed kept when the model
    /// would take it away.
    /// </summary>
    private string Decoded(string grapheme, float[,] shapeScores, float[,] toneScores, int at)
    {
        var letter = Base(grapheme);
        if (!legal.TryGetValue(letter, out var pairs) || pairs.Length < 2)
        {
            return grapheme;
        }

        var best = pairs.MaxBy(pair => shapeScores[at, pair.Shape] + toneScores[at, pair.Tone]);
        var written = Composed(letter, shapes[best.Shape], tones[best.Tone]);
        return written == letter && grapheme != letter ? grapheme : written;
    }

    private static string Composed(string letter, string shape, string tone)
    {
        var marks = shape == "NONE" ? "" : shape.Split('+').Select(tag => ShapeMarks.GetValueOrDefault(tag)).All(mark => mark is not null)
            ? string.Concat(shape.Split('+').Select(tag => ShapeMarks[tag]))
            : null;
        return marks is null ? letter : (letter + marks + tone).Normalize(NormalizationForm.FormC);
    }

    /// <summary>A character's letter with its marks taken off, where they are marks the model knows on a Latin letter; else the character itself.</summary>
    private static string Base(string grapheme)
    {
        var split = grapheme.Normalize(NormalizationForm.FormD);
        if (split.Length < 2 || !(char.IsAscii(split[0]) || split[0] is >= 'À' and <= 'ɏ'))
        {
            return grapheme;
        }

        return split[1..].All(mark => ShapeMarks.ContainsValue(mark.ToString())) ? split[..1] : grapheme;
    }

    /// <summary>The characters left as they are: in a URL, an address, a handle; or carrying a mark Spanish does not write (a foreign word's).</summary>
    private static bool[] ProtectedFlags(List<string> graphemes)
    {
        var flags = new bool[graphemes.Count];
        var text = string.Concat(graphemes);
        var spans = Protected.Matches(text).Select(match => (match.Index, End: match.Index + match.Length)).ToList();
        var position = 0;
        for (var at = 0; at < graphemes.Count; at++)
        {
            var split = graphemes[at].Normalize(NormalizationForm.FormD);
            flags[at] = spans.Any(span => position >= span.Index && position < span.End)
                || (split.Length > 1 && !split[1..].All(SpanishMarks.Contains));
            position += graphemes[at].Length;
        }

        return flags;
    }

    /// <summary>The text's characters, each with the marks that follow it, as olaverse reads them.</summary>
    private static List<string> Graphemes(string text)
    {
        List<string> graphemes = [];
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            graphemes.Add(elements.GetTextElement());
        }

        return graphemes;
    }

    private sealed record Labels(string[] Chars, string[] Shapes, string[] Tones, string[] Langs, Dictionary<string, Dictionary<string, int[][]>> Legal);
}
