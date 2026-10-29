using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Berpiztu AI Keyboard's window on Windows: Gboard's floating card, over
/// every window and never taking the focus from the one typed in (a click on
/// it leaves the caret where it was), so its keys reach that window. Over the
/// keys a toolbar: the keyboard in sight (a phone for Estándar, a computer
/// for the others), a tap Estándar PC or Estándar, Diseñar, and ✕; while
/// designing, the designer's buttons in its place, as Android's; under them Gboard's handle, which
/// moves it, as Windows moves a window; its edges and corners make it larger
/// or smaller as any window's, the keys' rows growing and shrinking with it.
/// Where it was left, how wide, how tall its rows, which keyboard and
/// language, kept for the next time. As Android's,
/// it shows when a text field takes the focus and, as the preferences say,
/// hides, becomes its ⌨ icon or stays when the focus leaves; ✕ hides it, and
/// only Salir, by Windows' clock, ends it.
/// </summary>
public sealed class KeyboardWindow : Window
{
    /// <summary>The card's corners, the keyboard's and the settings' preview's.</summary>
    public const double CardCornerDp = 16;
    private const double ShadowDp = 12;
    /// <summary>The toolbar's height, and the designer's pills' in its place, here and in the settings' preview.</summary>
    public const double BarDp = 40;
    private const double SignDp = 22;
    private const double HandleBandDp = 22;
    /// <summary>The size of the short words the card shows by themselves: the toolbar's notices, the band's administrator.</summary>
    private const double NoticeFontSize = 13;
    private const double MinWidthDp = 360;

    /// <summary>Windows' message as a window's place is about to change.</summary>
    private const int WindowPosChanging = 0x0046;

    /// <summary>Redimensionar's frame, Android's: the keys dimmed under it, its edges' bars (how thick, and the line in their middle), its buttons' height.</summary>
    private static readonly Color FrameDim = Color.FromArgb(0xC0, 0x12, 0x12, 0x12);
    private const double EdgeDp = 16;
    private const double EdgeLineDp = 6;
    private const double EdgeLineLengthDp = 56;
    private const double FrameButtonDp = 36;

    /// <summary>The docked bar Windows gave, in pixels, where the window is held.</summary>
    private Int32Rect? dockedPlace;

    /// <summary>
    /// Docked, the keys' own width, never stretched to the screen's, how far from the bar's middle they stand and how high
    /// above its bottom, as wide as they floated, in its middle and on its bottom at first, then as Redimensionar leaves them;
    /// Redimensionar's button and frame; the keyboard as the frame found it, for Cancelar; a drag's start, the cursor on the
    /// screen in pixels and the keyboard as it was, every step measured from there as Android's.
    /// </summary>
    private double dockedWidth;
    private double dockedOffset;
    private double dockedLift;
    private readonly SignButton resize;
    private readonly Grid resizeFrame = new() { Visibility = Visibility.Collapsed };
    private Sizing resizeStart = new(0, 0, 0, 1, 0, 0, 0);
    private (Point Cursor, Sizing Keyboard, double Room) held;

    /// <summary>The keyboard's place and size: floating, the window's; docked, the keys' width, place and lift on the bar; the rows' scale either way.</summary>
    private sealed record Sizing(double Left, double Top, double Width, double RowScale, double DockedWidth, double DockedOffset, double DockedLift);

    /// <summary>Windows' WINDOWPOS, a window's place as it is about to change.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition
    {
        public IntPtr Window;
        public IntPtr InsertAfter;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public uint Flags;
    }

    /// <summary>The room the keyboard keeps between itself and the caret or menu it moved away from, Android's.</summary>
    private const double CaretGapDp = 16;

    private static readonly string Kept = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Berpiztu", "Keyboard", "window.json");

    private readonly Palette colors;

    /// <summary>
    /// The application typed in (<see cref="FocusWatch.App"/>) and its window's colour as seen, where it asks for it (Por
    /// aplicación's De la aplicación): what the colours were made for, given again to a keyboard made again.
    /// </summary>
    private string? app;
    private Color? ownColor;
    private readonly IDisposable appsWatch;
    private readonly KeyboardSurface keys;
    private readonly SignButton switcher;
    private readonly TextBlock notice;
    private readonly SwipeEngine swipe;

    /// <summary>The personal dictionary and the words chosen for swipes, shared with the settings through their file (<see cref="PersonalWords"/>).</summary>
    private readonly PersonalWords personal = new(PersonalFile);
    private readonly IDisposable personalWatch;

    public static readonly string PersonalFile = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Berpiztu", "Keyboard", PersonalWords.FileName);
    private readonly EmojiPanel emoji;
    private readonly NAudioMicrophone microphone = new();
    private readonly VoiceTyping voice;
    private readonly SignButton mic;

    /// <summary>The toolbar's Spelling on and off (<see cref="KeyboardPreferences.SpellingOn"/>), lit while on.</summary>
    private readonly SignButton spellingButton;

    /// <summary>The toolbar's Glide typing on and off for the language typed in (<see cref="KeyboardPreferences.GlidesIn"/>), lit while on with its files.</summary>
    private readonly SignButton glideButton;
    private readonly FocusWatch focus = new();
    private readonly IDisposable preferencesWatch;
    private readonly IDisposable keyboardsWatch;
    private KeyboardPreferences preferences = KeyboardPreferences.Read();

    /// <summary>The keyboard of the list in sight, as saved: designed again, it opens as saved.</summary>
    private KeyboardEntry keyboard = ShownIn(KeyboardPreferences.Read());

    /// <summary>
    /// Docked at the bottom of the screen as an app bar (Acoplar), or floating as the card, as it starts; the bar once the
    /// window has a handle; the card's frame, shadow and handle, and Windows' resizing at its edges, which docked it does without.
    /// </summary>
    private bool docked;
    private DockedBar? dockedBar;
    private readonly SignButton dock;
    private readonly Border frame;
    private readonly DockPanel handleBand = new() { Height = HandleBandDp };
    private readonly DropShadowEffect shadow = new() { BlurRadius = ShadowDp * 1.5, ShadowDepth = 2, Opacity = 0.3 };
    private readonly System.Windows.Shell.WindowChrome chrome = new()
    {
        ResizeBorderThickness = new Thickness(ShadowDp),
        CaptionHeight = 0,
        GlassFrameThickness = new Thickness(0),
        UseAeroCaptionButtons = false,
    };

    /// <summary>The toolbar, and in its place while designing the designer's pills.</summary>
    private readonly DockPanel bar = new() { Height = BarDp, Background = Brushes.Transparent };
    private readonly KeyboardDesigner designer;

    /// <summary>Whether the open menu has moved the keyboard already: once a menu, so its keys can be reached.</summary>
    private bool movedForMenu;
    private bool transcribing;

    /// <summary>The words a swipe read as, in the toolbar's middle as Gboard's: the one typed in bold in the centre, one on each side, a tap on one taking its place; in the emoji row where it shows.</summary>
    private readonly Grid suggestions = new() { Visibility = Visibility.Collapsed };

    /// <summary>The toolbar's middle, where the words offered go without the emoji row.</summary>
    private readonly Grid barMiddle = new();

    /// <summary>
    /// Over the toolbar (Preferences' Show the emoji row, on unless turned off), the Android keyboard's height, Gboard's
    /// 100 px of the Fold: the emoji typed lately, or the words offered while typing, so the toolbar keeps its buttons.
    /// </summary>
    private const double EmojiRowDp = 100 / 2.25;
    private readonly Grid emojiRow = new() { Height = EmojiRowDp, Background = Brushes.Transparent };
    private readonly EmojiStrip recentEmoji = new();

    /// <summary>The toolbar's buttons that make room for the words offered in its middle, without the emoji row: all but the first and the last.</summary>
    private UIElement[] between = [];

    private const double SuggestionSize = 15;
    private readonly DispatcherTimer noticeTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    /// <summary>Being moved by the user, by the toolbar or the handle.</summary>
    private bool moving;

    /// <summary>
    /// What the window keeps between runs: the language, the icon's place, the keyboard's size and place for each way it is
    /// shown (<see cref="Way(bool, bool, bool)"/>); Left to DockedLift, as it was left before the size was kept for each way,
    /// where a way not kept yet starts.
    /// </summary>
    private sealed record State(string Language, double? IconLeft = null, double? IconTop = null, Dictionary<string, Placing>? Placings = null, double? Left = null, double? Top = null, double? Width = null, double RowScale = 1, double? DockedWidth = null, double DockedOffset = 0, double DockedLift = 0);

    /// <summary>The keyboard's size and place in one way of showing it: floating, the window's width and place; docked, the keys' width, place on the bar (Offset) and lift; the rows' scale either way.</summary>
    private sealed record Placing(double Width, double RowScale, double Left = 0, double Top = 0, double Offset = 0, double Lift = 0);

    /// <summary>The way of showing the keyboard the size and place in sight belong to, which <see cref="Keep"/> keeps them under.</summary>
    private string placedFor = "";

    /// <summary>The keyboard window's title, by which another start of the program finds it.</summary>
    public const string WindowTitle = "Berpiztu AI Keyboard";

    /// <summary>The message another start of the program sends the running keyboard: show yourself.</summary>
    public static readonly uint ShowMessage = RegisterWindowMessage("Berpiztu.KeyPlus.Windows.Show");

    private readonly KeyboardIconWindow icon;
    private readonly TrayIcon tray;
    private bool quitting;

    /// <summary>Opened by hand (started, the clock's icon, ⌨, the settings) rather than by a text field: it stays until a text field takes the focus.</summary>
    private bool openedByHand = true;

    /// <summary>The presses and the computer's keys, while the keyboard runs: a tap on the text after ✕ (<see cref="ShowIfTapped"/>), and Spelling's sentence touched by anything but the keys.</summary>
    private readonly InputWatch input = new();

    /// <summary>Hidden by ✕ while text has the focus: a tap on it brings the keyboard back.</summary>
    private bool hiddenByCross;

    /// <summary>How soon after a press the caret moves for the press to be taken for a tap on the text.</summary>
    private static readonly TimeSpan TapToCaret = TimeSpan.FromMilliseconds(600);

    /// <summary>The keyboard started: in the colours of the application in front, its window's as seen there last.</summary>
    public KeyboardWindow()
        : this(FocusWatch.FrontApp(), null)
    {
    }

    /// <summary>The keyboard in the colours of the application typed in, as Tema has them for it.</summary>
    private KeyboardWindow(string? app, Color? ownColor)
    {
        ownColor ??= app is not null && AppColors.For(app) is { Look: AppLook.Own } appColor ? Palette.Parse(appColor.WindowColor) : null;
        (this.app, this.ownColor) = (app, ownColor);
        colors = Palette.Read(KeyboardPreferences.Read().ColorsIn(app, ownColor));
        Title = WindowTitle;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResize;
        MinWidth = MinWidthDp + (2 * ShadowDp);

        // Windows' own resizing at the card's edges and corners: the shadow's band around it.
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, chrome);
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = true;

