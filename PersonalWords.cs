using System.IO;
using System.Text.Json;

namespace Berpiztu.KeyPlus;

/// <summary>
/// What the keyboard learnt of the one typing, the same on Android and
/// Windows, kept by language in a file of its own on the device and sent
/// nowhere (Settings › Dictionary):
/// <list type="bullet">
/// <item>The personal dictionary: words no word list has (a web domain, a
/// name), added by hand, or learnt by themselves once typed letter by letter
/// <see cref="LearnAfter"/> times; swiping reads them with the word list, and
/// they are offered first as a word is typed.</item>
/// <item>The words chosen for a swipe: a word of the toolbar taken in place of
/// the one a swipe typed is noted for the word read first; the next time a
/// swipe reads that first, the word chosen most often for it is typed
/// instead, so the keyboard follows how its user swipes ("está" over
/// "esta").</item>
/// </list>
/// Every change is read from the file and written back at once, so the
/// keyboard and its settings, apart on Windows, share it.
/// </summary>
public sealed class PersonalWords(string path)
{
    /// <summary>The file's name, in the app's own folder (Android's files, Windows' %APPDATA%\Berpiztu\Keyboard).</summary>
    public const string FileName = "personal-words.json";

    /// <summary>How many times a word no word list has is typed before it is learnt.</summary>
    public const int LearnAfter = 2;

    /// <summary>A personal word's frequency in the swipe's word list (AOSP's run 0 to 255): as common as "también".</summary>
    public const int Frequency = 180;

    /// <summary>The file's content.</summary>
    public sealed class Kept
    {
        /// <summary>Settings › Dictionary › Learn new words as I type.</summary>
        public bool LearnTyped { get; set; } = true;

        /// <summary>Settings › Dictionary › Learn from the words I choose.</summary>
        public bool LearnChosen { get; set; } = true;

        /// <summary>Each language's personal dictionary.</summary>
        public Dictionary<string, List<string>> Words { get; set; } = [];

        /// <summary>Each language's swipes: the word read first, lower case, and how often each word was taken for it.</summary>
        public Dictionary<string, Dictionary<string, Dictionary<string, int>>> Chosen { get; set; } = [];

        /// <summary>Each language's words typed that no word list has, lower case, and how often, until learnt.</summary>
        public Dictionary<string, Dictionary<string, int>> Typed { get; set; } = [];
    }

    private Kept kept = new();

    /// <summary>The file as it is now: read again, as the other side may have changed it. Whether the dictionary's words changed.</summary>
    public bool Reload()
    {
        var before = Signature(kept);
        kept = Read();
        return Signature(kept) != before;
    }

    public bool LearnTyped => kept.LearnTyped;

    public bool LearnChosen => kept.LearnChosen;

    /// <summary>A language's personal dictionary, in order.</summary>
    public IReadOnlyList<string> Words(string language) =>
        kept.Words.TryGetValue(language, out var words) ? [.. words.Order(StringComparer.CurrentCultureIgnoreCase)] : [];

    /// <summary>A word added to the dictionary, by hand or learnt; none for one already in it, or blank.</summary>
    public void Add(string language, string word)
    {
        word = word.Trim();
        if (word.Length == 0 || word.Contains(' '))
        {
            return;
        }

        Change(file =>
        {
            var words = Of(file.Words, language, () => []);
            if (!words.Contains(word, StringComparer.CurrentCultureIgnoreCase))
            {
                words.Add(word);
            }

            Of(file.Typed, language, () => []).Remove(word.ToLowerInvariant());
        });
    }

    public void Remove(string language, string word) => Change(file =>
    {
        if (file.Words.TryGetValue(language, out var words))
        {
            words.RemoveAll(kept => string.Equals(kept, word, StringComparison.CurrentCultureIgnoreCase));
        }
    });

    /// <summary>A word of the dictionary written another way, in its place; a blank one, or one with a space, leaves it as it was.</summary>
    public void Rename(string language, string word, string written)
    {
        written = written.Trim();
        if (written.Length == 0 || written.Contains(' ') || written == word)
        {
            return;
        }

        Change(file =>
        {
            var words = Of(file.Words, language, () => []);
            words.RemoveAll(kept => string.Equals(kept, word, StringComparison.CurrentCultureIgnoreCase)
                || string.Equals(kept, written, StringComparison.CurrentCultureIgnoreCase));
            words.Add(written);
        });
    }

