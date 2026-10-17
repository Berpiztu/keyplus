using Android.Content;
using Android.Views;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Where KeyPlus keeps what it remembers. Whether it was closed is one thing
/// for the whole phone; the window's layout (size, place, Keys or ABC, docked
/// or floating, minimized, the icon's place) is kept apart for each screen —
/// the phone's own screen as a phone (a Fold closed), the same screen as a
/// tablet (a Fold open) held upright and on its side, and Samsung DeX, a
/// display of its own — so each opens as it was left there.
/// </summary>
public static class KeyPlusPreferences
{
    /// <summary>The preference that says KeyPlus was closed, which the service watches.</summary>
    public const string HiddenKey = "hidden";

    /// <summary>The least width, in dp, of a screen Android takes for a tablet: a Fold open is one, closed it is not.</summary>
    private const int TabletWidthDp = 600;

    private const string PhoneName = "keyplus";
    private const string DexName = "keyplus.dex";
    private const string HandsetName = "keyplus.phone";
    private const string TabletPortraitName = "keyplus.tablet.portrait";
    private const string TabletLandscapeName = "keyplus.tablet.landscape";
    private const string PortraitName = "keyplus.portrait";
    private const string LandscapeName = "keyplus.landscape";
    private const string LayoutsSplitKey = "layoutsSplit";
    private const string ScreensSplitKey = "screensSplit";
    private const string TabletSplitKey = "tabletSplit";

    /// <summary>The screens a layout is kept for.</summary>
    public enum Screen
    {
        /// <summary>The phone's own screen as a phone, whichever way it is held.</summary>
        Phone,

        /// <summary>The phone's own screen as a tablet (a Fold open), held upright.</summary>
        TabletPortrait,

        /// <summary>The same, on its side.</summary>
        TabletLandscape,

        /// <summary>Samsung DeX, or any display but the phone's own.</summary>
        Dex,
    }

    /// <summary>What holds for the whole phone.</summary>
    public static ISharedPreferences Global(Context context) =>
        context.GetSharedPreferences(PhoneName, FileCreationMode.Private)!;

    /// <summary>
    /// The screen a display is now: DeX for any but the phone's own; else a
    /// tablet where its narrower side is a tablet's (a Fold open), upright or
    /// on its side; else the phone.
    /// </summary>
    public static Screen ScreenOf(Context context, int displayId)
    {
        if (displayId != Display.DefaultDisplay)
        {
            return Screen.Dex;
        }

        var configuration = context.Resources!.Configuration!;
        if (configuration.SmallestScreenWidthDp < TabletWidthDp)
        {
            return Screen.Phone;
        }

        return configuration.Orientation == Android.Content.Res.Orientation.Landscape ? Screen.TabletLandscape : Screen.TabletPortrait;
    }

    /// <summary>The layout kept for a screen.</summary>
    public static ISharedPreferences ForScreen(Context context, Screen screen) =>
        context.GetSharedPreferences(screen switch
        {
            Screen.Dex => DexName,
            Screen.TabletPortrait => TabletPortraitName,
            Screen.TabletLandscape => TabletLandscapeName,
            _ => HandsetName,
        }, FileCreationMode.Private)!;

    /// <summary>
    /// Once, when DeX first got a layout of its own: the layout kept until
    /// then was made under DeX, so it moved to DeX's. Once, when the phone's
    /// screen got one for each way it is held: the phone's layout went to
    /// both. Once, when the phone was told apart from the tablet: the upright
    /// layout becomes the phone's and the tablet's upright one, the one on its
    /// side the tablet's on its side, so none starts afresh. Whether KeyPlus
    /// is closed stays where it is.
    /// </summary>
    public static void SplitLayoutsOnce(Context context)
    {
        var global = Global(context);
        var portrait = context.GetSharedPreferences(PortraitName, FileCreationMode.Private)!;
        var landscape = context.GetSharedPreferences(LandscapeName, FileCreationMode.Private)!;
        if (!global.GetBoolean(LayoutsSplitKey, false))
        {
            Copy(global, [ForScreen(context, Screen.Dex)], clear: true);
            global.Edit()!.PutBoolean(LayoutsSplitKey, true)!.Apply();
        }

        if (!global.GetBoolean(ScreensSplitKey, false))
        {
            Copy(global, [portrait, landscape], clear: true);
            global.Edit()!.PutBoolean(ScreensSplitKey, true)!.Apply();
        }

        if (!global.GetBoolean(TabletSplitKey, false))
        {
            Copy(portrait, [ForScreen(context, Screen.Phone), ForScreen(context, Screen.TabletPortrait)], clear: false);
            Copy(landscape, [ForScreen(context, Screen.TabletLandscape)], clear: false);
            global.Edit()!.PutBoolean(TabletSplitKey, true)!.Apply();
        }
    }

    /// <summary>Every layout preference of one copied to the others; taken from it where <paramref name="clear"/>.</summary>
    private static void Copy(ISharedPreferences from, IReadOnlyList<ISharedPreferences> to, bool clear)
    {
        var editors = to.Select(preferences => preferences.Edit()!).ToList();
        var left = from.Edit()!;
        foreach (var (key, value) in from.All!)
        {
            if (key is HiddenKey or LayoutsSplitKey or ScreensSplitKey or TabletSplitKey)
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

            if (clear)
            {
                left.Remove(key);
            }
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
