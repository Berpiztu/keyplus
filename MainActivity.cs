using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Views.Accessibility;
using Android.Views.InputMethods;
using Android.Widget;
using FeedbackFlags = Android.AccessibilityServices.FeedbackFlags;

[assembly: Application(Label = Berpiztu.KeyPlus.MainActivity.AppLabel)]
#if !KEYBOARD
[assembly: UsesPermission(Berpiztu.KeyPlus.MainActivity.WriteSecureSettings)]
#endif

namespace Berpiztu.KeyPlus;

/// <summary>
/// The app's one screen: a box to try the keys in (the keyboard's), what KeyPlus does,
/// whether it is on, and the places in Settings that turn it on: for the
/// popup the accessibility service
/// (KeyPlusService), and opening the app brings the popup back on screen
/// after ✕ closed it; for the keyboard, the keyboard (KeyPlusKeyboard). The
/// keys themselves live there, not here.
/// </summary>
[Activity(Label = AppLabel, MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.DeviceDefault.DayNight")]
public sealed class MainActivity : Activity
{
    /// <summary>The product's name, the app's and its screen's.</summary>
#if KEYBOARD
    public const string AppLabel = "@string/keyboard_app_name";
#else
    public const string AppLabel = "@string/app_name";
#endif

    private TextView? status;

#if !KEYBOARD
    /// <summary>
    /// Android's secure settings, where the accessibility services that are
    /// on are listed: no app holds it by itself, but adb grants it, once,
    /// from a computer or from an ADB app on the device itself (Bugjaeger),
    /// and then the app turns its service on by itself (<see cref="TurnOn"/>).
    /// </summary>
    public const string WriteSecureSettings = "android.permission.WRITE_SECURE_SETTINGS";

    /// <summary>
    /// What an ADB app on the device itself runs, once after each new
    /// install, so KeyPlus can turn its service on without a computer:
    /// restricted settings allowed (Android 13 and later keep an app
    /// installed outside a store from being an accessibility service until
    /// they are), and the secure settings granted.
    /// </summary>
    private string AdbCommands =>
        $"appops set {PackageName} ACCESS_RESTRICTED_SETTINGS allow\n" +
        $"pm grant {PackageName} {WriteSecureSettings}";
#endif

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var padding = (int)(24 * Resources!.DisplayMetrics!.Density);
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.SetPadding(padding, 0, padding, padding);

#if KEYBOARD
        content.AddView(Text(
            "Berpiztu AI Keyboard is a keyboard with Esc, Tab, Ctrl, Alt, Shift, the arrows, Home, End and the page keys, " +
            "and ABC in its title bar for a whole Spanish keyboard, sent to the field you are typing in: " +
            "a terminal, an SSH app, or a web editor such as vscode.dev in the browser.\n\n" +
            "Ctrl and Alt hold for the next key: Ctrl then \"s\" is Ctrl+S. Shift stays on until pressed again. " +
            "Keys repeat while held. Move the keyboard as the headset moves any keyboard; drag a corner up or down " +
            "for larger or smaller keys. – minimizes it into the ⌨ icon, which a tap opens again. " +
            "⚙ at the right of its title bar has its settings: Cambiar de teclado picks another keyboard."));

        status = Text("");
        content.AddView(status);

        content.AddView(Button("Open keyboard settings", () =>
            StartActivity(new Intent(Settings.ActionInputMethodSettings))));
        content.AddView(Text("Turn on \"Berpiztu AI Keyboard\" there, then choose it below."));
        content.AddView(Button("Choose the keyboard", () =>
            ((InputMethodManager)GetSystemService(InputMethodService)!).ShowInputMethodPicker()));
        content.AddView(Text(
            "Android puts no screenshot on the clipboard: it saves it among your photos. Let the keyboard " +
            "read them, and each screenshot you take from then on joins the copied texts above the keys, to paste."));
        content.AddView(Button("Allow screenshots", () => RequestPermissions([ScreenshotWatcher.Permission], 0)));
#else
        content.AddView(Text(
            "KeyPlus adds Esc, Tab, Ctrl, Alt, Shift, the arrows, Home, End and the page keys " +
            "above the keyboard you already use, and sends them to the field you are typing in: " +
            "a terminal, an SSH app, or a web editor such as vscode.dev in the browser.\n\n" +
            "Ctrl+A, Ctrl+C, Ctrl+V, Ctrl+Z, Ctrl+Y and Del (delete forward) have keys of their own, in the top row, " +
            "and ⌫ and ⏎ end the rows below. ABC in the title bar turns the keys into a whole Spanish keyboard; " +
            "Keys turns it back. " +
            "Ctrl and Alt hold for the next key, one of the strip's or a letter typed on your keyboard: " +
            "Ctrl then \"s\" is Ctrl+S. Shift stays on until pressed again. " +
            "Keys repeat while held, as a keyboard's do.\n\n" +
            "The keys are a small window above the keyboard while one is open. Hold its title bar " +
            "a moment to float it anywhere, in sight all the time, keyboard or not; drag a corner to resize it; " +
            "⤓ docks it above the keyboard again. – minimizes it into the ⌨ icon, always in sight, " +
            "which a tap opens again. ✕ closes KeyPlus, after asking; open this app to bring it back."));

        status = Text("");
        content.AddView(status);

        content.AddView(Button("Turn on KeyPlus keys", TurnOn));
        content.AddView(Text(
            "With the two commands below run once in an ADB app on this device (Bugjaeger, for one), " +
            "the button above turns KeyPlus on by itself, no computer needed. Without them it opens " +
            "Android's switch for it; if Android says the setting is restricted, open the app's info " +
            "below, choose ⋮ at the top right, then \"Allow restricted settings\", and try again. " +
            "A new install needs them again; installing over the app keeps them."));
        content.AddView(Button("Copy the ADB commands", () =>
        {
            ((ClipboardManager)GetSystemService(ClipboardService)!).PrimaryClip = ClipData.NewPlainText("KeyPlus ADB commands", AdbCommands);
            Toast.MakeText(this, "Copied: paste them into the ADB app's shell", ToastLength.Long)!.Show();
        }));
        content.AddView(Button("Open accessibility settings", () =>
            StartActivity(new Intent(Settings.ActionAccessibilitySettings))));
        content.AddView(Button("Open app info", () =>
            StartActivity(new Intent(Settings.ActionApplicationDetailsSettings, Android.Net.Uri.Parse($"package:{PackageName}")))));
        if (KeyPlusService.Current?.OnHeadset ?? PackageManager!.HasSystemFeature("android.software.xr.api.spatial"))
        {
            content.AddView(Text("On a headset the keys have a window of their own: place it beside the keyboard."));
            content.AddView(Button("Open the keys' window", () => StartActivity(new Intent(this, typeof(KeyWindowActivity)))));
        }

#endif

        // The box stays at the top, whole, while the rest scrolls under it:
        // the keyboard opening never pushes it out of sight.
        var scroll = new ScrollView(this);
        scroll.AddView(content);
        var page = new LinearLayout(this) { Orientation = Orientation.Vertical };

        // Android 15 lays the app out from the screen's edges, under its bars
        // and the keyboard: the page keeps clear of them.
        page.SetOnApplyWindowInsetsListener(new Insets(padding));
#if KEYBOARD
        page.AddView(TryBox());
#endif
        var rest = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f);
        rest.SetMargins(-padding, 0, -padding, 0);
        page.AddView(scroll, rest);
        SetContentView(page);
    }

    protected override void OnResume()
    {
        base.OnResume();
#if !KEYBOARD
        KeyPlusPreferences.SetHidden(this, false);
#endif
        ShowStatus();
    }

    /// <summary>Whether KeyPlus is on, in the line under its description.</summary>
    private void ShowStatus()
    {
#if KEYBOARD
        status!.Text = (IsKeyboardOn()
            ? "Berpiztu AI Keyboard is on: choose it as the keyboard to type with it."
            : "Berpiztu AI Keyboard is off.")
            + (ScreenshotWatcher.Allowed(this) ? " Screenshots join the copied texts." : "");
#else
        status!.Text = IsServiceOn()
            ? "KeyPlus is on: the ⌨ icon is on screen, and the keys appear whenever a keyboard is open."
            : "KeyPlus is off.";
#endif
    }

#if KEYBOARD
    /// <summary>Whether KeyPlus's keyboard is turned on in the keyboard list.</summary>
    private bool IsKeyboardOn() =>
        (((InputMethodManager)GetSystemService(InputMethodService)!).EnabledInputMethodList ?? [])
            .Any(keyboard => keyboard.PackageName == PackageName);
#else
    /// <summary>
    /// KeyPlus keys turned on: by the app itself where it may write the
    /// secure settings (<see cref="WriteSecureSettings"/>), the services
    /// already on kept, and said a moment later whether Android took it;
    /// else Android's own page for it, where its switch is.
    /// </summary>
    private void TurnOn()
    {
        var service = new ComponentName(PackageName!, $"{PackageName}.KeyPlusService").FlattenToShortString()!;
        if (CheckSelfPermission(WriteSecureSettings) != Android.Content.PM.Permission.Granted)
        {
            try
            {
                StartActivity(new Intent(AccessibilityDetailsSettings).PutExtra(Intent.ExtraComponentName, service));
            }
            catch (ActivityNotFoundException)
            {
                StartActivity(new Intent(Settings.ActionAccessibilitySettings));
            }

            return;
        }

        var listed = Settings.Secure.GetString(ContentResolver, Settings.Secure.EnabledAccessibilityServices) ?? "";
        var others = listed.Split(':', StringSplitOptions.RemoveEmptyEntries).Where(name => !name.StartsWith($"{PackageName}/", StringComparison.Ordinal));
        Settings.Secure.PutString(ContentResolver, Settings.Secure.EnabledAccessibilityServices, string.Join(':', others.Append(service)));
        Settings.Secure.PutInt(ContentResolver, Settings.Secure.AccessibilityEnabled, 1);
        status!.PostDelayed(ShowStatus, 2000);
        if (!IsServiceOn())
        {
            status.Text = "Turning KeyPlus on…";
        }
    }

    /// <summary>Android's page for one accessibility service, with its switch (Settings.ACTION_ACCESSIBILITY_DETAILS_SETTINGS, Android 13), which .NET does not name.</summary>
    private const string AccessibilityDetailsSettings = "android.settings.ACCESSIBILITY_DETAILS_SETTINGS";

    private bool IsServiceOn()
    {
        var accessibility = (AccessibilityManager)GetSystemService(AccessibilityService)!;
        return (accessibility.GetEnabledAccessibilityServiceList(FeedbackFlags.AllMask) ?? [])
            .Any(service => service.ResolveInfo?.ServiceInfo?.PackageName == PackageName);
    }
#endif

    /// <summary>The page clear of the screen's bars and of the keyboard, with its own margin.</summary>
    private sealed class Insets(int padding) : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
        {
            var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout() | WindowInsets.Type.Ime());
            view.SetPadding(padding + bars.Left, padding + bars.Top, padding + bars.Right, bars.Bottom);
            return WindowInsets.Consumed!;
        }
    }

    /// <summary>
    /// A box to try the keys in right after installing: several lines, so
    /// the arrows, Home, End and the pages have room to move in. Its size is
    /// fixed: a long text scrolls inside it, the screen never moves.
    /// </summary>
    private EditText TryBox()
    {
        var box = new EditText(this)
        {
            Hint = "Try the keys here",
            InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextFlagMultiLine,
            Gravity = GravityFlags.Top | GravityFlags.Left,
            VerticalScrollBarEnabled = true,
        };
        box.SetLines(6);
        box.SetMaxLines(6);
        box.SetHorizontallyScrolling(false);
        box.SetTextSize(Android.Util.ComplexUnitType.Sp, 16);

        // Outlined, so it reads as a box, not as the theme's line alone.
        var density = Resources!.DisplayMetrics!.Density;
        var outline = new Android.Graphics.Drawables.GradientDrawable();
        outline.SetStroke((int)(1.5f * density), Android.Graphics.Color.Gray);
        outline.SetCornerRadius(8 * density);
        box.Background = outline;
        box.SetPadding((int)(12 * density), (int)(10 * density), (int)(12 * density), (int)(10 * density));
        var layout = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        layout.SetMargins(0, 0, 0, (int)(16 * Resources!.DisplayMetrics!.Density));
        box.LayoutParameters = layout;
        return box;
    }

    private TextView Text(string text)
    {
        var view = new TextView(this) { Text = text };
        view.SetTextSize(Android.Util.ComplexUnitType.Sp, 16);
        view.SetPadding(0, 0, 0, (int)(16 * Resources!.DisplayMetrics!.Density));
        return view;
    }

    private Button Button(string text, System.Action onClick)
    {
        var button = new Button(this) { Text = text };
        button.Click += (_, _) => onClick();
        var layout = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        layout.SetMargins(0, 0, 0, (int)(16 * Resources!.DisplayMetrics!.Density));
        button.LayoutParameters = layout;
        return button;
    }
}
