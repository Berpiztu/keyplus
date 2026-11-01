using System.IO;

namespace Berpiztu.KeyPlus;

/// <summary>A group of the emoji panel: its name, its tab's sign and its emoji.</summary>
public sealed record EmojiGroup(string Name, int Icon, IReadOnlyList<string> Items);

/// <summary>
/// The emoji list both keyboards offer, Android's and Windows': Unicode's
/// (Assets/emoji/emoji-15.1.txt, emoji-test.txt's fully-qualified emoji
/// without skin tones, a group to a line), its groups in Unicode's order,
/// each with its name's and its tab's ids; and the emoji typed lately, kept
/// as one line, the latest first.
/// </summary>
public static class EmojiList
{
    public const string Asset = "emoji/emoji-15.1.txt";

    /// <summary>The most kept as typed lately: Gboard's four rows of eight.</summary>
    public const int MostRecent = 32;

    /// <summary>Unicode's groups, in its order, as the panel names them and signs their tabs.</summary>
    public static readonly (string Unicode, int Name, int Icon)[] Kinds =
    [
        ("Smileys & Emotion", Resource.String.emoji_smileys, Resource.Drawable.ic_key_emoji),
        ("People & Body", Resource.String.emoji_people, Resource.Drawable.ic_emoji_people),
        ("Animals & Nature", Resource.String.emoji_nature, Resource.Drawable.ic_emoji_nature),
        ("Food & Drink", Resource.String.emoji_food, Resource.Drawable.ic_emoji_food),
        ("Travel & Places", Resource.String.emoji_travel, Resource.Drawable.ic_emoji_travel),
        ("Activities", Resource.String.emoji_activities, Resource.Drawable.ic_emoji_activities),
        ("Objects", Resource.String.emoji_objects, Resource.Drawable.ic_emoji_objects),
        ("Symbols", Resource.String.emoji_symbols, Resource.Drawable.ic_emoji_symbols),
        ("Flags", Resource.String.emoji_flags, Resource.Drawable.ic_emoji_flags),
    ];

    /// <summary>The list read, each Unicode group with its emoji, those a drawer can draw.</summary>
    public static Dictionary<string, List<string>> Read(TextReader reader, Func<string, bool> drawn)
    {
        var groups = new Dictionary<string, List<string>>();
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('#') || line.Split('\t') is not [var name, var items])
            {
                continue;
            }

            groups[name] = [.. items.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(drawn)];
        }

        return groups;
    }

    /// <summary>A language's emoji names (Unicode CLDR's), for the search: es or en, English for any other.</summary>
    public static string NamesAsset(string language) => $"emoji/emoji-names-{(language == "es" ? "es" : "en")}.txt";

    /// <summary>Each emoji's words as the search compares them (<see cref="Fold"/>): its name's first, then its other words'.</summary>
    public static Dictionary<string, string[]> ReadNames(TextReader reader)
    {
        var names = new Dictionary<string, string[]>();
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('#') || line.Split('\t') is not [var emoji, var name, .. var others])
            {
                continue;
            }

            names[emoji] = [Fold(name), .. others.Select(Fold)];
        }

        return names;
    }

    /// <summary>
    /// The emoji a search finds, in the list's order: each word typed begins a word of the emoji's name or of its other
    /// words, accents and capitals left out ("cora roj" finds ❤️); those whose name begins as typed first, then those whose
    /// name has every word typed, then those found by their other words.
    /// </summary>
    public static IReadOnlyList<string> Search(IEnumerable<string> emoji, IReadOnlyDictionary<string, string[]> names, string typed)
    {
        var query = Fold(typed).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (query.Length == 0)
        {
            return [];
        }

        var whole = string.Join(' ', query);
        static bool Begins(IEnumerable<string> texts, string word) => texts.SelectMany(text => text.Split(' ')).Any(part => part.StartsWith(word, StringComparison.Ordinal));
        var found = new List<(string Emoji, int Rank)>();
        foreach (var item in emoji)
        {
            if (!names.TryGetValue(item, out var words))
            {
                continue;
            }

            var rank = words[0].StartsWith(whole, StringComparison.Ordinal) ? 0
                : query.All(word => Begins([words[0]], word)) ? 1
                : query.All(word => Begins(words, word)) ? 2
                : -1;
            if (rank >= 0)
            {
                found.Add((item, rank));
            }
        }

        return [.. found.OrderBy(match => match.Rank).Select(match => match.Emoji)];
    }

    /// <summary>A text as the search compares it: small letters, without accents ("Corazón" is "corazon").</summary>
    public static string Fold(string text)
    {
        var plain = new System.Text.StringBuilder();
        foreach (var letter in text.Normalize(System.Text.NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(letter) != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                plain.Append(char.ToLowerInvariant(letter));
            }
        }

        return plain.ToString();
    }

    /// <summary>The emoji typed lately, from the line they are kept as.</summary>
    public static IReadOnlyList<string> Recent(string? kept) => kept?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

    /// <summary>The line kept once an emoji is typed: it first, those before it after, so many at most.</summary>
    public static string Used(string? kept, string emoji) =>
        string.Join(' ', Recent(kept).Where(earlier => earlier != emoji).Prepend(emoji).Take(MostRecent));
}
