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

    /// <summary>What holds for the whole phone; also the phone screen's layout, as it was before DeX had its own.</summary>
    public static ISharedPreferences Global(Context context) =>
        context.GetSharedPreferences(PhoneName, FileCreationMode.Private)!;

    /// <summary>The layout of the display the window is on: the phone's own screen, or DeX.</summary>
    public static ISharedPreferences ForDisplay(Context context, int displayId) =>
        displayId == Display.DefaultDisplay ? Global(context) : context.GetSharedPreferences(DexName, FileCreationMode.Private)!;

    /// <summary>Whether KeyPlus was closed, nothing of it in sight until the app is opened again.</summary>
    public static bool IsHidden(Context context) => Global(context).GetBoolean(HiddenKey, false);

    /// <summary>Closes KeyPlus, or brings it back; written at once, for the app and the service are apart.</summary>
    public static void SetHidden(Context context, bool hidden) =>
        Global(context).Edit()!.PutBoolean(HiddenKey, hidden)!.Apply();
}
