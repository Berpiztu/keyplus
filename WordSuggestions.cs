using System.IO;
using System.IO.Compression;
using System.Text;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The words offered while a word is typed letter by letter, as Gboard's
/// suggestions, the same on Android and Windows: from the language's AOSP
/// word list (the one swiping reads against), those the letters typed may
/// start, the accents aside ("cancio" offers "canción"), found without the
/// sentence (<see cref="For"/>), each spelled as the word list has it and
/// cased as typed.
/// </summary>
public sealed class WordSuggestions
{
    /// <summary>How many words are offered: the toolbar's centre and one on each side, as Gboard's.</summary>
    public const int Offered = 3;

    /// <summary>
    /// Whether a word mended differs from the word typed only in its accents (como, cómo; el, él): a space leaves a word
    /// right as typed so, the sentence's close telling whether it is a question (the owner, 2026-11-01).
    /// </summary>
    public static bool OnlyMarks(string typed, string mended)
    {
        // Its capitals alone changed ("pue", "Pue": the model wrote the word after a comma it put): never taken at a space,
        // a sentence's capital being Spelling's own.
        if (!typed.Equals(mended, StringComparison.Ordinal) && typed.Equals(mended, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        static string Plain(string word) => new string([.. word.Normalize(NormalizationForm.FormD)
            .Where(letter => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(letter) != System.Globalization.UnicodeCategory.NonSpacingMark)]).ToLowerInvariant();
        return !typed.Equals(mended, StringComparison.Ordinal) && Plain(typed) == Plain(mended);
    }

    private readonly StandardLanguage language;

    /// <summary>Each word by its letters alone, in order, with its commonest frequency and its spellings, the commonest first.</summary>
    private readonly string[] letters;
    private readonly int[] frequencies;
    private readonly string[][] spellings;
    private readonly Dictionary<char, char[]> keysBeside;
    private readonly Dictionary<char, char[]> sounds;

    private WordSuggestions(StandardLanguage language, string[] letters, int[] frequencies, string[][] spellings)
    {
        this.language = language;
        this.letters = letters;
        this.frequencies = frequencies;
        this.spellings = spellings;
        keysBeside = KeysBeside(language);
        sounds = SoundsOf(language);
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

    /// <summary>
    /// The words the letters typed may be the start of, as Gboard finds them, without the sentence: those starting as
    /// typed, in Spanish those that sound so too (<see cref="SpanishSounds"/>: "hiva", iba; "aora", ahora), and those with
    /// a letter that is a key beside the one typed ("quw", que). In the centre, first, a word at most a letter longer than
    /// typed, the commonest, one with a key beside only where no other is that short; then the others that short starting
    /// as typed ("h": ha, he, never hasta); then the rest, longer ones too, the commonest first, those with a key beside
    /// last (the owner, 2026-11-01). So many at most, cased as typed: a capital
    /// first, or all capitals; none for what is not a word's start.
    /// </summary>
    public IReadOnlyList<string> For(string typed, int most = Offered)
    {
        if (typed.Length == 0 || SwipeWords.Letters(typed, language.SwipeLetters) is not { } start)
        {
            return [];
        }

        var found = new Found(this, start.Length + 1, most);
        Walk(start, 0, 0, 0, letters.Length, Reached.AsTyped, found);
        var capitals = typed.Length > 1 && typed.All(character => !char.IsLetter(character) || char.IsUpper(character));
        var capital = char.IsUpper(typed[0]);
        return [.. found.InOrder().Take(most).Select(at => Cased(spellings[at][0], capital, capitals)).Distinct()];
    }

    /// <summary>
    /// The one word the letters typed can only have meant, what a toolbar's centre in bold promises and a space writes (the
    /// owner, 2026-11-01, as Gboard): none for a word of the list, nor while words start so, for it is not known yet which;
    /// one with only an h left out, in Spanish ("oy": hoy); else, where nothing starts so, the only word that sounds the same
    /// ("hiba": iba), or, none sounding so, the only one a key away ("quw": que). Several, none: it is not clear.
    /// </summary>
    public string? OnlyMeant(string typed)
    {
        if (typed.Length < 2 || SwipeWords.Letters(typed, language.SwipeLetters) is not { } start || Array.BinarySearch(letters, start, StringComparer.Ordinal) >= 0)
        {
            return null;
        }

        if (sounds.Count > 0 && OnlyOf(WithH(start)) is { } withH)
        {
            return Spelled(withH, typed);
        }

        if (!NothingStarts(typed))
        {
            return null;
        }

        var found = new Found(this, start.Length + 1, Offered, keys: false);
        Walk(start, 0, 0, 0, letters.Length, Reached.AsTyped, found);
        if (found.WholeBySound.Count > 0)
        {
            return found.WholeBySound.Count == 1 ? Spelled(found.WholeBySound.First(), typed) : null;
        }

        return OnlyOf(KeyAway(start)) is { } keyAway ? Spelled(keyAway, typed) : null;
    }

    /// <summary>Whether no word of the list starts with the letters typed, the word itself included.</summary>
    public bool NothingStarts(string typed)
    {
        if (SwipeWords.Letters(typed, language.SwipeLetters) is not { } start)
        {
            return true;
        }

        var from = Array.BinarySearch(letters, start, StringComparer.Ordinal);
        from = from < 0 ? ~from : from;
        return from >= letters.Length || !letters[from].StartsWith(start, StringComparison.Ordinal);
    }

    /// <summary>The words offered with the one it is sure of first, the toolbar's centre.</summary>
    public static IReadOnlyList<string> Surely(IReadOnlyList<string> words, string sure) =>
        [.. new[] { sure }.Concat(words.Where(word => !word.Equals(sure, StringComparison.OrdinalIgnoreCase))).Take(Offered)];

    private string Spelled(int at, string typed) =>
        Cased(spellings[at][0], char.IsUpper(typed[0]), typed.Length > 1 && typed.All(character => !char.IsLetter(character) || char.IsUpper(character)));

    /// <summary>The place of the only one of these letters the list has; none where it has none, or several.</summary>
    private int? OnlyOf(IEnumerable<string> forms)
    {
        var found = forms.Select(form => Array.BinarySearch(letters, form, StringComparer.Ordinal)).Where(at => at >= 0).Distinct().ToList();
        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>The letters with an h Spanish does not sound: at the start, or after a vowel ("aora": ahora), never after a c, which is another letter's sound.</summary>
    private static IEnumerable<string> WithH(string start)
    {
        for (var at = 0; at <= start.Length; at++)
        {
            if (at == 0 || "aeiou".Contains(start[at - 1]))
            {
                yield return start.Insert(at, "h");
            }
        }
    }

    /// <summary>The letters with one of them a key beside the one typed.</summary>
    private IEnumerable<string> KeyAway(string start)
    {
        for (var at = 0; at < start.Length; at++)
        {
            foreach (var near in keysBeside.GetValueOrDefault(start[at], []))
            {
                yield return start[..at] + near + start[(at + 1)..];
            }
        }
    }

    /// <summary>How a word's letters were reached from those typed, the nearer first.</summary>
    private enum Reached
    {
        AsTyped,
        BySound,
        ByKey,
    }

    /// <summary>
    /// What a word reached by sound is worth less than one as typed, in the word list's frequencies, which are logarithmic.
    /// Read from the Spanish list on 2026-11-01: at least 15 for "zar" (111, as typed) to stay before "sal" (125, by sound)
    /// for "za"; at most 49 for "hoy" (152) before "oye" (102) for "oy" and "voz" (143) before "BOE" (93) for "bo".
    /// </summary>
    private const int SoundCost = 30;

    /// <summary>
    /// Follows the letters typed down the sorted word list, the range from..to holding the words that start as reached so
    /// far, each step one by halves, so the whole list is never read; a letter's other ways follow the letter's own, so a
    /// word is first reached the nearest way. One letter at most is a key beside.
    /// </summary>
    private void Walk(string typed, int at, int depth, int from, int to, Reached way, Found found)
    {
        if (from >= to)
        {
            return;
        }

        if (at == typed.Length)
        {
            // Nothing but a silent h typed reaches every word: nothing yet.
            for (var word = depth > 0 ? from : to; word < to; word++)
            {
                found.Add(word, way, letters[word].Length == depth);
            }

            return;
        }

        var letter = typed[at];
        Step(letter, way);
        foreach (var alike in sounds.GetValueOrDefault(letter, []))
        {
            Step(alike, Max(way, Reached.BySound));
        }

        if (sounds.Count > 0)
        {
            // Spanish does not sound its h: one typed that the word lacks, or one the word has that was not typed.
            if (letter == 'h')
            {
                Walk(typed, at + 1, depth, from, to, Max(way, Reached.BySound), found);
            }
            else
            {
                var (first, end) = Narrowed(from, to, depth, 'h');
                Walk(typed, at, depth + 1, first, end, Max(way, Reached.BySound), found);
            }
        }

        if (way != Reached.ByKey && found.Keys)
        {
            foreach (var near in keysBeside.GetValueOrDefault(letter, []))
            {
                Step(near, Reached.ByKey);
            }
        }

        void Step(char next, Reached how)
        {
            var (first, end) = Narrowed(from, to, depth, next);
            Walk(typed, at + 1, depth + 1, first, end, how, found);
        }
    }

    private static Reached Max(Reached one, Reached other) => one > other ? one : other;

    /// <summary>The part of a range of words starting alike, sorted, with this letter next, found by halves; a word that ends there sorts before any letter.</summary>
    private (int From, int To) Narrowed(int from, int to, int depth, char letter)
    {
        int FirstFrom(int low, int high, int least)
        {
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                var word = letters[middle];
                if ((depth < word.Length ? word[depth] : -1) < least)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        var first = FirstFrom(from, to, letter);
        return (first, FirstFrom(first, to, letter + 1));
    }

    /// <summary>The words reached, kept as <see cref="For"/> orders them: nothing but the few best is kept, for a letter's words may be tens of thousands.</summary>
    private sealed class Found(WordSuggestions words, int shortUpTo, int most, bool keys = true)
    {
        /// <summary>Whether a letter may be a key beside the one typed.</summary>
        public bool Keys => keys;

        /// <summary>The words reached whole by sound, no letter left over: those that sound as typed.</summary>
        public HashSet<int> WholeBySound { get; } = [];

        private readonly List<(int Word, int Worth)> spoken = new(most + 2);
        private readonly List<(int Word, int Worth)> keyed = new(most + 2);
        private readonly List<(int Word, int Worth)> shortAsTyped = new(most + 2);
        private (int Word, int Worth) shortSpoken = (-1, int.MinValue);
        private (int Word, int Worth) shortKeyed = (-1, int.MinValue);

        public void Add(int word, Reached way, bool whole)
        {
            if (whole && way == Reached.BySound)
            {
                WholeBySound.Add(word);
            }

            var worth = words.frequencies[word] - (way == Reached.BySound ? SoundCost : 0);
            var isShort = words.letters[word].Length <= shortUpTo;
            if (way == Reached.ByKey)
            {
                if (isShort && worth > shortKeyed.Worth)
                {
                    shortKeyed = (word, worth);
                }

                Keep(keyed, word, worth);
            }
            else
            {
                if (isShort && worth > shortSpoken.Worth)
                {
                    shortSpoken = (word, worth);
                }

                if (isShort && way == Reached.AsTyped)
                {
                    Keep(shortAsTyped, word, worth);
                }

                Keep(spoken, word, worth);
            }
        }

        /// <summary>The centre, then the rest, each word once.</summary>
        public IEnumerable<int> InOrder()
        {
            var centre = shortSpoken.Word >= 0 ? shortSpoken.Word : shortKeyed.Word;
            IEnumerable<int> first = centre >= 0 ? [centre] : [];
            return first.Concat(shortAsTyped.Select(kept => kept.Word)).Concat(spoken.Select(kept => kept.Word)).Concat(keyed.Select(kept => kept.Word)).Distinct();
        }

        private void Keep(List<(int Word, int Worth)> best, int word, int worth)
        {
            // One more than asked, for the centre may be among them.
            var room = most + 1;
            if (best.Count == room && worth <= best[^1].Worth)
            {
                return;
            }

            if (best.Exists(kept => kept.Word == word))
            {
                return;
            }

            var place = best.FindIndex(kept => worth > kept.Worth);
            best.Insert(place < 0 ? best.Count : place, (word, worth));
            if (best.Count > room)
            {
                best.RemoveAt(room);
            }
        }
    }

    /// <summary>
    /// The keys beside each letter on the language's letter rows, Gboard's, each row centred on the widest as it lies: those
    /// on its row or the next at most a key's width across (Spanish's "w": q, e, a, s, d).
    /// </summary>
    private static Dictionary<char, char[]> KeysBeside(StandardLanguage language)
    {
        var rows = language.LetterRows;
        var widest = rows.Max(row => row.Length);
        var keys = rows.SelectMany((row, down) => row.Select((letter, across) => (Letter: letter, Down: down, Across: (widest - row.Length) / 2f + across))).ToList();
        return keys.ToDictionary(key => key.Letter, key => keys
            .Where(other => other.Letter != key.Letter && Math.Abs(other.Down - key.Down) <= 1 && Math.Abs(other.Across - key.Across) <= 1)
            .Select(other => other.Letter)
            .ToArray());
    }

    /// <summary>The letters written for each letter in Spanish (<see cref="SpanishSounds.Alike"/>); none in another language.</summary>
    private static Dictionary<char, char[]> SoundsOf(StandardLanguage language) =>
        language.Code != StandardKeys.Spanish.Code
            ? []
            : SpanishSounds.Alike.SelectMany(pair => pair).Distinct().ToDictionary(letter => letter, letter => SpanishSounds.Alike
                .Where(pair => pair.Contains(letter))
                .SelectMany(pair => pair)
                .Where(other => other != letter)
                .Distinct()
                .ToArray());

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

    /// <summary>Whether the list has the word spelled as it is written, its accents too ("canción", not "cancion"), whatever its case.</summary>
    public bool HasSpelled(string word) =>
        SwipeWords.Letters(word, language.SwipeLetters) is { } key && Array.BinarySearch(letters, key, StringComparer.Ordinal) is >= 0 and var at
        && spellings[at].Any(spelled => spelled.Equals(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>How common the word is, by its letters alone; 0 for one the list lacks.</summary>
    public int Frequency(string word) =>
        SwipeWords.Letters(word, language.SwipeLetters) is { } key && Array.BinarySearch(letters, key, StringComparer.Ordinal) is >= 0 and var at ? frequencies[at] : 0;

    internal static string Cased(string word, bool capital, bool capitals) =>
        capitals ? word.ToUpperInvariant()
        : capital ? char.ToUpperInvariant(word[0]) + word[1..]
        : word;
}