        personal.Reload();
        swipe = new SwipeEngine(language => personal.Words(language.Code));
        keys = new KeyboardSurface(colors);
        keys.NextLanguage += NextLanguage;
        keys.Swiped += async (path, capital, capitals) =>
        {
            var read = await swipe.Words(path);
            keys.TypeSwiped(personal.Ordered(path.Language.Code, read), read.FirstOrDefault() ?? "", capital, capitals);
        };
        // Nothing learnt while Autocorrect is off, in the language or from the toolbar: writing code, its names are no words.
        keys.SwipeChosen += (language, read, word) =>
        {
            if (Learns(language))
            {
                personal.Chose(language.Code, read, word);
            }
        };
        // The settings changed the dictionary: swiping reads it again; a choice noted changes no word, and no trie.
        personalWatch = FileWatch.Changed(PersonalFile, () =>
        {
            if (personal.Reload())
            {
                swipe.Forget();
            }
        });
        keys.Suggestions += ShowSuggestions;
        keys.Offered += ShowTyping;
        keys.AddressesStarting = typed => [.. personal.AddressesStarting(typed).Take(WordSuggestions.Offered)];
        keys.SelectionCase = TurnSelectionCase;
        keys.PressedAt = () => input.PressedAt;
        keys.KeyedAt = () => input.KeyedAt;
        keys.InDocument = () => focus.Document;
        keys.KeyPressed += LogFieldSoon;
        spellingEngine = new SpellingEngine(personal, code => preferences.Applied(code), () => Wikipedia,
            () => File.OpenRead(System.IO.Path.Combine(AppContext.BaseDirectory, CommonErrors.OwnList)), Log.Write);
        keys.Suggesting = (language, typed) => personal.WithAddresses(spellingEngine.Suggest(language, typed), typed, WordSuggestions.Offered);
        spellingEngine.FixesFound += (language, word) =>
        {
            if (keys.TypingWord == word)
            {
                ShowTyping(spellingEngine.Suggest(language, word));
            }
        };
        keys.SpellingIn = language => preferences.Applied(language.Code);
        keys.Fixing = (language, word) => spellingEngine.Fix(language, word);
        keys.WordFixed += ShowWordFix;
        keys.CorrectFieldNow = ended =>
        {
            var spelling = preferences.Applied(keys.KeyLanguage.Code);
            var punctuation = spelling.Punctuation ? spellingEngine.PunctuationOf(keys.KeyLanguage.Code)?.InUse() : null;
            var accents = spelling.Accents ? spellingEngine.Accents.InUse() : null;
            if ((punctuation is null && accents is null) || FieldEndingWith(ended) is not ({ } text, var readAt)
                || SentenceIn(text, closedNow: false) is not { } sentence)
            {
                return false;
            }

            var corrected = Corrected(sentence, punctuation, accents, opens: true);
            return corrected == sentence || keys.ReplaceRead(sentence, corrected, readAt);
        };
        keys.FieldSentenceClosed += async closing =>
        {
            var spelling = preferences.Applied(keys.KeyLanguage.Code);
            var punctuation = spelling.Punctuation && spellingEngine.PunctuationOf(keys.KeyLanguage.Code) is { } punctuationModel ? await punctuationModel.Async() : null;
            var accents = spelling.Accents ? await spellingEngine.Accents.Async() : null;
            if (punctuation is null && accents is null)
            {
                return;
            }

            await Task.Delay(FieldSettles);
            var readAt = Environment.TickCount64;
            var read = await focus.ReadField(SentenceReach);
            if (read is not { Before: { } text } || text.TrimEnd() is not { Length: > 0 } ended || ended[^1] != closing
                || SentenceIn(text, closedNow: true) is not { } sentence)
            {
                Log.Detail("Spelling: the sentence closed was not found in the field, so it is not corrected");
                return;
            }

            var corrected = await Task.Run(() => Corrected(sentence, punctuation, accents, opens: true));
            if (corrected == sentence)
            {
                Log.Detail("Spelling: the model changed nothing in a sentence");
            }
            else if (keys.ReplaceRead(sentence, corrected, readAt))
            {
                ShowSentence(sentence, corrected, applied: true);
            }
        };
        keys.CorrectNow = (sentence, byKeys) =>
        {
            var spelling = preferences.Applied(keys.KeyLanguage.Code);
            var punctuation = spelling.Punctuation ? spellingEngine.PunctuationOf(keys.KeyLanguage.Code)?.InUse() : null;
            var accents = spelling.Accents ? spellingEngine.Accents.InUse() : null;
            if (punctuation is null && accents is null)
            {
                return null;
            }

            // Enter cannot wait long: the field read for a moment, else whether these keys began the sentence.
            var reading = OpensSentence(sentence, byKeys);
            var opens = punctuation is not null && (reading.Wait(FieldWait) ? reading.Result : byKeys);
            return Corrected(sentence, punctuation, accents, opens);
        };
        keys.SentenceClosed += async (sentence, byKeys) =>
        {
            var spelling = preferences.Applied(keys.KeyLanguage.Code);
            var punctuation = spelling.Punctuation && spellingEngine.PunctuationOf(keys.KeyLanguage.Code) is { } punctuationModel ? await punctuationModel.Async() : null;
            var accents = spelling.Accents ? await spellingEngine.Accents.Async() : null;
            if (punctuation is null && accents is null)
            {
                return;
            }

            var opens = false;
            if (punctuation is not null)
            {
                await Task.Delay(FieldSettles);
                opens = await OpensSentence(sentence, byKeys);
            }

            // Autocorrect: applied at once, what it was offered in the toolbar to take back; else the correction offered there.
            var corrected = await Task.Run(() => Corrected(sentence, punctuation, accents, opens));
            if (corrected == sentence)
            {
                Log.Detail("Spelling: the model changed nothing in a sentence");
                return;
            }

            if (!preferences.Applied(keys.KeyLanguage.Code).Autocorrect || !focus.Document)
            {
                ShowSentence(sentence, corrected, applied: false);
            }
            else if (keys.ApplyCorrection(sentence, corrected, undoable: true))
            {
                ShowSentence(sentence, corrected, applied: true);
            }
        };
        keys.EmojisAsked += ShowEmoji;
        emoji = new EmojiPanel(colors) { Visibility = Visibility.Collapsed };
        emoji.Typed += keys.TypeText;
        emoji.Typed += _ => FillRecentEmoji();
        recentEmoji.Chosen += typed =>
        {
            EmojiArt.Used(typed);
            keys.TypeText(typed);
            FillRecentEmoji();
        };
        emojiRow.Children.Add(recentEmoji);
        emoji.Deleted += keys.DeleteBack;
        emoji.Closed += HideEmoji;

        switcher = new SignButton(colors) { Width = BarDp, Height = BarDp, Size = SignDp };
        keys.RowsChanged += () => FitKeys(keys.RowScale);
        switcher.Clicked += () =>
        {
            Keyboards.Select(keys.KeyLanguage.Code, keyboard.Id == Keyboards.StandardId ? Keyboards.StandardPcId : Keyboards.StandardId);
            preferences = KeyboardPreferences.Read();
            ChooseKeyboard(Keyboards.Selected(preferences, keys.KeyLanguage.Code));
        };
        var design = new SignButton(colors) { Width = BarDp, Height = BarDp, Size = SignDp, Sign = Resource.Drawable.ic_grid_design };
        design.Clicked += StartDesign;
        dock = new SignButton(colors) { Width = BarDp, Height = BarDp, Size = SignDp, Sign = Resource.Drawable.ic_grid_floating_off };
        dock.Clicked += () => SetDocked(!docked);
        resize = new SignButton(colors) { Width = BarDp, Height = BarDp, Size = SignDp, Sign = Resource.Drawable.ic_tool_resize };
        resize.Clicked += ToggleResize;
        FillResizeFrame();
        var close = new SignButton(colors) { Width = BarDp, Height = BarDp, Size = SignDp, Sign = Resource.Drawable.ic_tool_close };
        close.Clicked += () => HideKeyboardByHand(asIcon: !preferences.ShowKeyboard && preferences.KeyboardIcon);
        spellingButton = new SignButton(colors) { Width = BarDp, Height = BarDp, Size = SignDp, Sign = Resource.Drawable.ic_tool_spellcheck };
        glideButton = new SignButton(colors) { Width = BarDp, Height = BarDp, Size = SignDp, Sign = Resource.Drawable.ic_settings_gesture };
        mic = new SignButton(colors) { Width = BarDp, Height = BarDp, Size = SignDp, Sign = Resource.Drawable.ic_tool_mic, ToolTip = "ia-voice" };
        mic.Clicked += Listen;
        voice = new VoiceTyping(microphone, Log.Write);
        voice.Level += Heard;
        voice.Partial += keys.TypeSpokenSoFar;
        voice.Silence += EndListening;
        keys.SpokenLetGo += StopListening;
        var settings = new SignButton(colors) { Width = BarDp, Height = BarDp, Size = SignDp, Sign = Resource.Drawable.ic_tool_settings, ToolTip = Texts.Get(Resource.String.keyboard_settings) };
        settings.Clicked += SettingsWindow.Start;
        focus.CaretMoved += at =>
        {
            ShowIfTapped();
            AvoidText(at);
        };
        focus.MenuOpened += at => Avoid(Dips(at), menu: true);
        focus.MenuClosed += () => movedForMenu = false;
        focus.TextFocused += () =>
        {
            // A field come to: the sentence typed from here is the keys' own, as far as Spelling can tell; the keys its kind's.
            keys.BeginSentence();
            keys.Field = focus.FieldKind;
            keys.InTerminal = focus.InTerminal;
            if (focus.App is { } typedIn)
            {
                AppColors.Seen(typedIn);
            }

            if (InAppColors())
            {
                return;
            }

            if (preferences.ShowKeyboard)
            {
                openedByHand = false;
                if (!IsVisible)
                {
                    Log.Detail("Keyboard: shown, a text field took the focus");
                }

                ShowKeyboard();
            }
        };
        focus.TextLeft += FocusLeft;
        icon = new KeyboardIconWindow(colors);
        icon.Tapped += OpenByHand;
        icon.Moved += Keep;
        tray = new TrayIcon(OpenByHand, SettingsWindow.Start, Quit);
        preferencesWatch = KeyboardPreferences.Watch(Prefer);
        appsWatch = AppColors.Watch(() => Prefer(KeyboardPreferences.Read()));
        // A saved keyboard's design changed elsewhere (the settings' Tema): the one in sight shown as it now is.
        keyboardsWatch = Keyboards.Watch(() =>
        {
            if (!keys.Designing)
            {
                ChooseKeyboard(Keyboards.Selected(preferences, keys.KeyLanguage.Code));
            }
        });
        notice = new TextBlock { Foreground = new SolidColorBrush(colors.Glyph), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontSize = NoticeFontSize };
        noticeTimer.Tick += (_, _) =>
        {
            noticeTimer.Stop();
            notice.Text = "";
        };

        DockPanel.SetDock(switcher, Dock.Left);
        var smiley = new SignButton(colors) { Width = BarDp, Height = BarDp, Size = SignDp, Sign = Resource.Drawable.ic_key_emoji, ToolTip = "Emojis" };
        smiley.Clicked += () =>
        {
            if (emoji.Visibility == Visibility.Visible)
            {
                HideEmoji();
            }
            else
            {
                ShowEmoji();
            }
        };
        DockPanel.SetDock(smiley, Dock.Left);

        // Spelling on and off at once, lit while on: off to write code, on again for a message.
        spellingButton.Clicked += () =>
        {
            var kept = KeyboardPreferences.Read();

            // Nothing downloaded to correct the language typed in with: said, with Download beside it; nothing turned on.
            if (!kept.CanCorrect(keys.KeyLanguage.Code))
            {
                Log.Write($"Spelling: nothing downloaded to correct {keys.KeyLanguage.Code} with, so the toolbar's button stays off");
                OfferDictionary(keys.KeyLanguage);
                return;
            }

            (kept with { SpellingOn = !spellingButton.Lit }).Keep();
        };
        DockPanel.SetDock(spellingButton, Dock.Left);

