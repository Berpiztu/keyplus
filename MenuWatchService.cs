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
/// closed; and, for Tema's Por app, sees the colour just inside the corner of
/// the field typed in, on a screenshot (Android 11 and later), for De la app
/// to take an application's colour as it shows, where the build carries it
/// (FIELD_COLOR, KeyPlusFieldColor in the csproj). It reads nothing else.
/// </summary>
#if FIELD_COLOR
[Service(Name = "ai.berpiztu.keyboard.MenuWatchService", Label = "@string/menus_service_label",
    Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Exported = true)]
[MetaData("android.accessibilityservice", Resource = "@xml/keyboard_menus_color")]
#else
[Service(Name = "ai.berpiztu.keyboard.MenuWatchService", Label = "@string/menus_only_service_label",
    Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Exported = true)]
[MetaData("android.accessibilityservice", Resource = "@xml/keyboard_menus")]
#endif
[IntentFilter(["android.accessibilityservice.AccessibilityService"])]
public sealed class MenuWatchService : AccessibilityService
{
#if FIELD_COLOR
    /// <summary>The service's name and what it sees, as Android shows them.</summary>
    public const int Label = Resource.String.menus_service_label, Description = Resource.String.menus_service_description;
#else
    /// <summary>The service's name and what it sees, as Android shows them.</summary>
    public const int Label = Resource.String.menus_only_service_label, Description = Resource.String.menus_only_service_description;
#endif

    /// <summary>A menu opened: its place on the screen, in pixels.</summary>
    public static event System.Action<Android.Graphics.Rect>? MenuOpened;

    /// <summary>The menu that opened closed.</summary>
    public static event System.Action? MenuClosed;

    /// <summary>The open menu's window, to see it close.</summary>
    private int? menuWindow;

    /// <summary>The service as it runs, for the keyboard to ask it a field's colour; none while it is off.</summary>
    public static MenuWatchService? Running { get; private set; }

    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();
        Running = this;
    }

    public override bool OnUnbind(Intent? intent)
    {
        Running = null;
        return base.OnUnbind(intent);
    }

#if FIELD_COLOR
    /// <summary>The colour just inside the corner of the field typed in, as the screen shows it, told once seen; nothing where it cannot be seen.</summary>
    public void SeeFieldColor(System.Action<Rgb> seen)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30) || FindFocus(NodeFocus.Input) is not { } field)
        {
            return;
        }

        var place = new Android.Graphics.Rect();
        field.GetBoundsInScreen(place);
        if (place.IsEmpty)
        {
            return;
        }

        TakeScreenshot(Android.Views.Display.DefaultDisplay, MainExecutor!, new Screenshot(shot =>
        {
            using var buffer = shot.HardwareBuffer;
            using var drawn = Android.Graphics.Bitmap.WrapHardwareBuffer(buffer!, shot.ColorSpace)?.Copy(Android.Graphics.Bitmap.Config.Argb8888!, false);
            if (drawn is not null && place.Left + 3 < drawn.Width && place.Top + 3 < drawn.Height)
            {
                var pixel = new Android.Graphics.Color(drawn.GetPixel(place.Left + 3, place.Top + 3));
                seen(new Rgb(pixel.R, pixel.G, pixel.B));
            }
        }));
    }

    /// <summary>A screenshot taken, told; one refused (too soon after another, the screen secure) told nothing.</summary>
    private sealed class Screenshot(System.Action<ScreenshotResult> taken) : Java.Lang.Object, ITakeScreenshotCallback
    {
        public void OnSuccess(ScreenshotResult screenshot) => taken(screenshot);

        public void OnFailure(int errorCode) => Log.Write($"Screenshot refused: {errorCode}");
    }
#endif

    /// <summary>Whether the service is turned on, as Android's secure setting of the accessibility services on has it (written at once, where Android's manager tells it a moment later).</summary>
    public static bool On(Context context)
    {
        var on = Android.Provider.Settings.Secure.GetString(context.ContentResolver, Android.Provider.Settings.Secure.EnabledAccessibilityServices) ?? "";
        return on.Split(':').Contains(Id(context));
    }

    /// <summary>The service as Android names it among the accessibility services on.</summary>
    public static string Id(Context context) =>
        new ComponentName(context, Java.Lang.Class.FromType(typeof(MenuWatchService))).FlattenToString()!;

    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
        if (e is null)
        {
            return;
        }

        if (menuWindow is { } open && Window(open) is null)
        {
            menuWindow = null;
            Log.Detail("Menu: closed");
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
                Log.Detail($"Menu: open at {place.ToShortString()}");
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
