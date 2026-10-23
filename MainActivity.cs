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
[assembly: UsesPermission(Berpiztu.KeyPlus.MainActivity.WriteSecureSettings)]

namespace Berpiztu.KeyPlus;

/// <summary>
/// The app's one screen. The popup's: what KeyPlus does, whether it is on,
/// and the places in Settings that turn its accessibility service
/// (KeyPlusService) on; opening the app brings the popup back on screen
/// after ✕ closed it. The keyboard's, as Gboard's: while the keyboard is not
/// on or not the one in use, its two steps (turn it on, choose it), each
/// marked done as Android has it; then its settings (SettingsScreen),
/// a box to try the keys in over them. The keys themselves live in their
/// services, not here.
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

#if !KEYBOARD
    private TextView? status;
#endif

    /// <summary>
    /// Android's secure settings, where the accessibility services that are
    /// on and the keyboards are listed: no app holds it by itself, but adb
    /// grants it, once, from a computer or from the app itself over the
    /// device's Wireless debugging (<see cref="AdbGrant"/>), and then the app
    /// turns itself on.
    /// </summary>
    public const string WriteSecureSettings = "android.permission.WRITE_SECURE_SETTINGS";

    /// <summary>Settings' Developer options scrolled to Wireless debugging, as Shizuku opens it.</summary>
    private const string WirelessDebuggingKey = "toggle_adb_wireless";

    private const int NotificationsRequest = 1;
    private const int MicrophoneRequest = 2;

    /// <summary>The keyboard's 🎤 without the microphone's permission: this app opens on Escribir por voz and asks for it.</summary>
    public const string AskMicrophoneExtra = "askMicrophone";
    private const int ExportRequest = 2;
    private const int ImportRequest = 3;

#if KEYBOARD
    /// <summary>The keyboard as Android names it.</summary>
    internal static string KeyboardId(Context context) =>
        new ComponentName(context.PackageName!, $"{context.PackageName}.KeyPlusKeyboard").FlattenToShortString()!;
#endif

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
#if KEYBOARD
        // Gboard's settings have their title in the page, not in a bar.
        ActionBar?.Hide();
        Keyboards.MoveDesignsOnce(this);
        settings = new SettingsScreen(this);
        tryBox = TryBox();
        OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(0, new BackCallback(this));

        // Android 15 lays the app out from the screen's edges, under its bars
        // and the keyboard: the page keeps clear of them.
        settings.View.SetOnApplyWindowInsetsListener(new Insets(0));
        SetContentView(settings.View);
#else
        var padding = (int)(24 * Resources!.DisplayMetrics!.Density);
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.SetPadding(padding, 0, padding, padding);

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
            "The button below turns KeyPlus on over this device's Wireless debugging, in Developer options, " +
            "with Wi-Fi on, no computer needed: it opens Wireless debugging; tap Pair device with pairing code " +
            "there, then type the code in KeyPlus's notification, without leaving Settings. From then on, the " +
            "button above turns it on by itself. A new install needs it again; installing over the app keeps it. " +
            "Without it, the button above opens Android's switch; if Android says the setting is restricted, " +
            "open the app's info below, choose ⋮ at the top right, then \"Allow restricted settings\", and try again."));
        content.AddView(Button("Turn it on with Wireless debugging", PairWithWirelessDebugging));
        content.AddView(Button("Open accessibility settings", () =>
            StartActivity(new Intent(Settings.ActionAccessibilitySettings))));
        content.AddView(Button("Open app info", () =>
            StartActivity(new Intent(Settings.ActionApplicationDetailsSettings, Android.Net.Uri.Parse($"package:{PackageName}")))));
        if (KeyPlusService.Current?.OnHeadset ?? PackageManager!.HasSystemFeature("android.software.xr.api.spatial"))
        {
            content.AddView(Text("On a headset the keys have a window of their own: place it beside the keyboard."));
            content.AddView(Button("Open the keys' window", () => StartActivity(new Intent(this, typeof(KeyWindowActivity)))));
        }

        // The box stays at the top, whole, while the rest scrolls under it:
        // the keyboard opening never pushes it out of sight.
        var scroll = new ScrollView(this);
        scroll.AddView(content);
        var page = new LinearLayout(this) { Orientation = Orientation.Vertical };

        // Android 15 lays the app out from the screen's edges, under its bars
        // and the keyboard: the page keeps clear of them.
        page.SetOnApplyWindowInsetsListener(new Insets(padding));
        var rest = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f);
        rest.SetMargins(-padding, 0, -padding, 0);
        page.AddView(scroll, rest);
        SetContentView(page);
