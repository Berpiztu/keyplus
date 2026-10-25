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
    private const int DictionaryExportRequest = 4;
    private const int DictionaryImportRequest = 5;
    private const int LogSaveRequest = 6;
    private const int AddressesExportRequest = 7;
    private const int AddressesImportRequest = 8;

    /// <summary>The log's box, Windows': as tall whatever it holds, its type small and of one width.</summary>
    private const int LogViewerDp = 220;
    private const float LogSp = 11;

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
        content.AddView(Button(GetString(Resource.String.updates_check), () => _ = AndroidUpdates.CheckAsync(this, asked: true)));
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
        HandleUpdates();
#if KEYBOARD
        if (Intent?.GetBooleanExtra(AskMicrophoneExtra, false) == true)
        {
            Intent.RemoveExtra(AskMicrophoneExtra);
            settings!.OpenSection(VoicePage);
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
    /// The app opened by Android's installer (how the update went), by the
    /// update's notification (whether to update), or by hand, when the day's
    /// check is due (<see cref="AndroidUpdates"/>).
    /// </summary>
    private void HandleUpdates()
    {
        if (Intent?.GetBooleanExtra(AndroidUpdates.InstallExtra, false) == true)
        {
            Intent.RemoveExtra(AndroidUpdates.InstallExtra);
            AndroidUpdates.Installed(this, Intent);
        }
        else if (Intent?.GetBooleanExtra(AndroidUpdates.AskExtra, false) == true || AndroidUpdates.Due(this))
        {
            Intent?.RemoveExtra(AndroidUpdates.AskExtra);
            _ = AndroidUpdates.CheckAsync(this, asked: false);
        }
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
    private View? tryBox;

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
            new(GetString(Resource.String.languages), GetString(Resource.String.languages_summary), Resource.Drawable.ic_key_language, () => settings!.Open(LanguagesPage), Opens: true),
            new(GetString(Resource.String.preferences), Icon: Resource.Drawable.ic_settings_tune, Tapped: () => settings!.Open(PreferencesPage), Opens: true),
            new(GetString(Resource.String.theme), Icon: Resource.Drawable.ic_grid_theme, Tapped: () => settings!.Open(ThemePage), Opens: true),
        ],
        [
            new(GetString(Resource.String.spelling), Icon: Resource.Drawable.ic_settings_spellcheck, Tapped: () => settings!.Open(SpellingPage), Opens: true),
            new(GetString(Resource.String.glide), Icon: Resource.Drawable.ic_settings_gesture, Tapped: () => settings!.Open(GlidePage), Opens: true),
            new(GetString(Resource.String.voice_typing), Icon: Resource.Drawable.ic_tool_mic, Tapped: () => settings!.Open(VoicePage), Opens: true),
            NotMadeRow(Resource.String.write_fields, Resource.Drawable.ic_settings_stylus),
            NotMadeRow(Resource.String.emoji_settings, Resource.Drawable.ic_key_emoji),
        ],
        [
            new(GetString(Resource.String.clipboard), Icon: Resource.Drawable.ic_clipboard, Tapped: () => settings!.Open(ClipboardPage), Opens: true),
            new(GetString(Resource.String.dictionary), Icon: Resource.Drawable.ic_settings_dictionary, Tapped: () => settings!.Open(DictionaryPage), Opens: true),
            new(GetString(Resource.String.designs), Icon: Resource.Drawable.ic_settings_dashboard_customize, Tapped: () => settings!.Open(DesignsPage), Opens: true),
        ],
        [
            NotMadeRow(Resource.String.privacy, Resource.Drawable.ic_settings_shield),
            NotMadeRow(Resource.String.share, Resource.Drawable.ic_grid_share),
            new(GetString(Resource.String.about), Icon: Resource.Drawable.ic_settings_info, Tapped: () => settings!.Open(AboutPage), Opens: true),
            new(GetString(Resource.String.troubleshooting), Icon: Resource.Drawable.ic_settings_help, Tapped: () => settings!.Open(TroubleshootingPage), Opens: true),
            NotMadeRow(Resource.String.help, Resource.Drawable.ic_settings_help),
        ],
    ]);

    private SettingsRow NotMadeRow(int title, int icon) =>
        new(GetString(title), Icon: icon, Tapped: () => Toast.MakeText(this, GetString(Resource.String.not_made_yet, new Java.Lang.String(GetString(title))), ToastLength.Short)!.Show());

    /// <summary>Tema, the Windows app's (<see cref="ThemeView"/>), for the screen the settings are on.</summary>
    private SettingsPage ThemePage() =>
        new(GetString(Resource.String.theme), [], new ThemeView(this, settings!, KeyPlusPreferences.ScreenOf(this, Display?.DisplayId ?? Android.Views.Display.DefaultDisplay), () => settings!.Refresh(ThemePage())));

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
                new(GetString(Resource.String.voice_silence), $"{VoicePreferences.Silence(this):0.#} s\n{GetString(Resource.String.voice_silence_hint)}", Tapped: () => ChooseVoiceStep(Resource.String.voice_silence, VoicePreferences.SilenceKey, VoicePreferences.Silences, VoicePreferences.SilenceDefault, step => $"{step:0.#} s")),
            ]
            : [VoiceSwitch()]);
        groups.Add(
        [
            new(GetString(Resource.String.voice_service), GetString(Resource.String.voice_service_hint)),
            new(GetString(Resource.String.voice_local), Chosen: choice == VoiceTyping.OnDevice, Tapped: () => ChooseVoiceService(VoiceTyping.OnDevice)),
            new(GetString(Resource.String.voice_lan), Chosen: choice == VoiceTyping.OnLan, Tapped: () => ChooseVoiceService(VoiceTyping.OnLan)),
            new(GetString(Resource.String.voice_remote), Chosen: choice == VoiceTyping.Remote, Tapped: () => ChooseVoiceService(VoiceTyping.Remote)),
        ]);
        // What ia-voice hears by, on the local network or remote: Android's own recognizer listens by the system's.
        var microphoneRow = new SettingsRow(GetString(Resource.String.voice_microphone),
            VoicePreferences.Microphone(this) is { Length: > 0 } microphone ? microphone : GetString(Resource.String.voice_microphone_default), Tapped: ChooseMicrophone);
        var gainRow = new SettingsRow(GetString(Resource.String.voice_gain), $"×{VoicePreferences.Gain(this):0.#}\n{GetString(Resource.String.voice_gain_hint)}",
            Tapped: () => ChooseVoiceStep(Resource.String.voice_gain, VoicePreferences.GainKey, VoicePreferences.Gains, 1, step => $"×{step:0.#}"));
        if (choice == VoiceTyping.OnLan)
        {
            groups.Add(
            [
                new(GetString(Resource.String.voice_lan), VoicePreferences.LanAddress(this) is { Length: > 0 } ip ? ip : GetString(Resource.String.voice_lan_hint),
                    Tapped: () => AskText(Resource.String.voice_lan, VoicePreferences.LanAddress(this), secret: false, text => KeepVoice(VoicePreferences.LanAddressKey, text))),
                microphoneRow,
                gainRow,
            ]);
        }
        else if (choice == VoiceTyping.Remote)
        {
            var password = VoicePreferences.Password(this);
            groups.Add(
            [
                new(GetString(Resource.String.voice_address), VoicePreferences.Address(this) is { Length: > 0 } address ? address : notSet,
                    Tapped: () => AskText(Resource.String.voice_address, VoicePreferences.Address(this), secret: false, text => KeepVoice(VoicePreferences.AddressKey, text))),
                new(GetString(Resource.String.voice_user), VoicePreferences.User(this) is { Length: > 0 } user ? user : notSet,
                    Tapped: () => AskText(Resource.String.voice_user, VoicePreferences.User(this), secret: false, text => KeepVoice(VoicePreferences.UserKey, text))),
                new(GetString(Resource.String.voice_password), password.Length > 0 ? new string('•', 8) : notSet,
                    Tapped: () => AskText(Resource.String.voice_password, "", secret: true, text =>
                    {
                        VoicePreferences.SetPassword(this, text);
                        settings!.Refresh(VoicePage());
                    })),
                microphoneRow,
                gainRow,
            ]);
        }

        return new(GetString(Resource.String.voice_typing), groups);
    }

    /// <summary>A voice setting written in its dialog kept, the page made again with it.</summary>
    private void KeepVoice(string key, string text)
    {
        KeyPlusPreferences.Global(this).Edit()!.PutString(key, text.Trim())!.Apply();
        settings!.Refresh(VoicePage());
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

    /// <summary>The microphone chosen, as Windows': the system's default or one of those connected now, the one in use marked.</summary>
    private void ChooseMicrophone()
    {
        string[] names = [GetString(Resource.String.voice_microphone_default), .. AndroidMicrophone.Names(this)];
        var kept = KeyPlusPreferences.Global(this);
        new AlertDialog.Builder(this)
            .SetTitle(Resource.String.voice_microphone)!
            .SetSingleChoiceItems(names, Math.Max(0, Array.IndexOf(names, VoicePreferences.Microphone(this))), (sender, chosen) =>
            {
                kept.Edit()!.PutString(VoicePreferences.MicrophoneKey, chosen.Which == 0 ? "" : names[chosen.Which])!.Apply();
                (sender as AlertDialog)?.Dismiss();
                settings!.Refresh(VoicePage());
            })!
            .Show();
    }

    /// <summary>The microphone's permission asked, for 🎤.</summary>
    private void AskMicrophone() => RequestPermissions([Android.Manifest.Permission.RecordAudio], MicrophoneRequest);

    /// <summary>A text asked (an address, a user, a password, hidden as typed), the one it has offered; kept as written, an empty one too.</summary>
    private void AskText(int title, string offered, bool secret, Action<string> written, bool address = false)
    {
        var field = new EditText(this) { Text = offered };
        field.SetSingleLine(true);
        if (secret)
        {
            field.InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword;
        }
        else if (address)
        {
            // An e-mail address's field: the keyboard's @ and .com, the addresses kept offered, nothing corrected.
            field.InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationEmailAddress;
        }

        field.SelectAll();
        var padding = (int)(20 * Resources!.DisplayMetrics!.Density);
        var frame = new FrameLayout(this);
        frame.SetPadding(padding, padding / 2, padding, 0);
        frame.AddView(field);
        new AlertDialog.Builder(this)
            .SetTitle(title)!
            .SetView(frame)!
            .SetPositiveButton(Resource.String.ok, (_, _) => written(field.Text ?? ""))!
            .SetNegativeButton(Resource.String.cancel, (_, _) => { })!
            .Show();
    }

    /// <summary>Idiomas: the keyboard's languages, each opening its page with More options (its Spelling, its Glide typing, its words).</summary>
    private SettingsPage LanguagesPage() => new(GetString(Resource.String.languages),
    [
        LanguageRows(),
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
                .. FloatingRows(here),
                Switch(Resource.String.avoid_caret, Resource.String.avoid_caret_summary, KeyPlusPreferences.AvoidCaretKey),
                Switch(Resource.String.emoji_row, Resource.String.emoji_row_summary, KeyPlusPreferences.EmojiRowKey),
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
                Switch(Resource.String.key_sound, null, KeyPlusPreferences.SoundKey, byDefault: false),
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

    /// <summary>
    /// The pop-up keyboard and docking by dragging, off a headset alone: there the headset places the keyboard's window in
    /// a box of its own, and the keyboard stays docked, filling it.
    /// </summary>
    private IEnumerable<SettingsRow> FloatingRows(KeyPlusPreferences.Screen here)
    {
        if (!KeyPlusPreferences.IsHeadset(this))
        {
            yield return new(GetString(Resource.String.popup), GetString(Resource.String.popup_summary), On: new PanelPlacement(KeyPlusPreferences.ForScreen(this, here)).Floating, Switched: SetPopup);
            yield return Switch(Resource.String.dock_by_drag, Resource.String.dock_by_drag_summary, KeyPlusPreferences.DockByDragKey, byDefault: false);
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

    /// <summary>
    /// Spelling, what goes for every language, as Windows': all of it on and
    /// off at once (the keyboard's spellcheck tool too), off while no
    /// language has anything downloaded to correct with and saying so then;
    /// Download again, for what every language's choices turned on fetched;
    /// and the languages, each opening its page, where its Spelling is.
    /// </summary>
    private SettingsPage SpellingPage()
    {
        bool AnyCorrects() => StandardKeys.Languages.Any(language => LanguageChoices.CanCorrect(this, language.Code));
        var turnedOn = StandardKeys.Languages
            .SelectMany(language => LanguageOptions.SpellingChoices(language.Code, Text)
                .Where(choice => choice.Fetches is not null && choice.Get(LanguageChoices.SpellingIn(this, language.Code)))
                .Select(choice => choice.Fetches!))
            .Concat(StandardKeys.Languages.Where(language => LanguageChoices.GlidesIn(this, language.Code)).Select(LanguageOptions.GlideOf))
            .DistinctBy(fetched => fetched.Named)
            .ToList();
        var again = turnedOn.Select(fetched => downloading.GetValueOrDefault(fetched.Named)).FirstOrDefault(state => state is not null);
        List<SettingsRow> general =
        [
            new(GetString(Resource.String.spelling_on), GetString(Resource.String.spelling_on_summary), On: LanguageChoices.SpellingOn(this) && AnyCorrects(),
                Switched: on =>
                {
                    if (on && !AnyCorrects())
                    {
                        Log.Write("Spelling: nothing downloaded to correct with, so Correct as I type stays off");
                        Told(Resource.String.spelling, GetString(Resource.String.spelling_nothing_downloaded));
                        settings!.Refresh(SpellingPage());
                        return;
                    }

                    LanguageChoices.SetSpellingOn(this, on);
                }),
            Switch(Resource.String.spelling_capitalize, Resource.String.spelling_capitalize_summary, KeyPlusPreferences.CapitalizeSentencesKey, byDefault: false),
        ];
        if (turnedOn.Count > 0)
        {
            general.Add(new(GetString(Resource.String.spelling_download_again), again ?? GetString(Resource.String.spelling_download_again_summary), Tapped: async () =>
            {
                foreach (var fetched in turnedOn)
                {
                    await FetchAsync(fetched, SpellingPage);
                }
            }));
        }

        return new(GetString(Resource.String.spelling), [general, LanguageRows(Resource.String.spelling_by_language)]);
    }

    private PersonalWords Personal()
    {
        var personal = new PersonalWords(Path.Combine(FilesDir!.AbsolutePath, PersonalWords.FileName));
        personal.Reload();
        return personal;
    }

    /// <summary>
    /// Dictionary (<see cref="PersonalWords"/>), what goes for every language,
    /// as Windows': offering new words to learn and learning from the words
    /// chosen for a swipe, the words chosen forgotten, every language's words
    /// exported; and the languages, each opening its page, where its words are.
    /// </summary>
    private SettingsPage DictionaryPage()
    {
        var personal = Personal();
        return new(GetString(Resource.String.dictionary),
        [
            [
                new(GetString(Resource.String.dictionary_learn_typed), GetString(Resource.String.dictionary_learn_typed_summary), On: personal.LearnTyped,
                    Switched: on => personal.SetLearning(on, personal.LearnChosen)),
                new(GetString(Resource.String.dictionary_learn_chosen), GetString(Resource.String.dictionary_learn_chosen_summary), On: personal.LearnChosen,
                    Switched: on => personal.SetLearning(personal.LearnTyped, on)),
                Switch(Resource.String.dictionary_learn_emails, Resource.String.dictionary_learn_emails_summary, KeyPlusPreferences.LearnEmailsKey),
                new(GetString(Resource.String.dictionary_forget_chosen), Tapped: personal.ForgetChosen),
                new(GetString(Resource.String.dictionary_export), GetString(Resource.String.dictionary_export_summary), Tapped: () =>
                    StartActivityForResult(new Intent(Intent.ActionCreateDocument)
                        .AddCategory(Intent.CategoryOpenable)
                        .SetType("application/json")
                        .PutExtra(Intent.ExtraTitle, $"berpiztu-dictionary-{DateTime.Now:yyyy-MM-dd_HHmm}.json"), DictionaryExportRequest)),
            ],
            LanguageRows(Resource.String.dictionary_by_language),
            [
                new(GetString(Resource.String.dictionary_addresses), GetString(Resource.String.dictionary_addresses_summary)),
                new(GetString(Resource.String.dictionary_import_addresses), GetString(Resource.String.dictionary_import_addresses_summary), Tapped: () =>
                    StartActivityForResult(new Intent(Intent.ActionOpenDocument)
                        .AddCategory(Intent.CategoryOpenable)
                        .SetType("*/*"), AddressesImportRequest)),
                new(GetString(Resource.String.dictionary_export_addresses), GetString(Resource.String.dictionary_export_addresses_summary), Tapped: () =>
                    StartActivityForResult(new Intent(Intent.ActionCreateDocument)
                        .AddCategory(Intent.CategoryOpenable)
                        .SetType("application/json")
                        .PutExtra(Intent.ExtraTitle, $"berpiztu-addresses-{DateTime.Now:yyyy-MM-dd_HHmm}.json"), AddressesExportRequest)),
                new(GetString(Resource.String.dictionary_add_address), Tapped: () =>
                    AskText(Resource.String.dictionary_add_address, "", secret: false, written =>
                    {
                        // Only an address: anything else belongs in a language's words.
                        if (!PersonalWords.IsAddress(written))
                        {
                            Log.Write("Dictionary: an address not added, for it has no @");
                            Told(Resource.String.dictionary_addresses, GetString(Resource.String.dictionary_not_address));
                            return;
                        }

                        personal.Add(PersonalWords.Shared, written);
                        settings!.Refresh(DictionaryPage());
                    }, address: true)),
                .. personal.Addresses.Select(address => new SettingsRow(address,
                    Removed: () =>
                    {
                        personal.Remove(PersonalWords.Shared, address);
                        settings!.Refresh(DictionaryPage());
                    },
                    Edited: () => AskText(Resource.String.dictionary_edit, address, secret: false, written =>
                    {
                        personal.Rename(PersonalWords.Shared, address, written);
                        settings!.Refresh(DictionaryPage());
                    }, address: true))),
            ],
        ]);
    }

    /// <summary>The language whose page a dictionary is being imported from, its words without a language of their own going into it.</summary>
    private StandardLanguage dictionaryLanguage = StandardKeys.Spanish;

    /// <summary>The keyboard's languages, each a row with More options opening its page (<see cref="LanguagePage"/>), under a line saying what is there; on Languages, Spelling, Dictionary and Glide typing alike, so a language added comes with its own.</summary>
    private IReadOnlyList<SettingsRow> LanguageRows(int? said = null) =>
        [
            .. said is { } line ? [new SettingsRow(GetString(line))] : Array.Empty<SettingsRow>(),
            .. StandardKeys.Languages.Select(language => new SettingsRow(language.Name, GetString(Resource.String.more_options),
                Tapped: () => settings!.Open(() => LanguagePage(language)), Opens: true)),
        ];

    /// <summary>
    /// A language's own page, as Windows': its Spelling, only what Spelling
    /// has for it (<see cref="LanguageOptions.SpellingChoices"/>); its Glide
    /// typing; and its words, a word added, a dictionary imported into it,
    /// then each word with ✏ and a red ✕. A choice that fetches files fetches
    /// them as it is turned on, the row under it saying how much while they
    /// come, or, turned on without them, that they are missing, tapped to
    /// download them; nothing is fetched otherwise.
    /// </summary>
    private SettingsPage LanguagePage(StandardLanguage language)
    {
        var code = language.Code;
        SettingsPage Again() => LanguagePage(language);
        List<SettingsRow> spelling = [new(GetString(Resource.String.spelling))];
        foreach (var choice in LanguageOptions.SpellingChoices(code, Text))
        {
            spelling.AddRange(ChoiceRows(choice.Label, choice.Summary, choice.Get(LanguageChoices.SpellingIn(this, code)), choice.Fetches,
                on => LanguageChoices.SetSpelling(this, code, choice.Set(LanguageChoices.SpellingIn(this, code), on)), Again));
        }

        List<SettingsRow> glide =
        [
            new(GetString(Resource.String.glide)),
            .. ChoiceRows(Resource.String.glide, LanguageOptions.GlideSummary(language, Text), LanguageChoices.GlidesIn(this, code), LanguageOptions.GlideOf(language),
                on => LanguageChoices.SetGlide(this, code, on), Again),
        ];
        var personal = Personal();
        List<IReadOnlyList<SettingsRow>> groups =
        [
            spelling,
            glide,
            [
                new(GetString(Resource.String.dictionary)),
                new(GetString(Resource.String.dictionary_import), GetString(Resource.String.dictionary_import_summary), Tapped: () =>
                {
                    dictionaryLanguage = language;
                    StartActivityForResult(new Intent(Intent.ActionOpenDocument)
                        .AddCategory(Intent.CategoryOpenable)
                        .SetType("*/*"), DictionaryImportRequest);
                }),
                new(GetString(Resource.String.dictionary_add), GetString(Resource.String.dictionary_words_summary), Tapped: () =>
                    AskText(Resource.String.dictionary_add, "", secret: false, written =>
                    {
                        personal.Add(code, written);
                        settings!.Refresh(Again());
                    })),
            ],
        ];
        var words = personal.Words(code);
        if (words.Count > 0)
        {
            groups.Add([.. words.Select(word => new SettingsRow(word,
                Removed: () =>
                {
                    personal.Remove(code, word);
                    settings!.Refresh(Again());
                },
                Edited: () => AskText(Resource.String.dictionary_edit, word, secret: false, written =>
                {
                    personal.Rename(code, word, written);
                    settings!.Refresh(Again());
                })))]);
        }

        return new(language.Name, groups);
    }

    /// <summary>What is being downloaded now, by its name, and how much: its row says so while it comes.</summary>
    private readonly Dictionary<string, string> downloading = [];

    /// <summary>
    /// A choice's rows: its switch, kept as it is turned (<paramref name="keep"/>);
    /// where it fetches files, fetched as it is turned on, and under it a row
    /// saying how much while they come, that they are here, or, turned on
    /// without them, that they are missing, tapped to download them.
    /// </summary>
    private IEnumerable<SettingsRow> ChoiceRows(int label, string summary, bool on, Fetched? fetches, Action<bool> keep, Func<SettingsPage> page)
    {
        yield return new(GetString(label), summary, On: on, Switched: async turned =>
        {
            if (turned && fetches is not null && !fetches.Downloaded())
            {
                await FetchAsync(fetches, page);
                turned = fetches.Downloaded();
            }

            keep(turned);
            settings?.Refresh(page());
        });
        if (fetches is null)
        {
            yield break;
        }

        if (downloading.TryGetValue(fetches.Named, out var coming))
        {
            yield return new(coming);
        }
        else if (fetches.Downloaded())
        {
            yield return new(GetString(fetches.Done));
        }
        else if (on)
        {
            yield return new(GetString(Resource.String.download), GetString(Resource.String.spelling_not_downloaded), Tapped: async () => await FetchAsync(fetches, page));
        }
    }

    /// <summary>
    /// What a choice fetches fetched, how much said in its row as it comes (the
    /// page made again a few times a second at most); a failure said in a
    /// window and the log, the files there before kept until new ones are checked.
    /// </summary>
    private async Task FetchAsync(Fetched what, Func<SettingsPage> page)
    {
        var shown = 0L;
        downloading[what.Named] = Said(what.Downloading, Megabytes(0), "…");
        settings?.Refresh(page());
        try
        {
            await what.Download(new Progress<(long Done, long Total)>(at =>
            {
                downloading[what.Named] = Said(what.Downloading, Megabytes(at.Done), Megabytes(Math.Max(at.Total, at.Done)));
                if (System.Environment.TickCount64 - shown > 300)
                {
                    shown = System.Environment.TickCount64;
                    settings?.Refresh(page());
                }
            }), CancellationToken.None);
        }
        catch (Exception failure) when (failure is System.Net.Http.HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
        {
            Log.Write($"Downloads: {what.Named} could not be downloaded: {failure.Message}");
            Told(Resource.String.spelling, Said(what.Failed, failure.Message));
        }
        finally
        {
            downloading.Remove(what.Named);
            settings?.Refresh(page());
        }
    }

    /// <summary>Megabytes as a row says them.</summary>
    private static string Megabytes(long bytes) => (bytes / 1_000_000.0).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>A string by its id, filled with the values given: the words the shared language options are made with.</summary>
    private string Text(int id, object[] values) => GetString(id, [.. values.Select(value => (Java.Lang.Object)new Java.Lang.String(value.ToString() ?? ""))]);

    /// <summary>A string by its id, filled with the values given, each as text.</summary>
    private string Said(int id, params object[] values) => Text(id, values);

    /// <summary>Something said in a window with OK: a failure, or why a choice stays as it was.</summary>
    private void Told(int title, string said) =>
        new AlertDialog.Builder(this)
            .SetTitle(title)!
            .SetMessage(said)!
            .SetPositiveButton(Resource.String.ok, (_, _) => { })!
            .Show();

    /// <summary>
    /// The dictionary written to the file chosen (every language's words), or
    /// a file read into it, its words added and none taken away; what came of
    /// it told, a failure written to the log too.
    /// </summary>
    private void DictionaryFile(Android.Net.Uri file, bool exporting, bool onlyAddresses = false)
    {
        var personal = Personal();
        try
        {
            if (exporting)
            {
                using (var stream = ContentResolver!.OpenOutputStream(file, "wt")!)
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(personal.Export(onlyAddresses ? PersonalWords.Shared : null));
                }

                var (said, count) = onlyAddresses ? (Resource.String.dictionary_addresses_exported, personal.Addresses.Count) : (Resource.String.dictionary_exported, personal.Count);
                Toast.MakeText(this, GetString(said, new Java.Lang.String(count.ToString(System.Globalization.CultureInfo.CurrentCulture))), ToastLength.Long)!.Show();
                return;
            }

            string text;
            using (var stream = ContentResolver!.OpenInputStream(file)!)
            using (var reader = new StreamReader(stream))
            {
                text = reader.ReadToEnd();
            }

            var added = personal.Import(text, onlyAddresses ? PersonalWords.Shared : dictionaryLanguage.Code);
            settings?.Refresh(onlyAddresses ? DictionaryPage() : LanguagePage(dictionaryLanguage));
            Toast.MakeText(this, GetString(onlyAddresses ? Resource.String.dictionary_addresses_imported : Resource.String.dictionary_imported,
                new Java.Lang.String(added.ToString(System.Globalization.CultureInfo.CurrentCulture))), ToastLength.Long)!.Show();
        }
        catch (Exception failure) when (failure is IOException or System.Text.Json.JsonException or InvalidDataException or Java.Lang.Exception)
        {
            Log.Write($"Dictionary: the file could not be read or written: {failure.Message}");
            new AlertDialog.Builder(this)
                .SetTitle(Resource.String.dictionary)!
                .SetMessage(GetString(Resource.String.dictionary_file_failed, new Java.Lang.String(failure.Message)))!
                .SetPositiveButton(Resource.String.ok, (_, _) => { })!
                .Show();
        }
    }

    /// <summary>Glide typing, what goes for every language (its trail, deleting a swiped word whole); each language's own on its page.</summary>
    private SettingsPage GlidePage() => new(GetString(Resource.String.glide),
    [
        [
            Switch(Resource.String.glide_trail, Resource.String.glide_trail_summary, KeyPlusPreferences.SwipeTrailKey),
            Switch(Resource.String.glide_delete_word, Resource.String.glide_delete_word_summary, KeyPlusPreferences.SwipeDeleteWordKey),
        ],
        LanguageRows(Resource.String.glide_by_language),
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

    /// <summary>
    /// Solución de problemas, as Windows': what Android needs for the keyboard
    /// to work as asked; the keyboard's log (<see cref="Log"/>), Detailed log,
    /// what it keeps word for word in a box of one size, Refresh, Clear the
    /// log, and, for it cannot be opened where Android keeps it, a copy saved
    /// where one chooses, to send it.
    /// </summary>
    private SettingsPage TroubleshootingPage() => new(GetString(Resource.String.troubleshooting),
    [
        [new(GetString(Resource.String.troubleshooting_menus), GetString(Resource.String.troubleshooting_menus_summary))],
        [
            new(GetString(Resource.String.troubleshooting_log), GetString(Resource.String.troubleshooting_log_summary)),
            Switch(Resource.String.troubleshooting_detailed_log, Resource.String.troubleshooting_detailed_log_summary, KeyPlusPreferences.DetailedLogKey, byDefault: false),
            new(GetString(Resource.String.troubleshooting_log_view), GetString(Resource.String.troubleshooting_log_view_summary), Below: LogViewer()),
            new(GetString(Resource.String.troubleshooting_log_refresh), Tapped: () => settings!.Refresh(TroubleshootingPage())),
            new(GetString(Resource.String.troubleshooting_log_clear), Tapped: () =>
            {
                Log.Clear();
                settings!.Refresh(TroubleshootingPage());
            }),
            new(GetString(Resource.String.troubleshooting_log_save), GetString(Resource.String.troubleshooting_log_save_summary), Tapped: () =>
                StartActivityForResult(new Intent(Intent.ActionCreateDocument)
                    .AddCategory(Intent.CategoryOpenable)
                    .SetType("text/plain")
                    .PutExtra(Intent.ExtraTitle, $"keyboard-log-{DateTime.Now:yyyy-MM-dd_HHmm}.txt"), LogSaveRequest)),
        ],
    ]);

    /// <summary>
    /// What the log keeps, word for word, at its end, in a box of one size
    /// that scrolls both ways, as Windows': its lines are never wrapped. The
    /// page's list lets it have the finger while it scrolls.
    /// </summary>
    private View LogViewer()
    {
        var shown = new TextView(this) { Text = Log.Read(), Typeface = Android.Graphics.Typeface.Monospace };
        shown.SetTextSize(Android.Util.ComplexUnitType.Sp, LogSp);
        shown.SetTextColor(settings!.Words);
        var across = new HorizontalScrollView(this);
        across.AddView(shown);
        var down = new ScrollView(this)
        {
            LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, (int)Math.Round(LogViewerDp * Resources!.DisplayMetrics!.Density)) { TopMargin = (int)Math.Round(8 * Resources.DisplayMetrics.Density) },
        };
        down.AddView(across);
        down.Touch += (_, touched) =>
        {
            down.Parent?.RequestDisallowInterceptTouchEvent(true);
            touched.Handled = false;
        };
        down.Post(() => down.FullScroll(FocusSearchDirection.Down));
        return down;
    }

    /// <summary>A copy of the log written to the file chosen; what came of it told, a failure written to the log too.</summary>
    private void SaveLog(Android.Net.Uri file)
    {
        try
        {
            using (var stream = ContentResolver!.OpenOutputStream(file, "wt")!)
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(Log.Read());
            }

            Toast.MakeText(this, Resource.String.troubleshooting_log_saved, ToastLength.Long)!.Show();
        }
        catch (Exception failure) when (failure is IOException or Java.Lang.Exception)
        {
            Log.Write($"Troubleshooting: the copy of the log could not be written: {failure.Message}");
            Told(Resource.String.troubleshooting, Said(Resource.String.troubleshooting_log_save_failed, failure.Message));
        }
    }

    private SettingsPage AboutPage() => new(GetString(Resource.String.about),
    [
        [
            new(GetString(Resource.String.version), AndroidUpdates.Running(this)),
            Switch(Resource.String.updates_automatic, Resource.String.updates_automatic_summary, KeyPlusPreferences.UpdatesAutomaticKey),
            new(GetString(Resource.String.updates_check), Tapped: () => _ = AndroidUpdates.CheckAsync(this, asked: true)),
        ],
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
        if (resultCode == Result.Ok && data?.Data is { } logFile && requestCode == LogSaveRequest)
        {
            SaveLog(logFile);
            return;
        }

        if (resultCode == Result.Ok && data?.Data is { } chosen && requestCode is DictionaryExportRequest or DictionaryImportRequest)
        {
            DictionaryFile(chosen, exporting: requestCode == DictionaryExportRequest);
            return;
        }

        if (resultCode == Result.Ok && data?.Data is { } addresses && requestCode is AddressesExportRequest or AddressesImportRequest)
        {
            DictionaryFile(addresses, exporting: requestCode == AddressesExportRequest, onlyAddresses: true);
            return;
        }

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
            Log.Write($"Designs not {(exporting ? "exported" : "imported")}: {failure}");
            Toast.MakeText(this, $"The designs could not be {(exporting ? "exported" : "imported")}: {failure.Message}", ToastLength.Long)!.Show();
        }
    }

    /// <summary>The keyboards saved in the list (Ajustes › Tema) written to the file chosen, each by its name with its design.</summary>
    private void WriteDesigns(Android.Net.Uri file)
    {
        var designs = Keyboards.Saved(this)
            .Where(keyboard => keyboard.Design is not null)
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
            Keyboards.SaveAs(this, name, Keyboards.PcBase, design, StandardKeys.Spanish.Code);
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
    /// The boxes to try the keys in right after installing, a box for each kind of field the keyboard suits
    /// (<see cref="StandardField"/>), as the Windows settings': text, a number's, an e-mail address's and a web
    /// address's, by their short signs, two by two, as wide as the list and each a line tall, about as tall as the one
    /// box of three lines they took the place of (the owner's, 2026-10-25). Their size is fixed: a long text scrolls
    /// inside and the screen never moves.
    /// </summary>
    private View TryBox()
    {
        (int Hint, Android.Text.InputTypes Kind)[] kinds =
        [
            (Resource.String.try_keys, Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextFlagCapSentences),
            (Resource.String.try_number, Android.Text.InputTypes.ClassNumber),
            (Resource.String.try_email, Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationEmailAddress),
            (Resource.String.try_web, Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationUri),
        ];
        var gap = (int)(8 * Resources!.DisplayMetrics!.Density);
        var table = new LinearLayout(this) { Orientation = Orientation.Vertical };
        for (var r = 0; r < kinds.Length; r += 2)
        {
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            for (var i = r; i < r + 2; i++)
            {
                var field = Outlined(new EditText(this) { Hint = GetString(kinds[i].Hint), InputType = kinds[i].Kind, Ellipsize = Android.Text.TextUtils.TruncateAt.End });
                field.SetSingleLine(true);
                row.AddView(field, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { LeftMargin = i > r ? gap : 0 });
            }

            table.AddView(row, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = r > 0 ? gap : 0 });
        }

        table.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { BottomMargin = 2 * gap };
        return table;
    }

    /// <summary>A box to type in outlined, so it reads as a box, not as the theme's line alone; its text 16 sp.</summary>
    private EditText Outlined(EditText box)
    {
        var density = Resources!.DisplayMetrics!.Density;
        var outline = new Android.Graphics.Drawables.GradientDrawable();
        outline.SetStroke((int)(1.5f * density), Android.Graphics.Color.Gray);
        outline.SetCornerRadius(8 * density);
        box.Background = outline;
        box.SetPadding((int)(12 * density), (int)(10 * density), (int)(12 * density), (int)(10 * density));
        box.SetTextSize(Android.Util.ComplexUnitType.Sp, 16);
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
