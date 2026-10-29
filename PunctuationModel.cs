using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Punctuation and capitals put in by 1-800-BAD-CODE's models (Apache-2.0),
/// one for each language (<see cref="Files"/>: Spanish's
/// punctuation_fullstop_truecase_romance, English's
/// punctuation_fullstop_truecase_english), on the device with ONNX Runtime:
/// it reads a sentence in small letters and without marks, and for each
/// SentencePiece token tells whether ¿ goes before it, whether . , or ? goes
/// after it and which of its letters are capitals, as punctuators' collector
/// applies them (<c>pcs_collector.py</c>). It only adds: a mark the user typed
/// is kept, none is taken away, a word is never changed but for its first
/// letter made a capital; in Spanish a ¿ is added only with its ?, and a ?
/// the model puts with no ¿ becomes a full stop, for Spanish opens what it
/// closes. Loaded once, away from the window's thread, from the folder it was
/// put in; none, nothing is offered.
/// </summary>
public sealed class PunctuationModel : IDisposable
{
    /// <summary>
    /// A language's model: its folder under %LOCALAPPDATA%\Berpiztu\Keyboard,
    /// its files as Hugging Face holds them at one revision, each checked by
    /// its SHA-256 once downloaded (Spelling turns it on, and only then is it
    /// fetched), and whether its language opens a question with ¿.
    /// </summary>
    public sealed record Files(string Language, string Named, string Folder, string Repository, string Revision, string Onnx, string OnnxSha256, string Pieces, string PiecesSha256, bool OpensQuestions)
    {
        public string Path(string name) => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Berpiztu", "Keyboard", Folder, name);

        private Downloads.Item[] Items =>
        [
            new($"https://huggingface.co/{Repository}/resolve/{Revision}/{Onnx}", Onnx, OnnxSha256),
            new($"https://huggingface.co/{Repository}/resolve/{Revision}/{Pieces}", Pieces, PiecesSha256),
        ];

        /// <summary>Whether the model's files are on this device.</summary>
        public bool Downloaded => Downloads.Present(Path(""), Items);

        /// <summary>When the model's files were last written: downloaded again, the keyboard loads them again.</summary>
        public DateTime Stamp => Downloads.Stamp(Path(""), Items);

        /// <summary>The model's files fetched from Hugging Face at their revision (<see cref="Downloads"/>); how much of all is done, told as it goes.</summary>
        public async Task DownloadAsync(IProgress<(long Done, long Total)> progress, CancellationToken cancel = default)
        {
            await Downloads.FilesAsync(Path(""), Items, progress, cancel);
            Log.Write($"Spelling: the {Language} punctuation model downloaded");
        }
    }

    /// <summary>Spanish's: punctuation_fullstop_truecase_romance, 144 MB.</summary>
    public static readonly Files Spanish = new(
        StandardKeys.Spanish.Code, "punctuation_fullstop_truecase_romance, 144", "punctuation-romance",
        "1-800-BAD-CODE/punctuation_fullstop_truecase_romance", "1e8d395731a9e19471cee4b636f034e0cff7b405",
        "model.onnx", "66963a466ac42871e8e021db4cdf75cca91e022dec5d8e264fdd62e79aa4c968",
        "sp.model", "e280feacd179d1eac4968298b126b342eeb949443a5dbeb3a0dec9c6bad14db3", OpensQuestions: true);

    /// <summary>English's: punctuation_fullstop_truecase_english, 209 MB.</summary>
    public static readonly Files English = new(
        "en", "punctuation_fullstop_truecase_english, 209", "punctuation-english",
        "1-800-BAD-CODE/punctuation_fullstop_truecase_english", "b26fd1c40e88678859048898218ea4edcc24c84a",
        "punct_cap_seg_en.onnx", "dd922d459da618cd324280889740608b76fb3e9e61d3f402291be1251f91421b",
        "spe_32k_lc_en.model", "9e86d0263de80b3b68327a21f5350c8cdf846e4c4400253c9baf05e3d44871c3", OpensQuestions: false);

