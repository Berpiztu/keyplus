using System.Text.Json;
using Android.Content;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The keyboards of the list: Estándar (Gboard's) and Estándar PC (a PC's
/// keys: Alt Gr, Ctrl, Alt, ⊞, Esc, F1–F12, the arrows), which the keyboard
/// comes with and which never change, and those saved, kept for every
/// screen; and the one each screen shows docked and the one it shows
/// floating, kept by the screen: at first Estándar on a phone and a tablet
/// upright, Estándar PC on a tablet on its side and on DeX.
/// </summary>
public static class Keyboards
{
    public const string StandardId = "standard";
    public const string StandardPcId = "standardPc";
    public const string StandardBase = "standard";
    public const string StandardPcBase = "standardPc";
    public const string PcBase = "pc";

    /// <summary>What a screen's chosen keyboards are kept under: this, then ".docked" or ".floating".</summary>
    public const string SelectedKey = "keyboardId";

    /// <summary>The keyboards saved, kept for every screen.</summary>
    private const string MadeKey = "keyboards";

    /// <summary>Before the list, a screen kept its PC design and whether it showed the standard keyboard; turned into keyboards of the list once.</summary>
    private const string MovedKey = "keyboardsMoved";
    private const string StandardShownKey = "standard";

    /// <summary>Every keyboard: the two the keyboard comes with, then those saved, in the order they were.</summary>
    public static IReadOnlyList<KeyboardEntry> All(Context context) =>
    [
        new(StandardId, context.GetString(Resource.String.keyboard_standard), StandardBase),
        new(StandardPcId, context.GetString(Resource.String.keyboard_standard_pc), StandardPcBase),
        .. Made(context),
    ];

    /// <summary>Whether a preference changed is a screen's chosen keyboard.</summary>
    public static bool IsSelection(string? key) => key?.StartsWith(SelectedKey, StringComparison.Ordinal) == true;

    /// <summary>The keyboard a screen shows docked or floating: the one chosen for it there, at first that screen's own.</summary>
    public static KeyboardEntry Selected(Context context, ISharedPreferences screen, KeyPlusPreferences.Screen kind, bool floating)
    {
        var id = screen.GetString(KeyOf(floating), null) ?? FirstOf(kind);
        var all = All(context);
        return all.FirstOrDefault(keyboard => keyboard.Id == id) ?? all.First(keyboard => keyboard.Id == FirstOf(kind));
    }

    /// <summary>A keyboard chosen for a screen, docked or floating: the keyboard open there made again with it.</summary>
    public static void Select(ISharedPreferences screen, bool floating, string id) => screen.Edit()!.PutString(KeyOf(floating), id)!.Apply();

    /// <summary>A keyboard saved, with its base and its design, under a name.</summary>
    public static KeyboardEntry SaveAs(Context context, string name, string from, string? design)
    {
        var made = new KeyboardEntry(Guid.NewGuid().ToString("N")[..8], name, from, design);
        Keep(context, [.. Made(context), made]);
        return made;
    }

    /// <summary>A name for a keyboard saved as it is designed, the user to rename it in Tema: its own and the time.</summary>
    public static string NewName(Context context, KeyboardEntry from) =>
        context.GetString(Resource.String.saved_as, new Java.Lang.String(from.Name), new Java.Lang.String(DateTime.Now.ToString("dd-MM HH:mm", System.Globalization.CultureInfo.InvariantCulture)));

    public static void Rename(Context context, string id, string name) =>
        Keep(context, [.. Made(context).Select(keyboard => keyboard.Id == id ? keyboard with { Name = name } : keyboard)]);

    /// <summary>A saved keyboard deleted: the screens that showed it, docked or floating, show their first one again there.</summary>
    public static void Delete(Context context, string id)
    {
        Keep(context, [.. Made(context).Where(keyboard => keyboard.Id != id)]);
        foreach (var kind in Enum.GetValues<KeyPlusPreferences.Screen>())
        {
            var screen = KeyPlusPreferences.ForScreen(context, kind);
            foreach (var floating in new[] { false, true })
            {
                if (screen.GetString(KeyOf(floating), null) == id)
                {
                    screen.Edit()!.Remove(KeyOf(floating))!.Apply();
                }
            }
        }
    }

    /// <summary>A saved keyboard's design, kept with it; none, as its base is.</summary>
    public static void SetDesign(Context context, string id, string? design) =>
        Keep(context, [.. Made(context).Select(keyboard => keyboard.Id == id ? keyboard with { Design = design } : keyboard)]);

    /// <summary>
    /// Once, the PC designs each screen kept before the list made keyboards
    /// of it, named "My design – phone" and so on, none lost; a screen that
    /// showed PC shows its own, docked and floating.
    /// </summary>
    public static void MoveDesignsOnce(Context context)
    {
        var global = KeyPlusPreferences.Global(context);
        if (global.GetBoolean(MovedKey, false))
        {
            return;
        }

        foreach (var kind in Enum.GetValues<KeyPlusPreferences.Screen>())
        {
            var screen = KeyPlusPreferences.ForScreen(context, kind);
            if (KeyboardDesign.Kept(screen) is not { } design)
            {
                continue;
            }

            var made = SaveAs(context, context.GetString(Resource.String.my_design, new Java.Lang.String(ScreenName(context, kind))), PcBase, design);
            if (!screen.GetBoolean(StandardShownKey, true))
            {
                Select(screen, floating: false, made.Id);
                Select(screen, floating: true, made.Id);
            }
        }

        global.Edit()!.PutBoolean(MovedKey, true)!.Apply();
    }

    /// <summary>A screen as its name reads.</summary>
    public static string ScreenName(Context context, KeyPlusPreferences.Screen kind) => context.GetString(kind switch
    {
        KeyPlusPreferences.Screen.TabletPortrait => Resource.String.screen_tablet_portrait,
        KeyPlusPreferences.Screen.TabletLandscape => Resource.String.screen_tablet_landscape,
        KeyPlusPreferences.Screen.Dex => Resource.String.screen_dex,
        _ => Resource.String.screen_phone,
    });

    private static string KeyOf(bool floating) => SelectedKey + (floating ? ".floating" : ".docked");

    /// <summary>A screen's keyboard until one is chosen: a PC's keys where the screen is wide, Gboard's elsewhere.</summary>
    private static string FirstOf(KeyPlusPreferences.Screen kind) =>
        kind is KeyPlusPreferences.Screen.TabletLandscape or KeyPlusPreferences.Screen.Dex ? StandardPcId : StandardId;

    private static List<KeyboardEntry> Made(Context context)
    {
        var kept = KeyPlusPreferences.Global(context).GetString(MadeKey, null);
        if (string.IsNullOrEmpty(kept))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<KeyboardEntry>>(kept) ?? [];
        }
        catch (JsonException unreadable)
        {
            Android.Util.Log.Warn("KeyPlus", $"The keyboards saved could not be read: {unreadable.Message}");
            return [];
        }
    }

    private static void Keep(Context context, List<KeyboardEntry> made) =>
        KeyPlusPreferences.Global(context).Edit()!.PutString(MadeKey, JsonSerializer.Serialize(made))!.Apply();
}
