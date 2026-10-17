using Android.Content;
using Android.Views;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Where KeyPlus keeps what it remembers. Whether it was closed is one thing
/// for the whole phone; the window's layout (size, place, Keys or ABC, docked
/// or floating, minimized) is kept apart for the phone's own screen and for
/// Samsung DeX, a display of its own, so each keeps the layout made for it.
/// </summary>
public static class KeyPlusPreferences
{
    /// <summary>The preference that says KeyPlus was closed, which the service watches.</summary>
    public const string HiddenKey = "hidden";

    private const string PhoneName = "keyplus";
    private const string DexName = "keyplus.dex";
    private const string LayoutsSplitKey = "layoutsSplit";

    /// <summary>What holds for the whole phone; also the phone screen's layout, as it was before DeX had its own.</summary>
    public static ISharedPreferences Global(Context context) =>
        context.GetSharedPreferences(PhoneName, FileCreationMode.Private)!;

    /// <summary>The layout of the display the window is on: the phone's own screen, or DeX.</summary>
    public static ISharedPreferences ForDisplay(Context context, int displayId) =>
        displayId == Display.DefaultDisplay ? Global(context) : context.GetSharedPreferences(DexName, FileCreationMode.Private)!;

    /// <summary>
    /// Once, when DeX first gets a layout of its own: the layout kept until
    /// then was made under DeX, so it moves to DeX's, and the phone's screen
    /// starts afresh. Whether KeyPlus is closed stays where it is.
    /// </summary>
    public static void SplitLayoutsOnce(Context context)
    {
        var global = Global(context);
        if (global.GetBoolean(LayoutsSplitKey, false))
        {
            return;
        }

        var dex = context.GetSharedPreferences(DexName, FileCreationMode.Private)!.Edit()!;
        var phone = global.Edit()!;
        foreach (var (key, value) in global.All!)
        {
            if (key == HiddenKey)
            {
                continue;
            }

            _ = value switch
            {
                bool flag => dex.PutBoolean(key, flag),
                int number => dex.PutInt(key, number),
                float number => dex.PutFloat(key, number),
                Java.Lang.Boolean flag => dex.PutBoolean(key, (bool)flag),
                Java.Lang.Integer number => dex.PutInt(key, (int)number),
                Java.Lang.Float number => dex.PutFloat(key, (float)number),
                _ => dex,
            };
            phone.Remove(key);
        }

        dex.Apply();
        phone.PutBoolean(LayoutsSplitKey, true)!.Apply();
    }

    /// <summary>Whether KeyPlus was closed, nothing of it in sight until the app is opened again.</summary>
    public static bool IsHidden(Context context) => Global(context).GetBoolean(HiddenKey, false);

    /// <summary>Closes KeyPlus, or brings it back; written at once, for the app and the service are apart.</summary>
    public static void SetHidden(Context context, bool hidden) =>
        Global(context).Edit()!.PutBoolean(HiddenKey, hidden)!.Apply();
}
