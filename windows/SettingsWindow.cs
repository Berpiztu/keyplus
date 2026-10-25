using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Berpiztu.KeyPlus;

#pragma warning disable WPF0001 // ThemeMode, Windows 11's own look for the controls, is still marked experimental in WPF.

/// <summary>
/// ⚙: the keyboard's settings, an app of its own as on Android (the same
/// program started with --settings, a process apart, so closing the keyboard
/// leaves it open and the keyboard types in it as in any window), its pages
/// listed at the left and the one chosen at the right, as Android shows them
/// on a wide screen, with Android's words; each change kept at once, where
/// the keyboard reads it.
/// </summary>
public sealed class SettingsWindow : Window
{
    private const double WidthDp = 760;
    private const double HeightDp = 600;
    private const double ListWidthDp = 220;
    private const double MarginDp = 20;
    private const double FieldGapDp = 16;

    /// <summary>The room between the parts of Tema's Colores row, which holds many: the least that keeps them apart.</summary>
    private const double RowGapDp = 4;
    private const double Step = 0.5;

    /// <summary>Tema's choices' type and their hint's, smaller than the other pages', the room left to the keyboard drawn under them.</summary>
    private const double CompactSize = 12;
    private const double CompactHintSize = 11;

    /// <summary>The argument that starts the program as the settings.</summary>
    public const string Argument = "--settings";

    private readonly ContentControl page = new();

    /// <summary>The pages' list, at the left, which Tema's designer takes the window from.</summary>
    private readonly DockPanel side = new() { Width = ListWidthDp };

    public SettingsWindow()
    {
        Title = Texts.Get(Resource.String.settings_title);
        ThemeMode = ThemeMode.System;
        Width = WidthDp;
        Height = HeightDp;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        (string Name, Func<FrameworkElement> Page)[] pages =
        [
            (Texts.Get(Resource.String.languages), LanguagesPage),
            (Texts.Get(Resource.String.preferences), PreferencesPage),
            (Texts.Get(Resource.String.theme), ThemePage),
            (Texts.Get(Resource.String.glide), GlidePage),
            (Texts.Get(Resource.String.dictionary), DictionaryPage),
            (Texts.Get(Resource.String.voice_typing), VoicePage),
            (Texts.Get(Resource.String.troubleshooting), TroubleshootingPage),
            (Texts.Get(Resource.String.about), AboutPage),
        ];
        var list = new ListBox { Margin = new Thickness(0, MarginDp / 2, 0, 0) };
        foreach (var (name, _) in pages)
        {
            list.Items.Add(name);
        }

        list.SelectionChanged += (_, _) =>
        {
            appsWatch?.Dispose();
            appsWatch = null;
            ShowPage(pages[Math.Max(0, list.SelectedIndex)].Page());
        };
        list.SelectedIndex = 0;

        // Under the pages, the keyboard opened: started, or shown where it runs hidden.
        var open = new Button { Content = Texts.Get(Resource.String.open_keyboard), Margin = new Thickness(MarginDp / 2), HorizontalAlignment = HorizontalAlignment.Stretch };
        open.Click += (_, _) => System.Diagnostics.Process.Start(Environment.ProcessPath!);
        DockPanel.SetDock(open, Dock.Bottom);
        side.Children.Add(open);
        side.Children.Add(list);

        var panes = new DockPanel();
        DockPanel.SetDock(side, Dock.Left);
        panes.Children.Add(side);
        panes.Children.Add(page);
        Content = panes;
    }

    /// <summary>A page in the window's right, scrolling where it is taller.</summary>
    private void ShowPage(FrameworkElement shown) => page.Content = new ScrollViewer { Content = shown, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    /// <summary>The settings started as a program of their own; where they run already, that one comes to the front.</summary>
    public static void Start() => System.Diagnostics.Process.Start(Environment.ProcessPath!, Argument);

    /// <summary>Idiomas: the keyboard's languages, the one in use marked; one chosen, the keyboard types in it, as 🌐 changes it.</summary>
    private static FrameworkElement LanguagesPage()
    {
        var preferences = KeyboardPreferences.Read();
        var form = Form(Resource.String.languages);
        foreach (var language in StandardKeys.Languages)
        {
            var option = new RadioButton { Content = language.Name, IsChecked = (preferences.Language ?? StandardKeys.Spanish.Code) == language.Code, GroupName = "language" };
            option.Checked += (_, _) => (preferences = KeyboardPreferences.Read() with { Language = language.Code }).Keep();
            form.Children.Add(option);
        }

        return form;
    }

    /// <summary>
    /// Tema, as Android's, by language (a PC's keys differ between them), its
    /// parts each of one size whatever they hold, so nothing on the page moves:
    /// the language looked at (the one typed in at first, left as it is);
    /// the keyboard shown in it, chosen from that language's list (as the
    /// toolbar's switch changes it); its colours (Sistema, Claro, Oscuro), the
    /// page made again in them; a name with Guardar como, which copies the
    /// keyboard chosen under it (to duplicate it, or design a copy), and
    /// Borrar, for one saved; and the keyboard drawn as the card shows it,
    /// zoomed whole to the page, its proportions kept, only to be seen, with
    /// Diseñar in its bar's place, which designs it there one to one, as it
    /// will be seen, the window to itself (made larger where it is smaller
    /// than the keyboard), Hecho changing the keyboard on screen at once.
    /// </summary>
    private FrameworkElement ThemePage()
    {
        // Docked, the preview last, so designing it fills the window.
        var form = new DockPanel { Margin = new Thickness(MarginDp) };
        var title = Heading(Resource.String.theme);
        DockPanel.SetDock(title, Dock.Top);
        form.Children.Add(title);
        var language = KeyboardPreferences.Read().TypingLanguage;
        var languages = new ComboBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, FieldGapDp, 0) };
        var keyboards = new ComboBox();
        var name = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
        var saveAs = new Button { Content = Texts.Get(Resource.String.save_as), Margin = new Thickness(FieldGapDp / 2, 0, 0, 0) };
        var delete = new Button { Content = Texts.Get(Resource.String.delete), Margin = new Thickness(FieldGapDp / 2, 0, 0, 0) };
        var naming = new DockPanel();
        DockPanel.SetDock(delete, Dock.Right);
        DockPanel.SetDock(saveAs, Dock.Right);
        naming.Children.Add(delete);
        naming.Children.Add(saveAs);
        naming.Children.Add(name);