        // The language typed in swiped or not, lit while on; without its files, said, with Download beside it for that language alone.
        glideButton.Clicked += () =>
        {
            var language = keys.KeyLanguage;
            if (!SwipeData.ReadyFor(language))
            {
                Log.Write($"Glide typing: {language.Code}'s word list or the model is not downloaded, so the toolbar's button stays off");
                OfferDownload(Texts.Get(Resource.String.glide_not_downloaded, language.Name), $"glide typing for {language.Code}", progress => SwipeData.DownloadFor(language, progress),
                    () => KeyboardPreferences.Read().WithGlide(language.Code, true).Keep(), Resource.String.glide_downloaded, Resource.String.glide_download_failed);
                return;
            }

            KeyboardPreferences.Read().WithGlide(language.Code, !glideButton.Lit).Keep();
        };
        DockPanel.SetDock(glideButton, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(mic, Dock.Right);
        DockPanel.SetDock(settings, Dock.Right);
        DockPanel.SetDock(design, Dock.Right);
        DockPanel.SetDock(dock, Dock.Right);
        DockPanel.SetDock(resize, Dock.Right);
        bar.Children.Add(switcher);
        bar.Children.Add(smiley);
        bar.Children.Add(spellingButton);
        bar.Children.Add(glideButton);
        bar.Children.Add(close);
        bar.Children.Add(mic);
        bar.Children.Add(settings);
        bar.Children.Add(design);
        bar.Children.Add(dock);
        bar.Children.Add(resize);
        barMiddle.Children.Add(notice);
        bar.Children.Add(barMiddle);

        // While words are suggested, as Android's (Gboard's): the first and the last button, the words between them.
        between = [smiley, spellingButton, glideButton, mic, settings, design, dock, resize];
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(VisibilityProperty, typeof(Grid)).AddValueChanged(suggestions, (_, _) => ShowTools());
        Moves(bar);

        // The band under the keys: Windows moves the window from anywhere on it, so it needs no handle drawn; it says
        // whether the keyboard runs as administrator (Troubleshooting's Start as administrator), empty otherwise.
        var handle = new Border
        {
            Height = HandleBandDp,
            Background = Brushes.Transparent,
            Child = new TextBlock
            {
                Text = Elevation.Running ? Texts.Get(Resource.String.running_as_administrator) : "",
                FontWeight = FontWeights.Bold,
                FontSize = NoticeFontSize,
                Foreground = new SolidColorBrush(colors.Glyph),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Cursor = Cursors.SizeAll,
        };
        Moves(handle);
        Moves(emojiRow);

        handleBand.Children.Add(handle);

        designer = new KeyboardDesigner(colors, keys);
        designer.Ended += DesignEnded;
        var top = new Grid();
        top.Children.Add(bar);
        top.Children.Add(designer.Bar);
        var card = new DockPanel();
        DockPanel.SetDock(emojiRow, Dock.Top);
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(handleBand, Dock.Bottom);
        card.Children.Add(emojiRow);
        card.Children.Add(top);
        card.Children.Add(handleBand);
        var middleKeys = new Grid();
        middleKeys.Children.Add(keys);
        middleKeys.Children.Add(emoji);
        middleKeys.Children.Add(designer.ExtraKeys);
        card.Children.Add(middleKeys);

        Content = frame = new Border
        {
            Margin = new Thickness(ShadowDp),
            CornerRadius = new CornerRadius(CardCornerDp),
            Background = new SolidColorBrush(colors.Ground),
            Child = new Grid { Children = { card, resizeFrame } },
            ClipToBounds = false,
            Effect = shadow,
        };

        ShowEmojiRow();
        Restore();
        ShowSwitcher();
        Activated += (_, _) => GiveFrontBack();
        Closing += (_, e) =>
        {
            // Closed from its taskbar button: hidden, as ✕; only Salir ends it.
            if (!quitting)
            {
                e.Cancel = true;
                HideKeyboard(asIcon: false);
                return;
            }

            Keep();
        };
        Closed += (_, _) =>
        {
            voice.Dispose();
            focus.Dispose();
            input.Dispose();
            preferencesWatch.Dispose();
            keyboardsWatch.Dispose();
            appsWatch.Dispose();
            personalWatch.Dispose();
            dockedBar?.Undock();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            tray.Dispose();
            icon.Close();
        };
        LocationChanged += (_, _) => KeepSoon();
        SizeChanged += (_, _) => KeepSoon();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
        // Docked, the room is kept only while the keyboard is in sight.
        IsVisibleChanged += (_, _) =>
        {
            if (docked && IsVisible)
            {
                PlaceDocked();
            }
            else if (docked)
            {
                dockedBar?.Undock();
            }
        };
    }

    /// <summary>A click on the window never takes the focus from the window typed in: no activation, and a taskbar button all the same; another start of the program shows it.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NeverActivated.Apply(this, taskbar: true);
        dockedBar = new DockedBar(new WindowInteropHelper(this).Handle);
        if (preferences.Docked)
        {
            SetDocked(true);
        }

        // Run as administrator, the keyboard is still shown by the program started again as usual (from the Start menu),
        // whose message Windows would otherwise keep from it.
        ChangeWindowMessageFilterEx(new WindowInteropHelper(this).Handle, ShowMessage, AllowMessage, IntPtr.Zero);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook((IntPtr window, int message, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if ((uint)message == ShowMessage)
            {
                OpenByHand();
                handled = true;
            }
            else if (message == WindowPosChanging && docked && dockedPlace is { } place)
            {
                // Docked, the window stays on its bar: Windows keeps a window inside the work area, which leaves the bar out, and
                // would push it up by its own height.
                var position = Marshal.PtrToStructure<WindowPosition>(l);
                (position.X, position.Y, position.Width, position.Height) = (place.X, place.Y, place.Width, place.Height);
                Marshal.StructureToPtr(position, l, fDeleteOld: false);
            }

            return IntPtr.Zero;
        });
    }

    private void OpenByHand()
    {
        Log.Detail("Keyboard: opened by hand");
        openedByHand = true;
        ShowKeyboard();
    }

    /// <summary>The keyboard in sight, its icon gone; never activated, the focus stays where it is.</summary>
    private void ShowKeyboard()
    {
        hiddenByCross = false;
        icon.Hide();
        if (!IsVisible)
        {
            Show();
        }
    }

    /// <summary>The keyboard out of sight, as its ⌨ icon where asked; a dictation going on ends first and types its words.</summary>
    private void HideKeyboard(bool asIcon)
    {
        if (voice.Recording)
        {
            EndListening();
        }

        Log.Detail(asIcon ? "Keyboard: hidden as its icon" : "Keyboard: hidden");
        HideEmoji();
        Hide();
        if (asIcon)
        {
            PlaceIcon();
            icon.Show();
        }
    }

    private void HideKeyboardByHand(bool asIcon)
    {
        Log.Detail("Keyboard: ✕");
        HideKeyboard(asIcon);
        if (preferences.ShowKeyboard)
        {
            hiddenByCross = true;
        }
    }

    /// <summary>
    /// After ✕, the text tapped or clicked again: the focus stays where it
    /// was, so Windows tells only the caret moving; moved just after a press,
    /// the keyboard shows, as Android's does when its field is tapped. Typing
    /// on the computer's keyboard moves the caret with no press and leaves it
    /// hidden.
    /// </summary>
    private void ShowIfTapped()
    {
        if (hiddenByCross && !IsVisible && input.PressedWithin(TapToCaret))
        {
            Log.Detail("Keyboard: shown, the text tapped again");
            openedByHand = false;
            ShowKeyboard();
        }
    }

    /// <summary>The focus gone from where text is typed: the keyboard hides, becomes its icon or stays, as the preferences say; never while it is dragged, listening or being designed, nor while it was opened by hand.</summary>
    private void FocusLeft()
    {
        if (!preferences.ShowKeyboard || !IsVisible || openedByHand || moving || voice.Recording || transcribing || keys.Designing)
        {
            return;
        }

        if (preferences.FocusLost is KeyboardPreferences.FocusLostHide or KeyboardPreferences.FocusLostIcon)
        {
            HideKeyboard(asIcon: preferences.FocusLost == KeyboardPreferences.FocusLostIcon);
        }
    }

    /// <summary>
    /// A text field focused, with Por aplicación on: the application typed in noted, its window's colour seen where it asks
    /// for it; the keyboard made again, in sight, where its colours are not those. Whether it was.
    /// </summary>
    private bool InAppColors()
    {
        if (remade)
        {
            return true;
        }

        var kept = KeyboardPreferences.Read();
        var sameApp = focus.App == app;
        app = focus.App;
        if (!kept.PerApp || keys.Designing)
        {
            return false;
        }

        // Its window's colour seen once as the application is come to, then kept while it is typed in: seen again at each
        // focus (a field, a button) it differs a little each time, and each would make the keyboard again. Not seen, the one
        // seen there last.
        var appColor = app is not null ? AppColors.For(app) : null;
        ownColor = appColor?.Look != AppLook.Own ? null
            : sameApp && ownColor is not null ? ownColor
            : SeenColor() ?? Palette.Parse(appColor.WindowColor);
        if (app is not null && ownColor is { } seen)
        {
            AppColors.SeenColor(app, Palette.Text(seen));
        }
        if (colors.Matches(kept.ColorsIn(app, ownColor)))
        {
            return false;
        }

        // Made again once this focus has been told to all that heed it, for the old keyboard closes, its focus watch with it;
        // until then it heeds nothing more.
        preferences = kept;
        remade = true;
        Dispatcher.BeginInvoke(() => Remake(show: true));
        return true;
    }