    public void SetLearning(bool typed, bool chosen) => Change(file =>
    {
        file.LearnTyped = typed;
        file.LearnChosen = chosen;
    });

    /// <summary>Every word chosen for a swipe forgotten: swipes type again what they read first.</summary>
    public void ForgetChosen() => Change(file => file.Chosen.Clear());

    /// <summary>
    /// A swipe's words in the order to offer them: the word chosen most often
    /// for the one read first, where it is among them and was chosen more
    /// often than that one, first; the others as read.
    /// </summary>
    public IReadOnlyList<string> Ordered(string language, IReadOnlyList<string> read)
    {
        if (!kept.LearnChosen || read.Count < 2 || !kept.Chosen.TryGetValue(language, out var swipes)
            || !swipes.TryGetValue(read[0].ToLowerInvariant(), out var taken))
        {
            return read;
        }

        var best = read.MaxBy(word => taken.GetValueOrDefault(word.ToLowerInvariant()))!;
        return taken.GetValueOrDefault(best.ToLowerInvariant()) > taken.GetValueOrDefault(read[0].ToLowerInvariant())
            ? [best, .. read.Where(word => word != best)]
            : read;
    }

    /// <summary>A word taken for a swipe that read <paramref name="read"/> first: the toolbar's word taken, or the one read taken back.</summary>
    public void Chose(string language, string read, string word)
    {
        if (kept.LearnChosen)
        {
            Change(file =>
            {
                var taken = Of(Of(file.Chosen, language, () => []), read.ToLowerInvariant(), () => []);
                taken[word.ToLowerInvariant()] = taken.GetValueOrDefault(word.ToLowerInvariant()) + 1;
            });
        }
    }

    /// <summary>
    /// A word typed letter by letter and ended: one no word list has
    /// (<paramref name="known"/>) and the dictionary lacks counted, and learnt
    /// once typed <see cref="LearnAfter"/> times, lower case unless its own
    /// capitals are inside it ("mySaaS"). Whether it was learnt now.
    /// </summary>
    public bool Typed(string language, string word, Func<string, bool> known)
    {
        if (!kept.LearnTyped || word.Length < 2 || known(word) || Words(language).Contains(word, StringComparer.CurrentCultureIgnoreCase))
        {
            return false;
        }

        var spelled = word[1..].Any(char.IsUpper) && !word.All(char.IsUpper) ? word : word.ToLowerInvariant();
        var learnt = false;
        Change(file =>
        {
            var typed = Of(file.Typed, language, () => []);
            var times = typed.GetValueOrDefault(spelled.ToLowerInvariant()) + 1;
            if (times < LearnAfter)
            {
                typed[spelled.ToLowerInvariant()] = times;
                return;
            }

            typed.Remove(spelled.ToLowerInvariant());
            Of(file.Words, language, () => []).Add(spelled);
            learnt = true;
        });
        return learnt;
    }

    /// <summary>The dictionary's words that start as the typed one does, accents aside, cased as it is.</summary>
    public IReadOnlyList<string> Starting(StandardLanguage language, string typed)
    {
        if (SwipeWords.Letters(typed, language.SwipeLetters) is not { } start)
        {
            return [];
        }

        var capitals = typed.Length > 1 && typed.All(character => !char.IsLetter(character) || char.IsUpper(character));
        var capital = char.IsUpper(typed[0]);
        return [.. Words(language.Code)
            .Where(word => SwipeWords.Letters(word, language.SwipeLetters) is { } letters && letters.StartsWith(start, StringComparison.Ordinal) && letters != start)
            .Select(word => WordSuggestions.Cased(word, capital, capitals))];
    }

    private void Change(Action<Kept> change)
    {
        var file = Read();
        change(file);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(file));
        }
        catch (IOException)
        {
            // Busy for a moment (the other side writing): the change stays in memory and goes with the next one.
        }

        kept = file;
    }

    private Kept Read()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Kept>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception unreadable) when (unreadable is JsonException or IOException)
        {
            return kept;
        }
    }

    private static string Signature(Kept file) =>
        string.Join('|', file.Words.OrderBy(language => language.Key).Select(language => $"{language.Key}:{string.Join(',', language.Value.Order())}"));

    private static TValue Of<TValue>(Dictionary<string, TValue> map, string key, Func<TValue> made)
    {
        if (!map.TryGetValue(key, out var value))
        {
            map[key] = value = made();
        }

        return value;
    }
}
