using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Views.Accessibility;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Apartarse también de los menús on Android, as on Windows: a keyboard sees
/// only the caret of the field it types in, never another app's menus, so
/// this accessibility service, Berpiztu AI Keyboard's own and on only where
/// the user turns it on, watches for a menu opening (a popup: a window of an
/// app's own with the app's window as its parent, as an app's menu or an
/// autocompletion's list is) and tells the keyboard where it is, then that it
/// closed. It reads nothing else, only the windows' places.
/// </summary>
[Service(Name = "ai.berpiztu.keyboard.MenuWatchService", Label = "@string/menus_service_label",
    Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Exported = true)]
[IntentFilter(["android.accessibilityservice.AccessibilityService"])]
[MetaData("android.accessibilityservice", Resource = "@xml/keyboard_menus")]
public sealed class MenuWatchService : AccessibilityService
{
    /// <summary>A menu opened: its place on the screen, in pixels.</summary>
    public static event System.Action<Android.Graphics.Rect>? MenuOpened;

    /// <summary>The menu that opened closed.</summary>
    public static event System.Action? MenuClosed;

    /// <summary>The open menu's window, to see it close.</summary>
    private int? menuWindow;

    /// <summary>Whether the service is turned on, as Android's secure setting of the accessibility services on has it (written at once, where Android's manager tells it a moment later).</summary>
    public static bool On(Context context)
    {
        var watch = new ComponentName(context, Java.Lang.Class.FromType(typeof(MenuWatchService))).FlattenToString();
        var on = Android.Provider.Settings.Secure.GetString(context.ContentResolver, Android.Provider.Settings.Secure.EnabledAccessibilityServices) ?? "";
        return on.Split(':').Contains(watch);
    }

    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
        if (e is null)
        {
            return;
        }

        if (menuWindow is { } open && Window(open) is null)
        {
            menuWindow = null;
            Android.Util.Log.Info("KeyPlus", "Menu: closed");
            MenuClosed?.Invoke();
        }

        // A popup: a window of an app's own, opened over it (a menu, a list that drops down), told as it opens and as it takes its place.
        if (Window(e.WindowId) is { Parent: not null, Type: AccessibilityWindowType.Application } window)
        {
            var place = new Android.Graphics.Rect();
            window.GetBoundsInScreen(place);
            if (place.IsEmpty || Math.Min(place.Width(), place.Height()) < Dp(LeastMenuDp))
            {
                return;
            }

            if (menuWindow != e.WindowId)
            {
                Android.Util.Log.Info("KeyPlus", $"Menu: open at {place.ToShortString()}");
            }

            menuWindow = e.WindowId;
            MenuOpened?.Invoke(place);
        }
    }

    public override void OnInterrupt()
    {
    }

    /// <summary>The least side a menu has, in dp: a menu's row and a finger's; smaller popups (a hint, a handle) are not menus.</summary>
    private const int LeastMenuDp = 48;

    private int Dp(int value) => (int)((value * Resources!.DisplayMetrics!.Density) + 0.5f);

    private AccessibilityWindowInfo? Window(int id) => Windows?.FirstOrDefault(window => window.Id == id);
}
