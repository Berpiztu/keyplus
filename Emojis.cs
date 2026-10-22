using Android.Content;
using Android.Graphics;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The emoji the Android keyboard offers: <see cref="EmojiList"/>'s, only
/// those the device draws, read once away from the main thread; and those
/// typed lately, kept for every screen.
/// </summary>
public static class Emojis
{
    private const string RecentKey = "recentEmojis";

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
        return [.. EmojiList.Kinds.Select(kind => new EmojiGroup(context.GetString(kind.Name), kind.Icon, read.GetValueOrDefault(kind.Unicode) ?? []))];
    }

    /// <summary>The emoji typed lately, the latest first.</summary>
    public static IReadOnlyList<string> Recent(Context context) =>
        EmojiList.Recent(KeyPlusPreferences.Global(context).GetString(RecentKey, null));

    /// <summary>An emoji typed: first among those typed lately.</summary>
    public static void Used(Context context, string emoji)
    {
        var kept = KeyPlusPreferences.Global(context);
        kept.Edit()!.PutString(RecentKey, EmojiList.Used(kept.GetString(RecentKey, null), emoji))!.Apply();
    }

    private static Dictionary<string, List<string>> ReadDrawable()
    {
        var paint = new Paint();
        using var reader = new StreamReader(assets!.Assets!.Open(EmojiList.Asset));
        return EmojiList.Read(reader, paint.HasGlyph);
    }
}
