using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The words FUTO Swipe reads against, the same on Android and Windows: a
/// language's AOSP word list written again by each word's letters alone
/// (the trie knows "también" as "tambien"), and the letters read written
/// back as the word list spells them, the commonest spelling first.
/// </summary>
public static class SwipeWords
{
    /// <summary>
    /// A language's AOSP word list, gzipped ("word=…,f=…" lines), written to
    /// a path by each word's letters alone, the accents taken off, at the
    /// commonest frequency among its spellings; the words with a letter the
    /// keyboard has no key for left out. Its spellings by letters, back.
    /// </summary>
    public static Dictionary<string, string[]> WriteLetterForms(Stream wordList, StandardLanguage language, string path)
    {
        var forms = new Dictionary<string, List<(string Word, int Frequency)>>();
        using (var reader = new StreamReader(new GZipStream(wordList, CompressionMode.Decompress), Encoding.UTF8))
        {
            while (reader.ReadLine() is { } line)
            {
                if (Entry(line) is not { } entry || Letters(entry.Word, language.SwipeLetters) is not { } letters)
                {
                    continue;
                }

                if (!forms.TryGetValue(letters, out var spelled))
                {
                    forms[letters] = spelled = [];
                }

                spelled.Add(entry);
            }
        }

        using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
        {
            foreach (var (letters, spelled) in forms)
            {
                writer.Write(" word=");
                writer.Write(letters);
                writer.Write(",f=");
                writer.Write(spelled.Max(form => form.Frequency).ToString(CultureInfo.InvariantCulture));
                writer.Write('\n');
            }
        }

        return forms.ToDictionary(
            form => form.Key,
            form => form.Value.OrderByDescending(spelled => spelled.Frequency).Select(spelled => spelled.Word).Distinct().ToArray());
    }

    /// <summary>The words read, by their letters, spelled as the word list has them, the commonest spelling first, so many at most.</summary>
    public static IReadOnlyList<string> Spelled(IEnumerable<string> read, Dictionary<string, string[]> spellings, int most) =>
        read.SelectMany(letters => spellings.TryGetValue(letters, out var spelled) ? spelled : [letters])
            .Distinct()
            .Take(most)
            .ToList();

    /// <summary>A word list's line, " word=está,f=200,flags=,…": its word and frequency; none for any other line.</summary>
    private static (string Word, int Frequency)? Entry(string line)
    {
        var text = line.AsSpan().TrimStart();
        if (!text.StartsWith("word="))
        {
            return null;
        }

        text = text[5..];
        var comma = text.IndexOf(',');
        if (comma <= 0)
        {
            return null;
        }

        var word = text[..comma].ToString();
        var rest = text[comma..];
        var at = rest.IndexOf(",f=");
        if (at < 0)
        {
            return null;
        }

        var frequency = rest[(at + 3)..];
        var end = frequency.IndexOf(',');
        return int.TryParse(end < 0 ? frequency : frequency[..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? (word, value)
            : null;
    }

    /// <summary>A word by its letters alone, lower case and its accents taken off ("Está" is "esta"); none where it has a letter the keyboard has no key for.</summary>
    private static string? Letters(string word, string keys)
    {
        var letters = new StringBuilder(word.Length);
        foreach (var character in word.ToLowerInvariant())
        {
            var plain = character switch
            {
                'á' => 'a',
                'é' => 'e',
                'í' => 'i',
                'ó' => 'o',
                'ú' or 'ü' => 'u',
                _ => character,
            };
            if (!keys.Contains(plain))
            {
                return null;
            }

            letters.Append(plain);
        }

        return letters.Length > 0 ? letters.ToString() : null;
    }
}
