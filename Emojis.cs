using Android.Content;
using Android.Graphics;

namespace Berpiztu.KeyPlus;

/// <summary>A group of the emoji panel: its name, its tab's sign and its emoji.</summary>
public sealed record EmojiGroup(string Name, int Icon, IReadOnlyList<string> Items);

/// <summary>
/// The emoji the keyboard offers: Unicode's list (Assets/emoji/emoji-15.1.txt,
/// emoji-test.txt's fully-qualified emoji without skin tones, by group),
/// only those the device draws, read once away from the main thread; and
/// those typed lately, the latest first, kept for every screen.
/// </summary>
public static class Emojis
{
    private const string Asset = "emoji/emoji-15.1.txt";
    private const string RecentKey = "recentEmojis";

    /// <summary>The most kept as typed lately: Gboard's four rows of eight.</summary>
    private const int MostRecent = 32;

    /// <summary>Unicode's groups, in its order, as the panel names them and signs their tabs.</summary>
    private static readonly (string Unicode, int Name, int Icon)[] Kinds =
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

    private static readonly Lazy<Task<Dictionary<string, List<string>>>> Read = new(() => Task.Run(ReadDrawable));
    private static Context? assets;

    /// <summary>Starts reading the list, so the panel opens at once.</summary>
    public static void Prepare(Context context)
    {
        assets ??= context.ApplicationContext;
        _ = Read.Value;
    }

    /// <summary>The groups, in Unicode's order, each with the emoji the device draws.</summary>
    public static IReadOnlyList<EmojiGroup> Groups(Context context)
    {
        Prepare(context);
        var read = Read.Value.GetAwaiter().GetResult();
        return [.. Kinds.Select(kind => new EmojiGroup(context.GetString(kind.Name), kind.Icon, read.GetValueOrDefault(kind.Unicode) ?? []))];
    }

    /// <summary>The emoji typed lately, the latest first.</summary>
    public static IReadOnlyList<string> Recent(Context context) =>
        KeyPlusPreferences.Global(context).GetString(RecentKey, null)?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

    /// <summary>An emoji typed: first among those typed lately.</summary>
    public static void Used(Context context, string emoji)
    {
        var recent = Recent(context).Where(kept => kept != emoji).Prepend(emoji).Take(MostRecent);
        KeyPlusPreferences.Global(context).Edit()!.PutString(RecentKey, string.Join(' ', recent))!.Apply();
    }

    private static Dictionary<string, List<string>> ReadDrawable()
    {
        var paint = new Paint();
        var groups = new Dictionary<string, List<string>>();
        using var reader = new StreamReader(assets!.Assets!.Open(Asset));
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('#') || line.Split('\t') is not [var name, var items])
            {
                continue;
            }

            groups[name] = [.. items.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(paint.HasGlyph)];
        }

        return groups;
    }
}
