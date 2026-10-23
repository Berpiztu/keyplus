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
    private const double CardCornerDp = 16;
    private const double ShadowDp = 12;
    private const double BarDp = 40;
    private const double SignDp = 22;
    private const double HandleBandDp = 22;
    private const double HandleWidthDp = 40;
    private const double HandleHeightDp = 4;
    private const double MinWidthDp = 360;

    /// <summary>The designer's buttons, Android's toolbar pills: 89 px tall on the Fold, 2 dp apart.</summary>
    private const double PillDp = 89 / 2.625;
    private const double PillGapDp = 2;

    /// <summary>+ Tecla's list, as Android's: five keys across, 40 dp tall.</summary>
    private const int ExtrasAcross = 5;
    private const double ExtraKeyDp = 40;

    /// <summary>The room the keyboard keeps between itself and the caret or menu it moved away from, Android's.</summary>
    private const double CaretGapDp = 16;

    private static readonly string Kept = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Berpiztu", "Keyboard", "window.json");

    private readonly Palette colors = Palette.Read();
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
    private KeyboardPreferences preferences = KeyboardPreferences.Read();

    /// <summary>The keyboard of the list in sight, as saved: designed again, it opens as saved.</summary>
    private KeyboardEntry keyboard = Keyboards.Selected(KeyboardPreferences.Read());

    /// <summary>The toolbar, and in its place while designing the designer's buttons; + Tecla's list in the keys' place.</summary>
    private readonly DockPanel bar = new() { Height = BarDp, Background = Brushes.Transparent };
    private readonly Grid designBar = new() { Height = BarDp, Visibility = Visibility.Collapsed };
    private readonly ScrollViewer extraKeys = new() { Visibility = Visibility.Collapsed, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };

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
    private sealed record State(double Left, double Top, double Width, string Language, double? IconLeft = null, double? IconTop = null, double RowScale = 1);

    /// <summary>The keyboard window's title, by which another start of the program finds it.</summary>
    public const string WindowTitle = "Berpiztu AI Keyboard";

    /// <summary>The message another start of the program sends the running keyboard: show yourself.</summary>
    public static readonly uint ShowMessage = RegisterWindowMessage("Berpiztu.KeyPlus.Windows.Show");

    private readonly KeyboardIconWindow icon;
    private readonly TrayIcon tray;
    private bool quitting;

    /// <summary>Opened by hand (started, the clock's icon, ⌨, the settings) rather than by a text field: it stays until a text field takes the focus.</summary>
    private bool openedByHand = true;

    public KeyboardWindow()
    {
        Title = WindowTitle;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResize;
        MinWidth = MinWidthDp + (2 * ShadowDp);

        // Windows' own resizing at the card's edges and corners: the shadow's band around it.
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
        {
            ResizeBorderThickness = new Thickness(ShadowDp),
            CaptionHeight = 0,
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
        });
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = true;

        keys = new KeyboardSurface(colors);
        keys.NextLanguage += NextLanguage;
        keys.NotMade += what => Notice($"{what}: todavía no está hecho");
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
            Keyboards.Select(keyboard.Id == Keyboards.StandardId ? Keyboards.StandardPcId : Keyboards.StandardId);
            preferences = KeyboardPreferences.Read();
            ChooseKeyboard(Keyboards.Selected(preferences));
        };
        var design = new SignButton(colors) { Width = BarDp, Height = BarDp, Size = SignDp, Sign = Resource.Drawable.ic_grid_design };
        design.Clicked += StartDesign;
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
        bar.Children.Add(switcher);
        bar.Children.Add(smiley);
        bar.Children.Add(close);
        bar.Children.Add(mic);
        bar.Children.Add(settings);
        bar.Children.Add(design);
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

        var bottom = new DockPanel { Height = HandleBandDp };
        bottom.Children.Add(handle);

        FillDesignBar();
        var top = new Grid();
        top.Children.Add(bar);
        top.Children.Add(designBar);
        var card = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        card.Children.Add(top);
        card.Children.Add(bottom);
        var middleKeys = new Grid();
        middleKeys.Children.Add(keys);
        middleKeys.Children.Add(emoji);
        middleKeys.Children.Add(extraKeys);
        card.Children.Add(middleKeys);

        Content = new Border
        {
            Margin = new Thickness(ShadowDp),
            CornerRadius = new CornerRadius(CardCornerDp),
            Background = new SolidColorBrush(colors.Ground),
            Child = card,
            ClipToBounds = false,
            Effect = new DropShadowEffect { BlurRadius = ShadowDp * 1.5, ShadowDepth = 2, Opacity = 0.3 },
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
            tray.Dispose();
            icon.Close();
        };
        LocationChanged += (_, _) => KeepSoon();
        SizeChanged += (_, _) => KeepSoon();
    }

    /// <summary>A click on the window never takes the focus from the window typed in: no activation, and a taskbar button all the same; another start of the program shows it.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NeverActivated.Apply(this, taskbar: true);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook((IntPtr window, int message, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if ((uint)message == ShowMessage)
            {
                OpenByHand();
                handled = true;
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

    /// <summary>Salir: the keyboard ends, its icons with it.</summary>
    private void Quit()
    {
        quitting = true;
        Close();
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
        (preferences = preferences with { Language = keys.KeyLanguage.Code }).Keep();
        Keep();
    }

    /// <summary>A keyboard of the list shown, as designed, its rows as tall as they were.</summary>
    private void ChooseKeyboard(KeyboardEntry chosen)
    {
        if (chosen == keyboard && keys.Design?.Text == chosen.Design)
        {
            return;
        }

        var scale = keys.RowScale;
        keyboard = chosen;
        keys.Pc = chosen.Base == Keyboards.StandardPcBase;
        keys.Design = StandardDesign.FromText(chosen.Design);
        FitKeys(scale);
        ShowSwitcher();
        Keep();
    }

    /// <summary>The preferences as the settings saved them: the keyboard, its language and swiping taken at once; the keyboard not while it is being designed.</summary>
    private void Prefer(KeyboardPreferences kept)
    {
        preferences = kept;
        if (!keys.Designing)
        {
            ChooseKeyboard(Keyboards.Selected(kept));
        }

        if (kept.Language is { } code && code != keys.KeyLanguage.Code && StandardKeys.Languages.FirstOrDefault(language => language.Code == code) is { } chosen)
        {
            keys.KeyLanguage = chosen;
            Keep();
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
        if (avoided is not { } shown || !(menu ? preferences.AvoidMenus : preferences.AvoidCaret) || moving || !IsLoaded || (menu && movedForMenu))
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
        var made = StandardKeys.Page(keys.Pc ? StandardPage.Pc : StandardPage.Letters, keys.KeyLanguage);
        keys.Design = StandardDesign.FromText(keyboard.Design) ?? StandardDesign.From(made);
        keys.Designing = true;
        bar.Visibility = Visibility.Collapsed;
        designBar.Visibility = Visibility.Visible;
        Log.Write($"Keyboard: designing {keyboard.Name}");
    }

    /// <summary>
    /// Hecho: the design kept, a keyboard the keyboard comes with saved as a
    /// new one, which the keyboard then shows (renamed in Tema); a saved one
    /// keeps it in its place. Cancelar: as it was, its rows as tall.
    /// </summary>
    private void EndDesign(bool keep)
    {
        var scale = keys.RowScale;
        keys.Designing = false;
        extraKeys.Visibility = Visibility.Collapsed;
        keys.Visibility = Visibility.Visible;
        designBar.Visibility = Visibility.Collapsed;
        bar.Visibility = Visibility.Visible;
        if (keep && keys.Design is { } designed)
        {
            // The keyboard shown is then the one saved, as it now is: designed again, it opens as saved, and Hecho keeps it in its place.
            if (keyboard.BuiltIn)
            {
                keyboard = Keyboards.SaveAs(Keyboards.NewName(keyboard), keyboard.Base, designed.Text);
                Keyboards.Select(keyboard.Id);
                preferences = KeyboardPreferences.Read();
            }
            else
            {
                Keyboards.SetDesign(keyboard.Id, designed.Text);
                keyboard = keyboard with { Design = designed.Text };
            }

            Log.Write($"Keyboard: design kept as {keyboard.Name}");
        }
        else
        {
            keys.Design = StandardDesign.FromText(keyboard.Design);
            FitKeys(scale);
        }

        ShowSwitcher();
    }

    /// <summary>The designer's buttons in the toolbar's place, Gboard's pills: −, +, + Tecla, + Fila, + Columna, Quitar, Cancelar, Hecho in blue; a sign's pill narrower than a word's.</summary>
    private void FillDesignBar()
    {
        (string Label, Action Pressed, bool Done)[] buttons =
        [
            ("−", () => keys.ResizeMarked(wider: false), false),
            ("+", () => keys.ResizeMarked(wider: true), false),
            (Texts.Get(Resource.String.add_key), ToggleExtraKeys, false),
            (Texts.Get(Resource.String.add_row), keys.AddRow, false),
            (Texts.Get(Resource.String.add_column), keys.AddColumn, false),
            (Texts.Get(Resource.String.remove), keys.RemoveMarked, false),
            (Texts.Get(Resource.String.cancel), () => EndDesign(keep: false), false),
            (Texts.Get(Resource.String.done), () => EndDesign(keep: true), true),
        ];
        foreach (var (label, pressed, done) in buttons)
        {
            var pill = new PillButton(colors, label, done, 16) { Height = PillDp, Margin = new Thickness(PillGapDp, 0, PillGapDp, 0), CornerRadius = new CornerRadius(PillDp / 2) };
            pill.Clicked += pressed;
            designBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(label.Length <= 1 ? 0.7 : 1, GridUnitType.Star) });
            Grid.SetColumn(pill, designBar.Children.Count);
            designBar.Children.Add(pill);
        }
    }

    /// <summary>+ Tecla: the keys a design can add and the keyboard lacks, in the keys' place, by group, as Android's; one clicked comes loose over the keys, to be dragged to its place; again, the keys.</summary>
    private void ToggleExtraKeys()
    {
        if (extraKeys.Visibility == Visibility.Visible)
        {
            extraKeys.Visibility = Visibility.Collapsed;
            keys.Visibility = Visibility.Visible;
            return;
        }

        var list = new StackPanel { Margin = new Thickness(12, 4, 12, 8) };
        foreach (var (name, offered) in StandardKeys.Extras)
        {
            var ids = offered.Where(id => !keys.HasKey(id)).ToArray();
            if (ids.Length == 0)
            {
                continue;
            }

            list.Children.Add(new TextBlock { Text = Texts.Get(name), Foreground = new SolidColorBrush(colors.Outline), FontSize = 13, Margin = new Thickness(4, 8, 0, 4) });
            for (var first = 0; first < ids.Length; first += ExtrasAcross)
            {
                var row = new UniformGrid { Columns = ExtrasAcross };
                for (var i = first; i < first + ExtrasAcross; i++)
                {
                    if (i >= ids.Length || StandardKeys.Extra(ids[i], keys.KeyLanguage) is not { } key)
                    {
                        row.Children.Add(new Border());
                        continue;
                    }

                    var id = ids[i];
                    var button = new PillButton(colors, key.Label, done: false, 15) { Height = ExtraKeyDp, Margin = new Thickness(2, 0, 2, 4), CornerRadius = new CornerRadius(8) };
                    button.Clicked += () =>
                    {
                        ToggleExtraKeys();
                        keys.AddKey(id);
                    };
                    row.Children.Add(button);
                }

                list.Children.Add(row);
            }
        }

        extraKeys.Content = list;
        extraKeys.Height = keys.ActualHeight;
        extraKeys.Background = new SolidColorBrush(colors.Ground);
        keys.Visibility = Visibility.Hidden;
        extraKeys.Visibility = Visibility.Visible;
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
            if (e.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            moving = true;
            DragMove();
            moving = false;
            Keep();
        };
    }

    /// <summary>The bar, the handle and the shadow's band around the keys, at the window's height for rows at a share of Gboard's.</summary>
    private double ChromeHeight => (2 * ShadowDp) + BarDp + HandleBandDp;

    /// <summary>The window as tall as the keyboard in use needs for rows at a share of Gboard's: its rows keep their height when the keyboard changes.</summary>
    private void FitKeys(double scale)
    {
        MinHeight = ChromeHeight + keys.HeightAt(KeyboardSurface.MinRowScale);
        Height = ChromeHeight + keys.HeightAt(Math.Max(KeyboardSurface.MinRowScale, scale));
    }

    /// <summary>Where it was left, as wide, with the keyboard and language it had; the first time, three fifths of the screen wide at its bottom's middle.</summary>
    private void Restore()
    {
        var area = SystemParameters.WorkArea;
        var state = ReadState();
        Width = Math.Max(MinWidth, state?.Width ?? area.Width * 0.6);
        // The keyboard and language as the preferences have them; the language kept before they did, as the window kept it.
        keys.Pc = keyboard.Base == Keyboards.StandardPcBase;
        keys.Design = StandardDesign.FromText(keyboard.Design);
        var language = preferences.Language ?? state?.Language;
        keys.KeyLanguage = StandardKeys.Languages.FirstOrDefault(known => known.Code == language) ?? StandardKeys.Spanish;
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

    private void Keep()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Kept)!);
        var iconKept = icon.IsVisible ? (icon.Left, icon.Top) : (ReadState() is { IconLeft: { } left, IconTop: { } top } ? (left, top) : ((double, double)?)null);
        File.WriteAllText(Kept, JsonSerializer.Serialize(new State(Left, Top, Width, keys.KeyLanguage.Code, iconKept?.Item1, iconKept?.Item2, keys.RowScale)));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);
}

/// <summary>
/// A word on a pill, the designer's, as Android's toolbar pills: a function
/// key's tint, darker while pressed, or the accent for Hecho; the word as
/// large as asked, smaller where the pill has no room for it; a click, what
/// it does.
/// </summary>
public sealed class PillButton : Border
{
    private readonly Brush ground;
    private readonly Brush pressedGround;

    public PillButton(Palette colors, string label, bool done, double size)
    {
        ground = new SolidColorBrush(done ? colors.Primary : colors.Function);
        pressedGround = done ? ground : new SolidColorBrush(colors.FunctionPressed);
        Background = ground;
        Child = new Viewbox
        {
            StretchDirection = StretchDirection.DownOnly,
            Margin = new Thickness(4, 0, 4, 0),
            Child = new TextBlock { Text = label, FontSize = size, Foreground = new SolidColorBrush(done ? colors.OnPrimary : colors.FunctionGlyph) },
        };
    }

    public event Action? Clicked;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        Background = pressedGround;
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var inside = new Rect(RenderSize).Contains(e.GetPosition(this));
        ReleaseMouseCapture();
        Background = ground;
        e.Handled = true;
        if (inside)
        {
            Clicked?.Invoke();
        }
    }
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
