using System.IO;
using System.Text.Json;

namespace Berpiztu.KeyPlus;

/// <summary>
/// What the keyboard learnt of the one typing, the same on Android and
/// Windows, kept by language in a file of its own on the device and sent
/// nowhere (Settings › Dictionary):
/// <list type="bullet">
/// <item>The personal dictionary: words no word list has (a web domain, a
/// name), added by hand, or learnt from the toolbar, where a word being typed
/// that no list has is offered at the left and a tap learns it; nothing is
/// learnt by itself. Swiping reads them with the word list, and they are
/// offered first as a word is typed.</item>
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

    /// <summary>
    /// Where the e-mail addresses are kept, every language's alike, the
    /// owner's: one typed or added in any language goes here, and one an
    /// older file kept in a language's list is moved here as it is read.
    /// </summary>
    public const string Shared = "all";

    /// <summary>Whether a word is an e-mail address, kept with every language's (<see cref="Shared"/>).</summary>
    public static bool IsAddress(string word) => word.Contains('@');

    /// <summary>The e-mail addresses, every language's.</summary>
    public IReadOnlyList<string> Addresses => Words(Shared);

    /// <summary>The e-mail addresses that start as what is typed, whatever its case.</summary>
    public IReadOnlyList<string> AddressesStarting(string typed) =>
        [.. Addresses.Where(address => IsAddress(address) && address.StartsWith(typed, StringComparison.OrdinalIgnoreCase))];

    /// <summary>
    /// A word's suggestions with the addresses that start as it, as Gboard's
    /// in any field ("name": name@example.com): after the first, the
    /// one in the toolbar's centre, so beside it; from two letters on.
    /// </summary>
    public IReadOnlyList<string> WithAddresses(IReadOnlyList<string> words, string typed, int most) =>
        typed.Length < 2 || AddressesStarting(typed) is not { Count: > 0 } addresses
            ? words
            : [.. words.Take(1).Concat(addresses).Concat(words.Skip(1)).Distinct().Take(most)];

    /// <summary>An address's type in a toolbar, a share of a word's, the owner's: a little smaller, its two lines clear in a word's room.</summary>
    public const double AddressTypeShare = 0.7;

    /// <summary>An address as a toolbar shows it, in two lines in a word's room, as Gboard's: its name, then from its @.</summary>
    public static string InTwoLines(string address) => address.IndexOf('@') is > 0 and var at ? $"{address[..at]}\n{address[at..]}" : address;

    /// <summary>A personal word's frequency in the swipe's word list (AOSP's run 0 to 255): as common as "también".</summary>
    public const int Frequency = 180;

    /// <summary>The file's content.</summary>
    public sealed class Kept
    {
        /// <summary>Settings › Dictionary › Learn new words as I type: a word being typed that no list has offered at the toolbar's left, a tap learning it.</summary>
        public bool LearnTyped { get; set; } = true;

        /// <summary>Settings › Dictionary › Learn from the words I choose.</summary>
        public bool LearnChosen { get; set; } = true;

        /// <summary>Each language's personal dictionary, and every language's e-mail addresses (<see cref="Shared"/>).</summary>
        public Dictionary<string, List<string>> Words { get; set; } = [];

        /// <summary>Each language's swipes: the word read first, lower case, and how often each word was taken for it.</summary>
        public Dictionary<string, Dictionary<string, Dictionary<string, int>>> Chosen { get; set; } = [];
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

        language = IsAddress(word) ? Shared : language;

        Change(file =>
        {
            var words = Of(file.Words, language, () => []);
            if (!words.Contains(word, StringComparer.CurrentCultureIgnoreCase))
            {
                words.Add(word);
            }

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
            Of(file.Words, language, () => []).RemoveAll(kept => string.Equals(kept, word, StringComparison.CurrentCultureIgnoreCase));
            var words = Of(file.Words, IsAddress(written) ? Shared : language, () => []);
            words.RemoveAll(kept => string.Equals(kept, written, StringComparison.CurrentCultureIgnoreCase));
            words.Add(written);
        });
    }

    public void SetLearning(bool typed, bool chosen) => Change(file =>
    {
        file.LearnTyped = typed;
        file.LearnChosen = chosen;
    });

    /// <summary>What an exported dictionary says it is, so an import knows it.</summary>
    public const string ExportKind = "berpiztu-dictionary";

    private sealed record ExportFile(string Kind, int Version, DateTime Exported, Dictionary<string, List<string>> Words);

    /// <summary>The dictionary as a file the keyboard of another device imports: every language's words; what was chosen for swipes stays here.</summary>
    public string Export() => Export(null);

    /// <summary>One list alone as such a file: the e-mail addresses (<see cref="Shared"/>), to take them to another device; none, every language's and the addresses.</summary>
    public string Export(string? only)
    {
        Reload();
        var words = only is null ? kept.Words : new Dictionary<string, List<string>> { [only] = [.. Words(only)] };
        return JsonSerializer.Serialize(new ExportFile(ExportKind, 1, DateTime.Now, words), new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>How many words an export holds.</summary>
    public int Count => kept.Words.Values.Sum(words => words.Count);

    /// <summary>
    /// A file imported, its words added to those there and none taken away:
    /// a dictionary exported here, every language; Gboard's (a word, its
    /// shortcut and its locale a line, "es-ES" put in Spanish), or a text with
    /// a word a line, in <paramref name="language"/>. How many were new.
    /// </summary>
    public int Import(string text, string language)
    {
        Reload();
        var before = Count;
        Dictionary<string, List<string>> words = [];
        if (text.TrimStart().StartsWith('{'))
        {
            var file = JsonSerializer.Deserialize<ExportFile>(text);
            if (file?.Kind != ExportKind)
            {
                throw new InvalidDataException("It is not a dictionary exported by the keyboard.");
            }

            words = file.Words;
        }
        else
        {
            foreach (var line in text.Split('\n'))
            {
                var columns = line.Trim().Split('\t');
                if (columns[0].Length == 0 || columns[0].StartsWith('#'))
                {
                    continue;
                }

                var code = columns.Length > 2 && columns[2].Length >= 2 && StandardKeys.Languages.Any(known => known.Code == columns[2][..2].ToLowerInvariant())
                    ? columns[2][..2].ToLowerInvariant()
                    : language;
                Of(words, code, () => []).Add(columns[0]);
            }
        }

        Change(file =>
        {
            foreach (var (code, added) in words)
            {
                // Imported as the addresses, a file's addresses alone.
                foreach (var word in added.Select(word => word.Trim()).Where(word => word.Length > 0 && !word.Contains(' ') && (language != Shared || IsAddress(word))))
                {
                    var kept = Of(file.Words, IsAddress(word) ? Shared : code, () => []);
                    if (!kept.Contains(word, StringComparer.CurrentCultureIgnoreCase))
                    {
                        kept.Add(word);
                    }
                }
            }
        });
        return Count - before;
    }

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
    /// A word typed letter by letter, taken from the toolbar's left to be
    /// learnt: lower case unless its own capitals are inside it ("mySaaS"),
    /// as a word with a capital only for starting a sentence is not one.
    /// </summary>
    public void Learn(string language, string word) =>
        Add(language, word[1..].Any(char.IsUpper) && !word.All(char.IsUpper) ? word : word.ToLowerInvariant());

    /// <summary>The dictionary's words that start as the typed one does, accents aside, and in Spanish as it sounds too ("mis": mysaasplace, <see cref="SpanishSounds.Key"/>), cased as it is.</summary>
    public IReadOnlyList<string> Starting(StandardLanguage language, string typed)
    {
        if (SwipeWords.Letters(typed, language.SwipeLetters) is not { } start)
        {
            return [];
        }

        var capitals = typed.Length > 1 && typed.All(character => !char.IsLetter(character) || char.IsUpper(character));
        var capital = char.IsUpper(typed[0]);
        var sounds = language.Code == StandardKeys.Spanish.Code ? SpanishSounds.Key(typed) : null;
        return [.. Words(language.Code)
            .Where(word => SwipeWords.Letters(word, language.SwipeLetters) is { } letters && letters != start
                && (letters.StartsWith(start, StringComparison.Ordinal) || (sounds is not null && SpanishSounds.Key(word).StartsWith(sounds, StringComparison.Ordinal))))
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
            return Gathered(File.Exists(path) ? JsonSerializer.Deserialize<Kept>(File.ReadAllText(path)) ?? new() : new());
        }
        catch (Exception unreadable) when (unreadable is JsonException or IOException)
        {
            return kept;
        }
    }

    /// <summary>The e-mail addresses a language's list holds (kept so before they were every language's) moved to <see cref="Shared"/>.</summary>
    private static Kept Gathered(Kept file)
    {
        foreach (var (language, words) in file.Words.Where(language => language.Key != Shared).ToList())
        {
            foreach (var address in words.Where(IsAddress).ToList())
            {
                words.Remove(address);
                var shared = Of(file.Words, Shared, () => []);
                if (!shared.Contains(address, StringComparer.CurrentCultureIgnoreCase))
                {
                    shared.Add(address);
                }
            }
        }

        return file;
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
