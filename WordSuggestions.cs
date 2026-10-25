using System.IO;
using System.IO.Compression;
using System.Text;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The words offered while a word is typed letter by letter, as Gboard's
/// suggestions, the same on Android and Windows: from the language's AOSP
/// word list (the one swiping reads against), those whose letters start with
/// the letters typed, the accents aside ("cancio" offers "canción"), the
/// commonest first, each spelled as the word list has it and cased as typed.
/// </summary>
public sealed class WordSuggestions
{
    /// <summary>How many words are offered: the toolbar's centre and one on each side, as Gboard's.</summary>
    public const int Offered = 3;

    private readonly StandardLanguage language;

    /// <summary>Each word by its letters alone, in order, with its commonest frequency and its spellings, the commonest first.</summary>
    private readonly string[] letters;
    private readonly int[] frequencies;
    private readonly string[][] spellings;

    private WordSuggestions(StandardLanguage language, string[] letters, int[] frequencies, string[][] spellings)
    {
        this.language = language;
        this.letters = letters;
        this.frequencies = frequencies;
        this.spellings = spellings;
    }

    /// <summary>A language's suggestions, read from its gzipped word list.</summary>
    public static WordSuggestions Read(Stream wordList, StandardLanguage language)
    {
        var forms = new Dictionary<string, List<(string Word, int Frequency)>>();
        using (var reader = new StreamReader(new GZipStream(wordList, CompressionMode.Decompress), Encoding.UTF8))
        {
            while (reader.ReadLine() is { } line)
            {
                if (SwipeWords.Entry(line) is not { } entry || SwipeWords.Letters(entry.Word, language.SwipeLetters) is not { } key)
                {
                    continue;
                }

                if (!forms.TryGetValue(key, out var spelled))
                {
                    forms[key] = spelled = [];
                }

                spelled.Add(entry);
            }
        }

        var ordered = forms.OrderBy(form => form.Key, StringComparer.Ordinal).ToArray();
        return new WordSuggestions(
            language,
            [.. ordered.Select(form => form.Key)],
            [.. ordered.Select(form => form.Value.Max(spelled => spelled.Frequency))],
            [.. ordered.Select(form => form.Value.OrderByDescending(spelled => spelled.Frequency).Select(spelled => spelled.Word).Distinct().ToArray())]);
    }

    /// <summary>The words that start as the typed one does, the commonest first, so many at most, cased as it is: a capital first, or all capitals; none for what is not a word's start.</summary>
    public IReadOnlyList<string> For(string typed, int most = Offered)
    {
        if (typed.Length == 0 || SwipeWords.Letters(typed, language.SwipeLetters) is not { } start)
        {
            return [];
        }

        // The words starting so lie together in the sorted letters: the first of them found by halves, then each taken in turn.
        var from = Array.BinarySearch(letters, start, StringComparer.Ordinal);
        from = from < 0 ? ~from : from;
        var best = new List<int>(most + 1);
        for (var at = from; at < letters.Length && letters[at].StartsWith(start, StringComparison.Ordinal); at++)
        {
            var place = best.FindIndex(kept => frequencies[at] > frequencies[kept]);
            if (place < 0 && best.Count < most)
            {
                best.Add(at);
            }
            else if (place >= 0)
            {
                best.Insert(place, at);
                if (best.Count > most)
                {
                    best.RemoveAt(most);
                }
            }
        }

        var capitals = typed.Length > 1 && typed.All(character => !char.IsLetter(character) || char.IsUpper(character));
        var capital = char.IsUpper(typed[0]);
        return [.. best.Select(at => Cased(spellings[at][0], capital, capitals)).Distinct()];
    }

    /// <summary>Whether the word list has the word, by its letters alone ("cancion" is "canción"'s).</summary>
    public bool Knows(string word) =>
        SwipeWords.Letters(word, language.SwipeLetters) is { } key && Array.BinarySearch(letters, key, StringComparer.Ordinal) >= 0;

    /// <summary>
    /// The list's words close to a typed one, as <paramref name="close"/>
    /// judges them, cased as it is: what a misspelled word may have meant
    /// that a spelling dictionary does not offer ("hai": "ahí"). Only words
    /// within a letter of its length are judged.
    /// </summary>
    public IEnumerable<string> Near(string typed, Func<string, bool> close)
    {
        var length = SwipeWords.Letters(typed, language.SwipeLetters)?.Length ?? typed.Length;
        var capitals = typed.Length > 1 && typed.All(character => !char.IsLetter(character) || char.IsUpper(character));
        var capital = typed.Length > 0 && char.IsUpper(typed[0]);
        for (var at = 0; at < letters.Length; at++)
        {
            if (Math.Abs(letters[at].Length - length) > 1)
            {
                continue;
            }

            foreach (var spelled in spellings[at])
            {
                if (close(spelled))
                {
                    yield return Cased(spelled, capital, capitals);
                }
            }
        }
    }

    /// <summary>How common the word is, by its letters alone; 0 for one the list lacks.</summary>
    public int Frequency(string word) =>
        SwipeWords.Letters(word, language.SwipeLetters) is { } key && Array.BinarySearch(letters, key, StringComparer.Ordinal) is >= 0 and var at ? frequencies[at] : 0;

    internal static string Cased(string word, bool capital, bool capitals) =>
        capitals ? word.ToUpperInvariant()
        : capital ? char.ToUpperInvariant(word[0]) + word[1..]
        : word;
}