    /// <summary>A language's model; none where it has none.</summary>
    public static Files? For(string language) => language == Spanish.Language ? Spanish : language == English.Language ? English : null;

    /// <summary>As both models' config.yaml: the longest they read (256 tokens, their start and end among them), what may go before a token and what after it.</summary>
    private const int MostTokens = 256;
    private static readonly string?[] Before = [null, "¿"];
    private static readonly string?[] After = [null, null, ".", ",", "?"];

    /// <summary>What SentencePiece writes for the space before a word.</summary>
    private const char WordStart = '▁';

    /// <summary>The marks a word typed may carry before and after its letters; the model reads it without them.</summary>
    private const string Openings = "¿¡\"'(«";
    private const string Closings = ".,;:?!\"')»…";

    private static readonly Regex Words = new(@"\S+", RegexOptions.CultureInvariant);

    private readonly InferenceSession session;
    private readonly SentencePieceTokenizer tokenizer;
    private readonly bool opensQuestions;
    private readonly Lock running = new();
    private bool disposed;

    private PunctuationModel(InferenceSession session, SentencePieceTokenizer tokenizer, bool opensQuestions)
    {
        this.session = session;
        this.tokenizer = tokenizer;
        this.opensQuestions = opensQuestions;
    }

    /// <summary>A language's model loaded from its folder; none where its files are not there or could not be read, written to the log.</summary>
    public static PunctuationModel? Load(Files files)
    {
        var onnx = files.Path(files.Onnx);
        var pieces = files.Path(files.Pieces);
        if (!File.Exists(onnx) || !File.Exists(pieces))
        {
            Log.Write($"Spelling: no {files.Language} punctuation model in {files.Path("")}");
            return null;
        }

        try
        {
            SentencePieceTokenizer tokenizer;
            using (var stream = File.OpenRead(pieces))
            {
                tokenizer = SentencePieceTokenizer.Create(stream, false, false);
            }

            var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, IntraOpNumThreads = 2 };
            var model = new PunctuationModel(new InferenceSession(onnx, options), tokenizer, files.OpensQuestions);
            Log.Write($"Spelling: the {files.Language} punctuation model loaded");
            return model;
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException or OnnxRuntimeException or ArgumentException)
        {
            Log.Write($"Spelling: the {files.Language} punctuation model could not be loaded: {failure.Message}");
            return null;
        }
    }

    /// <summary>
    /// The sentence with the marks and capitals the model adds; itself where
    /// it adds none. <paramref name="opens"/>: whether it begins where a
    /// sentence does, so its first letter may become a capital.
    /// </summary>
    public string Punctuate(string text, bool opens)
    {
        var typed = Words.Matches(text).Select(Typed).ToList();
        var read = typed.Where(word => word.Letters.Length > 0).ToList();
        if (read.Count == 0 || Read(string.Join(' ', read.Select(word => word.Letters.ToLowerInvariant()))) is not { } marked)
        {
            return text;
        }

        if (marked.Count != read.Count)
        {
            Log.Write($"Spelling: the punctuation model read {marked.Count} words where {read.Count} were typed, so it adds nothing");
            return text;
        }

        // The model's marks and capitals on each word read, as long as its letters are the same.
        var written = new Dictionary<int, string>();
        int? opened = null;
        for (var at = 0; at < read.Count; at++)
        {
            var word = read[at];
            var model = marked[at];
            var letters = word.Letters;
            if (model.Letters.Length == letters.Length && char.IsUpper(model.Letters[0]) && char.IsLower(letters[0]) && (at > 0 || opens))
            {
                letters = char.ToUpperInvariant(letters[0]) + letters[1..];
            }

            var lead = word.Lead.Length > 0 ? word.Lead : model.Lead;
            var trail = word.Trail.Length > 0 ? word.Trail : model.Trail;

            // Spanish's ¿ kept only with its ?; a ? the model closes with no ¿ open, a full stop. Another language's opens nothing.
            if (!opensQuestions)
            {
                lead = word.Lead;
            }
            else if (word.Lead.Length == 0 && model.Lead == "¿")
            {
                opened = at;
            }

            if (opensQuestions && word.Trail.Length == 0 && model.Trail == "?" && opened is null && !word.Lead.Contains('¿'))
            {
                trail = ".";
            }

            if (trail.Contains('?'))
            {
                opened = null;
            }
            else if (trail.IndexOfAny(['.', '!']) >= 0 && opened is { } open)
            {
                written[read[open].Index] = written[read[open].Index][1..];
                opened = null;
            }

            written[word.Index] = lead + letters + trail;
        }

        if (opened is { } left)
        {
            written[read[left].Index] = written[read[left].Index][1..];
        }

        var result = new StringBuilder(text);
        foreach (var word in typed.Where(word => written.ContainsKey(word.Index)).OrderByDescending(word => word.Index))
        {
            result.Remove(word.Index, word.Length).Insert(word.Index, written[word.Index]);
        }

        return result.ToString();
    }

    /// <summary>Let go once no sentence is being read.</summary>
    public void Dispose()
    {
        lock (running)
        {
            disposed = true;
            session.Dispose();
        }
    }

    /// <summary>A word as typed: where it is, how long, its marks before and after, and its letters between them.</summary>
    private sealed record TypedWord(int Index, int Length, string Lead, string Letters, string Trail);

    private static TypedWord Typed(Match match)
    {
        var word = match.Value;
        var start = 0;
        while (start < word.Length && Openings.Contains(word[start]))
        {
            start++;
        }

        var end = word.Length;
        while (end > start && Closings.Contains(word[end - 1]))
        {
            end--;
        }

        return new(match.Index, match.Length, word[..start], word[start..end], word[end..]);
    }

    /// <summary>A word as the model writes it: the mark before it, its letters with their capitals, the mark after it.</summary>
    private sealed record MarkedWord(string Lead, string Letters, string Trail);

    /// <summary>The words read (small letters, no marks, one space between them) as the model marks them; none where they are too long for it, or it was let go.</summary>
    private List<MarkedWord>? Read(string plain)
    {
        var tokens = tokenizer.EncodeToTokens(plain, out _);
        if (tokens.Count == 0 || tokens.Count > MostTokens - 2)
        {
            return null;
        }

        var ids = new DenseTensor<long>([1, tokens.Count + 2]);
        ids[0, 0] = tokenizer.BeginningOfSentenceId;
        for (var at = 0; at < tokens.Count; at++)
        {
            ids[0, at + 1] = tokens[at].Id;
        }

        ids[0, tokens.Count + 1] = tokenizer.EndOfSentenceId;
        Tensor<long> before, after;
        Tensor<bool> capitals;
        lock (running)
        {
            if (disposed)
            {
                return null;
            }

            using var outputs = session.Run([NamedOnnxValue.CreateFromTensor("input_ids", ids)]);
            before = outputs.First(output => output.Name == "pre_preds").AsTensor<long>().Clone();
            after = outputs.First(output => output.Name == "post_preds").AsTensor<long>().Clone();
            capitals = outputs.First(output => output.Name == "cap_preds").AsTensor<bool>().Clone();
        }

        List<MarkedWord> words = [];
        StringBuilder? letters = null;
        string lead = "", trail = "";
        for (var at = 0; at < tokens.Count; at++)
        {
            var piece = tokens[at].Value;
            var position = at + 1;
            var first = piece.Length > 0 && piece[0] == WordStart ? 1 : 0;
            if (first == 1 || letters is null)
            {
                if (letters is not null)
                {
                    words.Add(new(lead, letters.ToString(), trail));
                }

                letters = new StringBuilder();
                lead = Before[before[0, position]] ?? "";
                trail = "";
            }

            for (var c = first; c < piece.Length; c++)
            {
                letters.Append(c < capitals.Dimensions[2] && capitals[0, position, c] ? char.ToUpperInvariant(piece[c]) : piece[c]);
            }

            // What goes after a word is its last token's.
            trail = After[after[0, position]] ?? "";
        }

        if (letters is not null)
        {
            words.Add(new(lead, letters.ToString(), trail));
        }

        return words;
    }
}
