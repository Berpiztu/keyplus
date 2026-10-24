using System.Text.Json;
using Android.Content;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The keyboards of the list, by language as on Windows (a PC's keys differ
/// between them): Estándar (Gboard's) and Estándar PC (a PC's keys: Alt Gr,
/// Ctrl, Alt, ⊞, Esc, F1–F12, the arrows, with the language's design), which
/// the keyboard comes with in every language and which never change, and
/// those saved, kept for every screen, each in the language it was designed
/// in; and the one each screen shows in each language, docked and floating
/// alike (each mode with its own design where it has one), kept by the
/// screen: at first Estándar on a phone and a tablet upright, Estándar PC on
/// a tablet on its side and on DeX.
/// </summary>
public static class Keyboards
{
    public const string StandardId = "standard";
    public const string StandardPcId = "standardPc";
    public const string StandardBase = "standard";
    public const string StandardPcBase = "standardPc";

    /// <summary>Estándar XR, Gboard's on the Galaxy XR (a tablet's keys), listed on a headset alone.</summary>
    public const string StandardXrId = "standardXr";
    public const string StandardXrBase = "standardXr";
    public const string PcBase = "pc";

    /// <summary>What a screen's chosen keyboards are kept under: this, then "." and the language's code.</summary>
    public const string SelectedKey = "keyboardId";

    /// <summary>Before the languages, a screen kept the keyboard it showed docked under this; read as Spanish's.</summary>
    private const string DockedBeforeKey = SelectedKey + ".docked";

    /// <summary>The keyboards saved, kept for every screen.</summary>
    private const string MadeKey = "keyboards";

    /// <summary>Before the list, a screen kept its PC design and whether it showed the standard keyboard; turned into keyboards of the list once.</summary>
    private const string MovedKey = "keyboardsMoved";
    private const string StandardShownKey = "standard";

    /// <summary>A language's keyboards: the two the keyboard comes with, Estándar PC with that language's design, then those saved in it, in the order they were.</summary>
    public static IReadOnlyList<KeyboardEntry> All(Context context, string language) =>
    [
        new(StandardId, context.GetString(Resource.String.keyboard_standard), StandardBase),
        new(StandardPcId, context.GetString(Resource.String.keyboard_standard_pc), StandardPcBase, StandardKeys.PcDesign(language)),
        .. KeyPlusPreferences.IsHeadset(context) ? [new KeyboardEntry(StandardXrId, context.GetString(Resource.String.keyboard_standard_xr), StandardXrBase)] : Array.Empty<KeyboardEntry>(),
        .. Made(context).Where(keyboard => keyboard.LanguageCode == language),
    ];

    /// <summary>The keyboards saved, in every language.</summary>
    public static IReadOnlyList<KeyboardEntry> Saved(Context context) => Made(context);

    /// <summary>Whether a preference changed is a screen's chosen keyboard.</summary>
    public static bool IsSelection(string? key) => key?.StartsWith(SelectedKey, StringComparison.Ordinal) == true;

    /// <summary>Whether a preference changed is the keyboards saved: one designed, renamed or deleted.</summary>
    public static bool IsList(string? key) => key == MadeKey;

    /// <summary>The keyboard a screen shows in a language, docked and floating: the one chosen for it there, at first that screen's own.</summary>
    public static KeyboardEntry Selected(Context context, ISharedPreferences screen, KeyPlusPreferences.Screen kind, string language)
    {
        var id = screen.GetString(KeyOf(language), null)
            ?? (language == StandardKeys.Spanish.Code ? screen.GetString(DockedBeforeKey, null) : null)
            ?? FirstOf(context, kind);
        var all = All(context, language);
        return all.FirstOrDefault(keyboard => keyboard.Id == id) ?? all.First(keyboard => keyboard.Id == FirstOf(context, kind));
    }

    /// <summary>
    /// A language come to keeps the kind of keyboard in sight (Estándar, Estándar PC, Estándar XR): the screen's keyboard
    /// in it where it is of that kind, a saved one too; else the one of that kind the keyboard comes with chosen there.
    /// </summary>
    public static void Carry(Context context, ISharedPreferences screen, KeyPlusPreferences.Screen kind, string language, KeyboardEntry shown)
    {
        if (SameKind(Selected(context, screen, kind, language)) != SameKind(shown))
        {
            Select(screen, language, SameKind(shown));
        }
    }

    /// <summary>A keyboard's kind as a keyboard the keyboard comes with: itself, or the one a saved one was made from.</summary>
    private static string SameKind(KeyboardEntry keyboard) => keyboard.BuiltIn ? keyboard.Id : keyboard.Base switch
    {
        StandardBase => StandardId,
        StandardXrBase => StandardXrId,
        _ => StandardPcId,
    };

    /// <summary>A keyboard chosen for a screen in a language: the keyboard open there made again with it.</summary>
    public static void Select(ISharedPreferences screen, string language, string id) => screen.Edit()!.PutString(KeyOf(language), id)!.Apply();

    /// <summary>A keyboard saved in a language, with its base, its design and its floating one, under a name.</summary>
    public static KeyboardEntry SaveAs(Context context, string name, string from, string? design, string language, string? floatingDesign = null)
    {
        var made = new KeyboardEntry(Guid.NewGuid().ToString("N")[..8], name, from, design, language, FloatingDesign: floatingDesign);
        Keep(context, [.. Made(context), made]);
        return made;
    }

    /// <summary>A name for a keyboard saved as it is designed, the user to rename it in Tema: its own and the time.</summary>
    public static string NewName(Context context, KeyboardEntry from) =>
        context.GetString(Resource.String.saved_as, new Java.Lang.String(from.Name), new Java.Lang.String(DateTime.Now.ToString("dd-MM HH:mm", System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>A saved keyboard deleted: the screens that showed it, in any language, show their first one again there.</summary>
    public static void Delete(Context context, string id)
    {
        Keep(context, [.. Made(context).Where(keyboard => keyboard.Id != id)]);
        foreach (var kind in Enum.GetValues<KeyPlusPreferences.Screen>())
        {
            var screen = KeyPlusPreferences.ForScreen(context, kind);
            foreach (var key in StandardKeys.Languages.Select(language => KeyOf(language.Code)).Append(DockedBeforeKey))
            {
                if (screen.GetString(key, null) == id)
                {
                    screen.Edit()!.Remove(key)!.Apply();
                }
            }
        }
    }

    /// <summary>A saved keyboard's design, kept with it: docked, its design; floating, its floating one.</summary>
    public static void SetDesign(Context context, string id, string? design, bool floating) =>
        Keep(context, [.. Made(context).Select(keyboard => keyboard.Id != id ? keyboard : floating ? keyboard with { FloatingDesign = design } : keyboard with { Design = design })]);

    /// <summary>
    /// Once, the PC designs each screen kept before the list made keyboards
    /// of it, in Spanish, named "My design – phone" and so on, none lost; a
    /// screen that showed PC shows its own.
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

            var made = SaveAs(context, context.GetString(Resource.String.my_design, new Java.Lang.String(ScreenName(context, kind))), PcBase, design, StandardKeys.Spanish.Code);
            if (!screen.GetBoolean(StandardShownKey, true))
            {
                Select(screen, StandardKeys.Spanish.Code, made.Id);
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

    private static string KeyOf(string language) => SelectedKey + "." + language;

    /// <summary>A screen's keyboard until one is chosen: on a headset Gboard's there (Estándar XR); else a PC's keys where the screen is wide, Gboard's elsewhere.</summary>
    private static string FirstOf(Context context, KeyPlusPreferences.Screen kind) =>
        KeyPlusPreferences.IsHeadset(context) ? StandardXrId
        : kind is KeyPlusPreferences.Screen.TabletLandscape or KeyPlusPreferences.Screen.Dex ? StandardPcId
        : StandardId;

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
