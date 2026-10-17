using Android.Content;
using Android.Views;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Where KeyPlus keeps what it remembers. Whether it was closed is one thing
/// for the whole phone; the window's layout (size, place, Keys or ABC, docked
/// or floating, minimized, the icon's place) is kept apart for each screen —
/// the phone held upright, the phone on its side, and Samsung DeX, a display
/// of its own — so each opens as it was left there.
/// </summary>
public static class KeyPlusPreferences
{
    /// <summary>The preference that says KeyPlus was closed, which the service watches.</summary>
    public const string HiddenKey = "hidden";

    private const string PhoneName = "keyplus";
    private const string DexName = "keyplus.dex";
    private const string PortraitName = "keyplus.portrait";
    private const string LandscapeName = "keyplus.landscape";
    private const string LayoutsSplitKey = "layoutsSplit";
    private const string ScreensSplitKey = "screensSplit";

    /// <summary>The screens a layout is kept for.</summary>
    public enum Screen
    {
        Portrait,
        Landscape,
        Dex,
    }

    /// <summary>What holds for the whole phone.</summary>
    public static ISharedPreferences Global(Context context) =>
        context.GetSharedPreferences(PhoneName, FileCreationMode.Private)!;

    /// <summary>The screen a display is now: DeX for any but the phone's own, else the phone's own the way it is held.</summary>
    public static Screen ScreenOf(Context context, int displayId) =>
        displayId != Display.DefaultDisplay ? Screen.Dex
        : context.Resources!.Configuration!.Orientation == Android.Content.Res.Orientation.Landscape ? Screen.Landscape
        : Screen.Portrait;

    /// <summary>The layout kept for a screen.</summary>
    public static ISharedPreferences ForScreen(Context context, Screen screen) =>
        context.GetSharedPreferences(screen switch
        {
            Screen.Dex => DexName,
            Screen.Landscape => LandscapeName,
            _ => PortraitName,
        }, FileCreationMode.Private)!;

    /// <summary>
    /// Once, when DeX first got a layout of its own: the layout kept until
    /// then was made under DeX, so it moved to DeX's. Then once, when the
    /// phone's screen got one for each way it is held: the phone's layout
    /// goes to both, so neither starts afresh. Whether KeyPlus is closed
    /// stays where it is.
    /// </summary>
    public static void SplitLayoutsOnce(Context context)
    {
        var global = Global(context);
        if (!global.GetBoolean(LayoutsSplitKey, false))
        {
            Move(global, [ForScreen(context, Screen.Dex)]);
            global.Edit()!.PutBoolean(LayoutsSplitKey, true)!.Apply();
        }

        if (!global.GetBoolean(ScreensSplitKey, false))
        {
            Move(global, [ForScreen(context, Screen.Portrait), ForScreen(context, Screen.Landscape)]);
            global.Edit()!.PutBoolean(ScreensSplitKey, true)!.Apply();
        }
    }

    /// <summary>Every layout preference of the phone's own copied to these, and taken from the phone's.</summary>
    private static void Move(ISharedPreferences from, IReadOnlyList<ISharedPreferences> to)
    {
        var editors = to.Select(preferences => preferences.Edit()!).ToList();
        var left = from.Edit()!;
        foreach (var (key, value) in from.All!)
        {
            if (key is HiddenKey or LayoutsSplitKey or ScreensSplitKey)
            {
                continue;
            }

            foreach (var editor in editors)
            {
                _ = value switch
                {
                    bool flag => editor.PutBoolean(key, flag),
                    int number => editor.PutInt(key, number),
                    float number => editor.PutFloat(key, number),
                    Java.Lang.Boolean flag => editor.PutBoolean(key, (bool)flag),
                    Java.Lang.Integer number => editor.PutInt(key, (int)number),
                    Java.Lang.Float number => editor.PutFloat(key, (float)number),
                    _ => editor,
                };
            }

            left.Remove(key);
        }

        editors.ForEach(editor => editor.Apply());
        left.Apply();
    }

    /// <summary>Whether KeyPlus was closed, nothing of it in sight until the app is opened again.</summary>
    public static bool IsHidden(Context context) => Global(context).GetBoolean(HiddenKey, false);

    /// <summary>Closes KeyPlus, or brings it back; written at once, for the app and the service are apart.</summary>
    public static void SetHidden(Context context, bool hidden) =>
        Global(context).Edit()!.PutBoolean(HiddenKey, hidden)!.Apply();
}
