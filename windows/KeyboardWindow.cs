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
    private const double HandleWidthDp = 40;
    private const double HandleHeightDp = 4;
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
    private readonly SwipeEngine swipe = new();
    private readonly EmojiPanel emoji;
    private readonly NAudioMicrophone microphone = new();
    private readonly VoiceTyping voice;
    private readonly SignButton mic;
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

    /// <summary>The words a swipe read as, in the toolbar's middle as Gboard's: the one typed in bold in the centre, one on each side, a tap on one taking its place.</summary>
    private readonly Grid suggestions = new() { Visibility = Visibility.Collapsed };

    private const double SuggestionSize = 15;
    private readonly DispatcherTimer noticeTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    /// <summary>Being moved by the user, by the toolbar or the handle.</summary>
    private bool moving;

    /// <summary>What the window keeps between runs.</summary>
    private sealed record State(double Left, double Top, double Width, string Language, double? IconLeft = null, double? IconTop = null, double RowScale = 1, double? DockedWidth = null, double DockedOffset = 0, double DockedLift = 0);

    /// <summary>The keyboard window's title, by which another start of the program finds it.</summary>
    public const string WindowTitle = "Berpiztu AI Keyboard";

    /// <summary>The message another start of the program sends the running keyboard: show yourself.</summary>
    public static readonly uint ShowMessage = RegisterWindowMessage("Berpiztu.KeyPlus.Windows.Show");

    private readonly KeyboardIconWindow icon;
    private readonly TrayIcon tray;
    private bool quitting;

    /// <summary>Opened by hand (started, the clock's icon, ⌨, the settings) rather than by a text field: it stays until a text field takes the focus.</summary>
    private bool openedByHand = true;

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

        keys = new KeyboardSurface(colors);
        keys.NextLanguage += NextLanguage;
        keys.Swiped += async (path, capital, capitals) => keys.TypeSwiped(await swipe.Words(path), capital, capitals);
        keys.Suggestions += ShowSuggestions;
        keys.Offered += words => ShowSuggestions(words, null);
        keys.Suggesting = Suggest;
        keys.EmojisAsked += ShowEmoji;
        emoji = new EmojiPanel(colors) { Visibility = Visibility.Collapsed };
        emoji.Typed += keys.TypeText;
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
        mic = new SignButton(colors) { Width = BarDp, Height = BarDp, Size = SignDp, Sign = Resource.Drawable.ic_tool_mic, ToolTip = "ia-voice" };
        mic.Clicked += Listen;
        voice = new VoiceTyping(microphone, Log.Write);
        voice.Level += Heard;
        voice.Partial += keys.TypeSpokenSoFar;
        voice.Silence += EndListening;
        var settings = new SignButton(colors) { Width = BarDp, Height = BarDp, Size = SignDp, Sign = Resource.Drawable.ic_tool_settings, ToolTip = Texts.Get(Resource.String.keyboard_settings) };
        settings.Clicked += SettingsWindow.Start;
        focus.CaretMoved += AvoidText;
        focus.MenuOpened += at => Avoid(Dips(at), menu: true);
        focus.MenuClosed += () => movedForMenu = false;
        focus.TextFocused += () =>
        {
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
                    Log.Write("Keyboard: shown, a text field took the focus");
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
        notice = new TextBlock { Foreground = new SolidColorBrush(colors.Glyph), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 13 };
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
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(mic, Dock.Right);
        DockPanel.SetDock(settings, Dock.Right);
        DockPanel.SetDock(design, Dock.Right);
        DockPanel.SetDock(dock, Dock.Right);
        DockPanel.SetDock(resize, Dock.Right);
        bar.Children.Add(switcher);
        bar.Children.Add(smiley);
        bar.Children.Add(close);
        bar.Children.Add(mic);
        bar.Children.Add(settings);
        bar.Children.Add(design);
        bar.Children.Add(dock);
        bar.Children.Add(resize);
        var middle = new Grid();
        middle.Children.Add(notice);
        middle.Children.Add(suggestions);
        bar.Children.Add(middle);
        Moves(bar);

        var handle = new Border
        {
            Height = HandleBandDp,
            Background = Brushes.Transparent,
            Child = new Border
            {
                Width = HandleWidthDp,
                Height = HandleHeightDp,
                CornerRadius = new CornerRadius(HandleHeightDp / 2),
                Background = new SolidColorBrush(colors.Handle),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Cursor = Cursors.SizeAll,
        };
        Moves(handle);

        handleBand.Children.Add(handle);

        designer = new KeyboardDesigner(colors, keys);
        designer.Ended += DesignEnded;
        var top = new Grid();
        top.Children.Add(bar);
        top.Children.Add(designer.Bar);
        var card = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(handleBand, Dock.Bottom);
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

        Restore();
        ShowSwitcher();
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
            preferencesWatch.Dispose();
            keyboardsWatch.Dispose();
            appsWatch.Dispose();
            dockedBar?.Undock();
            tray.Dispose();
            icon.Close();
        };
        LocationChanged += (_, _) => KeepSoon();
        SizeChanged += (_, _) => KeepSoon();
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
        Log.Write("Keyboard: opened by hand");
        openedByHand = true;
        ShowKeyboard();
    }

    /// <summary>The keyboard in sight, its icon gone; never activated, the focus stays where it is.</summary>
    private void ShowKeyboard()
    {
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

        Log.Write(asIcon ? "Keyboard: hidden as its icon" : "Keyboard: hidden");
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
        Log.Write("Keyboard: ✕");
        HideKeyboard(asIcon);
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
        Log.Write($"Keyboard: made again in its colours, {preferences.Look}{(app is null ? "" : $", typing in {app}")}");
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

        var scale = keys.RowScale;
        keyboard = chosen;
        keys.Pc = chosen.Base == Keyboards.StandardPcBase;
        keys.Design = StandardDesign.FromText(chosen.DesignFor(docked));
        FitKeys(scale);
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

        preferences = kept;
        if (!colors.Matches(kept.ColorsIn(app, ownColor)) && !keys.Designing)
        {
            Remake(show: false);
            return;
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

        keys.Glides = kept.Glide;
        keys.ShowsTrail = kept.GlideTrail;
    }

    /// <summary>Each language's words to offer as a word is typed, read from its word list the first time it is asked for, away from the window's thread.</summary>
    private readonly Dictionary<StandardLanguage, Task<WordSuggestions?>> wordLists = [];

    /// <summary>The words offered for a word being typed; none until the language's word list is read.</summary>
    private IReadOnlyList<string> Suggest(StandardLanguage language, string typed)
    {
        if (!wordLists.TryGetValue(language, out var reading))
        {
            wordLists[language] = reading = Task.Run(() =>
            {
                try
                {
                    using var wordList = File.OpenRead(System.IO.Path.Combine(AppContext.BaseDirectory, language.WordList));
                    return WordSuggestions.Read(wordList, language);
                }
                catch (IOException failure)
                {
                    Log.Write($"The {language.Code} words to offer could not be read: {failure.Message}");
                    return null;
                }
            });
        }

        return reading.IsCompletedSuccessfully && reading.Result is { } ready ? ready.For(typed) : [];
    }

    /// <summary>
    /// Words in the toolbar's middle, as Gboard's: a swipe's, the typed one in
    /// the centre in bold and the next likeliest on each side, a side one
    /// taken in its place; or, with none typed, those offered for the word
    /// being typed, the likeliest in the centre, each taken with a tap. None,
    /// the toolbar empty again.
    /// </summary>
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
            var label = new TextBlock
            {
                Text = word,
                FontSize = SuggestionSize,
                FontWeight = centre ? FontWeights.Bold : FontWeights.Normal,
                Foreground = new SolidColorBrush(colors.Glyph),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var takes = typing || !centre;
            var place = new Border { Background = Brushes.Transparent, Child = label, Cursor = takes ? Cursors.Hand : null };
            if (takes)
            {
                place.MouseLeftButtonUp += (_, e) =>
                {
                    if (typing)
                    {
                        keys.TakeSuggestion(word);
                    }
                    else
                    {
                        keys.ReplaceSwiped(word);
                    }

                    e.Handled = true;
                };
                place.MouseLeftButtonDown += (_, e) => e.Handled = true;
            }

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
    private void Listen()
    {
        if (transcribing)
        {
            return;
        }

        if (voice.Recording)
        {
            EndListening();
            return;
        }

        ShowSuggestions([], null);
        Topmost = false;
        try
        {
            var settings = VoiceSettings.Read();
            microphone.Name = settings.Microphone;
            voice.Start(settings.Options(keys.KeyLanguage.Locale));
        }
        catch (Exception none)
        {
            Topmost = true;
            Log.Write($"The microphone could not listen: {none.Message}");
            Notice(Texts.Get(Resource.String.voice_no_microphone));
            return;
        }

        mic.Lit = true;
        Notice(Texts.Get(Resource.String.voice_listening), lasting: true);
    }

    /// <summary>The voice's level on 🎤; the first sound heard, Windows' question answered and the window on top again.</summary>
    private void Heard(double level)
    {
        mic.Level = level;
        if (level > 0)
        {
            Topmost = true;
        }
    }

    private async void EndListening()
    {
        if (!voice.Recording || transcribing)
        {
            return;
        }

        transcribing = true;
        mic.Lit = false;
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
        Log.Write($"Keyboard: moved out of the way of the {(menu ? "menu" : "text")} at {shown}, from {Top:0} to {cardTop - ShadowDp:0}");
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

            moving = true;
            DragMove();
            moving = false;
            Keep();
        };
    }

    /// <summary>The bar, the handle and the shadow's band around the keys, at the window's height for rows at a share of Gboard's; docked, the bar alone.</summary>
    private double ChromeHeight => docked ? BarDp + dockedLift : (2 * ShadowDp) + BarDp + HandleBandDp;

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
    /// its rows as tall.
    /// </summary>
    private void SetDocked(bool on)
    {
        if (on == docked)
        {
            return;
        }

        var scale = keys.RowScale;
        if (on)
        {
            Keep();
            // The width set, not the one laid out: docked as it starts, the window is not laid out yet.
            var kept = ReadState();
            (dockedWidth, dockedOffset, dockedLift) = (kept?.DockedWidth ?? (Width - (2 * ShadowDp)), kept?.DockedOffset ?? 0, kept?.DockedLift ?? 0);
        }

        docked = on;
        if (preferences.Docked != on)
        {
            (preferences = KeyboardPreferences.Read() with { Docked = on }).Keep();
        }

        frame.Margin = on ? new Thickness(0, 0, 0, dockedLift) : new Thickness(ShadowDp);
        frame.RenderTransform = on ? new TranslateTransform(dockedOffset, 0) : Transform.Identity;
        frame.CornerRadius = new CornerRadius(on ? 0 : CardCornerDp);
        frame.Effect = on ? null : shadow;
        frame.Width = on ? dockedWidth : double.NaN;
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
            if (ReadState() is { } floating)
            {
                (Left, Top, Width) = (floating.Left, floating.Top, floating.Width);
            }
        }

        FitKeys(scale);
        Log.Write(on ? "Keyboard: docked" : "Keyboard: floating");
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
            Log.Write($"Keyboard: docked bar at {place} on the screen {screen}, its work area {System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea}");
        }

        dockedPlace = place;
        var corner = target.TransformFromDevice.Transform(new Point(place.X, place.Y));
        var size = target.TransformFromDevice.Transform(new Vector(place.Width, place.Height));
        (Left, Top, Width, Height) = (corner.X, corner.Y, size.X, size.Y);
        if (asked)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => Log.Write($"Keyboard: docked window at {Left:0},{Top:0} {Width:0}x{Height:0}"));
        }
    }

    /// <summary>Where it was left, as wide, with the keyboard and language it had; the first time, three fifths of the screen wide at its bottom's middle.</summary>
    private void Restore()
    {
        var area = SystemParameters.WorkArea;
        var state = ReadState();
        Width = Math.Max(MinWidth, state?.Width ?? area.Width * FirstWidthShare);
        // The language and its keyboard as the preferences have them; the language kept before they did, as the window kept it.
        var language = preferences.Language ?? state?.Language;
        keys.KeyLanguage = StandardKeys.LanguageOf(language);
        keyboard = Keyboards.Selected(preferences, keys.KeyLanguage.Code);
        keys.Pc = keyboard.Base == Keyboards.StandardPcBase;
        keys.Design = StandardDesign.FromText(keyboard.DesignFor(docked));
        keys.Glides = preferences.Glide;
        keys.ShowsTrail = preferences.GlideTrail;
        FitKeys(state?.RowScale ?? 1);
        if (state is not null && area.Contains(new Point(state.Left + 40, state.Top + 40)))
        {
            Left = state.Left;
            Top = state.Top;
        }
        else
        {
            Left = area.Left + ((area.Width - Width) / 2);
            Loaded += (_, _) => Top = area.Bottom - ActualHeight;
        }
    }

    /// <summary>The keyboard's width the first time, a share of the screen's.</summary>
    private const double FirstWidthShare = 0.6;

    /// <summary>The keys' width and their rows' share of Gboard's height as the keyboard was left (the first time, as it first opens; docked, its docked width), for the settings' preview to show it as it is.</summary>
    public static (double Width, double RowScale) KeptKeys()
    {
        var state = ReadState();
        var floating = state is not null ? state.Width - (2 * ShadowDp) : (SystemParameters.WorkArea.Width * FirstWidthShare) - (2 * ShadowDp);
        var width = KeyboardPreferences.Read().Docked ? state?.DockedWidth ?? floating : floating;
        return (width, state?.RowScale ?? 1);
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

    /// <summary>The window's state kept; docked, its floating place and width as they were, to float there again.</summary>
    private void Keep()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Kept)!);
        var kept = ReadState();
        var iconKept = icon.IsVisible ? (icon.Left, icon.Top) : (kept is { IconLeft: { } left, IconTop: { } top } ? (left, top) : ((double, double)?)null);
        var (placeLeft, placeTop, placeWidth) = docked && kept is not null ? (kept.Left, kept.Top, kept.Width) : (Left, Top, Width);
        var (keptDockedWidth, keptOffset, keptLift) = docked ? (dockedWidth, dockedOffset, dockedLift) : (kept?.DockedWidth, kept?.DockedOffset ?? 0, kept?.DockedLift ?? 0);
        File.WriteAllText(Kept, JsonSerializer.Serialize(new State(placeLeft, placeTop, placeWidth, keys.KeyLanguage.Code, iconKept?.Item1, iconKept?.Item2, keys.RowScale, keptDockedWidth, keptOffset, keptLift)));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);
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

    /// <summary>The level heard (0 to 1) while it is lit: its circle the wider, the louder.</summary>
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

        Icons.Draw(context, sign, new Point(RenderSize.Width / 2, RenderSize.Height / 2), Size, colors.Glyph);
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
