using System.IO;
using WeCantSpell.Hunspell;

namespace Berpiztu.KeyPlus;

/// <summary>
/// A language's words spelled right, and the words a misspelled one may have
/// meant ("kasa": "casa", "helo": "hello"), as LibreOffice checks them:
/// Hunspell (WeCantSpell.Hunspell, a port to .NET) with LibreOffice's Spanish
/// (Spain) dictionary from RLA-ES and its English (US) one from SCOWL, on the
/// device. Off until Spelling turns it on, when the dictionaries are
/// downloaded once; each loaded away from the window's thread from the folder
/// it was put in; none, no word of its language is corrected.
/// </summary>
public sealed class SpellingDictionary
{
    /// <summary>The dictionaries' files: %LOCALAPPDATA%\Berpiztu\Keyboard\hunspell (es_ES.aff and .dic, en_US.aff and .dic).</summary>
    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Berpiztu", "Keyboard", "hunspell");

    /// <summary>LibreOffice's dictionaries repository at one commit; each file checked by its SHA-256 once downloaded (1.4 MB in all).</summary>
    private const string Revision = "762abe74008b94b2ff06db6f4024b59a8254c467";

    /// <summary>Each language's dictionary, by the keyboard's language code: its name, its size in megabytes and its two files.</summary>
    private static readonly Dictionary<string, (string Name, double Megabytes, Downloads.Item[] Files)> Languages = new()
    {
        [StandardKeys.Spanish.Code] = ("es_ES", 0.9,
        [
            Hosted("es", "es_ES.aff", "e73a9bf8e1383f4986a5dc9e2fbed49371c0c61f511c626d15586bd433c1cad9"),
            Hosted("es", "es_ES.dic", "6975dddec3d5d2c676069537bc67b4b5f786c65c5d4cf6703a82acf779ac9ec1"),
        ]),
        [StandardKeys.English.Code] = ("en_US", 0.55,
        [
            Hosted("en", "en_US.aff", "e746c882dd6f303c2c46e7452804b9201115a6942cfeb15f18f8edf774d2e24e"),
            Hosted("en", "en_US.dic", "f0b1a234bd178bdd01875b2a392a9647f888b8fe879f79c52aae62c2759b3647"),
        ]),
    };

    private static Downloads.Item Hosted(string folder, string name, string sha256) =>
        new($"https://raw.githubusercontent.com/LibreOffice/dictionaries/{Revision}/{folder}/{name}", name, sha256);

    /// <summary>Whether Spelling has a dictionary for a language.</summary>
    public static bool Has(string language) => Languages.ContainsKey(language);

    /// <summary>Whether a language's dictionary is on this computer; none for a language that has none.</summary>
    public static bool DownloadedFor(string language) => Languages.TryGetValue(language, out var known) && Downloads.Present(Folder, known.Files);

    /// <summary>When a language's dictionary was last written: downloaded again, the keyboard loads it again.</summary>
    public static DateTime StampFor(string language) => Downloads.Stamp(Folder, Languages[language].Files);

    /// <summary>How large a language's dictionary is, in megabytes, for its choice to say what turning it on downloads.</summary>
    public static double MegabytesFor(string language) => Languages[language].Megabytes;

    /// <summary>A language's dictionary's files, each with the folder it goes to, from LibreOffice's repository at its commit (<see cref="Downloads"/>).</summary>
    public static IEnumerable<(string Folder, Downloads.Item File)> FilesFor(string language) => Languages[language].Files.Select(file => (Folder, file));

    private readonly WordList words;

    /// <summary>Whether the language is Spanish, whose letters of one sound (<see cref="SpanishSounds"/>) weigh a mistake less.</summary>
    private readonly bool spanish;

    private SpellingDictionary(WordList words, bool spanish) => (this.words, this.spanish) = (words, spanish);

    /// <summary>A language's dictionary loaded from its folder; none where its files are not there or could not be read, written to the log.</summary>
    public static SpellingDictionary? Load(string language)
    {
        if (!DownloadedFor(language))
        {
            Log.Write($"Spelling: no {language} dictionary in {Folder}");
            return null;
        }

        var name = Languages[language].Name;
        try
        {
            var dictionary = new SpellingDictionary(
                WordList.CreateFromFiles(Path.Combine(Folder, name + ".dic"), Path.Combine(Folder, name + ".aff")), language == StandardKeys.Spanish.Code);
            Log.Write($"Spelling: the {language} dictionary loaded");
            return dictionary;
        }
        // Any failure of a file Hunspell cannot read leaves the keyboard without a dictionary, never ended.
        catch (Exception failure)
        {
            Log.Write($"Spelling: the {language} dictionary could not be loaded: {failure.Message}");
            return null;
        }
    }

    /// <summary>Whether the word is spelled right.</summary>
    public bool Knows(string word) => words.Check(word);

