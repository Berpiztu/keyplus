using System.IO;

namespace Berpiztu.KeyPlus;

/// <summary>
/// What glide typing reads with, never carried by the app: FUTO Swipe's
/// encoder (honorable_sturgeon, from FUTO's Hugging Face repository), one for
/// every language, and each language's word list (AOSP LatinIME's, with each
/// word's frequency, from Android's source); a language's Glide typing turned
/// on fetches its word list, and the encoder the first time, at a fixed
/// revision, each file checked by its SHA-256 (<see cref="Downloads"/>). A
/// language's word list also gives the words offered as a word is typed and
/// how common a correction is; without it, neither.
/// </summary>
public static class SwipeData
{
    /// <summary>The files: %LOCALAPPDATA%\Berpiztu\Keyboard\swipe-data (Android's app files, there).</summary>
    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Berpiztu", "Keyboard", "swipe-data");

    /// <summary>FUTO's futo-swipe repository at one revision.</summary>
    private const string ModelRevision = "18328c3042b066952c0936b3771d492fe2ec289a";

    /// <summary>AOSP's LatinIME at one commit; its source browser sends a file as base64.</summary>
    private const string WordListRevision = "127336e9f29d69607eab55982324b210279ae8c5";

    private static readonly Downloads.Item[] Model =
    [
        new($"https://huggingface.co/futo-org/futo-swipe/resolve/{ModelRevision}/honorable_sturgeon/model_fp32.pte",
            "model_fp32.pte", "725242bab5d14345e96ff214e8de2bfbc1f962c232d320df9c24cb82ffd1fbaf"),
        new($"https://huggingface.co/futo-org/futo-swipe/resolve/{ModelRevision}/honorable_sturgeon/metadata.json",
            "metadata.json", "d2c5aecd89d97e21125046eb1f311b5aed1bdb5805e97316bba70b13f1c7be2c"),
    ];

    /// <summary>Each language's word list by its code, its SHA-256 the file's own, decoded.</summary>
    private static readonly Dictionary<string, Downloads.Item> WordLists = new()
    {
        [StandardKeys.Spanish.Code] = WordList(StandardKeys.Spanish, "889ad52bce2933e2a30a0560b8d5a76f5334500e776a7db7c7cb3e9e79fc2652"),
        [StandardKeys.English.Code] = WordList(StandardKeys.English, "0f78dd455b532be169a23f233227b811fabced4b5bd7fc9c40cc05839793bcbd"),
    };

    private static Downloads.Item WordList(StandardLanguage language, string sha256) =>
        new($"https://android.googlesource.com/platform/packages/inputmethods/LatinIME/+/{WordListRevision}/{language.WordList}?format=TEXT",
            Path.GetFileName(language.WordList), sha256, Base64: true);

    /// <summary>The encoder, where it is once downloaded.</summary>
    public static string ModelPath => Path.Combine(Folder, Model[0].Name);

    /// <summary>A language's word list, where it is once downloaded.</summary>
    public static string WordListPath(StandardLanguage language) => Path.Combine(Folder, Path.GetFileName(language.WordList));

    /// <summary>Whether the encoder is on this device.</summary>
    public static bool ModelDownloaded => Downloads.Present(Folder, Model);

    /// <summary>Whether a language can be swiped in: the encoder and its word list on this device.</summary>
    public static bool ReadyFor(StandardLanguage language) => ModelDownloaded && HasWordList(language);

    /// <summary>How large the encoder is, in megabytes, fetched once for every language.</summary>
    public const double ModelMegabytes = 2.6;

    /// <summary>Whether a language's word list is on this device.</summary>
    public static bool HasWordList(StandardLanguage language) => File.Exists(WordListPath(language));

    /// <summary>A language's word list with the folder it goes to, for a download of its own (Correct words fetches it with the dictionary).</summary>
    public static (string Folder, Downloads.Item File) WordListFile(StandardLanguage language) => (Folder, WordLists[language.Code]);

    /// <summary>How large a language's word list is, in megabytes, for a choice to say what turning it on downloads.</summary>
    public static double WordListMegabytes(StandardLanguage language) => language.Code == StandardKeys.Spanish.Code ? 1.2 : 0.9;

    /// <summary>When a language's word list was last written: downloaded again, it is read again.</summary>
    public static DateTime WordListStamp(StandardLanguage language) => File.GetLastWriteTimeUtc(WordListPath(language));

    /// <summary>The encoder fetched, one for every language; how much is done, told as it goes.</summary>
    public static async Task DownloadModel(IProgress<(long Done, long Total)> progress, CancellationToken cancel = default)
    {
        await Downloads.FilesAsync([.. Model.Select(file => (Folder, file))], progress, cancel);
        Log.Write("Glide typing: FUTO Swipe's model downloaded");
    }

    /// <summary>A language's word list fetched alone; how much is done, told as it goes.</summary>
    public static async Task DownloadWordList(StandardLanguage language, IProgress<(long Done, long Total)> progress, CancellationToken cancel = default)
    {
        await Downloads.FilesAsync([WordListFile(language)], progress, cancel);
        Log.Write($"Suggestions: the {language.Code} word list downloaded");
    }

    /// <summary>A language's word list fetched, and the encoder with it where it is not here yet; how much of all is done, told as it goes.</summary>
    public static async Task DownloadFor(StandardLanguage language, IProgress<(long Done, long Total)> progress, CancellationToken cancel = default)
    {
        await Downloads.FilesAsync([.. (ModelDownloaded ? [] : Model).Select(file => (Folder, file)), WordListFile(language)], progress, cancel);
        Log.Write($"Glide typing: the {language.Code} word list downloaded, with FUTO Swipe's model where it was missing");
    }
}