        // The preview in the colours of the application looked at, with Por app on, its window's as the keyboard last saw it
        // there; else Tema's.
        var lookedAt = AppLookedAt();
        var colors = Palette.Read(KeyboardPreferences.Read().ColorsIn(lookedAt, lookedAt is null ? null : Palette.Parse(AppColors.For(lookedAt).WindowColor)));
        // The row smaller as a whole where the page is narrower than it, never cut.
        var coloring = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, Child = ColorsRow() };
        var preview = new KeyboardSurface(colors) { Types = false };
        // The keyboard as it shows now, docked or floating, designed so.
        var docked = KeyboardPreferences.Read().Docked;
        var designer = new KeyboardDesigner(colors, preview) { Docked = docked };
        // Diseñar in the bar's place, a pill as the bar's: designing, the bar stands there and the preview keeps its size.
        var design = KeyboardDesigner.Pill(colors, Texts.Get(Resource.String.design), done: false);
        design.HorizontalAlignment = HorizontalAlignment.Left;
        var bar = new Grid { Height = KeyboardWindow.BarDp };
        bar.Children.Add(design);
        bar.Children.Add(designer.Bar);
        var keysPlace = new Grid();
        keysPlace.Children.Add(preview);
        keysPlace.Children.Add(designer.ExtraKeys);
        var card = new StackPanel();
        card.Children.Add(bar);
        card.Children.Add(keysPlace);
        // The card as it is on screen: a box of one size whatever the keyboard chosen, the card's width and Estándar PC's rows
        // at its rows' height, zoomed whole to the page, so only the settings' size changes it, and one to one while designed.
        // The keyboard chosen at its own size at its top, a shorter one leaving room under it, a taller one (a design's more
        // rows) made smaller to fit.
        var (keysWidth, rowScale) = KeyboardWindow.KeptKeys();
        var rows = StandardKeys.Page(StandardPage.Pc, StandardKeys.Spanish).Count;
        card.Width = keysWidth;
        var box = new Border
        {
            Width = keysWidth,
            Height = KeyboardWindow.BarDp + KeyboardSurface.HeightOf(rows, rowScale),
            Background = new SolidColorBrush(colors.Ground),
            CornerRadius = new CornerRadius(KeyboardWindow.CardCornerDp),
            ClipToBounds = true,
            Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, VerticalAlignment = VerticalAlignment.Top, Child = card },
        };

        // The language, the keyboard and its name in two rows of small type, each label beside its control, the room left to the keyboard drawn under them.
        TextBlock Caption(int text) => new() { Text = Texts.Get(text), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, FieldGapDp / 2, 0) };
        var languageLabel = Caption(Resource.String.languages);
        var keyboardLabel = Caption(Resource.String.keyboard);
        var choosing = new DockPanel { Margin = new Thickness(0, 0, 0, FieldGapDp / 2) };
        foreach (var part in new FrameworkElement[] { languageLabel, languages, keyboardLabel })
        {
            DockPanel.SetDock(part, Dock.Left);
            choosing.Children.Add(part);
        }

        choosing.Children.Add(keyboards);
        var controls = new StackPanel();
        TextElement.SetFontSize(controls, CompactSize);
        controls.Children.Add(choosing);
        controls.Children.Add(coloring);
        controls.Children.Add(naming);
        controls.Children.Add(new TextBlock { Text = Texts.Get(Resource.String.keyboards_saved_summary), Opacity = 0.7, TextWrapping = TextWrapping.Wrap, FontSize = CompactHintSize, Margin = new Thickness(0, 2, 0, FieldGapDp / 2) });
        DockPanel.SetDock(controls, Dock.Top);
        form.Children.Add(controls);
        var zoom = new Viewbox { Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Child = box };
        form.Children.Add(zoom);

        // Designing, the keyboard alone in the window and one to one, the window made larger where it is smaller than the
        // keyboard; then the page as it was, the keyboard zoomed again.
        void Designing(bool on)
        {
            themeDesigning = on;
            side.Visibility = title.Visibility = controls.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            zoom.Stretch = on ? Stretch.None : Stretch.Uniform;
            if (on && form.ActualWidth < box.Width)
            {
                Width += box.Width - form.ActualWidth;
            }
        }

        var shown = Keyboards.Selected(KeyboardPreferences.Read(), language.Code);
        var filling = false;

        // The keyboard chosen as the card shows it: its keys, its design and the language looked at, its rows as tall as the card's; its name to copy it under, Borrar for one saved.
        void Show(KeyboardEntry keyboard)
        {
            shown = keyboard;
            name.Text = keyboard.Name;
            delete.IsEnabled = !keyboard.BuiltIn;
            preview.KeyLanguage = language;
            preview.Pc = keyboard.Base == Keyboards.StandardPcBase;
            preview.Design = StandardDesign.FromText(keyboard.DesignFor(docked));
            preview.Height = preview.HeightAt(rowScale);
        }

        // The language's keyboards in the list, the one it shows chosen.
        void Fill()
        {
            filling = true;
            keyboards.Items.Clear();
            var chosen = Keyboards.Selected(KeyboardPreferences.Read(), language.Code);
            foreach (var keyboard in Keyboards.All(language.Code))
            {
                keyboards.Items.Add(new ComboBoxItem { Content = keyboard.Name, Tag = keyboard });
                if (keyboard.Id == chosen.Id)
                {
                    keyboards.SelectedIndex = keyboards.Items.Count - 1;
                }
            }

            filling = false;
            Show(chosen);
        }

        foreach (var known in StandardKeys.Languages)
        {
            languages.Items.Add(new ComboBoxItem { Content = known.Name, Tag = known });
        }

        languages.SelectedIndex = StandardKeys.Languages.ToList().IndexOf(language);
        languages.SelectionChanged += (_, _) =>
        {
            if (languages.SelectedItem is ComboBoxItem { Tag: StandardLanguage known })
            {
                language = known;
                Fill();
            }
        };

        keyboards.SelectionChanged += (_, _) =>
        {
            if (!filling && keyboards.SelectedItem is ComboBoxItem { Tag: KeyboardEntry keyboard })
            {
                Keyboards.Select(language.Code, keyboard.Id);
                Show(keyboard);
            }
        };
        saveAs.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text))
            {
                return;
            }

            // A copy under the name written, chosen to be designed; the keyboard it was made from stays as it is.
            var copy = Keyboards.SaveAs(name.Text.Trim(), shown.Base, shown.Design, language.Code, shown.DockedDesign);
            Keyboards.Select(language.Code, copy.Id);
            Fill();
        };
        delete.Click += (_, _) =>
        {
            var asked = MessageBox.Show(Texts.Get(Resource.String.windows_delete_ask, shown.Name), shown.Name, MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (asked == MessageBoxResult.OK)
            {
                Keyboards.Delete(shown.Id);
                Fill();
            }
        };
        preview.RowsChanged += () => preview.Height = preview.HeightAt(rowScale);
        design.Clicked += () =>
        {
            design.Visibility = Visibility.Hidden;
            Designing(true);
            designer.Start(shown);
        };
        designer.Ended += (_, _, _) =>
        {
            design.Visibility = Visibility.Visible;
            Designing(false);
            Fill();
        };

        Fill();

        // The applications' colours changed by the keyboard (an application typed in, its window's colour seen): the page drawn
        // again in them, not while the keyboard is designed here.
        appsWatch?.Dispose();
        appsWatch = AppColors.Watch(() =>
        {
            if (!themeDesigning)
            {
                ShowPage(ThemePage());
            }
        });
        return form;
    }

    /// <summary>The applications' colours watched while Tema is shown; whether its keyboard is being designed.</summary>
    private IDisposable? appsWatch;
    private bool themeDesigning;

    /// <summary>One of Personalizado's colours chosen: the two as they were kept for Deshacer, the page drawn again in the new ones, the keyboard taking them at once.</summary>
    private void KeepColors(Func<KeyboardPreferences, KeyboardColors> change)
    {
        var kept = KeyboardPreferences.Read();
        (kept.With(change(kept)) with { Before = kept.Colors }).Keep();
        ShowPage(ThemePage());
    }

    /// <summary>The application looked at in Por aplicación, kept while the page is drawn again.</summary>
    private string? lookedAtApp;

    /// <summary>The application looked at, with Por aplicación on: the one chosen, or the first typed in; none, Tema's colours.</summary>
    private string? AppLookedAt()
    {
        var apps = AppColors.All();
        lookedAtApp = lookedAtApp is not null && apps.ContainsKey(lookedAtApp) ? lookedAtApp : apps.Keys.FirstOrDefault();
        return KeyboardPreferences.Read().PerApp ? lookedAtApp : null;
    }

    /// <summary>
    /// Colores in one row of one size whatever it holds: Tema's choice (Sistema, Claro, Oscuro, Personalizado), and, under
    /// Personalizado alone, Por app, then, with it on, the applications typed in and, for the one chosen, De la app (its
    /// window's colour), then the two colours (that application's, with one chosen) and Deshacer. A part that does not apply is
    /// hidden in its place, the list as wide as its longest choice; each change kept at once, the page drawn again in it,
    /// the keyboard taking it.
    /// </summary>
    private StackPanel ColorsRow()
    {
        var kept = KeyboardPreferences.Read();
        var general = Palette.Read(kept);
        var custom = general.Look == KeyboardLook.Custom;
        var app = custom ? AppLookedAt() : null;
        var appColor = app is not null ? AppColors.For(app) : null;
        Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Hidden;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, FieldGapDp / 2) };
        row.Children.Add(new TextBlock { Text = Texts.Get(Resource.String.look), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, RowGapDp, 0) });

        var looks = new ComboBox { VerticalAlignment = VerticalAlignment.Center };
        foreach (var look in KeyboardLooks.All)
        {
            looks.Items.Add(new ComboBoxItem { Content = Texts.Get(KeyboardLooks.Name(look)), Tag = look });
        }

        looks.SelectedIndex = Array.IndexOf(KeyboardLooks.All, general.Look);
        WidestOf(looks, [.. KeyboardLooks.All.Select(look => Texts.Get(KeyboardLooks.Name(look)))]);
        looks.SelectionChanged += (_, _) =>
        {
            if (looks.SelectedItem is ComboBoxItem { Tag: KeyboardLook look } && look != general.Look)
            {
                (KeyboardPreferences.Read() with { Look = look }).Keep();
                ShowPage(ThemePage());
            }
        };
        row.Children.Add(looks);

        // The two colours: the application's chosen, else Tema's Personalizado's; hidden for an application taking its window's.
        void Choose(Func<AppColor, AppColor> forApp, Func<KeyboardColors, KeyboardColors> forTema)
        {
            if (app is not null && appColor is not null)
            {
                AppColors.Set(app, forApp(appColor) with { Look = AppLook.Custom });
                ShowPage(ThemePage());
            }
            else
            {
                KeepColors(keptNow => forTema(keptNow.Colors));
            }
        }

        // Each colour as the keyboard shows it: the application's, or Tema's.
        var showing = app is not null && appColor is not null ? Palette.Read(kept.ColorsIn(app, Palette.Parse(appColor.WindowColor))) : general;
        var keyShown = showing.KeyColor ?? showing.Ground;
        var accentShown = showing.AccentColor ?? showing.Primary;
        var colorsShown = Shown(custom && appColor?.Look != AppLook.Own);
        var keyColor = ColorButton(Resource.String.look_keyboard, keyShown, colorsShown, chosen => Choose(color => color with { KeyColor = chosen }, colors => colors with { KeyColor = chosen }));
        var accentColor = ColorButton(Resource.String.look_accent, accentShown, colorsShown, chosen => Choose(color => color with { AccentColor = chosen }, colors => colors with { AccentColor = chosen }));

        // Deshacer, Tema's Personalizado's: its two colours as they were before the last one was chosen, one step back, hidden in
        // its place once used, with none, for the other choices, or while an application's colours are shown.
        var undo = new Button { Content = "↶", Padding = new Thickness(RowGapDp, 0, RowGapDp, 0), Margin = new Thickness(RowGapDp, 0, 0, 0) };
        undo.Visibility = Shown(custom && app is null && kept.Before is not null);
        undo.Click += (_, _) =>
        {
            if (KeyboardPreferences.Read() is { Before: { } before } keptNow)
            {
                (keptNow.With(before) with { Before = null }).Keep();
                ShowPage(ThemePage());
            }
        };

        // Por app and the applications typed in, the list filled again as it opens: the keyboard adds to it.
        var on = new CheckBox { Content = Texts.Get(Resource.String.per_app), IsChecked = kept.PerApp, Visibility = Shown(custom), VerticalAlignment = VerticalAlignment.Center, MinWidth = 0, Margin = new Thickness(RowGapDp * 2, 0, 0, 0) };
        on.Click += (_, _) =>
        {
            (KeyboardPreferences.Read() with { PerApp = on.IsChecked == true }).Keep();
            ShowPage(ThemePage());
        };
        var apps = new ComboBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(RowGapDp, 0, 0, 0), Visibility = Shown(custom && kept.PerApp) };
        void FillApps()
        {
            apps.Items.Clear();
            foreach (var name in AppColors.All().Keys)
            {
                apps.Items.Add(name);
            }

            apps.SelectedItem = app;
        }

        FillApps();
        apps.DropDownOpened += (_, _) => FillApps();
        apps.SelectionChanged += (_, _) =>
        {
            if (apps.SelectedItem is string picked && picked != app)
            {
                lookedAtApp = picked;
                ShowPage(ThemePage());
            }
        };

        // De la app: the application chosen takes its window's colour.
        var own = new CheckBox { Content = Texts.Get(Resource.String.app_own), IsChecked = appColor?.Look == AppLook.Own, Visibility = Shown(appColor is not null), VerticalAlignment = VerticalAlignment.Center, MinWidth = 0, Margin = new Thickness(RowGapDp, 0, 0, 0) };
        own.Click += (_, _) =>
        {
            if (app is null || appColor is null)
            {
                return;
            }

            if (own.IsChecked == true)
            {
                AppColors.Set(app, appColor with { Look = AppLook.Own });
            }
            else
            {
                // Its own colours exactly as it showed them, to be changed from there: its window's, and Windows' accent with
                // Windows' own tones.
                AppColors.Set(app, appColor with { Look = AppLook.Custom, KeyColor = appColor.WindowColor ?? appColor.KeyColor, AccentColor = null });
            }

            ShowPage(ThemePage());
        };

        // In the owner's order: Por app, the application, De la app, then the colours and Deshacer.
        foreach (var part in new FrameworkElement[] { on, apps, own, keyColor, accentColor, undo })
        {
            row.Children.Add(part);
        }

        return row;
    }

    /// <summary>
    /// A list as wide as the longest of the words it may hold, whatever it holds, so its choices change and the row does
    /// not move: as drawn, its width less its word's is its own frame and arrow, to which the longest word is added.
    /// </summary>
    private static void WidestOf(ComboBox list, IReadOnlyList<string> words) => list.Loaded += (_, _) =>
    {
        double WidthOf(string word) => new FormattedText(word, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(list.FontFamily, list.FontStyle, list.FontWeight, list.FontStretch), list.FontSize, Brushes.Black,
            VisualTreeHelper.GetDpi(list).PixelsPerDip).WidthIncludingTrailingWhitespace;
        var shown = list.SelectedItem is ComboBoxItem { Content: string word } ? word : "";
        list.Width = list.ActualWidth - WidthOf(shown) + words.Max(WidthOf);
    };

    /// <summary>
    /// A colour of Personalizado's: a button with the colour shown and its name, which opens Windows' own colour picker on
    /// it; one chosen is told as it is kept, #RRGGBB.
    /// </summary>
    private Button ColorButton(int name, Color shown, Visibility visibility, Action<string> chosen)
    {
        var face = new StackPanel { Orientation = Orientation.Horizontal };
        face.Children.Add(new Border { Width = 14, Height = 14, Background = new SolidColorBrush(shown), BorderBrush = SystemColors.ControlTextBrush, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, RowGapDp, 0), VerticalAlignment = VerticalAlignment.Center });
        face.Children.Add(new TextBlock { Text = Texts.Get(name), VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = face, Visibility = visibility, Padding = new Thickness(RowGapDp, 0, RowGapDp, 0), Margin = new Thickness(RowGapDp, 0, 0, 0) };
        button.Click += (_, _) =>
        {
            using var picker = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true, Color = System.Drawing.Color.FromArgb(shown.R, shown.G, shown.B) };
            if (picker.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                chosen(Palette.Text(Color.FromRgb(picker.Color.R, picker.Color.G, picker.Color.B)));
            }
        };
        return button;
    }

    /// <summary>How tall the dictionary's list of words is, whatever it holds, so nothing under it moves.</summary>
    private const double WordListDp = 110;

    /// <summary>A word's line in the dictionary, shown or being written again: a text box's height.</summary>
    private const double WordLineDp = 24;

    /// <summary>The red of a word's ✕, the one that deletes it (<see cref="KeyboardLooks.DeleteRed"/>).</summary>
    private static readonly SolidColorBrush DeleteRed = new(Color.FromArgb(
        (byte)((KeyboardLooks.DeleteRed >> 24) & 0xFF), (byte)((KeyboardLooks.DeleteRed >> 16) & 0xFF), (byte)((KeyboardLooks.DeleteRed >> 8) & 0xFF), (byte)(KeyboardLooks.DeleteRed & 0xFF)));

    /// <summary>
    /// Dictionary (<see cref="PersonalWords"/>): by language, your words in a
    /// box of one size that scrolls, each with ✏ that writes it again and a
    /// red ✕ that deletes it on its line; a word added with Add or Enter; learning new words as you
    /// type and from the words chosen for a swipe, each a choice; the words
    /// chosen forgotten. Each change kept at once, the keyboard taking it
    /// through the file.
    /// </summary>
    private static FrameworkElement DictionaryPage()
    {
        var personal = new PersonalWords(KeyboardWindow.PersonalFile);
        personal.Reload();
        var form = Form(Resource.String.dictionary);
        var languages = new ComboBox();
        foreach (var language in StandardKeys.Languages)
        {
            languages.Items.Add(language.Name);
        }

        languages.SelectedIndex = Math.Max(0, StandardKeys.Languages.ToList().IndexOf(KeyboardPreferences.Read().TypingLanguage));
        var lines = new StackPanel();
        var words = new Border
        {
            Height = WordListDp,
            BorderBrush = SystemColors.ActiveBorderBrush,
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = lines },
        };
        var word = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
        var add = new Button { Content = Texts.Get(Resource.String.dictionary_add), Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(FieldGapDp / 2, 0, 0, 0) };
        string Code() => StandardKeys.Languages[languages.SelectedIndex].Code;

        static Button LineButton(string sign, Brush colour) => new()
        {
            Content = sign,
            Foreground = colour,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 0, 8, 0),
            FontWeight = FontWeights.Bold,
            Cursor = System.Windows.Input.Cursors.Hand,
        };

        // Each word's line: the word, ✏ that writes it again on its own line (Enter keeps it, Esc leaves it), and the red ✕ that deletes it;
        // every line one height, editing or not, so nothing moves.
        void Fill()
        {
            lines.Children.Clear();
            foreach (var kept in personal.Words(Code()))
            {
                var delete = LineButton("✕", DeleteRed);
                delete.Click += (_, _) =>
                {
                    personal.Remove(Code(), kept);
                    Fill();
                };
                var edit = LineButton("✏", SystemColors.ControlTextBrush);
                var shown = new TextBlock { Text = kept, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                var line = new DockPanel { Height = WordLineDp, Margin = new Thickness(6, 1, 0, 1) };
                edit.Click += (_, _) =>
                {
                    var editing = new TextBox { Text = kept, VerticalContentAlignment = VerticalAlignment.Center };
                    editing.KeyDown += (_, e) =>
                    {
                        if (e.Key == System.Windows.Input.Key.Enter)
                        {
                            personal.Rename(Code(), kept, editing.Text);
                            Fill();
                            e.Handled = true;
                        }
                        else if (e.Key == System.Windows.Input.Key.Escape)
                        {
                            Fill();
                            e.Handled = true;
                        }
                    };
                    line.Children.Remove(shown);
                    line.Children.Add(editing);
                    editing.Focus();
                    editing.SelectAll();
                };
                DockPanel.SetDock(delete, Dock.Right);
                DockPanel.SetDock(edit, Dock.Right);
                line.Children.Add(delete);
                line.Children.Add(edit);
                line.Children.Add(shown);
                lines.Children.Add(line);
            }
        }

        void Add()
        {
            personal.Add(Code(), word.Text);
            word.Clear();
            Fill();
        }

        languages.SelectionChanged += (_, _) => Fill();
        add.Click += (_, _) => Add();
        word.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Add();
                e.Handled = true;
            }
        };
        var typed = new CheckBox { Content = Texts.Get(Resource.String.dictionary_learn_typed), IsChecked = personal.LearnTyped };
        var chosen = new CheckBox { Content = Texts.Get(Resource.String.dictionary_learn_chosen), IsChecked = personal.LearnChosen };
        typed.Click += (_, _) => personal.SetLearning(typed.IsChecked == true, chosen.IsChecked == true);
        chosen.Click += (_, _) => personal.SetLearning(typed.IsChecked == true, chosen.IsChecked == true);
        var forget = new Button { Content = Texts.Get(Resource.String.dictionary_forget_chosen), Padding = new Thickness(12, 2, 12, 2), HorizontalAlignment = HorizontalAlignment.Left };
        forget.Click += (_, _) => personal.ForgetChosen();

        var adding = new DockPanel();
        DockPanel.SetDock(add, Dock.Right);
        adding.Children.Add(add);
        adding.Children.Add(word);
        // The choices first, the words last: a list at the top would push them out of the page.
        form.Children.Add(Field(null, typed, Resource.String.dictionary_learn_typed_summary));
        form.Children.Add(Field(null, chosen, Resource.String.dictionary_learn_chosen_summary));
        form.Children.Add(Field(null, forget, null));
        form.Children.Add(Field(Resource.String.dictionary_language, languages, null));
        form.Children.Add(Field(Resource.String.dictionary_words, adding, Resource.String.dictionary_words_summary));
        form.Children.Add(Field(null, words, null));
        Fill();
        return form;
    }

    /// <summary>Glide typing, as Android's: swiping over the letters, its trail and ⌫ deleting a swiped word whole; FUTO Swipe's notice in sight, as its licence asks.</summary>
    private static FrameworkElement GlidePage()
    {
        var preferences = KeyboardPreferences.Read();
        var form = Form(Resource.String.glide);
        var glide = new CheckBox { Content = Texts.Get(Resource.String.glide), IsChecked = preferences.Glide };
        glide.Click += (_, _) => (KeyboardPreferences.Read() with { Glide = glide.IsChecked == true }).Keep();
        var trail = new CheckBox { Content = Texts.Get(Resource.String.glide_trail), IsChecked = preferences.GlideTrail };
        trail.Click += (_, _) => (KeyboardPreferences.Read() with { GlideTrail = trail.IsChecked == true }).Keep();
        form.Children.Add(Field(null, glide, Resource.String.glide_summary));
        form.Children.Add(Field(null, trail, Resource.String.glide_trail_summary));
        var deleteWord = new CheckBox { Content = Texts.Get(Resource.String.glide_delete_word), IsChecked = preferences.GlideDeleteWord };
        deleteWord.Click += (_, _) => (KeyboardPreferences.Read() with { GlideDeleteWord = deleteWord.IsChecked == true }).Keep();
        form.Children.Add(Field(null, deleteWord, Resource.String.glide_delete_word_summary));
        form.Children.Add(Field(null, new TextBlock { Text = Texts.Get(Resource.String.futo), TextWrapping = TextWrapping.Wrap }, null));
        return form;
    }

    /// <summary>Solución de problemas, as Android's: what an app or Windows needs for the keyboard to work with it, and where the keyboard's log is.</summary>
    private static FrameworkElement TroubleshootingPage()
    {
        var form = Form(Resource.String.troubleshooting);
        // VS Code's menus: what they need, and a button that puts it in VS Code's shortcuts, saying what it did.
        var vscode = new StackPanel();
        vscode.Children.Add(new TextBlock { Text = Texts.Get(Resource.String.avoid_menus_electron), TextWrapping = TextWrapping.Wrap });
        var done = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        var put = new Button { Content = Texts.Get(Resource.String.troubleshooting_vscode_button), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, FieldGapDp / 2, 0, 0) };
        put.Click += (_, _) =>
        {
            var result = ElectronShortcuts.AddSwitch();
            done.Text = result.Changed > 0 ? Texts.Get(Resource.String.troubleshooting_vscode_done, result.Changed)
                : result.Already > 0 ? Texts.Get(Resource.String.troubleshooting_vscode_already)
                : Texts.Get(Resource.String.troubleshooting_vscode_none);
        };
        vscode.Children.Add(put);
        vscode.Children.Add(done);
        form.Children.Add(Field(Resource.String.troubleshooting_vscode, vscode, null));
        form.Children.Add(Field(Resource.String.troubleshooting_log, new TextBox { Text = Log.File, IsReadOnly = true }, Resource.String.troubleshooting_log_summary));
        return form;
    }

    /// <summary>Información, as Android's: the version, its updates (<see cref="UpdateCheck"/>), FUTO Swipe's notice and the open source licences the Windows app carries.</summary>
    private static FrameworkElement AboutPage()
    {
        var form = Form(Resource.String.about);
        form.Children.Add(Field(Resource.String.version, new TextBlock { Text = UpdateCheck.Running }, null));
        var automatic = new CheckBox { Content = Texts.Get(Resource.String.updates_automatic), IsChecked = UpdateSettings.Read().Automatic };
        automatic.Click += (_, _) => (UpdateSettings.Read() with { Automatic = automatic.IsChecked == true }).Keep();
        form.Children.Add(Field(null, automatic, Resource.String.updates_automatic_summary));
        var check = new Button { Content = Texts.Get(Resource.String.updates_check), HorizontalAlignment = HorizontalAlignment.Left };
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            await UpdateCheck.CheckAsync(asked: true);
            check.IsEnabled = true;
        };
        form.Children.Add(Field(null, check, null));
        form.Children.Add(Field(null, new TextBlock { Text = Texts.Get(Resource.String.futo), TextWrapping = TextWrapping.Wrap }, null));
        form.Children.Add(Field(Resource.String.licences, new TextBlock { Text = Texts.Get(Resource.String.windows_licences_summary), TextWrapping = TextWrapping.Wrap }, null));
        return form;
    }

    /// <summary>
    /// Android's preferences that apply on Windows, as Android lays them out:
    /// showing the keyboard when writing and, while it shows itself, what it
    /// does when the focus leaves, or else whether its icon stands in its
    /// place; then moving out of the way of the text, and of menus.
    /// </summary>
    private static FrameworkElement PreferencesPage()
    {
        var preferences = KeyboardPreferences.Read();
        var form = Form(Resource.String.preferences);
        var showing = new StackPanel();

        void Change(KeyboardPreferences changed)
        {
            preferences = changed;
            preferences.Keep();
            Showing();
        }

        void Showing()
        {
            showing.Children.Clear();
            var shows = new CheckBox { Content = Texts.Get(Resource.String.show_keyboard), IsChecked = preferences.ShowKeyboard };
            shows.Click += (_, _) => Change(preferences with { ShowKeyboard = shows.IsChecked == true });
            showing.Children.Add(Field(null, shows, Resource.String.show_keyboard_summary));
            if (preferences.ShowKeyboard)
            {
                var choices = new StackPanel();
                foreach (var (choice, name) in KeyboardPreferences.FocusLostChoices.Zip([Resource.String.focus_lost_hide, Resource.String.focus_lost_icon, Resource.String.focus_lost_stay]))
                {
                    var option = new RadioButton { Content = Texts.Get(name), IsChecked = preferences.FocusLost == choice, GroupName = "focusLost" };
                    option.Checked += (_, _) => Change(preferences with { FocusLost = choice });
                    choices.Children.Add(option);
                }

                showing.Children.Add(Field(Resource.String.focus_lost, choices, null));
            }
            else
            {
                var icon = new CheckBox { Content = Texts.Get(Resource.String.keyboard_icon), IsChecked = preferences.KeyboardIcon };
                icon.Click += (_, _) => Change(preferences with { KeyboardIcon = icon.IsChecked == true });
                showing.Children.Add(Field(null, icon, Resource.String.keyboard_icon_summary));
            }
        }

        Showing();
        form.Children.Add(showing);
        var avoid = new CheckBox { Content = Texts.Get(Resource.String.avoid_caret), IsChecked = preferences.AvoidCaret };
        avoid.Click += (_, _) => Change(preferences with { AvoidCaret = avoid.IsChecked == true });
        form.Children.Add(Field(null, avoid, Resource.String.avoid_caret_summary));
        var menus = new CheckBox { Content = Texts.Get(Resource.String.avoid_menus), IsChecked = preferences.AvoidMenus };
        menus.Click += (_, _) => Change(preferences with { AvoidMenus = menus.IsChecked == true });
        form.Children.Add(Field(null, menus, Resource.String.avoid_menus_summary));
        return form;
    }

    /// <summary>How 🎤 listens, as IA-BotMsgs' chat sets it, with its words: the microphone, whether it stops by itself after a silence, and how long, how much the volume is raised, and which ia-voice: this computer's, one on the local network by its IP, or a remote one at an address, with its user and password.</summary>
    private static FrameworkElement VoicePage()
    {
        var settings = VoiceSettings.Read();
        var microphone = new ComboBox();
        var autoStop = new CheckBox { Content = Texts.Get(Resource.String.voice_auto_stop), IsChecked = settings.AutoStop };
        var silence = Stepped(VoiceSettings.LeastSilence, VoiceSettings.MostSilence, settings.SilenceSeconds);
        var gain = Stepped(1, VoiceSettings.MostGain, settings.Gain);
        var local = new RadioButton { Content = Texts.Get(Resource.String.voice_local), IsChecked = settings.Choice == VoiceTyping.OnDevice };
        var lan = new RadioButton { Content = Texts.Get(Resource.String.voice_lan), IsChecked = settings.Choice == VoiceTyping.OnLan };
        var remote = new RadioButton { Content = Texts.Get(Resource.String.voice_remote), IsChecked = settings.Choice == VoiceTyping.Remote };
        var lanAddress = new TextBox { Text = settings.LanAddress };
        var address = new TextBox { Text = settings.Address };
        var user = new TextBox { Text = settings.User };
        var password = new PasswordBox { Password = settings.Password };

        microphone.Items.Add(Texts.Get(Resource.String.voice_microphone_default));
        foreach (var name in NAudioMicrophone.Names())
        {
            microphone.Items.Add(name);
        }

        microphone.SelectedIndex = Math.Max(0, microphone.Items.IndexOf(settings.Microphone));

        void Keep() => new VoiceSettings(microphone.SelectedIndex <= 0 ? "" : (string)microphone.SelectedItem, autoStop.IsChecked == true, silence.Value, gain.Value,
            lan.IsChecked == true ? VoiceTyping.OnLan : remote.IsChecked == true ? VoiceTyping.Remote : VoiceTyping.OnDevice,
            lanAddress.Text, address.Text, user.Text, password.Password).Keep();
        microphone.SelectionChanged += (_, _) => Keep();
        silence.ValueChanged += (_, _) => Keep();
        autoStop.Click += (_, _) =>
        {
            silence.IsEnabled = autoStop.IsChecked == true;
            Keep();
        };
        silence.IsEnabled = settings.AutoStop;
        gain.ValueChanged += (_, _) => Keep();
        local.Checked += (_, _) => Keep();
        lan.Checked += (_, _) => Keep();
        remote.Checked += (_, _) => Keep();
        lanAddress.TextChanged += (_, _) => Keep();
        address.TextChanged += (_, _) => Keep();
        user.TextChanged += (_, _) => Keep();
        password.PasswordChanged += (_, _) => Keep();

        // The local network's IP, and the remote address with its user and password, each only while it is chosen.
        var onLan = new StackPanel { Margin = new Thickness(FieldGapDp + 12, 4, 0, FieldGapDp / 2) };
        onLan.Children.Add(Field(null, lanAddress, Resource.String.voice_lan_hint, 0));
        var reached = new StackPanel { Margin = new Thickness(FieldGapDp + 12, 4, 0, 0) };
        reached.Children.Add(address);
        reached.Children.Add(Field(Resource.String.voice_user, user, null, FieldGapDp / 2));
        reached.Children.Add(Field(Resource.String.voice_password, password, null, FieldGapDp / 2));
        void Reachable()
        {
            onLan.IsEnabled = lan.IsChecked == true;
            reached.IsEnabled = remote.IsChecked == true;
        }

        local.Checked += (_, _) => Reachable();
        lan.Checked += (_, _) => Reachable();
        remote.Checked += (_, _) => Reachable();
        Reachable();
        var service = new StackPanel();
        service.Children.Add(local);
        service.Children.Add(lan);
        service.Children.Add(onLan);
        service.Children.Add(remote);
        service.Children.Add(reached);

        var form = Form(Resource.String.voice_typing);
        form.Children.Add(Field(Resource.String.voice_microphone, microphone, null));
        form.Children.Add(Field(null, autoStop, Resource.String.voice_auto_stop_summary));
        form.Children.Add(Field(Resource.String.voice_silence, Valued(silence, value => $"{value.ToString("0.#", CultureInfo.CurrentCulture)} s"), Resource.String.voice_silence_hint));
        form.Children.Add(Field(Resource.String.voice_gain, Valued(gain, value => $"×{value.ToString("0.#", CultureInfo.CurrentCulture)}"), Resource.String.voice_gain_hint));
        form.Children.Add(Field(Resource.String.voice_service, service, Resource.String.voice_service_hint));
        return form;
    }

    /// <summary>A page: its name on top, its fields under it.</summary>
    private static StackPanel Form(int name)
    {
        var form = new StackPanel { Margin = new Thickness(MarginDp) };
        form.Children.Add(Heading(name));
        return form;
    }

    /// <summary>A page's name, on its top.</summary>
    private static TextBlock Heading(int name) =>
        new() { Text = Texts.Get(name), FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, FieldGapDp) };

    private static Slider Stepped(double least, double most, double value) =>
        new() { Minimum = least, Maximum = most, Value = value, TickFrequency = Step, IsSnapToTickEnabled = true, SmallChange = Step, LargeChange = 1, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>A slider with its value written at its right.</summary>
    private static FrameworkElement Valued(Slider slider, Func<double, string> written)
    {
        var value = new TextBlock { Text = written(slider.Value), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(FieldGapDp / 2, 0, 0, 0) };
        slider.ValueChanged += (_, _) => value.Text = written(slider.Value);
        var row = new DockPanel();
        DockPanel.SetDock(value, Dock.Right);
        row.Children.Add(value);
        row.Children.Add(slider);
        return row;
    }

    /// <summary>A field: its name over it, if it has one, and, under it, what it does.</summary>
    private static FrameworkElement Field(int? name, FrameworkElement control, int? hint, double gapAbove = 0)
    {
        var field = new StackPanel { Margin = new Thickness(0, gapAbove, 0, gapAbove > 0 ? 0 : FieldGapDp) };
        if (name is { } named)
        {
            field.Children.Add(new TextBlock { Text = Texts.Get(named), Margin = new Thickness(0, 0, 0, 4) });
        }

        field.Children.Add(control);
        if (hint is { } said)
        {
            field.Children.Add(new TextBlock { Text = Texts.Get(said), Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), FontSize = 12 });
        }

        return field;
    }
}