    /// <summary>The colour of the text field focused, just inside its corner, as the screen shows it; none where it is unknown or under the keyboard.</summary>
    private Color? SeenColor()
    {
        if (focus.Field is not { } field)
        {
            return null;
        }

        var (x, y) = (field.X + 3, field.Y + 3);
        if (IsVisible && Dips(new Int32Rect(x, y, 1, 1)) is { } point && new Rect(Left, Top, ActualWidth, ActualHeight).Contains(point.TopLeft))
        {
            return null;
        }

        var screen = GetDC(IntPtr.Zero);
        var pixel = GetPixel(screen, x, y);
        ReleaseDC(IntPtr.Zero, screen);
        return pixel == uint.MaxValue ? null : Color.FromRgb((byte)(pixel & 0xFF), (byte)((pixel >> 8) & 0xFF), (byte)((pixel >> 16) & 0xFF));
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr context);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr context, int x, int y);

    /// <summary>Whether this keyboard was made again in new colours, and so ends: it heeds no more changes.</summary>
    private bool remade;

    /// <summary>Salir: the keyboard ends, its icons with it.</summary>
    private void Quit()
    {
        quitting = true;
        Close();
    }

    /// <summary>
    /// Tema's colours changed: every part of the keyboard took them as it was made, so the keyboard is made again with them,
    /// where it is and as it is kept, in sight or not as this one was, and this one ends; docked, its bar given back first, so
    /// the new one takes the same.
    /// </summary>
    private void Remake(bool show)
    {
        remade = true;
        preferencesWatch.Dispose();
        keyboardsWatch.Dispose();
        appsWatch.Dispose();
        personalWatch.Dispose();
        Log.Detail($"Keyboard: made again in its colours, {preferences.Look}{(app is null ? "" : $", typing in {app}")}");
        Keep();
        var shown = show || IsVisible;
        dockedBar?.Undock();
        var made = new KeyboardWindow(app, ownColor);
        Application.Current.MainWindow = made;
        if (shown)
        {
            made.Show();
        }
        else
        {
            new WindowInteropHelper(made).EnsureHandle();
        }

        Quit();
    }

    /// <summary>The icon where it was left; the first time, at the keyboard's bottom right corner.</summary>
    private void PlaceIcon()
    {
        var area = SystemParameters.WorkArea;
        var state = ReadState();
        if (state?.IconLeft is { } left && state.IconTop is { } top && area.Contains(new Point(left + icon.Width / 2, top + icon.Height / 2)))
        {
            (icon.Left, icon.Top) = (left, top);
        }
        else
        {
            (icon.Left, icon.Top) = (Left + ActualWidth - icon.Width, Top + ActualHeight - icon.Height);
        }
    }

    /// <summary>One button either way, as Android's: Estándar in sight a phone, any other a computer; a tap the other standard keyboard.</summary>
    private void ShowSwitcher()
    {
        var standard = keyboard.Id == Keyboards.StandardId;
        switcher.Sign = standard ? Resource.Drawable.ic_grid_phone : Resource.Drawable.ic_grid_pc;
        switcher.ToolTip = keyboard.Name;
    }

    private void NextLanguage()
    {
        var languages = StandardKeys.Languages;
        keys.KeyLanguage = languages[(languages.ToList().IndexOf(keys.KeyLanguage) + 1) % languages.Count];
        Keyboards.Carry(keys.KeyLanguage.Code, keyboard);
        (preferences = KeyboardPreferences.Read() with { Language = keys.KeyLanguage.Code }).Keep();
        ChooseKeyboard(Keyboards.Selected(preferences, keys.KeyLanguage.Code));
        ShowSpellingButton(preferences);
        TakeGlide(preferences);
        Keep();
    }

    /// <summary>The keyboard the preferences show in the language they type in.</summary>
    private static KeyboardEntry ShownIn(KeyboardPreferences preferences) => Keyboards.Selected(preferences, preferences.TypingLanguage.Code);

    /// <summary>A keyboard of the list shown, as designed, its rows as tall as they were.</summary>
    private void ChooseKeyboard(KeyboardEntry chosen)
    {
        if (chosen == keyboard && keys.Design?.Text == chosen.DesignFor(docked))
        {
            return;
        }

        Keep();
        keyboard = chosen;
        keys.Pc = chosen.Base == Keyboards.StandardPcBase;
        keys.Design = StandardDesign.FromText(chosen.DesignFor(docked));
        PlaceAsKept();
        ShowSwitcher();
        Keep();
    }

    /// <summary>The preferences as the settings saved them: the keyboard, its language, swiping and its colours taken at once; the keyboard and its colours not while it is being designed.</summary>
    private void Prefer(KeyboardPreferences kept)
    {
        // Made again already: Windows tells one change of the file several times, and those already on their way here
        // would each make another keyboard.
        if (remade)
        {
            return;
        }

        // Start as administrator turned on: started again so at once, this one closing once Windows' question is answered yes.
        if (kept.AsAdministrator && !preferences.AsAdministrator && !Elevation.Running && Elevation.StartAgain([]))
        {
            remade = true;
            Quit();
            return;
        }

        var rowBefore = preferences.EmojiRow;
        preferences = kept;
        if (!colors.Matches(kept.ColorsIn(app, ownColor)) && !keys.Designing)
        {
            Remake(show: false);
            return;
        }

        // The emoji row turned on or off: the keys keep their rows' height, the window as much taller or shorter.
        if (kept.EmojiRow != rowBefore)
        {
            ShowEmojiRow();
            FitKeys(keys.RowScale);
        }

        if (kept.Language is { } code && code != keys.KeyLanguage.Code && StandardKeys.Languages.FirstOrDefault(language => language.Code == code) is { } chosen)
        {
            keys.KeyLanguage = chosen;
            Keep();
        }

        // The keyboard chosen for the language typed in, each language its own.
        if (!keys.Designing)
        {
            ChooseKeyboard(Keyboards.Selected(kept, keys.KeyLanguage.Code));
        }

        TakeGlide(kept);
        keys.ShowsTrail = kept.GlideTrail;
        keys.DeletesSwipedWord = kept.GlideDeleteWord;
        TakeSpelling(kept);
    }

    /// <summary>
    /// The language typed in without a dictionary, offered in the toolbar as
    /// its Correct words would download (<see cref="OfferDownload"/>): its
    /// dictionary and word list, its Correct words turned on once they are.
    /// </summary>
    private void OfferDictionary(StandardLanguage language)
    {
        if (!SpellingDictionary.Has(language.Code))
        {
            Notice(Texts.Get(Resource.String.spelling_no_dictionary, language.Name));
            return;
        }

        OfferDownload(Texts.Get(Resource.String.spelling_no_dictionary, language.Name), $"the {language.Code} dictionary and word list",
            progress => Downloads.FilesAsync([.. SpellingDictionary.FilesFor(language.Code), SwipeData.WordListFile(language)], progress),
            () =>
            {
                var now = KeyboardPreferences.Read();
                (now.WithSpelling(language.Code, now.SpellingIn(language.Code) with { Words = true }) with { SpellingOn = true }).Keep();
            },
            Resource.String.spelling_dictionary_downloaded, Resource.String.spelling_dictionary_download_failed);
    }

    /// <summary>
    /// What a toolbar button needs and lacks, said in the toolbar's middle
    /// with Download beside it: fetched there with a tap, how much shown as
    /// it comes, and once it is, what it is for turned on (<paramref name="turnOn"/>),
    /// its button lit; a failure said there and in the log. Nothing is
    /// fetched but by that tap.
    /// </summary>
    private void OfferDownload(string missing, string named, Func<IProgress<(long Done, long Total)>, Task> download, Action turnOn, int done, int failed)
    {
        suggestions.Children.Clear();
        suggestions.ColumnDefinitions.Clear();
        suggestions.ColumnDefinitions.Add(new ColumnDefinition());
        suggestions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        suggestions.Visibility = Visibility.Visible;
        var said = SuggestionPlace(missing, bold: false, null);
        var label = (TextBlock)said.Child;
        Border? fetch = null;
        fetch = SuggestionPlace(Texts.Get(Resource.String.download), bold: true, async () =>
        {
            fetch!.Visibility = Visibility.Collapsed;
            try
            {
                await download(new Progress<(long Done, long Total)>(at => label.Text = Texts.Get(Resource.String.downloading_short,
                    (at.Done / 1_000_000.0).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture), (Math.Max(at.Total, at.Done) / 1_000_000.0).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture))));
                Log.Write($"Downloads: {named} downloaded from the toolbar");
                turnOn();
                ShowSuggestions([], null);
                Notice(Texts.Get(done));
            }
            catch (Exception failure) when (failure is System.Net.Http.HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
            {
                Log.Write($"Downloads: {named} could not be downloaded from the toolbar: {failure.Message}");
                ShowSuggestions([], null);
                Notice(Texts.Get(failed, failure.Message));
            }
        }, ends: false);
        Grid.SetColumn(fetch, 1);
        suggestions.Children.Add(said);
        suggestions.Children.Add(fetch);
    }

    /// <summary>
    /// Glide typing as the language typed in has it: swiped, and its toolbar
    /// button lit, only while it is on in that language and its word list and
    /// the model are downloaded; another language, its own.
    /// </summary>
    private void TakeGlide(KeyboardPreferences kept)
    {
        var on = kept.GlidesIn(keys.KeyLanguage.Code) && SwipeData.ReadyFor(keys.KeyLanguage);
        keys.Glides = on;
        glideButton.Lit = on;

        // Swiping made ready at once, not at the first swipe, the owner's.
        if (on)
        {
            swipe.Prepare(keys.KeyLanguage);
        }
    }

    /// <summary>
    /// The toolbar's Spelling button lit only while Spelling is on and the
    /// language typed in has something downloaded to correct with: off
    /// without it, whatever was chosen, for nothing would be corrected.
    /// </summary>
    private void ShowSpellingButton(KeyboardPreferences kept) => spellingButton.Lit = kept.SpellingOn && kept.CanCorrect(keys.KeyLanguage.Code);

    /// <summary>Whether the dictionary learns from what is typed in a language: only while its Autocorrect is on and Spelling is not off from the toolbar.</summary>
    private bool Learns(StandardLanguage language) => preferences.Applied(language.Code).Autocorrect;

    /// <summary>Spelling's choices given to the keys, and what they turned on loaded at once, away from the window's thread.</summary>
    private void TakeSpelling(KeyboardPreferences kept)
    {
        // Off from the toolbar (writing code), nothing is corrected or offered (Applied); the choices stay as they are.
        ShowSpellingButton(kept);
        // Spelling off corrects nothing, a sentence's capital neither.
        keys.CapitalizesSentences = kept.CapitalizeSentences && kept.SpellingOn;

        // What words were found to mean may change with what is now turned on.
        spellingEngine.Forget();
        spellingEngine.Prepare(keys.KeyLanguage);
    }

    /// <summary>Whether Spanish's Spelling uses the Wikipedia's common misspellings, whose list is Spanish.</summary>
    private bool Wikipedia => preferences.SpellingIn(StandardKeys.Spanish.Code).Wikipedia;

    /// <summary>What the keyboard knows of words as they are typed (<see cref="SpellingEngine"/>), shared with Android.</summary>
    private readonly SpellingEngine spellingEngine;

    /// <summary>
    /// A misspelled word in the toolbar's middle, as a sentence's accents
    /// are: written in its place (Autocorrect), the word as typed after ↶ in
    /// the centre, a tap taking it back, the other words it may have meant on
    /// each side; else those words, the likeliest in the centre after ✓. A
    /// tap on one writes it. The next words offered take its place.
    /// </summary>
    private void ShowWordFix(string typed, IReadOnlyList<string> meant, bool applied)
    {
        suggestions.Children.Clear();
        suggestions.ColumnDefinitions.Clear();
        suggestions.Visibility = Visibility.Visible;

        // Applied: ↶ and the word as typed in the centre, the other words it may have meant on each side.
        string?[] shown = applied
            ? [meant.ElementAtOrDefault(1), typed, meant.ElementAtOrDefault(2)]
            : [meant.ElementAtOrDefault(1), meant.ElementAtOrDefault(0), meant.ElementAtOrDefault(2)];
        for (var i = 0; i < shown.Length; i++)
        {
            suggestions.ColumnDefinitions.Add(new ColumnDefinition());
            if (shown[i] is { } word)
            {
                var centre = i == 1;
                var sign = centre ? (applied ? "↶ " : "✓ ") : "";
                var place = SuggestionPlace(sign + word, bold: centre, () => keys.ReplaceWord(word));
                Grid.SetColumn(place, i);
                suggestions.Children.Add(place);
            }
        }
    }

    /// <summary>A word or a correction in the toolbar's middle, in its colours; a tap on it, where it takes one, does it and ends the toolbar's words when told to.</summary>
    private Border SuggestionPlace(string text, bool bold, Action? taken, bool ends = true)
    {
        // An address in two lines in its room, as Gboard's and Android's, smaller to fit both.
        var address = PersonalWords.IsAddress(text);
        var label = new TextBlock
        {
            Text = address ? PersonalWords.InTwoLines(text) : text,
            TextAlignment = TextAlignment.Center,
            FontSize = address ? SuggestionSize * PersonalWords.AddressTypeShare : SuggestionSize,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = new SolidColorBrush(colors.Glyph),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var place = new Border { Background = Brushes.Transparent, Child = label, Cursor = taken is null ? null : Cursors.Hand };
        if (taken is not null)
        {
            // A word fills its room, so a drag from it moves the keyboard as the row's room does; a click takes it.
            var pressed = new Point();
            place.MouseLeftButtonDown += (_, e) =>
            {
                pressed = e.GetPosition(this);
                place.CaptureMouse();
                e.Handled = true;
            };
            place.MouseMove += (_, e) =>
            {
                var moved = e.GetPosition(this) - pressed;
                if (docked || !place.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed
                    || (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance))
                {
                    return;
                }

                place.ReleaseMouseCapture();
                Move();
            };
            place.MouseLeftButtonUp += (_, e) =>
            {
                if (!place.IsMouseCaptured)
                {
                    return;
                }

                place.ReleaseMouseCapture();
                taken();
                if (ends)
                {
                    ShowSuggestions([], null);
                }

                e.Handled = true;
            };
        }

        return place;
    }

    /// <summary>
    /// The accent model's sentence in the toolbar's middle, as a word offered
    /// is: applied as it closed (Autocorrect), the words it changed as
    /// they were typed after ↶, a tap writing the sentence again as it was;
    /// else the words it would change after ✓, a tap writing them. The next
    /// words offered take its place.
    /// </summary>
    private void ShowSentence(string original, string corrected, bool applied)
    {
        var shown = (applied ? original : corrected).Split(' ').Zip((applied ? corrected : original).Split(' '))
            .Where(pair => pair.First != pair.Second).Select(pair => pair.First);
        var place = SuggestionPlace((applied ? "↶ " : "✓ ") + string.Join(' ', shown), bold: true,
            () => keys.ApplyCorrection(applied ? corrected : original, applied ? original : corrected, undoable: false));
        suggestions.Children.Clear();
        suggestions.ColumnDefinitions.Clear();
        suggestions.Children.Add(place);
        suggestions.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Words in the toolbar's middle, as Gboard's: a swipe's, the typed one in
    /// the centre in bold and the next likeliest on each side, a side one
    /// taken in its place; or, with none typed, those offered for the word
    /// being typed, the likeliest in the centre, each taken with a tap. None,
    /// the toolbar empty again.
    /// </summary>
    /// <summary>
    /// The emoji row as the preferences have it: shown, the words offered in it and the toolbar's buttons all kept; not
    /// shown, the words in the toolbar's middle, between its first and last button.
    /// </summary>
    private void ShowEmojiRow()
    {
        var on = preferences.EmojiRow;
        emojiRow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        Panel home = on ? emojiRow : barMiddle;
        if (suggestions.Parent != home)
        {
            (suggestions.Parent as Panel)?.Children.Remove(suggestions);
            home.Children.Add(suggestions);
        }

        FillRecentEmoji();
        ShowTools();
    }

    /// <summary>The words offered in sight or not: in the toolbar, its buttons between the first and the last make room; in the emoji row, the emoji typed lately do.</summary>
    private void ShowTools()
    {
        var words = suggestions.Visibility == Visibility.Visible;
        foreach (var tool in between)
        {
            tool.Visibility = words && !preferences.EmojiRow ? Visibility.Collapsed : Visibility.Visible;
        }

        recentEmoji.Visibility = words ? Visibility.Hidden : Visibility.Visible;
    }

    /// <summary>The emoji row's emoji: those typed lately, the last first.</summary>
    private void FillRecentEmoji() => recentEmoji.Items = EmojiArt.Recent();

    private void ShowSuggestions(IReadOnlyList<string> words, string? typed)
    {
        suggestions.Children.Clear();
        suggestions.ColumnDefinitions.Clear();
        suggestions.Visibility = words.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (words.Count == 0)
        {
            return;
        }

        var typing = typed is null;
        var others = words.Where(word => word != typed).Take(2).ToList();
        string?[] shown = typing
            ? [words.ElementAtOrDefault(1), words.ElementAtOrDefault(0), words.ElementAtOrDefault(2)]
            : [others.ElementAtOrDefault(0), typed, others.ElementAtOrDefault(1)];
        for (var i = 0; i < shown.Length; i++)
        {
            suggestions.ColumnDefinitions.Add(new ColumnDefinition());
            if (shown[i] is not { } word)
            {
                continue;
            }

            var centre = i == 1;
            Action? taken = typing ? () => keys.TakeSuggestion(word) : centre ? null : () => keys.ReplaceSwiped(word);
            var place = SuggestionPlace(word, bold: centre, taken, ends: false);
            Grid.SetColumn(place, i);
            suggestions.Children.Add(place);
        }
    }

    /// <summary>
    /// The words offered for the word being typed (<see cref="ShowSuggestions"/>);
    /// one no list, no dictionary of the user's and no Hunspell has, with
    /// Learn new words as I type on and the language learning
    /// (<see cref="Learns"/>), shown as typed at the left in quotes, as
    /// Gboard's, the likeliest of the others in the centre: a tap on it writes
    /// it with a space and learns it. Nothing is learnt but by that tap.
    /// </summary>
    private void ShowTyping(IReadOnlyList<string> words)
    {
        var language = keys.KeyLanguage;
        var typed = keys.TypingWord;
        if (typed.Length < 2 || keys.TypingAddress || !personal.LearnTyped || !Learns(language) || !spellingEngine.Unknown(language, typed))
        {
            ShowSuggestions(words, null);
            return;
        }

        suggestions.Children.Clear();
        suggestions.ColumnDefinitions.Clear();
        suggestions.Visibility = Visibility.Visible;
        var others = words.Where(word => !word.Equals(typed, StringComparison.OrdinalIgnoreCase)).ToList();
        string?[] shown = [typed, others.ElementAtOrDefault(0), others.ElementAtOrDefault(1)];
        for (var i = 0; i < shown.Length; i++)
        {
            suggestions.ColumnDefinitions.Add(new ColumnDefinition());
            if (shown[i] is not { } word)
            {
                continue;
            }

            var place = i == 0
                ? SuggestionPlace($"\"{word}\"", bold: false, () =>
                {
                    keys.TakeSuggestion(word);
                    personal.Learn(language.Code, word);
                    Log.Detail($"Dictionary: a word learnt in {language.Code}, from the toolbar");
                }, ends: false)
                : SuggestionPlace(word, bold: i == 1, () => keys.TakeSuggestion(word), ends: false);
            Grid.SetColumn(place, i);
            suggestions.Children.Add(place);
        }
    }

    /// <summary>
    /// 🎤: a tap starts listening, 🎤 lit and beating with the voice's level,
    /// "Escuchando…" in the toolbar, the words typed where the keys go as they
    /// are heard, in the keyboard's language; a tap again, or the settings'
    /// silence after speaking, sends all that was heard to ia-voice once more
    /// and its words take the place of those typed so far. While Windows may still ask whether the app can listen, the
    /// window leaves the top, so its question is not hidden under it.
    /// </summary>
    private async void Listen()
    {
        if (transcribing || asking)
        {
            return;
        }

        if (voice.Recording)
        {
            EndListening();
            return;
        }

        ShowSuggestions([], null);
        var settings = VoiceSettings.Read();
        var options = settings.Options(keys.KeyLanguage.Locale);

        // The local network's or the remote service not answering (no network, the server down): this computer's instead.
        if (settings.Choice != VoiceTyping.OnDevice)
        {
            asking = true;
            var answers = await VoiceTyping.Answers(options.Service);
            asking = false;
            if (!answers)
            {
                Log.Write($"Voice typing: {settings.Choice} did not answer, so this computer's ia-voice listens");
                options = options with { Service = VoiceTyping.LocalService, User = "", Password = "" };
            }
        }

        Topmost = false;
        try
        {
            microphone.Name = settings.Microphone;
            keys.BeginSpoken();
            voice.Start(options);
        }
        catch (Exception none)
        {
            Topmost = true;
            Log.Write($"The microphone could not listen: {none.Message}");
            Notice(Texts.Get(Resource.String.voice_no_microphone));
            return;
        }

        mic.Lit = true;
        mic.Hearing = true;
        Notice(Texts.Get(options.Service == settings.Service ? Resource.String.voice_listening : Resource.String.voice_fallback_local), lasting: true);
    }

    /// <summary>The chosen service being asked whether it answers, before listening: 🎤 again waits for it.</summary>
    private bool asking;

    /// <summary>The voice's level on 🎤; the first sound heard, Windows' question answered and the window on top again.</summary>
    private void Heard(double level)
    {
        mic.Level = level;
        if (level > 0)
        {
            Topmost = true;
        }
    }

    /// <summary>How much of the field before the caret is read, and how long the window typed in is given to take a key first.</summary>
    private const int FieldReach = 200;
    private static readonly TimeSpan FieldSettles = TimeSpan.FromMilliseconds(150);

    /// <summary>How much before the caret is read for a sentence these keys lost: a sentence longer than this is left as it is.</summary>
    private const int SentenceReach = 1000;

    /// <summary>
    /// The sentence the field holds before the caret: from the last new line,
    /// or the last . ? or ! and a space before the one just closed
    /// (<paramref name="closedNow"/>), to the caret; from the field's start
    /// where it was all read. None where it begins beyond what was read, or is
    /// too short to correct.
    /// </summary>
    private static string? SentenceIn(string text, bool closedNow)
    {
        var search = closedNow ? text.TrimEnd().TrimEnd('.', '?', '!').Length : text.Length;
        var start = -1;
        for (var at = search - 1; at >= 0; at--)
        {
            if (text[at] is '\n' or '\r' || (text[at] is '.' or '?' or '!' && at + 1 < text.Length && char.IsWhiteSpace(text[at + 1])))
            {
                start = at + 1;
                break;
            }
        }

        if (start < 0)
        {
            if (text.Length >= SentenceReach)
            {
                return null;
            }

            start = 0;
        }

        var sentence = text[start..];
        return sentence.Trim().Length > 1 ? sentence : null;
    }

    /// <summary>
    /// What the field holds before the caret once it ends with what the keys
    /// typed last (<paramref name="ended"/>: a word just corrected may not be
    /// in it yet), read again a moment later until it does, for
    /// <see cref="FieldWait"/> at most; with when it was read. None where it
    /// never did: its sentence is left as it is.
    /// </summary>
    private (string Text, long ReadAt)? FieldEndingWith(string ended)
    {
        var until = Environment.TickCount64 + (long)FieldWait.TotalMilliseconds;
        while (true)
        {
            var readAt = Environment.TickCount64;
            var reading = focus.ReadField(SentenceReach);
            if (!reading.Wait(FieldWait) || reading.Result is not { Before: { } text })
            {
                return null;
            }

            if (text.EndsWith(ended, StringComparison.Ordinal))
            {
                return (text, readAt);
            }

            if (Environment.TickCount64 >= until)
            {
                Log.Detail("Spelling: the field did not end with what was typed last, so its sentence is not corrected");
                return null;
            }

            Thread.Sleep(FieldRetry);
        }
    }

    /// <summary>How long the field is given to take what was typed before it is read again.</summary>
    private static readonly TimeSpan FieldRetry = TimeSpan.FromMilliseconds(50);

    /// <summary>How long Enter waits for the field to be read, before it goes by what the keys know.</summary>
    private static readonly TimeSpan FieldWait = TimeSpan.FromMilliseconds(300);

    /// <summary>The field being read: one reading at a time.</summary>
    private bool readingField;

    /// <summary>The sentence as the models write it: its punctuation first, for the accent model reads a ¿ (¿como estás? → ¿cómo estás?), then its accents.</summary>
    private static string Corrected(string sentence, PunctuationModel? punctuation, AccentModel? accents, bool opens)
    {
        var punctuated = punctuation?.Punctuate(sentence, opens) ?? sentence;
        return accents?.Restore(punctuated) ?? punctuated;
    }

    /// <summary>
    /// Whether a sentence begins where one does, by what the field holds
    /// before it (UI Automation): nothing, a new line, or . ? ! … and spaces.
    /// Where the field cannot be read or its text is not found there, whether
    /// these keys began it. Found by its first characters, which the field
    /// has surely taken; never waiting on the keyboard's thread, so Enter may
    /// wait for it.
    /// </summary>
    private async Task<bool> OpensSentence(string sentence, bool byKeys)
    {
        var start = sentence.Trim();
        var reach = FieldReach + sentence.Length;
        var read = await focus.ReadField(reach).ConfigureAwait(false);
        if (start.Length == 0 || read is not { Before: { } text })
        {
            return byKeys;
        }

        var at = text.LastIndexOf(start[..Math.Min(start.Length, 20)], StringComparison.Ordinal);
        if (at < 0)
        {
            Log.Detail("Spelling: the sentence was not found in the field, so the keys say whether it begins one");
            return byKeys;
        }

        var before = text[..at].TrimEnd(' ', '\t', ' ');
        return before.Length == 0 ? text.Length < reach || byKeys : before[^1] is '\n' or '\r' or '.' or '?' or '!' or '…';
    }

    /// <summary>
    /// A key pressed, with the detailed log on: once the window typed in has
    /// taken it, what UI Automation reads before the caret, in the log, to
    /// learn which applications let their text be read, for a sentence's start
    /// and its punctuation; only its length and its last two characters, never
    /// the text.
    /// </summary>
    private async void LogFieldSoon()
    {
        if (readingField || !Log.Detailed)
        {
            return;
        }

        readingField = true;
        try
        {
            await Task.Delay(FieldSettles);
            var read = await focus.ReadField(FieldReach);
            var app = focus.App ?? "an application";
            Log.Detail(read is { Before: { } text }
                ? $"Field: {app}: {text.Length} characters before the caret, read by its {read.Value.How}, ending \"{Visible(text[^Math.Min(2, text.Length)..])}\""
                : $"Field: {app}: not read: {read?.How ?? "UI Automation could not be had"}");
        }
        finally
        {
            readingField = false;
        }

        static string Visible(string end) => end.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    }

    /// <summary>The dictation let go of the text (a key, a click elsewhere): 🎤 stops, and what it heard is neither read again nor typed.</summary>
    private async void StopListening()
    {
        if (!voice.Recording || transcribing)
        {
            return;
        }

        mic.Lit = false;
        mic.Hearing = false;
        mic.Level = 0;
        Topmost = true;
        Notice("");
        await voice.Cancel();
    }

    private async void EndListening()
    {
        if (!voice.Recording || transcribing)
        {
            return;
        }

        transcribing = true;
        mic.Lit = false;
        mic.Hearing = false;
        mic.Level = 0;
        Topmost = true;
        if (notice.Text == Texts.Get(Resource.String.voice_listening))
        {
            Notice(Texts.Get(Resource.String.voice_transcribing), lasting: true);
        }

        try
        {
            var text = await voice.Stop();
            keys.TypeSpoken(text);
            Notice(text.Length > 0 ? "" : Texts.Get(Resource.String.voice_nothing_heard));
        }
        catch (VoiceTyping.SignInRefused)
        {
            Log.Write("ia-voice's proxy did not take the user and password.");
            Notice(Texts.Get(Resource.String.voice_sign_in_refused));
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or JsonException)
        {
            Log.Write($"ia-voice did not answer: {failure.Message}");
            Notice(Texts.Get(Resource.String.voice_unavailable));
        }
        finally
        {
            transcribing = false;
        }
    }

    /// <summary>
    /// The caret shown or moved: what "Apartarse del texto" keeps clear of, as
    /// on Android: the whole field where the keyboard fits above or below it,
    /// so a field it covers in part is cleared too; else, a field too tall for
    /// that (an editor), the line the caret is on, across the field; with no
    /// field told, the caret alone.
    /// </summary>
    private void AvoidText(Int32Rect at)
    {
        if (Dips(at) is not { } caret)
        {
            return;
        }

        var card = ActualHeight - (2 * ShadowDp);
        var area = SystemParameters.WorkArea;
        var avoided = caret;
        if (focus.Field is { } place && Dips(place) is { } field && field.Contains(new Point(caret.Left, caret.Top + (caret.Height / 2))))
        {
            var fits = field.Top - CaretGapDp - card >= area.Top || field.Bottom + CaretGapDp + card <= area.Bottom;
            avoided = fits ? field : new Rect(field.Left, caret.Top, field.Width, caret.Height);
        }

        Avoid(avoided, menu: false);
    }

    /// <summary>A place on the screen in pixels as the window's units; none before the window is shown.</summary>
    private Rect? Dips(Int32Rect at) => PresentationSource.FromVisual(this)?.CompositionTarget is { } target
        ? new Rect(target.TransformFromDevice.Transform(new Point(at.X, at.Y)), target.TransformFromDevice.Transform(new Point(at.X + at.Width, at.Y + at.Height)))
        : null;

    /// <summary>
    /// "Apartarse del texto": the text (<see cref="AvoidText"/>) under the
    /// card, the keyboard moves above it where there is room, else below it,
    /// <see cref="CaretGapDp"/> away, else to the side with more room, and
    /// stays where it went, as after a drag. "Apartarse también de los menús":
    /// an open menu the same, once a menu, the keyboard then still however the
    /// user goes through it, so its keys can be reached.
    /// </summary>
    private void Avoid(Rect? avoided, bool menu)
    {
        if (docked || avoided is not { } shown || !(menu ? preferences.AvoidMenus : preferences.AvoidCaret) || moving || !IsLoaded || (menu && movedForMenu))
        {
            return;
        }

        var card = new Rect(Left + ShadowDp, Top + ShadowDp, ActualWidth - (2 * ShadowDp), ActualHeight - (2 * ShadowDp));
        if (shown.Bottom <= card.Top || shown.Top >= card.Bottom || shown.Right < card.Left || shown.Left > card.Right)
        {
            return;
        }

        var area = SystemParameters.WorkArea;
        var above = shown.Top - CaretGapDp - card.Height;
        var below = shown.Bottom + CaretGapDp;
        var cardTop = above >= area.Top ? above
            : below + card.Height <= area.Bottom ? below
            : shown.Top - area.Top >= area.Bottom - shown.Bottom ? area.Top : area.Bottom - card.Height;
        movedForMenu = menu;
        Log.Detail($"Keyboard: moved out of the way of the {(menu ? "menu" : "text")} at {shown}, from {Top:0} to {cardTop - ShadowDp:0}");
        Top = cardTop - ShadowDp;
        Keep();
    }

    /// <summary>The emoji in the keys' place, as tall, those typed lately first.</summary>
    private void ShowEmoji()
    {
        emoji.Height = keys.ActualHeight;
        emoji.Fill();
        keys.Visibility = Visibility.Hidden;
        emoji.Visibility = Visibility.Visible;
    }

    private void HideEmoji()
    {
        emoji.Visibility = Visibility.Collapsed;
        keys.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Diseñar, as Android's: a click marks a key in blue, − and + make it
    /// narrower or wider, a drag moves it; + Tecla, + Fila and Quitar, with
    /// Cancelar and Hecho, in the toolbar's place. The keyboard's design, or
    /// the keyboard as made, to start from.
    /// </summary>
    private void StartDesign()
    {
        if (voice.Recording)
        {
            EndListening();
        }

        HideEmoji();
        ShowSuggestions([], null);
        bar.Visibility = Visibility.Collapsed;
        designer.Start(keyboard);
    }

    /// <summary>The design ended (<see cref="KeyboardDesigner"/>): the keyboard as it now is in sight, the toolbar back; Cancelar's design as it was, its rows as tall.</summary>
    private void DesignEnded(KeyboardEntry shown, bool kept, double scale)
    {
        keyboard = shown;
        preferences = KeyboardPreferences.Read();
        if (!kept)
        {
            FitKeys(scale);
        }

        bar.Visibility = Visibility.Visible;
        ShowSwitcher();
    }

    /// <summary>A line in the toolbar's middle, gone after a while unless it lasts (Escuchando…) until the next.</summary>
    private void Notice(string text, bool lasting = false)
    {
        notice.Text = text;
        noticeTimer.Stop();
        if (!lasting && text.Length > 0)
        {
            noticeTimer.Start();
        }
    }

    /// <summary>A part that moves the window as Windows moves one, while the button is held.</summary>
    private void Moves(FrameworkElement part)
    {
        part.MouseLeftButtonDown += (_, e) =>
        {
            if (docked || e.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            Move();
        };
    }

    /// <summary>The window moved as Windows moves one, while the button is held, and kept where it is let go.</summary>
    private void Move()
    {
        moving = true;
        DragMove();
        moving = false;
        Keep();
        GiveFrontBack();
    }

    /// <summary>
    /// The keyboard activated by Windows after all (a drag begun with the shell or a WinUI app in front): the front given
    /// back to the window typed in, where its keys go; after a drag, once it ends, for taking the front away during one
    /// would end it.
    /// </summary>
    private void GiveFrontBack()
    {
        var back = focus.LastFront;
        if (!IsActive || moving || back == IntPtr.Zero)
        {
            return;
        }

        Log.Detail("Keyboard: activated by Windows, the front given back to the window typed in");
        SetForegroundWindow(back);
    }

    /// <summary>The bar, the handle and the shadow's band around the keys, at the window's height for rows at a share of Gboard's; docked, the bar alone.</summary>
    private double ChromeHeight => (preferences.EmojiRow ? EmojiRowDp : 0) + (docked ? BarDp + dockedLift : (2 * ShadowDp) + BarDp + HandleBandDp);

    /// <summary>
    /// The window as tall as the keyboard in use needs for rows at a share of Gboard's: its rows keep their height when the
    /// keyboard changes; docked, the bar made as tall, asked of Windows again or, while a drag goes on, only shown so.
    /// </summary>
    private void FitKeys(double scale, bool asked = true)
    {
        MinHeight = ChromeHeight + keys.HeightAt(KeyboardSurface.MinRowScale);
        Height = ChromeHeight + keys.HeightAt(Math.Max(KeyboardSurface.MinRowScale, scale));
        if (docked)
        {
            PlaceDocked(asked);
        }
    }

    /// <summary>
    /// Acoplar: docked at the bottom of the screen it is on, as wide, as Windows' touch keyboard docks (<see cref="DockedBar"/>),
    /// its card's shadow, corners and handle gone, never moved, the other windows made to give it room; or floating again where
    /// it was left. The same keyboard either way, shown as its docked design has it where it has one (designed while docked),
    /// at the size kept for that way (<see cref="PlaceAsKept"/>).
    /// </summary>
    private void SetDocked(bool on)
    {
        if (on == docked)
        {
            return;
        }

        Keep();
        docked = on;
        if (preferences.Docked != on)
        {
            (preferences = KeyboardPreferences.Read() with { Docked = on }).Keep();
        }

        frame.CornerRadius = new CornerRadius(on ? 0 : CardCornerDp);
        frame.Effect = on ? null : shadow;
        frame.HorizontalAlignment = on ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        Background = on ? frame.Background : Brushes.Transparent;
        handleBand.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        chrome.ResizeBorderThickness = new Thickness(on ? 0 : ShadowDp);
        resize.Lit = false;
        resizeFrame.Visibility = Visibility.Collapsed;

        dock.Sign = on ? Resource.Drawable.ic_grid_floating : Resource.Drawable.ic_grid_floating_off;
        designer.Docked = on;
        keys.Design = StandardDesign.FromText(keyboard.DesignFor(on));
        if (!on)
        {
            dockedPlace = null;
            dockedBar?.Undock();
            frame.Margin = new Thickness(ShadowDp);
            frame.RenderTransform = Transform.Identity;
            frame.Width = double.NaN;
        }

        PlaceAsKept();
        Log.Detail(on ? "Keyboard: docked" : "Keyboard: floating");
    }

    /// <summary>Redimensionar, as Android's: its frame over the keyboard, the keyboard as it is kept for Cancelar; or the keyboard as it was left.</summary>
    private void ToggleResize()
    {
        resize.Lit = !resize.Lit;
        resizeFrame.Visibility = resize.Lit ? Visibility.Visible : Visibility.Collapsed;
        if (resize.Lit)
        {
            resizeStart = Current();
        }
    }

    private Sizing Current() => new(Left, Top, Width, keys.RowScale, dockedWidth, dockedOffset, dockedLift);

    /// <summary>Cancelar: the keyboard as the frame found it, Restablecer undone too, the frame gone.</summary>
    private void CancelResize()
    {
        var start = resizeStart;
        if (docked)
        {
            (dockedWidth, dockedOffset, dockedLift) = (start.DockedWidth, start.DockedOffset, start.DockedLift);
            LayDocked();
            FitKeys(start.RowScale);
        }
        else
        {
            (Left, Top, Width) = (start.Left, start.Top, start.Width);
            FitKeys(start.RowScale);
        }

        Keep();
        ToggleResize();
    }

    /// <summary>What a drag on the frame holds: the whole keyboard, to move it, or one of its edges, to size it.</summary>
    private enum Part
    {
        Whole,
        Top,
        Bottom,
        Left,
        Right,
    }

    /// <summary>
    /// Redimensionar's frame over the keyboard, Android's (KeyboardFrame.cs): the keys dimmed and out of reach under it, a bar
    /// at each edge that can be dragged, the top and bottom ones for the height (the keys' size), the side ones for the width;
    /// ✥ in the middle, dragged, moves the keyboard; under it Restablecer, the keyboard at its usual size and place again,
    /// Cancelar, as the frame found it, and Hecho, the frame gone.
    /// </summary>
    private void FillResizeFrame()
    {
        resizeFrame.Background = new SolidColorBrush(FrameDim);
        resizeFrame.Children.Add(new Border { BorderBrush = new SolidColorBrush(colors.Primary), BorderThickness = new Thickness(2), IsHitTestVisible = false });
        resizeFrame.Children.Add(Edge(Part.Top, HorizontalAlignment.Stretch, VerticalAlignment.Top));
        resizeFrame.Children.Add(Edge(Part.Bottom, HorizontalAlignment.Stretch, VerticalAlignment.Bottom));
        resizeFrame.Children.Add(Edge(Part.Left, HorizontalAlignment.Left, VerticalAlignment.Stretch));
        resizeFrame.Children.Add(Edge(Part.Right, HorizontalAlignment.Right, VerticalAlignment.Stretch));

        var move = Held(Part.Whole, new TextBlock { Text = "✥", FontSize = 28, Foreground = Brushes.White, Margin = new Thickness(12, 0, 12, 0) }, Cursors.SizeAll);
        move.HorizontalAlignment = HorizontalAlignment.Center;
        var reset = new PillButton(colors, "↺ " + Texts.Get(Resource.String.reset_size), done: false, 14) { Height = FrameButtonDp, Margin = new Thickness(4) };
        reset.Clicked += ResetSize;
        var cancel = new PillButton(colors, "✕ " + Texts.Get(Resource.String.cancel), done: false, 14) { Height = FrameButtonDp, Margin = new Thickness(4) };
        cancel.Clicked += CancelResize;
        var done = new PillButton(colors, "✓ " + Texts.Get(Resource.String.done), done: true, 14) { Height = FrameButtonDp, Margin = new Thickness(4) };
        done.Clicked += ToggleResize;
        var buttons = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(move);
        buttons.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { reset, cancel, done } });
        resizeFrame.Children.Add(buttons);
    }

    /// <summary>An edge's bar: the whole edge to take hold of, a short line in its middle to show it is there.</summary>
    private Grid Edge(Part part, HorizontalAlignment across, VerticalAlignment down)
    {
        var horizontal = part is Part.Top or Part.Bottom;
        var line = new Border
        {
            Background = new SolidColorBrush(colors.Primary),
            CornerRadius = new CornerRadius(EdgeLineDp / 2),
            Width = horizontal ? EdgeLineLengthDp : EdgeLineDp,
            Height = horizontal ? EdgeLineDp : EdgeLineLengthDp,
        };
        var edge = Held(part, line, horizontal ? Cursors.SizeNS : Cursors.SizeWE);
        (edge.HorizontalAlignment, edge.VerticalAlignment) = (across, down);
        (edge.Width, edge.Height) = horizontal ? (double.NaN, EdgeDp) : (EdgeDp, double.NaN);
        return edge;
    }

    /// <summary>
    /// A part the keyboard or an edge is dragged by: what it shows in its middle, under a grip as large as the part, the drag
    /// told as it goes and once let go. Where it began is kept on the screen, not the grip's own change: the grip moves with
    /// what it sizes or not, so that change is a step at times and the whole way at others.
    /// </summary>
    private Grid Held(Part part, FrameworkElement shown, Cursor cursor)
    {
        var look = new ControlTemplate(typeof(Thumb)) { VisualTree = new FrameworkElementFactory(typeof(Border)) };
        look.VisualTree.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var grip = new Thumb { Template = look, Cursor = cursor };
        grip.DragStarted += (_, _) => held = (CursorOnScreen(), Current(), ScreenRoom());
        grip.DragDelta += (_, _) => Adjust(part);
        grip.DragCompleted += (_, _) => Adjusted(part);
        (shown.HorizontalAlignment, shown.VerticalAlignment, shown.IsHitTestVisible) = (HorizontalAlignment.Center, VerticalAlignment.Center, false);
        return new Grid { Children = { shown, grip } };
    }

    private Point CursorOnScreen() => PointToScreen(Mouse.GetPosition(this));

    /// <summary>The screen's height for the window, in DIPs: from its top to the taskbar, the docked bar's own room counted (Windows leaves it out of the work area).</summary>
    private double ScreenRoom()
    {
        var room = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea.Height + (docked && dockedPlace is { } place ? place.Height : 0);
        return PresentationSource.FromVisual(this)?.CompositionTarget is { } target ? target.TransformFromDevice.Transform(new Vector(0, room)).Y : room;
    }

    /// <summary>
    /// A drag on the frame, as it goes, from the keyboard as it began: the width and the place (docked, the keys' width and
    /// their place on the bar, the other edge staying where it is), the height (the keys' size) and, docked, how high above
    /// the bottom it stands, the bar Windows gives asked for again once it is let go.
    /// </summary>
    private void Adjust(Part part)
    {
        var moved = PresentationSource.FromVisual(this)?.CompositionTarget is { } target ? target.TransformFromDevice.Transform(CursorOnScreen() - held.Cursor) : CursorOnScreen() - held.Cursor;
        var start = held.Keyboard;
        switch (part)
        {
            case Part.Top:
                SizeKeys(keys.HeightAt(start.RowScale) - moved.Y, keepBottom: true);
                break;
            case Part.Bottom:
                SizeKeys(keys.HeightAt(start.RowScale) + moved.Y, keepBottom: false);
                break;
            case Part.Left when docked:
                SizeDocked(start.DockedWidth - moved.X, -1);
                break;
            case Part.Right when docked:
                SizeDocked(start.DockedWidth + moved.X, 1);
                break;
            case Part.Whole when docked:
                MoveDocked(start.DockedOffset + moved.X);
                LiftDocked(start.DockedLift - moved.Y);
                break;
            case Part.Left:
                var narrower = Math.Max(MinWidth, start.Width - moved.X);
                (Left, Width) = (start.Left + start.Width - narrower, narrower);
                break;
            case Part.Right:
                Width = Math.Max(MinWidth, start.Width + moved.X);
                break;
            case Part.Whole:
                (Left, Top) = (start.Left + moved.X, start.Top + moved.Y);
                break;
        }
    }

    /// <summary>A drag on the frame let go: docked, the bar it made asked of Windows; the keyboard kept.</summary>
    private void Adjusted(Part part)
    {
        if (docked && part is Part.Top or Part.Bottom or Part.Whole)
        {
            PlaceDocked();
        }

        Keep();
    }

    /// <summary>The keys as tall as dragged, the window no taller than the screen; floating, its bottom staying where it is when the top is dragged.</summary>
    private void SizeKeys(double height, bool keepBottom)
    {
        var bottom = Top + Height;
        FitKeys(keys.ScaleFor(Math.Min(height, held.Room - ChromeHeight)), asked: false);
        if (!docked && keepBottom)
        {
            Top = bottom - Height;
        }
    }

    /// <summary>The docked keys as high above the bar's bottom as dragged: half the screen at most, as Android's, and never past its top.</summary>
    private void LiftDocked(double lift)
    {
        var scale = held.Keyboard.RowScale;
        var most = Math.Min(held.Room / 2, held.Room - BarDp - keys.HeightAt(scale));
        dockedLift = Math.Clamp(lift, 0, Math.Max(0, most));
        frame.Margin = new Thickness(0, 0, 0, dockedLift);
        FitKeys(scale, asked: false);
    }

    /// <summary>The docked keys as wide as asked, at least the floating keyboard's least and at most the bar, the edge not dragged (side) staying where it was.</summary>
    private void SizeDocked(double width, int side)
    {
        var start = held.Keyboard;
        dockedWidth = Math.Clamp(width, MinWidthDp, Math.Max(MinWidthDp, ActualWidth));
        frame.Width = dockedWidth;
        MoveDocked(start.DockedOffset + (side * (dockedWidth - start.DockedWidth) / 2));
    }

    /// <summary>The docked keys at their width, place and lift on the bar.</summary>
    private void LayDocked()
    {
        frame.Width = dockedWidth;
        frame.RenderTransform = new TranslateTransform(dockedOffset, 0);
        frame.Margin = new Thickness(0, 0, 0, dockedLift);
    }

    /// <summary>The docked keys moved along the bar, from its middle, never past its ends.</summary>
    private void MoveDocked(double offset)
    {
        var room = Math.Max(0, (ActualWidth - dockedWidth) / 2);
        dockedOffset = Math.Clamp(offset, -room, room);
        frame.RenderTransform = new TranslateTransform(dockedOffset, 0);
        KeepSoon();
    }

    /// <summary>
    /// Restablecer, Android's: the keys at their usual size; floating, three fifths of the screen wide at its bottom's middle;
    /// docked, as wide as they float, in the bar's middle and on its bottom again.
    /// </summary>
    private void ResetSize()
    {
        var area = SystemParameters.WorkArea;
        if (docked)
        {
            (dockedWidth, dockedOffset, dockedLift) = ((area.Width * FirstWidthShare) - (2 * ShadowDp), 0, 0);
            LayDocked();
            FitKeys(1);
        }
        else
        {
            Width = area.Width * FirstWidthShare;
            Left = area.Left + ((area.Width - Width) / 2);
            FitKeys(1);
            Top = area.Bottom - Height;
        }

        Keep();
    }

    /// <summary>
    /// The window on the bar Windows gives it at the bottom of the screen it is on: as wide, as tall as its keys need; not
    /// <paramref name="asked"/>, as tall on the bottom it has, Windows asked once the drag making it so is let go, for each ask
    /// lays the other windows out again.
    /// </summary>
    private void PlaceDocked(bool asked = true)
    {
        if (dockedBar is null || !IsVisible || PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
        {
            return;
        }

        var height = (int)Math.Ceiling(target.TransformToDevice.Transform(new Vector(0, Height)).Y);
        Int32Rect place;
        if (!asked && dockedPlace is { } shown)
        {
            place = new Int32Rect(shown.X, shown.Y + shown.Height - height, shown.Width, height);
        }
        else
        {
            var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).Bounds;
            place = dockedBar.Dock(screen, height);
            Log.Detail($"Keyboard: docked bar at {place} on the screen {screen}, its work area {System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea}");
        }

        dockedPlace = place;
        var corner = target.TransformFromDevice.Transform(new Point(place.X, place.Y));
        var size = target.TransformFromDevice.Transform(new Vector(place.Width, place.Height));
        (Left, Top, Width, Height) = (corner.X, corner.Y, size.X, size.Y);
        if (asked)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => Log.Detail($"Keyboard: docked window at {Left:0},{Top:0} {Width:0}x{Height:0}"));
        }
    }

    /// <summary>With the keyboard and language it had, at the size and place kept for the way it is shown (<see cref="PlaceAsKept"/>).</summary>
    private void Restore()
    {
        // The language and its keyboard as the preferences have them; the language kept before they did, as the window kept it.
        var language = preferences.Language ?? ReadState()?.Language;
        keys.KeyLanguage = StandardKeys.LanguageOf(language);
        keyboard = Keyboards.Selected(preferences, keys.KeyLanguage.Code);
        keys.Pc = keyboard.Base == Keyboards.StandardPcBase;
        keys.Design = StandardDesign.FromText(keyboard.DesignFor(docked));
        TakeGlide(preferences);
        keys.ShowsTrail = preferences.GlideTrail;
        keys.DeletesSwipedWord = preferences.GlideDeleteWord;
        Log.Detailed = preferences.DetailedLog;
        TakeSpelling(preferences);
        PlaceAsKept();
    }

    /// <summary>
    /// The keyboard at the size and place kept for the way it is shown now (<see cref="Way(bool, bool)"/>); a way not kept yet,
    /// as the keyboard was left before; the first time, floating three fifths of the screen wide at its bottom's middle,
    /// docked as wide as it floats, in the bar's middle and on its bottom, its rows at their usual height.
    /// </summary>
    private void PlaceAsKept()
    {
        placedFor = Way(keys.Pc, docked);
        var placing = KeptFor(placedFor, docked);
        var area = SystemParameters.WorkArea;
        if (docked)
        {
            var floating = KeptFor(Way(keys.Pc, false), false)?.Width ?? (area.Width * FirstWidthShare);
            (dockedWidth, dockedOffset, dockedLift) = placing is { } kept ? (kept.Width, kept.Offset, kept.Lift) : (floating - (2 * ShadowDp), 0, 0);
            LayDocked();
            FitKeys(placing?.RowScale ?? 1);
            return;
        }

        Width = Math.Max(MinWidth, placing?.Width ?? area.Width * FirstWidthShare);
        FitKeys(placing?.RowScale ?? 1);
        if (placing is not null && area.Contains(new Point(placing.Left + 40, placing.Top + 40)))
        {
            (Left, Top) = (placing.Left, placing.Top);
        }
        else
        {
            (Left, Top) = (area.Left + ((area.Width - Width) / 2), area.Bottom - Height);
        }
    }

    /// <summary>
    /// A way of showing the keyboard, each with its own size and place, as the user may want Estándar PC's more keys larger
    /// than Estándar's: the keyboard (Estándar or Estándar PC), floating or docked, on a screen lying (landscape) or standing.
    /// </summary>
    private static string Way(bool pc, bool docked, bool landscape) =>
        $"{(pc ? "pc" : "standard")}.{(docked ? "docked" : "floating")}.{(landscape ? "landscape" : "portrait")}";

    /// <summary>A way of showing the keyboard on the screen the window is on.</summary>
    private string Way(bool pc, bool docked) => Way(pc, docked, Landscape(new WindowInteropHelper(this).Handle));

    /// <summary>Whether the screen the window is on lies wider than tall; with no window yet, the main screen.</summary>
    private static bool Landscape(IntPtr window)
    {
        var screen = (window == IntPtr.Zero ? System.Windows.Forms.Screen.PrimaryScreen : System.Windows.Forms.Screen.FromHandle(window))!.Bounds;
        return screen.Width >= screen.Height;
    }

    /// <summary>The size and place kept for a way of showing the keyboard; a way not kept yet, as the keyboard was left before; none, never kept.</summary>
    private static Placing? KeptFor(string way, bool docked)
    {
        var state = ReadState();
        if (state?.Placings?.GetValueOrDefault(way) is { } placing)
        {
            return placing;
        }

        return docked
            ? state?.DockedWidth is { } keysWidth ? new Placing(keysWidth, state.RowScale, Offset: state.DockedOffset, Lift: state.DockedLift) : null
            : state?.Width is { } width ? new Placing(width, state.RowScale, state.Left ?? 0, state.Top ?? 0) : null;
    }

    /// <summary>The size and place in sight, as kept for the way the keyboard is shown.</summary>
    private Placing Shown() => docked
        ? new Placing(dockedWidth, keys.RowScale, Offset: dockedOffset, Lift: dockedLift)
        : new Placing(Width, keys.RowScale, Left, Top);

    /// <summary>The screen turned or changed: the size and place kept for the way the keyboard is shown on it now.</summary>
    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (Way(keys.Pc, docked) != placedFor)
        {
            PlaceAsKept();
        }
    });

    /// <summary>The keyboard's width the first time, a share of the screen's.</summary>
    private const double FirstWidthShare = 0.6;

    /// <summary>The keys' width and their rows' share of Gboard's height as kept for the way the keyboard is shown (the first time, as it first opens; docked, its docked width), for the settings' preview to show it as it is.</summary>
    public static (double Width, double RowScale) KeptKeys()
    {
        var preferences = KeyboardPreferences.Read();
        var pc = ShownIn(preferences).Base == Keyboards.StandardPcBase;
        var landscape = Landscape(IntPtr.Zero);
        var floating = (KeptFor(Way(pc, false, landscape), false)?.Width ?? (SystemParameters.WorkArea.Width * FirstWidthShare)) - (2 * ShadowDp);
        var placing = KeptFor(Way(pc, preferences.Docked, landscape), preferences.Docked);
        return (preferences.Docked ? placing?.Width ?? floating : floating, placing?.RowScale ?? 1);
    }

    private static State? ReadState()
    {
        try
        {
            return File.Exists(Kept) ? JsonSerializer.Deserialize<State>(File.ReadAllText(Kept)) : null;
        }
        catch (JsonException unreadable)
        {
            Log.Write($"The window's state could not be read: {unreadable.Message}");
            return null;
        }
    }

    private DispatcherTimer? keepTimer;

    private void KeepSoon()
    {
        keepTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            keepTimer!.Stop();
            Keep();
        }, Dispatcher);
        keepTimer.Stop();
        keepTimer.Start();
    }

    /// <summary>The window's state kept: the size and place in sight under the way the keyboard is shown, the other ways' as they were.</summary>
    private void Keep()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Kept)!);
        var kept = ReadState() ?? new State(keys.KeyLanguage.Code);
        var iconKept = icon.IsVisible ? (icon.Left, icon.Top) : (kept is { IconLeft: { } left, IconTop: { } top } ? (left, top) : ((double, double)?)null);
        var placings = new Dictionary<string, Placing>(kept.Placings ?? []) { [placedFor] = Shown() };
        File.WriteAllText(Kept, JsonSerializer.Serialize(kept with { Language = keys.KeyLanguage.Code, IconLeft = iconKept?.Item1, IconTop = iconKept?.Item2, Placings = placings }));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    private const uint AllowMessage = 1;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr window, uint message, uint action, IntPtr filter);

    /// <summary>
    /// Shift tapped twice, as on Android: the text selected in the window
    /// typed in turned to its next case and selected again, to turn again.
    /// Windows lets no keyboard read another window's text, so it is copied
    /// (Ctrl+C), what the clipboard held kept and put back after; nothing
    /// copied in a moment, nothing is selected, and Shift stays locked. A
    /// selection of more than one line is left as it is.
    /// </summary>
    private async void TurnSelectionCase()
    {
        var held = HeldOnClipboard();
        var before = GetClipboardSequenceNumber();
        KeySender.Press(Android.Views.Keycode.A + ('c' - 'a'), KeySender.Held.Ctrl);
        for (var waited = 0; waited < SelectionWaits && GetClipboardSequenceNumber() == before; waited++)
        {
            await Task.Delay(SelectionWaitMs);
        }

        if (GetClipboardSequenceNumber() == before)
        {
            return;
        }

        var selected = ClipboardText();
        PutBack(held);
        if (selected is not { Length: > 0 } || selected.Contains('\n'))
        {
            Log.Detail("Selection's case: nothing selected on one line, so Shift stays locked");
            return;
        }

        var turned = StandardKeys.NextCase(selected, new System.Globalization.CultureInfo(keys.KeyLanguage.Locale));
        KeySender.Type(turned);
        foreach (var _ in turned)
        {
            KeySender.Press(Android.Views.Keycode.DpadLeft, KeySender.Held.Shift);
        }

        keys.ShiftOff();
    }

    /// <summary>How long the window typed in is given to copy what is selected: ten waits of 30 ms.</summary>
    private const int SelectionWaits = 10;
    private const int SelectionWaitMs = 30;

    /// <summary>What the clipboard holds, every format it can be read in, to put back.</summary>
    private static DataObject? HeldOnClipboard()
    {
        try
        {
            var copy = new DataObject();
            if (Clipboard.GetDataObject() is not { } now)
            {
                return copy;
            }

            foreach (var format in now.GetFormats(false))
            {
                try
                {
                    if (now.GetData(format) is { } data)
                    {
                        copy.SetData(format, data);
                    }
                }
                catch (Exception unreadable) when (unreadable is ExternalException or OutOfMemoryException)
                {
                    // A format the application that put it there keeps to itself: the others go back.
                }
            }

            return copy;
        }
        catch (ExternalException busy)
        {
            Log.Write($"Selection's case: the clipboard could not be read: {busy.Message}");
            return null;
        }
    }

    private static string? ClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (ExternalException busy)
        {
            Log.Write($"Selection's case: the copy could not be read: {busy.Message}");
            return null;
        }
    }

    private static void PutBack(DataObject? held)
    {
        try
        {
            // Not read, it is left as the copy made it; empty before, emptied again.
            if (held is null)
            {
                return;
            }

            if (held.GetFormats().Length == 0)
            {
                Clipboard.Clear();
            }
            else
            {
                Clipboard.SetDataObject(held, copy: true);
            }
        }
        catch (ExternalException busy)
        {
            Log.Write($"Selection's case: what the clipboard held could not be put back: {busy.Message}");
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}

/// <summary>A toolbar's sign, the Android keyboard's drawable, in the keys' letters' colour; a click, what it does.</summary>
public sealed class SignButton(Palette colors) : FrameworkElement
{
    /// <summary>The circle's radius as a share of the button's height: pressed, and lit in silence and at the loudest, still inside the button.</summary>
    private const double PressedShare = 0.42;
    private const double QuietShare = 0.36;
    private const double LoudShare = 0.5;

    private int sign;
    private bool down;
    private bool lit;
    private bool hearing;
    private double level;

    /// <summary>Whether it is in use (🎤 listening): circled in the modifiers' tone.</summary>
    public bool Lit
    {
        get => lit;
        set
        {
            lit = value;
            InvalidateVisual();
        }
    }

    /// <summary>Whether it shows the voice coming in (🎤 listening): <see cref="VoiceBars"/> in its sign's place.</summary>
    public bool Hearing
    {
        get => hearing;
        set
        {
            hearing = value;
            InvalidateVisual();
        }
    }

    /// <summary>The level heard (0 to 1) while it is lit: its circle the wider, the louder, and its bars, while hearing, the taller.</summary>
    public double Level
    {
        get => level;
        set
        {
            level = value;
            InvalidateVisual();
        }
    }

    public int Sign
    {
        get => sign;
        set
        {
            sign = value;
            InvalidateVisual();
        }
    }

    public double Size { get; init; } = 22;

    public event Action? Clicked;

    protected override void OnRender(DrawingContext context)
    {
        context.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (down || lit)
        {
            var radius = RenderSize.Height * (lit ? QuietShare + ((LoudShare - QuietShare) * level) : PressedShare);
            context.DrawEllipse(new SolidColorBrush(lit ? colors.On : colors.Function), null, new Point(RenderSize.Width / 2, RenderSize.Height / 2), radius, radius);
        }

        if (hearing)
        {
            DrawBars(context);
            return;
        }

        Icons.Draw(context, sign, new Point(RenderSize.Width / 2, RenderSize.Height / 2), Size, colors.Glyph);
    }

    /// <summary>The voice coming in, as Gboard's: the bars rounded, in the sign's colour, each as tall as the level and its beat make it.</summary>
    private void DrawBars(DrawingContext context)
    {
        var brush = new SolidColorBrush(colors.Glyph);
        var (left, top) = ((RenderSize.Width - Size) / 2, RenderSize.Height / 2);
        var width = VoiceBars.WidthShare * Size;
        var seconds = Environment.TickCount64 / 1000.0;
        for (var bar = 0; bar < VoiceBars.Count; bar++)
        {
            var height = Math.Max(width, VoiceBars.Height(bar, level, seconds) * Size);
            context.DrawRoundedRectangle(brush, null, new Rect(left + VoiceBars.Left(bar, Size), top - (height / 2), width, height), width / 2, width / 2);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        down = true;
        CaptureMouse();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var inside = new Rect(RenderSize).Contains(e.GetPosition(this));
        down = false;
        ReleaseMouseCapture();
        InvalidateVisual();
        e.Handled = true;
        if (inside)
        {
            Clicked?.Invoke();
        }
    }
}