    /// <summary>
    /// The words a misspelled one may have meant, the one most likely meant
    /// first, single words only, never two: Hunspell's, and those of the word
    /// list close to it (<paramref name="near"/>, <see cref="Close"/>), which
    /// Hunspell may not offer ("hai": ahí). Hunspell orders its own by the
    /// kind of mistake it tried first ("kasa": jasa, lasa, kas, asa, rasa,
    /// nasa, casa…), so all are ordered again: the nearest to what was typed
    /// first (<see cref="Distance"/>, where in Spanish a letter of the same
    /// sound or an h left out is a quarter), and among those as near the
    /// commonest in the word list. So kasa is casa (0.25) before pasa and asa
    /// (1); bamos vamos (0.25) before ambos (2); hai hay (0.25), ay (0.5), ahí
    /// (0.6) before al and ha (1, though commoner); helo hello (0.5) before
    /// held and help (1, though commoner). Hunspell's own time limits keep it short
    /// (13 ms for kasa, 47 ms the first time).
    /// </summary>
    public IReadOnlyList<string> Suggest(string word, Func<string, int> frequency, IEnumerable<string> near) =>
        [.. words.Suggest(word)
            .Where(suggested => !suggested.Contains(' ') && !suggested.Contains('-'))
            .Concat(near)
            .Distinct()
            .Select((suggested, order) => (Word: suggested, Order: order, Distance: Distance(word, suggested)))
            .OrderBy(weighed => weighed.Distance)
            .ThenByDescending(weighed => frequency(weighed.Word))
            .ThenBy(weighed => weighed.Order)
            .Select(weighed => weighed.Word)];

    /// <summary>Whether a word of the list is close enough to a misspelled one to be offered for it: a letter wrong at most, and a sound or an accent besides.</summary>
    public bool Close(string typed, string listed) => Distance(typed, listed, NearDistance) <= NearDistance;

    private const double NearDistance = 1.25;

    /// <summary>
    /// How far a word is from another, in letters wrong (Damerau–Levenshtein,
    /// adjacent letters swapped counting one): the same letter with another
    /// accent, a tenth; a letter written once for twice or twice for once,
    /// half; in Spanish, a letter for one of the same sound
    /// (<see cref="SpanishSounds.Alike"/>) or an h put in or left out, which
    /// Spanish does not sound, a quarter; any other letter changed, put in,
    /// left out or swapped, one.
    /// </summary>
    /// <remarks>
    /// Run over tens of thousands of a word list's words for each word typed
    /// (<see cref="Close"/>), so it makes nothing to throw away: the table's
    /// last three rows alone, on the stack, the letters lowered one by one;
    /// and past <paramref name="most"/> in a whole row, where no nearer can
    /// follow, it stops there. Made anew for each word, it held a phone's
    /// keyboard for seconds while a misspelled word was typed.
    /// </remarks>
    private double Distance(string typed, string meant, double most = double.MaxValue)
    {
        var (a, b) = (typed, meant);
        var size = b.Length + 1;
        Span<double> twoAbove = size <= MostOnStack ? stackalloc double[size] : new double[size];
        Span<double> above = size <= MostOnStack ? stackalloc double[size] : new double[size];
        Span<double> row = size <= MostOnStack ? stackalloc double[size] : new double[size];
        for (var j = 1; j < size; j++)
        {
            above[j] = above[j - 1] + Added(b, j - 1);
        }

        for (var i = 1; i <= a.Length; i++)
        {
            row[0] = above[0] + Added(a, i - 1);
            var least = row[0];
            for (var j = 1; j < size; j++)
            {
                var best = Math.Min(above[j - 1] + Changed(Lower(a, i - 1), Lower(b, j - 1)), Math.Min(above[j] + Added(a, i - 1), row[j - 1] + Added(b, j - 1)));
                if (i > 1 && j > 1 && Lower(a, i - 1) == Lower(b, j - 2) && Lower(a, i - 2) == Lower(b, j - 1))
                {
                    best = Math.Min(best, twoAbove[j - 2] + 1);
                }

                row[j] = best;
                least = Math.Min(least, best);
            }

            if (least > most)
            {
                return least;
            }

            var spare = twoAbove;
            twoAbove = above;
            above = row;
            row = spare;
        }

        return above[b.Length];
    }

    /// <summary>How long a word's table rows may be to stand on the stack; a longer one (a pasted line) is made on the heap.</summary>
    private const int MostOnStack = 128;

    private static char Lower(string word, int at) => char.ToLowerInvariant(word[at]);

    /// <summary>A letter of one word the other lacks: a letter written twice for once or once for twice (helo, hello) half a mistake; in Spanish an h a quarter.</summary>
    private double Added(string word, int at) =>
        spanish && Lower(word, at) == 'h' ? 0.25
        : (at > 0 && Lower(word, at - 1) == Lower(word, at)) || (at + 1 < word.Length && Lower(word, at + 1) == Lower(word, at)) ? 0.5
        : 1;

    private double Changed(char typed, char meant)
    {
        if (typed == meant)
        {
            return 0;
        }

        if (SpanishSounds.Bare(typed) == SpanishSounds.Bare(meant))
        {
            return 0.1;
        }

        return spanish && SpanishSounds.AreAlike(typed, meant) ? 0.25 : 1;
    }
}
