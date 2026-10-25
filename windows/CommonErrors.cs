using System.IO;
using System.Text;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Spanish words people commonly write for others, and what they meant
/// ("hai": hay, ahí, ay; "as": has, haz): the keyboard's own short list
/// (common-errors.txt, carried) and, where Spelling turns it on, the Spanish
/// Wikipedia's (Wikipedia:Corrector ortográfico/Listado, the list its bots
/// correct with, 21,169 entries), downloaded once. Both in the Wikipedia's
/// format: " written|parameter|meant (why), meant", cs matching the case too.
/// </summary>
public sealed class CommonErrors
{
    /// <summary>The Wikipedia's list: %LOCALAPPDATA%\Berpiztu\Keyboard\wikipedia (listado.txt).</summary>
    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Berpiztu", "Keyboard", "wikipedia");

    /// <summary>The page's revision of 2026-10-05, its text as written; checked by its SHA-256 once downloaded (0.6 MB).</summary>
    private const string Revision = "175669831";

    private static readonly Downloads.Item[] Files =
    [
        new($"https://es.wikipedia.org/w/index.php?title=Wikipedia:Corrector_ortogr%C3%A1fico/Listado&action=raw&oldid={Revision}",
            "listado.txt", "8f786c0046518012c11125cd3b96ecbebe43952de436567d18e3ab4a3a0db605"),
    ];

    /// <summary>The keyboard's own list, beside the app.</summary>
    private static readonly string Own = Path.Combine(AppContext.BaseDirectory, "spelling", "common-errors.txt");

    /// <summary>Whether the Wikipedia's list is on this computer.</summary>
    public static bool Downloaded => Downloads.Present(Folder, Files);

    /// <summary>When the Wikipedia's list was last written: downloaded again, the keyboard reads it again.</summary>
    public static DateTime Stamp => Downloads.Stamp(Folder, Files);

    /// <summary>The Wikipedia's list fetched at its revision (<see cref="Downloads"/>); how much is done, told as it goes.</summary>
    public static async Task DownloadAsync(IProgress<(long Done, long Total)> progress, CancellationToken cancel = default)
    {
        await Downloads.FilesAsync(Folder, Files, progress, cancel);
        Log.Write("Spelling: the Wikipedia's common misspellings downloaded");
    }

    /// <summary>Each word written, the words meant: those matched whatever their case, and those (cs) only as written.</summary>
    private readonly Dictionary<string, List<string>> anyCase = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> exactCase = new(StringComparer.Ordinal);

    /// <summary>The keyboard's own list and, where asked for and downloaded, the Wikipedia's after it; a list that cannot be read is left out, in the log.</summary>
    public static CommonErrors Load(bool wikipedia)
    {
        var errors = new CommonErrors();
        errors.Read(Own);
        if (wikipedia && Downloaded)
        {
            errors.Read(Path.Combine(Folder, Files[0].Name));
        }

        return errors;
    }

    /// <summary>The words the typed one may have meant, the likeliest first, cased as it is typed where its entry ignores the case; none where it is not a common error.</summary>
    public IReadOnlyList<string> For(string typed)
    {
        if (exactCase.TryGetValue(typed, out var exact))
        {
            return [.. exact.Where(meant => meant != typed)];
        }

        if (!anyCase.TryGetValue(typed, out var meanings))
        {
            return [];
        }

        var capitals = typed.Length > 1 && typed.All(character => !char.IsLetter(character) || char.IsUpper(character));
        var capital = char.IsUpper(typed[0]);
        return [.. meanings.Select(meant => WordSuggestions.Cased(meant, capital, capitals)).Where(meant => !meant.Equals(typed, StringComparison.OrdinalIgnoreCase)).Distinct()];
    }

    private void Read(string file)
    {
        try
        {
            foreach (var line in File.ReadLines(file, Encoding.UTF8))
            {
                Add(line);
            }
        }
        catch (IOException failure)
        {
            Log.Write($"Spelling: a list of common misspellings could not be read: {failure.Message}");
        }
    }

    /// <summary>An entry, a line starting with a space: what it adds to an entry already read comes after it.</summary>
    private void Add(string line)
    {
        if (!line.StartsWith(' ') || line.Split('|') is not [var written, var parameter, var meant])
        {
            return;
        }

        written = written.Trim();
        var words = Meanings(meant);
        if (written.Length == 0 || words.Count == 0)
        {
            return;
        }

        var into = parameter.Trim() == "cs" ? exactCase : anyCase;
        if (!into.TryGetValue(written, out var kept))
        {
            into[written] = kept = [];
        }

        kept.AddRange(words.Where(word => !kept.Contains(word)));
    }

    /// <summary>The words an entry gives, without what explains them: its comments, and each one's reason in brackets.</summary>
    private static List<string> Meanings(string meant)
    {
        var plain = new StringBuilder();
        var depth = 0;
        for (var at = 0; at < meant.Length; at++)
        {
            if (string.CompareOrdinal(meant, at, "<!--", 0, 4) == 0)
            {
                var end = meant.IndexOf("-->", at, StringComparison.Ordinal);
                at = end < 0 ? meant.Length : end + 2;
                continue;
            }

            var character = meant[at];
            depth += character == '(' ? 1 : character == ')' && depth > 0 ? -1 : 0;
            if (depth == 0 && character != ')')
            {
                plain.Append(character);
            }
        }

        return [.. plain.ToString().Split(',').Select(word => word.Trim()).Where(word => word.Length > 0)];
    }
}
