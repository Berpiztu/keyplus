using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Views.Accessibility;
using Android.Widget;
using FeedbackFlags = Android.AccessibilityServices.FeedbackFlags;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The app's one screen: what KeyPlus does, whether its service is on, and the
/// two places in Settings that turn it on. Opening it brings KeyPlus back on
/// screen after ✕ closed it. The keys themselves live in
/// <see cref="KeyPlusService"/>, not here.
/// </summary>
[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.DeviceDefault.DayNight")]
public sealed class MainActivity : Activity
{
    private TextView? status;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var padding = (int)(24 * Resources!.DisplayMetrics!.Density);
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.SetPadding(padding, padding, padding, padding);

        content.AddView(Text(
            "KeyPlus adds Esc, Tab, Ctrl, Alt, Shift, the arrows, Home, End and the page keys " +
            "above the keyboard you already use, and sends them to the field you are typing in: " +
            "a terminal, an SSH app, or a web editor such as vscode.dev in the browser.\n\n" +
            "Ctrl+A, Ctrl+C, Ctrl+V, Ctrl+Z, Ctrl+Y and Del (delete forward) have keys of their own, in the top row, " +
            "and ⌫ and ⏎ end the rows below. ABC in the title bar turns the keys into a whole Spanish keyboard; " +
            "Keys turns it back. " +
            "Ctrl and Alt hold for the next key, one of the strip's or a letter typed on your keyboard: " +
            "Ctrl then \"s\" is Ctrl+S. Shift stays on until pressed again. " +
            "The arrows and pages repeat while held.\n\n" +
            "The keys are a small window above the keyboard while one is open. Drag its title bar " +
            "to float it anywhere, in sight all the time, keyboard or not; drag a corner to resize it; " +
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

        var scroll = new ScrollView(this);
        scroll.AddView(content);
        SetContentView(scroll);
    }

    protected override void OnResume()
    {
        base.OnResume();
        KeyPlusPreferences.SetHidden(this, false);
        status!.Text = IsServiceOn()
            ? "KeyPlus is on: the ⌨ icon is on screen, and the keys appear whenever a keyboard is open."
            : "KeyPlus is off.";
    }

    private bool IsServiceOn()
    {
        var accessibility = (AccessibilityManager)GetSystemService(AccessibilityService)!;
        return (accessibility.GetEnabledAccessibilityServiceList(FeedbackFlags.AllMask) ?? [])
            .Any(service => service.ResolveInfo?.ServiceInfo?.PackageName == PackageName);
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