#endif
    }

    protected override void OnResume()
    {
        base.OnResume();
#if !KEYBOARD
        KeyPlusPreferences.SetHidden(this, false);
#endif
        ShowStatus();
#if KEYBOARD
        if (Intent?.GetBooleanExtra(AskMicrophoneExtra, false) == true)
        {
            Intent.RemoveExtra(AskMicrophoneExtra);
            settings!.Open(VoicePage());
            AskMicrophone();
        }
#endif
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
    }

    /// <summary>
    /// The app turned on over Wireless debugging (<see cref="AdbGrant"/>):
    /// its notification takes the pairing code, so notifications are asked
    /// for first; then Wireless debugging is opened, where the pairing starts.
    /// </summary>
    private void PairWithWirelessDebugging()
    {
        if (CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Android.Content.PM.Permission.Granted)
        {
            RequestPermissions([Android.Manifest.Permission.PostNotifications], NotificationsRequest);
            return;
        }

        AdbGrant.Start(this);
        try
        {
            StartActivity(new Intent(Settings.ActionApplicationDevelopmentSettings).PutExtra(":settings:fragment_args_key", WirelessDebuggingKey));
        }
        catch (ActivityNotFoundException)
        {
            Toast.MakeText(this, "Turn Developer options on first: tap Build number seven times in About phone.", ToastLength.Long)!.Show();
        }
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Android.Content.PM.Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
#if KEYBOARD
        if (requestCode == MicrophoneRequest)
        {
            settings?.Refresh(VoicePage());
            return;
        }
#endif
        if (requestCode != NotificationsRequest)
        {
            return;
        }

        if (grantResults.Length > 0 && grantResults[0] == Android.Content.PM.Permission.Granted)
        {
            PairWithWirelessDebugging();
        }
        else
        {
            Toast.MakeText(this, "The pairing code is typed in a notification: allow notifications to go on.", ToastLength.Long)!.Show();
        }
    }

    /// <summary>Whether KeyPlus is on: the keyboard's steps or settings; the popup's line under its description.</summary>
    private void ShowStatus()
    {
#if KEYBOARD
        if (!IsKeyboardOn() || !IsKeyboardInUse())
        {
            settings!.Show(SetupPage(), null, openFirst: false);
        }
        else if (!showingSettings)
        {
            settings!.Show(MainPage(), tryBox, openFirst: true);
        }

        showingSettings = IsKeyboardOn() && IsKeyboardInUse();
#else
        status!.Text = IsServiceOn()
            ? "KeyPlus is on: the ⌨ icon is on screen, and the keys appear whenever a keyboard is open."
            : "KeyPlus is off.";
#endif
    }

