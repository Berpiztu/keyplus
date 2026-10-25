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
    /// <summary>
    /// The keyboard's settings, one for every screen, as the app's settings
    /// set them (Preferencias, Escritura deslizando el dedo): their keys and
    /// Gboard's defaults.
    /// </summary>
    public const string VibrateKey = "vibrate";
    public const string SoundKey = "keySound";
    public const string UpdatesAutomaticKey = "updatesAutomatic";
    public const string UpdatesCheckedKey = "updatesChecked";
    public const string LongPressKey = "longPressMs";
    public const int LongPressDefault = 300;
    public const string SwipeTypingKey = "swipeTyping";
    public const string SwipeTrailKey = "swipeTrail";
    public const string SwipeDeleteWordKey = "swipeDeleteWord";

    /// <summary>Dock the floating keyboard dragged to the bottom, float the docked one dragged up (Gboard docks; the owner wants both or neither): off unless turned on.</summary>
    public const string DockByDragKey = "dockByDrag";

    /// <summary>A keyboard in a window moves out of the way of the caret it covers (the owner's, thought for Windows too): on unless turned off.</summary>
    public const string AvoidCaretKey = "avoidCaret";

    /// <summary>
    /// On a headset (Galaxy XR), whether the keyboard is the headset's own,
    /// its keys filling the box the headset gives a keyboard; off (the
    /// default), it is the phone's keyboard, as any Android keyboard is.
    /// </summary>
    public const string HeadsetBoxKey = "headsetBox";

    /// <summary>The system feature of Android XR's spatial windows: a headset.</summary>
    public const string XrSpatialFeature = "android.software.xr.api.spatial";

    /// <summary>Whether this device is a headset (Galaxy XR).</summary>
    public static bool IsHeadset(Context context) => context.PackageManager?.HasSystemFeature(XrSpatialFeature) == true;

    /// <summary>Whether Berpiztu AI Keyboard is the headset's own here: a headset, its box asked for in Preferencias.</summary>
    public static bool HeadsetKeyboard(Context context) => IsHeadset(context) && Global(context).GetBoolean(HeadsetBoxKey, false);

    /// <summary>Apartarse también de los menús, as on Windows: on by default, seen where the menu watch (MenuWatchService) is turned on.</summary>
    public const string AvoidMenusKey = "avoidMenus";

    /// <summary>The keyboard shows when a field is typed in: on unless turned off, for when another keyboard is in use and one covered the other.</summary>
    public const string ShowKeyboardKey = "showKeyboard";

    /// <summary>With the keyboard not shown, its ⌨ icon shows in its place, which opens it when tapped: on unless turned off, then nothing shows.</summary>
    public const string KeyboardIconKey = "keyboardIcon";

    /// <summary>
    /// What the keyboard does as its field lets it go (Enter, the focus
    /// elsewhere): hides (Android's, by default), becomes its ⌨ icon, or
    /// stays on screen; ∨ and Back hide it all the same.
    /// </summary>
    public const string FocusLostKey = "focusLost";
    public const string FocusLostHide = "hide";
    public const string FocusLostIcon = "icon";
    public const string FocusLostStay = "stay";
    public static readonly string[] FocusLostChoices = [FocusLostHide, FocusLostIcon, FocusLostStay];

    /// <summary>The standard keyboard's language, kept for every screen: 🌐 changes it, and with it the keyboard each screen shows in it.</summary>
    public const string LanguageKey = "standardLanguage";

    public static StandardLanguage Language(Context context) => StandardKeys.LanguageOf(Global(context).GetString(LanguageKey, null));

    /// <summary>
    /// Whether the device is dark, as its setting has it (Android's dark theme), or as a context's own configuration says:
    /// on the Galaxy XR, with the setting dark, an application's light theme was not seen as on a dark device.
    /// </summary>
    public static bool DeviceDark(Context context) =>
        context.GetSystemService(Context.UiModeService) is Android.App.UiModeManager modes && modes.NightMode == Android.App.UiNightMode.Yes
        || (context.Resources!.Configuration!.UiMode & Android.Content.Res.UiMode.NightMask) == Android.Content.Res.UiMode.NightYes;

    /// <summary>The keyboard's colours, Tema's choice, kept for every screen by its name: the system's at first.</summary>
    public const string LookKey = "look";

    public static KeyboardLook Look(Context context) => KeyboardLooks.Parse(Global(context).GetString(LookKey, null));

    /// <summary>
    /// Personalizado's keyboard and accent colours (#RRGGBB; one not chosen,
    /// the device's own), the two as they were before the last one was
    /// chosen (Deshacer, as "key|accent"), and whether each application has
    /// colours of its own (Por app, <see cref="AppThemes"/>).
    /// </summary>
    public const string KeyColorKey = "keyColor";
    public const string AccentColorKey = "accentColor";
    public const string ColorsBeforeKey = "colorsBefore";
    public const string PerAppKey = "perApp";

    /// <summary>Whether a preference changed is one of the keyboard's colours (Tema's Colores).</summary>
    public static bool IsColors(string? key) =>
        key is LookKey or KeyColorKey or AccentColorKey or PerAppKey || AppThemes.IsList(key);

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
