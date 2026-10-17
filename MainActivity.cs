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

namespace Berpiztu.KeyPlus;

/// <summary>
/// The app's one screen: what KeyPlus does, whether it is on, and the places
/// in Settings that turn it on: for the popup the accessibility service
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

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var padding = (int)(24 * Resources!.DisplayMetrics!.Density);
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.SetPadding(padding, padding, padding, padding);

#if KEYBOARD
        content.AddView(Text(
            "Berpiztu AI Keyboard is a keyboard with Esc, Tab, Ctrl, Alt, Shift, the arrows, Home, End and the page keys, " +
            "and ABC in its title bar for a whole Spanish keyboard, sent to the field you are typing in: " +
            "a terminal, an SSH app, or a web editor such as vscode.dev in the browser.\n\n" +
            "Ctrl and Alt hold for the next key: Ctrl then \"s\" is Ctrl+S. Shift stays on until pressed again. " +
            "Keys repeat while held. Move the keyboard as the headset moves any keyboard; drag a corner up or down " +
            "for larger or smaller keys. – minimizes it into the ⌨ icon, which a tap opens again. " +
            "✕ goes back to the keyboard you used before."));

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

        content.AddView(Button("Open accessibility settings", () =>
            StartActivity(new Intent(Settings.ActionAccessibilitySettings))));
        content.AddView(Text(
            "Turn on \"KeyPlus keys\" there. If Android says the setting is restricted, open the " +
            "app's info below, choose ⋮ at the top right, then \"Allow restricted settings\", and try again."));
        content.AddView(Button("Open app info", () =>
            StartActivity(new Intent(Settings.ActionApplicationDetailsSettings, Android.Net.Uri.Parse($"package:{PackageName}")))));

#endif

        var scroll = new ScrollView(this);
        scroll.AddView(content);
        SetContentView(scroll);
    }

    protected override void OnResume()
    {
        base.OnResume();
#if KEYBOARD
        status!.Text = (IsKeyboardOn()
            ? "Berpiztu AI Keyboard is on: choose it as the keyboard to type with it."
            : "Berpiztu AI Keyboard is off.")
            + (ScreenshotWatcher.Allowed(this) ? " Screenshots join the copied texts." : "");
#else
        KeyPlusPreferences.SetHidden(this, false);
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
    private bool IsServiceOn()
    {
        var accessibility = (AccessibilityManager)GetSystemService(AccessibilityService)!;
        return (accessibility.GetEnabledAccessibilityServiceList(FeedbackFlags.AllMask) ?? [])
            .Any(service => service.ResolveInfo?.ServiceInfo?.PackageName == PackageName);
    }
#endif

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