#if KEYBOARD
    private SettingsScreen? settings;
    private EditText? tryBox;

    /// <summary>Whether the settings show, the keyboard on and in use: shown once, not made again at each return.</summary>
    private bool showingSettings;

    /// <summary>The picker of keyboards is a dialog over this screen: its choice seen as the screen has the focus again.</summary>
    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
        {
            ShowStatus();
        }
    }

    /// <summary>Back: a settings' page closed; with none, the app left as Android leaves one.</summary>
    private sealed class BackCallback(MainActivity activity) : Java.Lang.Object, Android.Window.IOnBackInvokedCallback
    {
        public void OnBackInvoked()
        {
            if (activity.settings?.Back() != true)
            {
                activity.MoveTaskToBack(true);
            }
        }
    }

    /// <summary>The keyboard's two steps, as Gboard's first screen: each marked done as Android has it.</summary>
    private SettingsPage SetupPage() => new(GetString(Resource.String.setup_title),
    [
        [new(GetString(Resource.String.setup_turn_on), GetString(IsKeyboardOn() ? Resource.String.setup_done : Resource.String.setup_turn_on_summary), Tapped: TurnOnKeyboard)],
        [new(GetString(Resource.String.setup_choose), GetString(IsKeyboardInUse() ? Resource.String.setup_done : Resource.String.setup_choose_summary), Tapped: ChooseKeyboard)],
    ]);

    /// <summary>
    /// Turn it on: by the app itself where it may write the secure settings;
    /// else a choice, Wireless debugging (no computer, once) or Android's
    /// keyboard settings, where a device has no Wireless debugging (a pod).
    /// </summary>
    private void TurnOnKeyboard()
    {
        if (CheckSelfPermission(WriteSecureSettings) == Android.Content.PM.Permission.Granted)
        {
            UseKeyboard();
            return;
        }

        new AlertDialog.Builder(this)
            .SetTitle(Resource.String.setup_how)!
            .SetItems([GetString(Resource.String.setup_wireless), GetString(Resource.String.setup_android)], (_, chosen) =>
            {
                if (chosen.Which == 0)
                {
                    PairWithWirelessDebugging();
                }
                else
                {
                    StartActivity(new Intent(Settings.ActionInputMethodSettings));
                }
            })!
            .Show();
    }

    /// <summary>Choose it: by the app itself where it may, else Android's choice of keyboard.</summary>
    private void ChooseKeyboard() => UseKeyboard();

    /// <summary>The settings, Gboard's in Gboard's order: ours where they are made, the others saying they are not yet.</summary>
    private SettingsPage MainPage() => new(GetString(Resource.String.settings_title),
    [
        // The keyboard chosen, and turned on again over Wireless debugging when Android forgot the pairing.
        [
            new(GetString(Resource.String.choose_keyboard), Icon: Resource.Drawable.ic_grid_keyboard, Tapped: () =>
                ((InputMethodManager)GetSystemService(InputMethodService)!).ShowInputMethodPicker()),
            new(GetString(Resource.String.wireless_debugging), GetString(Resource.String.wireless_debugging_summary), Resource.Drawable.ic_settings_adb, PairWithWirelessDebugging),
        ],
        [
            new(GetString(Resource.String.languages), GetString(Resource.String.languages_summary), Resource.Drawable.ic_key_language, () => settings!.Open(LanguagesPage()), Opens: true),
            new(GetString(Resource.String.preferences), Icon: Resource.Drawable.ic_settings_tune, Tapped: () => settings!.Open(PreferencesPage()), Opens: true),
            new(GetString(Resource.String.theme), Icon: Resource.Drawable.ic_grid_theme, Tapped: () => settings!.Open(ThemePage()), Opens: true),
        ],
        [
            NotMadeRow(Resource.String.corrections, Resource.Drawable.ic_settings_spellcheck),
            new(GetString(Resource.String.glide), Icon: Resource.Drawable.ic_settings_gesture, Tapped: () => settings!.Open(GlidePage()), Opens: true),
            new(GetString(Resource.String.voice_typing), Icon: Resource.Drawable.ic_tool_mic, Tapped: () => settings!.Open(VoicePage()), Opens: true),
            NotMadeRow(Resource.String.write_fields, Resource.Drawable.ic_settings_stylus),
            NotMadeRow(Resource.String.emoji_settings, Resource.Drawable.ic_key_emoji),
        ],
        [
            new(GetString(Resource.String.clipboard), Icon: Resource.Drawable.ic_clipboard, Tapped: () => settings!.Open(ClipboardPage()), Opens: true),
            NotMadeRow(Resource.String.dictionary, Resource.Drawable.ic_settings_dictionary),
            new(GetString(Resource.String.designs), Icon: Resource.Drawable.ic_settings_dashboard_customize, Tapped: () => settings!.Open(DesignsPage()), Opens: true),
        ],
        [
            NotMadeRow(Resource.String.privacy, Resource.Drawable.ic_settings_shield),
            NotMadeRow(Resource.String.share, Resource.Drawable.ic_grid_share),
            new(GetString(Resource.String.about), Icon: Resource.Drawable.ic_settings_info, Tapped: () => settings!.Open(AboutPage()), Opens: true),
            new(GetString(Resource.String.troubleshooting), Icon: Resource.Drawable.ic_settings_help, Tapped: () => settings!.Open(TroubleshootingPage()), Opens: true),
            NotMadeRow(Resource.String.help, Resource.Drawable.ic_settings_help),
        ],
    ]);

    private SettingsRow NotMadeRow(int title, int icon) =>
        new(GetString(title), Icon: icon, Tapped: () => Toast.MakeText(this, GetString(Resource.String.not_made_yet, new Java.Lang.String(GetString(title))), ToastLength.Short)!.Show());

    /// <summary>
    /// Tema: the keyboards of the list twice, the one this screen shows
    /// docked and the one it shows floating each marked, a tap choosing
    /// another there; then those saved, one tapped renamed or deleted.
    /// </summary>
    private SettingsPage ThemePage()
    {
        var kind = KeyPlusPreferences.ScreenOf(this, Display?.DisplayId ?? Android.Views.Display.DefaultDisplay);
        var here = KeyPlusPreferences.ForScreen(this, kind);
        var all = Keyboards.All(this);
        SettingsRow[] Shown(bool floating)
        {
            var chosen = Keyboards.Selected(this, here, kind, floating);
            return
            [
                new(GetString(floating ? Resource.String.floating : Resource.String.docked), Keyboards.ScreenName(this, kind)),
                .. all.Select(keyboard => new SettingsRow(keyboard.Name, Chosen: keyboard.Id == chosen.Id, Tapped: () =>
                {
                    Keyboards.Select(here, floating, keyboard.Id);
                    settings!.Refresh(ThemePage());
                })),
            ];
        }

        List<IReadOnlyList<SettingsRow>> groups = [Shown(floating: false), Shown(floating: true)];
        if (all.Where(keyboard => !keyboard.BuiltIn).ToList() is { Count: > 0 } saved)
        {
            groups.Add([new(GetString(Resource.String.keyboards_saved), GetString(Resource.String.keyboards_saved_summary)), .. saved.Select(keyboard => new SettingsRow(keyboard.Name, Tapped: () => ChangeSaved(keyboard)))]);
        }

        return new(GetString(Resource.String.theme), groups);
    }

    /// <summary>A saved keyboard renamed or, once asked, deleted.</summary>
    private void ChangeSaved(KeyboardEntry keyboard) => new AlertDialog.Builder(this)
        .SetTitle(keyboard.Name)!
        .SetItems([GetString(Resource.String.rename), GetString(Resource.String.delete)], (_, picked) =>
        {
            if (picked.Which == 0)
            {
                AskName(keyboard.Name, name =>
                {
                    Keyboards.Rename(this, keyboard.Id, name);
                    settings!.Refresh(ThemePage());
                });
                return;
            }

            new AlertDialog.Builder(this)
                .SetMessage(GetString(Resource.String.delete_ask, new Java.Lang.String(keyboard.Name)))!
                .SetPositiveButton(Resource.String.delete, (_, _) =>
                {
                    Keyboards.Delete(this, keyboard.Id);
                    settings!.Refresh(ThemePage());
                })!
                .SetNegativeButton(Resource.String.cancel, (_, _) => { })!
                .Show();
        })!
        .Show();

    /// <summary>
    /// Escribir por voz, the Windows keyboard's settings with Android's own:
    /// the microphone's permission while it is missing; stopping by itself
    /// when the user stops speaking, and after how long a silence; the
    /// service, Local (Android's own recognizer), an ia-voice on the local
    /// network by its IP, or a remote one at an address with its user and
    /// password; the gain for the two ia-voice ones.
    /// </summary>
    private SettingsPage VoicePage()
    {
        var kept = KeyPlusPreferences.Global(this);
        var allowed = CheckSelfPermission(Android.Manifest.Permission.RecordAudio) == Android.Content.PM.Permission.Granted;
        var autoStop = VoicePreferences.AutoStop(this);
        var choice = VoicePreferences.Choice(this);
        var notSet = GetString(Resource.String.voice_not_set);
        List<IReadOnlyList<SettingsRow>> groups = [];
        if (!allowed)
        {
            groups.Add([new(GetString(Resource.String.voice_permission), GetString(Resource.String.voice_permission_summary), Tapped: AskMicrophone)]);
        }

        groups.Add(autoStop
            ?
            [
                VoiceSwitch(),
                new(GetString(Resource.String.voice_silence), $"{VoicePreferences.Silence(this):0.#} s", Tapped: () => ChooseVoiceStep(Resource.String.voice_silence, VoicePreferences.SilenceKey, VoicePreferences.Silences, VoicePreferences.SilenceDefault, step => $"{step:0.#} s")),
            ]
            : [VoiceSwitch()]);
        groups.Add(
        [
            new(GetString(Resource.String.voice_service), GetString(Resource.String.voice_service_hint)),
            new(GetString(Resource.String.voice_local), Chosen: choice == VoiceTyping.OnDevice, Tapped: () => ChooseVoiceService(VoiceTyping.OnDevice)),
            new(GetString(Resource.String.voice_lan), Chosen: choice == VoiceTyping.OnLan, Tapped: () => ChooseVoiceService(VoiceTyping.OnLan)),
            new(GetString(Resource.String.voice_remote), Chosen: choice == VoiceTyping.Remote, Tapped: () => ChooseVoiceService(VoiceTyping.Remote)),
        ]);
        var gainRow = new SettingsRow(GetString(Resource.String.voice_gain), $"×{VoicePreferences.Gain(this):0.#}",
            Tapped: () => ChooseVoiceStep(Resource.String.voice_gain, VoicePreferences.GainKey, VoicePreferences.Gains, 1, step => $"×{step:0.#}"));
        if (choice == VoiceTyping.OnLan)
        {
            groups.Add(
            [
                new(GetString(Resource.String.voice_lan), VoicePreferences.LanAddress(this) is { Length: > 0 } ip ? ip : GetString(Resource.String.voice_lan_hint),
                    Tapped: () => AskText(Resource.String.voice_lan, VoicePreferences.LanAddress(this), secret: false, text => kept.Edit()!.PutString(VoicePreferences.LanAddressKey, text.Trim())!.Apply())),
                gainRow,
            ]);
        }
        else if (choice == VoiceTyping.Remote)
        {
            var password = VoicePreferences.Password(this);
            groups.Add(
            [
                new(GetString(Resource.String.voice_address), VoicePreferences.Address(this) is { Length: > 0 } address ? address : notSet,
                    Tapped: () => AskText(Resource.String.voice_address, VoicePreferences.Address(this), secret: false, text => kept.Edit()!.PutString(VoicePreferences.AddressKey, text.Trim())!.Apply())),
                new(GetString(Resource.String.voice_user), VoicePreferences.User(this) is { Length: > 0 } user ? user : notSet,
                    Tapped: () => AskText(Resource.String.voice_user, VoicePreferences.User(this), secret: false, text => kept.Edit()!.PutString(VoicePreferences.UserKey, text.Trim())!.Apply())),
                new(GetString(Resource.String.voice_password), password.Length > 0 ? new string('•', 8) : notSet,
                    Tapped: () => AskText(Resource.String.voice_password, "", secret: true, text => VoicePreferences.SetPassword(this, text))),
                gainRow,
            ]);
        }

        return new(GetString(Resource.String.voice_typing), groups);
    }

    /// <summary>Parar solo al dejar de hablar: turned, the page made again, the silence's row under it while it is on.</summary>
    private SettingsRow VoiceSwitch() =>
        new(GetString(Resource.String.voice_auto_stop), GetString(Resource.String.voice_auto_stop_summary), On: VoicePreferences.AutoStop(this), Switched: turned =>
        {
            KeyPlusPreferences.Global(this).Edit()!.PutBoolean(VoicePreferences.AutoStopKey, turned)!.Apply();
            settings!.Refresh(VoicePage());
        });

    private void ChooseVoiceService(string choice)
    {
        KeyPlusPreferences.Global(this).Edit()!.PutString(VoicePreferences.ServiceKey, choice)!.Apply();
        settings!.Refresh(VoicePage());
    }

    /// <summary>A step of a voice setting chosen among those offered, the one in use marked.</summary>
    private void ChooseVoiceStep(int title, string key, float[] steps, float byDefault, Func<float, string> written)
    {
        var kept = KeyPlusPreferences.Global(this);
        new AlertDialog.Builder(this)
            .SetTitle(title)!
            .SetSingleChoiceItems([.. steps.Select(written)], Array.IndexOf(steps, kept.GetFloat(key, byDefault)), (sender, chosen) =>
            {
                kept.Edit()!.PutFloat(key, steps[chosen.Which])!.Apply();
                (sender as AlertDialog)?.Dismiss();
                settings!.Refresh(VoicePage());
            })!
            .Show();
    }

    /// <summary>The microphone's permission asked, for 🎤.</summary>
    private void AskMicrophone() => RequestPermissions([Android.Manifest.Permission.RecordAudio], MicrophoneRequest);

    /// <summary>A text asked (an address, a user, a password, hidden as typed), the one it has offered; kept as written, an empty one too.</summary>
    private void AskText(int title, string offered, bool secret, Action<string> written)
    {
        var field = new EditText(this) { Text = offered };
        field.SetSingleLine(true);
        if (secret)
        {
            field.InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword;
        }

        field.SelectAll();
        var padding = (int)(20 * Resources!.DisplayMetrics!.Density);
        var frame = new FrameLayout(this);
        frame.SetPadding(padding, padding / 2, padding, 0);
        frame.AddView(field);
        new AlertDialog.Builder(this)
            .SetTitle(title)!
            .SetView(frame)!
            .SetPositiveButton(Resource.String.ok, (_, _) =>
            {
                written(field.Text ?? "");
                settings!.Refresh(VoicePage());
            })!
            .SetNegativeButton(Resource.String.cancel, (_, _) => { })!
            .Show();
    }

    /// <summary>A keyboard's name asked, the one it has offered; an empty one leaves it as it was.</summary>
    private void AskName(string offered, Action<string> named)
    {
        var field = new EditText(this) { Text = offered, Hint = GetString(Resource.String.keyboard_name) };
        field.SetSingleLine(true);
        field.SelectAll();
        var padding = (int)(20 * Resources!.DisplayMetrics!.Density);
        var frame = new FrameLayout(this);
        frame.SetPadding(padding, padding / 2, padding, 0);
        frame.AddView(field);
        new AlertDialog.Builder(this)
            .SetTitle(Resource.String.keyboard_name)!
            .SetView(frame)!
            .SetPositiveButton(Resource.String.ok, (_, _) =>
            {
                if (field.Text?.Trim() is { Length: > 0 } name)
                {
                    named(name);
                }
            })!
            .SetNegativeButton(Resource.String.cancel, (_, _) => { })!
            .Show();
    }

    private SettingsPage LanguagesPage() => new(GetString(Resource.String.languages),
    [
        [new("Español (España)", "QWERTY (Ñ)"), new("English (US)", "QWERTY")],
        [NotMadeRow(Resource.String.add_keyboard, Resource.Drawable.ic_settings_add)],
    ]);

    /// <summary>The keyboard's preferences: the pop-up and Reset; the touch felt and the long press's duration.</summary>
    private SettingsPage PreferencesPage()
    {
        var here = KeyPlusPreferences.ScreenOf(this, Display?.DisplayId ?? Android.Views.Display.DefaultDisplay);
        var kept = KeyPlusPreferences.Global(this);
        var longPress = kept.GetInt(KeyPlusPreferences.LongPressKey, KeyPlusPreferences.LongPressDefault);
        var shows = kept.GetBoolean(KeyPlusPreferences.ShowKeyboardKey, true);
        var focusLost = kept.GetString(KeyPlusPreferences.FocusLostKey, KeyPlusPreferences.FocusLostHide);
        SettingsRow[] showing = shows
            ? [ShowKeyboardRow(true), new(GetString(Resource.String.focus_lost)), .. KeyPlusPreferences.FocusLostChoices.Select(choice => FocusLostRow(choice, choice == focusLost))]
            : [ShowKeyboardRow(false), Switch(Resource.String.keyboard_icon, Resource.String.keyboard_icon_summary, KeyPlusPreferences.KeyboardIconKey)];
        return new(GetString(Resource.String.preferences),
        [
            showing,
            [
                new(GetString(Resource.String.popup), GetString(Resource.String.popup_summary), On: new PanelPlacement(KeyPlusPreferences.ForScreen(this, here)).Floating, Switched: SetPopup),
                Switch(Resource.String.dock_by_drag, Resource.String.dock_by_drag_summary, KeyPlusPreferences.DockByDragKey, byDefault: false),
                Switch(Resource.String.avoid_caret, Resource.String.avoid_caret_summary, KeyPlusPreferences.AvoidCaretKey),
                .. HeadsetRows(),
                new(GetString(Resource.String.avoid_menus), GetString(Resource.String.avoid_menus_summary), On: kept.GetBoolean(KeyPlusPreferences.AvoidMenusKey, true), Switched: on =>
                {
                    kept.Edit()!.PutBoolean(KeyPlusPreferences.AvoidMenusKey, on)!.Apply();
                    settings!.Refresh(PreferencesPage());
                }),
                .. MenuWatchRows(kept.GetBoolean(KeyPlusPreferences.AvoidMenusKey, true)),
                new(GetString(Resource.String.reset), GetString(Resource.String.reset_summary), Tapped: () => new AlertDialog.Builder(this)
                    .SetMessage(Resource.String.reset_ask)!
                    .SetPositiveButton(Resource.String.yes, (_, _) => ResetKeyboard())!
                    .SetNegativeButton(Resource.String.no, (_, _) => { })!
                    .Show()),
            ],
            [
                Switch(Resource.String.vibration, null, KeyPlusPreferences.VibrateKey),
                new(GetString(Resource.String.long_press), GetString(Resource.String.milliseconds, longPress), Tapped: ChooseLongPress),
            ],
        ]);
    }

    /// <summary>On a headset, the choice of the headset's own keyboard, in its box, over the phone's.</summary>
    private IEnumerable<SettingsRow> HeadsetRows()
    {
        if (KeyPlusPreferences.IsHeadset(this))
        {
            yield return Switch(Resource.String.headset_box, Resource.String.headset_box_summary, KeyPlusPreferences.HeadsetBoxKey, byDefault: false);
        }
    }

    /// <summary>Apartarse también de los menús on while the menu watch is off: the row that turns it on.</summary>
    private IEnumerable<SettingsRow> MenuWatchRows(bool avoidMenus)
    {
        if (avoidMenus && !MenuWatchService.On(this))
        {
            yield return new(GetString(Resource.String.menus_watch_off), GetString(Resource.String.menus_watch_off_summary), Tapped: TurnOnMenuWatch);
        }
    }

    /// <summary>
    /// The menu watch turned on: by the app itself where it may write the
    /// secure settings (granted once over Wireless debugging, as the keyboard
    /// is turned on), added to the accessibility services already on; else
    /// Wireless debugging, to grant it, the row tapped again after.
    /// </summary>
    private void TurnOnMenuWatch()
    {
        if (CheckSelfPermission(WriteSecureSettings) != Android.Content.PM.Permission.Granted)
        {
            Toast.MakeText(this, Resource.String.menus_watch_grant, ToastLength.Long)!.Show();
            PairWithWirelessDebugging();
            return;
        }

        var watch = new ComponentName(this, Java.Lang.Class.FromType(typeof(MenuWatchService))).FlattenToString();
        var on = Settings.Secure.GetString(ContentResolver, Settings.Secure.EnabledAccessibilityServices) ?? "";
        var services = on.Split(':', StringSplitOptions.RemoveEmptyEntries).Append(watch).Distinct();
        Settings.Secure.PutString(ContentResolver, Settings.Secure.EnabledAccessibilityServices, string.Join(':', services));
        Settings.Secure.PutInt(ContentResolver, Settings.Secure.AccessibilityEnabled, 1);
        settings!.Refresh(PreferencesPage());
    }

    /// <summary>Mostrar el teclado al escribir: turned, the page made again, the icon's choice under it while it is off.</summary>
    private SettingsRow ShowKeyboardRow(bool on) =>
        new(GetString(Resource.String.show_keyboard), GetString(Resource.String.show_keyboard_summary), On: on, Switched: turned =>
        {
            KeyPlusPreferences.Global(this).Edit()!.PutBoolean(KeyPlusPreferences.ShowKeyboardKey, turned)!.Apply();
            settings!.Refresh(PreferencesPage());
        });

    /// <summary>One of Al perder el foco's three, all in sight under it, the chosen one marked; a tap chooses it.</summary>
    private SettingsRow FocusLostRow(string choice, bool chosen) =>
        new(GetString(FocusLostWords(choice)), Chosen: chosen, Tapped: () =>
        {
            KeyPlusPreferences.Global(this).Edit()!.PutString(KeyPlusPreferences.FocusLostKey, choice)!.Apply();
            settings!.Refresh(PreferencesPage());
        });

    private static int FocusLostWords(string? choice) => choice switch
    {
        KeyPlusPreferences.FocusLostIcon => Resource.String.focus_lost_icon,
        KeyPlusPreferences.FocusLostStay => Resource.String.focus_lost_stay,
        _ => Resource.String.focus_lost_hide,
    };

    /// <summary>The long press's durations offered, Gboard's 300 ms among them.</summary>
    private static readonly int[] LongPresses = [200, 300, 400, 500, 600, 800, 1000];

    private void ChooseLongPress()
    {
        var kept = KeyPlusPreferences.Global(this);
        var now = kept.GetInt(KeyPlusPreferences.LongPressKey, KeyPlusPreferences.LongPressDefault);
        new AlertDialog.Builder(this)
            .SetTitle(Resource.String.long_press)!
            .SetSingleChoiceItems([.. LongPresses.Select(ms => GetString(Resource.String.milliseconds, ms))], Array.IndexOf(LongPresses, now), (sender, chosen) =>
            {
                kept.Edit()!.PutInt(KeyPlusPreferences.LongPressKey, LongPresses[chosen.Which])!.Apply();
                (sender as AlertDialog)?.Dismiss();
                settings!.Refresh(PreferencesPage());
            })!
            .Show();
    }

    /// <summary>A switch kept among the keyboard's settings for every screen, as it is by default until turned.</summary>
    private SettingsRow Switch(int title, int? summary, string key, bool byDefault = true)
    {
        var kept = KeyPlusPreferences.Global(this);
        return new(GetString(title), summary is { } line ? GetString(line) : null, On: kept.GetBoolean(key, byDefault),
            Switched: on => kept.Edit()!.PutBoolean(key, on)!.Apply());
    }

    private SettingsPage GlidePage() => new(GetString(Resource.String.glide),
    [
        [
            Switch(Resource.String.glide, Resource.String.glide_summary, KeyPlusPreferences.SwipeTypingKey),
            Switch(Resource.String.glide_trail, Resource.String.glide_trail_summary, KeyPlusPreferences.SwipeTrailKey),
        ],
        // FUTO Swipe's model weights license asks for this notice in sight.
        [new(GetString(Resource.String.futo))],
    ]);

    /// <summary>The clipboard: screenshots among the copied things, once the keyboard may read the photos; turned off in Android's app info.</summary>
    private SettingsPage ClipboardPage() => new(GetString(Resource.String.clipboard),
    [
        [new(GetString(Resource.String.screenshots), On: ScreenshotWatcher.Allowed(this), Switched: on =>
        {
            if (on)
            {
                RequestPermissions([ScreenshotWatcher.Permission], 0);
            }
            else
            {
                StartActivity(new Intent(Settings.ActionApplicationDetailsSettings, Android.Net.Uri.Parse($"package:{PackageName}")));
            }
        })],
    ]);

    private SettingsPage DesignsPage() => new(GetString(Resource.String.designs),
    [
        [new(GetString(Resource.String.design), GetString(Resource.String.design_summary))],
        [
            new(GetString(Resource.String.export_designs), GetString(Resource.String.export_summary), Tapped: ExportDesigns),
            new(GetString(Resource.String.import_designs), GetString(Resource.String.import_summary), Tapped: ImportDesigns),
        ],
    ]);

    /// <summary>Solución de problemas, as Windows': what Android needs for the keyboard to work as asked.</summary>
    private SettingsPage TroubleshootingPage() => new(GetString(Resource.String.troubleshooting),
    [
        [new(GetString(Resource.String.troubleshooting_menus), GetString(Resource.String.troubleshooting_menus_summary))],
    ]);

    private SettingsPage AboutPage() => new(GetString(Resource.String.about),
    [
        [new(GetString(Resource.String.version), PackageManager!.GetPackageInfo(PackageName!, 0)!.VersionName)],
        [new(GetString(Resource.String.futo))],
        [new(GetString(Resource.String.licences), GetString(Resource.String.licences_summary))],
    ]);

    /// <summary>Whether Berpiztu AI Keyboard is the keyboard in use.</summary>
    private bool IsKeyboardInUse() =>
        Settings.Secure.GetString(ContentResolver, Settings.Secure.DefaultInputMethod) == KeyboardId(this);

    /// <summary>The screens a layout is kept for, each with its own place and size.</summary>
    private static readonly KeyPlusPreferences.Screen[] Screens = Enum.GetValues<KeyPlusPreferences.Screen>();

    /// <summary>
    /// Pop-up chosen or not, for every screen: the keyboard open now takes
    /// it at once (KeyPlusKeyboard watches it).
    /// </summary>
    private void SetPopup(bool windowed)
    {
        foreach (var screen in Screens)
        {
            PanelPlacement.SetFloating(KeyPlusPreferences.ForScreen(this, screen), windowed);
        }
    }

    /// <summary>A file of designs: every screen's, by the screen's name, as the keyboard keeps them.</summary>
    private sealed record DesignsFile(string Kind, int Version, string Device, DateTime Exported, Dictionary<string, string> Designs);

    private const string DesignsKind = "keyplus-designs";

    /// <summary>Export designs: the file to write them to, where the user chooses (Downloads, Drive…).</summary>
    private void ExportDesigns() =>
        StartActivityForResult(new Intent(Intent.ActionCreateDocument)
            .AddCategory(Intent.CategoryOpenable)
            .SetType("application/json")
            .PutExtra(Intent.ExtraTitle, $"keyplus-designs-{DateTime.Now:yyyy-MM-dd_HHmm}.json"), ExportRequest);

    /// <summary>Import designs: the file to read them from, sent from anywhere.</summary>
    private void ImportDesigns() =>
        StartActivityForResult(new Intent(Intent.ActionOpenDocument)
            .AddCategory(Intent.CategoryOpenable)
            .SetType("*/*"), ImportRequest);

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (resultCode != Result.Ok || data?.Data is not { } file || requestCode is not (ExportRequest or ImportRequest))
        {
            return;
        }

        var exporting = requestCode == ExportRequest;
        try
        {
            if (exporting)
            {
                WriteDesigns(file);
            }
            else
            {
                ReadDesigns(file);
            }
        }
        catch (Exception failure)
        {
            Android.Util.Log.Warn("KeyPlusDesigns", $"Designs not {(exporting ? "exported" : "imported")}: {failure}");
            Toast.MakeText(this, $"The designs could not be {(exporting ? "exported" : "imported")}: {failure.Message}", ToastLength.Long)!.Show();
        }
    }

    /// <summary>The keyboards saved in the list (Ajustes › Tema) written to the file chosen, each by its name with its design.</summary>
    private void WriteDesigns(Android.Net.Uri file)
    {
        var designs = Keyboards.All(this)
            .Where(keyboard => !keyboard.BuiltIn && keyboard.Design is not null)
            .GroupBy(keyboard => keyboard.Name)
            .ToDictionary(named => named.Key, named => named.First().Design!);
        var json = System.Text.Json.JsonSerializer.Serialize(
            new DesignsFile(DesignsKind, 1, Build.Model ?? "", DateTime.Now, designs),
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        using (var stream = ContentResolver!.OpenOutputStream(file, "wt")!)
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(json);
        }

        Toast.MakeText(this, designs.Count == 0
            ? "No keyboard saved has a design yet: the file holds none."
            : $"{designs.Count} keyboard(s) exported: {string.Join(", ", designs.Keys)}.", ToastLength.Long)!.Show();
    }

    /// <summary>
    /// A file's designs read and added to the list as PC keyboards, nothing
    /// replaced: each by its name, one kept before the list
    /// by its screen's ("My design – phone").
    /// </summary>
    private void ReadDesigns(Android.Net.Uri file)
    {
        string json;
        using (var stream = ContentResolver!.OpenInputStream(file)!)
        using (var reader = new StreamReader(stream))
        {
            json = reader.ReadToEnd();
        }

        var read = System.Text.Json.JsonSerializer.Deserialize<DesignsFile>(json);
        var designs = (read?.Kind == DesignsKind ? read.Designs : null)?
            .Where(design => KeyboardDesign.Reads(design.Value))
            .Select(design => (Name: Enum.TryParse<KeyPlusPreferences.Screen>(design.Key, out var screen)
                ? GetString(Resource.String.my_design, new Java.Lang.String(Keyboards.ScreenName(this, screen)))
                : design.Key, Design: design.Value))
            .ToList();
        if (designs is null || designs.Count == 0)
        {
            Toast.MakeText(this, "That file holds no keyboard design.", ToastLength.Long)!.Show();
            return;
        }

        foreach (var (name, design) in designs)
        {
            Keyboards.SaveAs(this, name, Keyboards.PcBase, design);
        }

        Toast.MakeText(this, $"{designs.Count} keyboard(s) added to Tema: {string.Join(", ", designs.Select(design => design.Name))}.", ToastLength.Long)!.Show();
    }

    /// <summary>
    /// Every screen's design as it is now, each copied to a file of its own
    /// in the app's files (designs-history), before a Reset clears them:
    /// none is lost, none written over.
    /// </summary>
    private void KeepDesignsAside()
    {
        var folder = Path.Combine(FilesDir!.AbsolutePath, "designs-history");
        Directory.CreateDirectory(folder);
        var now = DateTime.Now.ToString("yyyy-MM-dd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var screen in Screens)
        {
            if (KeyboardDesign.Kept(KeyPlusPreferences.ForScreen(this, screen)) is { } design)
            {
                File.WriteAllText(Path.Combine(folder, $"{now}_{screen}.txt"), design);
            }
        }
    }

    /// <summary>
    /// Every screen's place, size, mode and labels forgotten: the keyboard
    /// as first installed, docked at the bottom as wide as the screen at its
    /// usual size. The keyboard open now takes it at once.
    /// </summary>
    private void ResetKeyboard()
    {
        KeepDesignsAside();
        foreach (var screen in Screens)
        {
            KeyPlusPreferences.ForScreen(this, screen).Edit()!.Clear()!.Apply();
        }

        ShowStatus();
    }

    /// <summary>
    /// Berpiztu AI Keyboard made the keyboard in use: turned on first, where
    /// it is not, in Android's keyboard settings; then chosen by the app itself
    /// where it may write the secure settings (<see cref="WriteSecureSettings"/>),
    /// else in Android's choice of keyboard. The list of keyboards turned on
    /// is not written here: Android 14 lets no new app read it, and written
    /// whole it would lose the languages other keyboards keep in it (Gboard's).
    /// </summary>
    private void UseKeyboard()
    {
        if (!IsKeyboardOn())
        {
            StartActivity(new Intent(Settings.ActionInputMethodSettings));
            return;
        }

        if (CheckSelfPermission(WriteSecureSettings) != Android.Content.PM.Permission.Granted)
        {
            ((InputMethodManager)GetSystemService(InputMethodService)!).ShowInputMethodPicker();
            return;
        }

        Settings.Secure.PutString(ContentResolver, Settings.Secure.DefaultInputMethod, KeyboardId(this));
        ShowStatus();
    }


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
        if (CheckSelfPermission(WriteSecureSettings) != Android.Content.PM.Permission.Granted)
        {
            try
            {
                StartActivity(new Intent(AccessibilityDetailsSettings).PutExtra(Intent.ExtraComponentName, ServiceId(this)));
            }
            catch (ActivityNotFoundException)
            {
                StartActivity(new Intent(Settings.ActionAccessibilitySettings));
            }

            return;
        }

        TurnOnService(this);
        status!.PostDelayed(ShowStatus, 2000);
        if (!IsServiceOn())
        {
            status.Text = "Turning KeyPlus on…";
        }
    }

    /// <summary>
    /// The KeyPlus keys service written into the secure settings as on, the
    /// services already on kept: for an app that may write them
    /// (<see cref="WriteSecureSettings"/>), this screen or <see cref="AdbGrant"/>.
    /// </summary>
    internal static void TurnOnService(Context context)
    {
        var resolver = context.ContentResolver;
        var listed = Settings.Secure.GetString(resolver, Settings.Secure.EnabledAccessibilityServices) ?? "";
        var others = listed.Split(':', StringSplitOptions.RemoveEmptyEntries).Where(name => !name.StartsWith($"{context.PackageName}/", StringComparison.Ordinal));
        Settings.Secure.PutString(resolver, Settings.Secure.EnabledAccessibilityServices, string.Join(':', others.Append(ServiceId(context))));
        Settings.Secure.PutInt(resolver, Settings.Secure.AccessibilityEnabled, 1);
    }

    /// <summary>The KeyPlus keys service as Android names it.</summary>
    private static string ServiceId(Context context) =>
        new ComponentName(context.PackageName!, $"{context.PackageName}.KeyPlusService").FlattenToShortString()!;

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
            Hint = GetString(Resource.String.try_keys),
            InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextFlagMultiLine,
            Gravity = GravityFlags.Top | GravityFlags.Left,
            VerticalScrollBarEnabled = true,
        };
        box.SetLines(3);
        box.SetMaxLines(3);
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
