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

    /// <summary>
    /// The settings' one type, everywhere (the owner's, 2026-10-25): Fluent's font at 12, the buttons and every text and
    /// box alike; only the page's title over it larger, 14 in bold. Every button alike, its padding this one.
    /// </summary>
    private const double TextSize = 12;
    private const double TitleSize = 14;
    private static readonly Thickness ButtonPadding = new(12, 0, 12, 0);

    /// <summary>The height of every control, the owner's (2026-10-25): buttons, boxes to type in, drop-downs, the menu's lines, check boxes and radio buttons alike; Fluent's 32 to 40 were out of proportion to a type of 12.</summary>
    private const double ControlHeight = 24;

    /// <summary>The argument that starts the program as the settings.</summary>
    public const string Argument = "--settings";

    private readonly ContentControl page = new();

    /// <summary>The pages' list, at the left, which Tema's designer takes the window from.</summary>
    private readonly DockPanel side = new() { Width = ListWidthDp };

    public SettingsWindow()
    {
        Title = Texts.Get(Resource.String.settings_title);
        ThemeMode = ThemeMode.System;
        UniformType();
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
            (Texts.Get(Resource.String.spelling), SpellingPage),
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

        // Over the pages' list, as Android's app has it: the box to try the keys in.
        var trying = TryBox();
        DockPanel.SetDock(trying, Dock.Top);
        side.Children.Add(trying);
        side.Children.Add(list);

        var panes = new DockPanel();
        DockPanel.SetDock(side, Dock.Left);
        panes.Children.Add(side);
        panes.Children.Add(page);
        Content = panes;
    }

    /// <summary>
    /// The boxes to try each kind of field's keys in (<see cref="StandardField"/>), two by two: text, a number's, an
    /// e-mail address's and a web address's, by their short signs; the keyboard tells each kind by its UI Automation id
    /// (<see cref="FocusWatch.FieldIds"/>).
    /// </summary>
    private static readonly (int Hint, string? Id)[] TryFields =
    [
        (Resource.String.try_keys, null),
        (Resource.String.try_number, FocusWatch.NumberFieldId),
        (Resource.String.try_email, FocusWatch.EmailFieldId),
        (Resource.String.try_web, FocusWatch.WebFieldId),
    ];

    /// <summary>
    /// Over the pages' list, as Android's app has it: a box for each kind of field to try its keys in, and what Spelling
    /// does, without another program, two by two, as wide as the list and each a line tall, the four about as tall as
    /// the one box of three lines they took the place of (the owner's, 2026-10-25). Their size is fixed: a long text
    /// scrolls inside and nothing under them moves.
    /// </summary>
    private static FrameworkElement TryBox()
    {
        var table = new Grid { Margin = new Thickness(MarginDp / 2, MarginDp / 2, MarginDp / 2, 0) };
        table.ColumnDefinitions.Add(new ColumnDefinition());
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MarginDp / 2) });
        table.ColumnDefinitions.Add(new ColumnDefinition());
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(MarginDp / 2) });
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < TryFields.Length; i++)
        {
            var (hint, id) = TryFields[i];
            var box = new TextBox { AcceptsTab = id is null, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden };
            if (id is not null)
            {
                System.Windows.Automation.AutomationProperties.SetAutomationId(box, id);
            }

            var place = Hinted(box, Texts.Get(hint));
            Grid.SetColumn(place, (i % 2) * 2);
            Grid.SetRow(place, (i / 2) * 2);
            table.Children.Add(place);
        }

        return table;
    }

    /// <summary>The room WPF's text box keeps inside its border, either side of its text (its view's margin, 2 each side).</summary>
    private const double TextBoxInnerMargin = 4;

    /// <summary>WPF's text box has no hint of its own: Android's words over it while it is empty, where its text starts.</summary>
    private static Grid Hinted(TextBox box, string words)
    {
        // The window's type, as the box's; where the box's text starts once its look (Fluent's padding) is applied.
        var hint = new TextBlock
        {
            Text = words,
            Opacity = 0.6,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        box.Loaded += (_, _) => hint.Margin = new Thickness(box.BorderThickness.Left + box.Padding.Left + (TextBoxInnerMargin / 2), 0, 0, 0);
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden;
        var place = new Grid();
        place.Children.Add(box);
        place.Children.Add(hint);
        return place;
    }

    /// <summary>
    /// The settings' one type and one size of control: the window's size and font, which every text, box and control
    /// takes, Fluent's own size set to it too; every control on Fluent's look, one height (<see cref="ControlHeight"/>),
    /// with one padding across.
    /// </summary>
    private void UniformType()
    {
        FontSize = TextSize;
        var font = TryFindResource("ContentControlThemeFontFamily") as FontFamily ?? FontFamily;
        FontFamily = font;
        Resources["ControlContentThemeFontSize"] = TextSize;
        Uniform(typeof(Button), ButtonPadding);
        Uniform(typeof(TextBox), ButtonPadding);
        Uniform(typeof(ComboBox), ButtonPadding);
        Uniform(typeof(ListBoxItem), ButtonPadding);
        Uniform(typeof(CheckBox), null);
        Uniform(typeof(RadioButton), null);
    }

    /// <summary>A kind of control on Fluent's look, <see cref="ControlHeight"/> tall and, where given, this padding, for the whole window; a check box's and a radio button's own padding kept, the room between the box and its words.</summary>
    private void Uniform(Type kind, Thickness? padding)
    {
        var style = new Style(kind, TryFindResource(kind) as Style);
        style.Setters.Add(new Setter(MinHeightProperty, 0.0));
        style.Setters.Add(new Setter(HeightProperty, ControlHeight));
        if (padding is { } across)
        {
            style.Setters.Add(new Setter(Control.PaddingProperty, across));
        }

        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        if (padding is null)
        {
            style.Setters.Add(new EventSetter(LoadedEvent, new RoutedEventHandler((sender, _) => FitTemplate((FrameworkElement)sender))));
        }

        Resources[kind] = style;
    }

    /// <summary>
    /// A check box's or a radio button's look made as tall as the control: Fluent's draws its box or circle in a part of
    /// its own 32 tall, which a lower control cut; that part made <see cref="ControlHeight"/> tall, the box and the
    /// words on its middle line.
    /// </summary>
    private static void FitTemplate(FrameworkElement control)
    {
        var parts = new Stack<DependencyObject>([control]);
        while (parts.TryPop(out var part))
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(part); i++)
            {
                var child = VisualTreeHelper.GetChild(part, i);
                if (child is FrameworkElement shaped && shaped.TemplatedParent == control)
                {
                    // The part Fluent made 32 tall, made the control's height, in its middle; the box or circle and the words on one
                    // middle line, the words without the margin Fluent puts over them. A part that takes its parent's height is
                    // left so: centred, it would have none, and a check box's square showed as a line.
                    if (shaped.Height > ControlHeight)
                    {
                        shaped.Height = ControlHeight;
                        shaped.VerticalAlignment = VerticalAlignment.Center;
                    }

                    if (shaped.MinHeight > ControlHeight)
                    {
                        shaped.MinHeight = 0;
                    }

                    if (shaped is ContentPresenter)
                    {
                        shaped.VerticalAlignment = VerticalAlignment.Center;
                        shaped.Margin = new Thickness(shaped.Margin.Left, 0, shaped.Margin.Right, 0);
                    }
                }

                parts.Push(child);
            }
        }
    }

    /// <summary>The corners of a panel that groups a page's part, Fluent's card's.</summary>
    private const double CardCornerDp = 8;

    /// <summary>A part of a page grouped on a panel, raised as Fluent's cards are: their ground and their outline, round corners.</summary>
    private Border Card(UIElement content) => new()
    {
        Background = TryFindResource("CardBackgroundFillColorDefaultBrush") as Brush ?? SystemColors.WindowBrush,
        BorderBrush = TryFindResource("CardStrokeColorDefaultBrush") as Brush ?? SystemColors.ActiveBorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(CardCornerDp),
        Padding = new Thickness(FieldGapDp / 2),
        Margin = new Thickness(0, 0, 0, FieldGapDp / 2),
        Child = content,
    };

    /// <summary>A page in the window's right, scrolling where it is taller.</summary>
    private void ShowPage(FrameworkElement shown) => page.Content = new ScrollViewer { Content = shown, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    /// <summary>The settings started as a program of their own; where they run already, that one comes to the front.</summary>
    public static void Start() => System.Diagnostics.Process.Start(Environment.ProcessPath!, Argument);

    /// <summary>
    /// Idiomas: the keyboard's languages, a line each: the one typed in
    /// marked (one chosen, the keyboard types in it, as 🌐 changes it), and
    /// More options opening the language's own page (<see cref="LanguagePage"/>),
    /// its Spelling and its words; a language added comes with its line.
    /// </summary>
    private FrameworkElement LanguagesPage()
    {
        var form = Form(Resource.String.languages);
        form.Children.Add(LanguageLines(choosing: true, LanguagesPage));
        return form;
    }

    /// <summary>
    /// The keyboard's languages, a line each with More options, which opens
    /// the language's page, its ← back to the page it was opened from
    /// (<paramref name="from"/>): on Languages, the one typed in marked and
    /// chosen there; on Spelling and Dictionary, only named.
    /// </summary>
    private FrameworkElement LanguageLines(bool choosing, Func<FrameworkElement> from)
    {
        var preferences = KeyboardPreferences.Read();

        // A grid, so every More options stands in one column whatever the names' lengths.
        var lines = new Grid();
        lines.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        lines.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var language in StandardKeys.Languages)
        {
            var row = lines.RowDefinitions.Count;
            lines.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            FrameworkElement name;
            if (choosing)
            {
                var option = new RadioButton { Content = language.Name, IsChecked = (preferences.Language ?? StandardKeys.Spanish.Code) == language.Code, GroupName = "language" };
                option.Checked += (_, _) => (preferences = KeyboardPreferences.Read() with { Language = language.Code }).Keep();
                name = option;
            }
            else
            {
                name = new TextBlock { Text = language.Name };
            }

            name.VerticalAlignment = VerticalAlignment.Center;
            name.Margin = new Thickness(0, 0, 0, FieldGapDp / 2);
            var open = new Button { Content = Texts.Get(Resource.String.more_options), Margin = new Thickness(FieldGapDp, 0, 0, FieldGapDp / 2) };
            open.Click += (_, _) => ShowPage(LanguagePage(language, from));
            Grid.SetRow(name, row);
            Grid.SetRow(open, row);
            Grid.SetColumn(open, 1);
            lines.Children.Add(name);
            lines.Children.Add(open);
        }

        return lines;
    }

    /// <summary>
    /// A language's own page, opened from Languages, Spelling or Dictionary
    /// (← back to it, <paramref name="from"/>): its
    /// Spelling, only what Spelling has for it (<see cref="LanguageOptions.SpellingChoices"/>),
    /// a choice that fetches files fetching them as it is turned on, its line
    /// under it saying how much while it does; and its words, your
    /// dictionary's (<see cref="WordsPart"/>).
    /// </summary>
    private FrameworkElement LanguagePage(StandardLanguage language, Func<FrameworkElement> from)
    {
        var code = language.Code;
        var form = new StackPanel { Margin = new Thickness(MarginDp) };
        var back = new Button { Content = "←", Margin = new Thickness(0, 0, FieldGapDp / 2, 0), VerticalAlignment = VerticalAlignment.Center };
        back.Click += (_, _) => ShowPage(from());
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, FieldGapDp) };
        heading.Children.Add(back);
        heading.Children.Add(new TextBlock { Text = language.Name, FontSize = TitleSize, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        form.Children.Add(heading);
        form.Children.Add(Section(Resource.String.spelling));
        foreach (var choice in LanguageOptions.SpellingChoices(code, Texts.Get))
        {
            var box = new CheckBox { Content = Texts.Get(choice.Label), IsChecked = choice.Get(KeyboardPreferences.Read().SpellingIn(code)) };
            form.Children.Add(FieldSaying(null, box, choice.Summary));
            var state = Choosing(box, choice.Fetches, on =>
            {
                var now = KeyboardPreferences.Read();
                now.WithSpelling(code, choice.Set(now.SpellingIn(code), on)).Keep();
            });
            if (state is not null)
            {
                form.Children.Add(state);
            }
        }

        // Glide typing, the language's own: its word list, and the model the first time.
        form.Children.Add(Section(Resource.String.glide));
        var glide = new CheckBox { Content = Texts.Get(Resource.String.glide), IsChecked = KeyboardPreferences.Read().GlidesIn(code) };
        form.Children.Add(FieldSaying(null, glide, LanguageOptions.GlideSummary(language, Texts.Get)));
        form.Children.Add(Choosing(glide, LanguageOptions.GlideOf(language), on => KeyboardPreferences.Read().WithGlide(code, on).Keep())!);
        form.Children.Add(Section(Resource.String.dictionary));
        form.Children.Add(WordsPart(code));
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

        // Two panels, each its part (the owner's, 2026-10-25): the keyboard, its name and its summary; the colours.
        var choosingKeyboard = new StackPanel();
        choosingKeyboard.Children.Add(choosing);
        choosingKeyboard.Children.Add(naming);
        choosingKeyboard.Children.Add(new TextBlock { Text = Texts.Get(Resource.String.keyboards_saved_summary), Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        coloring.Margin = new Thickness(0);
        var controls = new StackPanel();
        controls.Children.Add(Card(choosingKeyboard));
        controls.Children.Add(Card(coloring));
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
    /// Colores in two lines of one size whatever they hold (the owner's, 2026-10-25): Tema's choice (Sistema, Claro, Oscuro,
    /// Personalizado, Por aplicación), the two colours and Deshacer: Personalizado's for every application, or, Por
    /// aplicación, the one chosen's; under them Aplicación, the applications typed in and, for the one chosen, De la app
    /// (its window's colours). Por aplicación is Personalizado with Por app on, as kept before. Nothing is
    /// hidden: a part that does not apply is there, turned off; the two lists as wide as the choices' longest, whatever
    /// they hold (an application's open list as wide as its names). Each change kept at once, the page drawn again in
    /// it, the keyboard taking it.
    /// </summary>
    private Grid ColorsRow()
    {
        var kept = KeyboardPreferences.Read();
        var general = Palette.Read(kept);
        var custom = general.Look == KeyboardLook.Custom;
        var perApp = custom && kept.PerApp;
        var app = perApp ? AppLookedAt() : null;
        var appColor = app is not null ? AppColors.For(app) : null;
        var rows = new Grid { Margin = new Thickness(0, 0, 0, FieldGapDp / 2) };
        foreach (var _ in Enumerable.Range(0, 3))
        {
            rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowGapDp) });
        rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Place(FrameworkElement part, int row, int column)
        {
            part.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(part, row * 2);
            Grid.SetColumn(part, column);
            rows.Children.Add(part);
        }

        Place(new TextBlock { Text = Texts.Get(Resource.String.look), Margin = new Thickness(0, 0, RowGapDp, 0) }, 0, 0);

        // Tema's choices, then Por aplicación, Personalizado with each application's colours.
        const string PerAppChoice = "perApp";
        var looks = new ComboBox();
        foreach (var look in KeyboardLooks.All)
        {
            looks.Items.Add(new ComboBoxItem { Content = Texts.Get(KeyboardLooks.Name(look)), Tag = look });
        }

        looks.Items.Add(new ComboBoxItem { Content = Texts.Get(Resource.String.per_app), Tag = PerAppChoice });
        looks.SelectedIndex = perApp ? KeyboardLooks.All.Length : Array.IndexOf(KeyboardLooks.All, general.Look);
        WidestOf(looks, [.. KeyboardLooks.All.Select(look => Texts.Get(KeyboardLooks.Name(look))), Texts.Get(Resource.String.per_app)]);
        looks.SelectionChanged += (_, _) =>
        {
            var now = KeyboardPreferences.Read();
            var chosen = looks.SelectedItem switch
            {
                ComboBoxItem { Tag: PerAppChoice } => now with { Look = KeyboardLook.Custom, PerApp = true },
                ComboBoxItem { Tag: KeyboardLook look } => now with { Look = look, PerApp = false },
                _ => now,
            };
            if (chosen != now)
            {
                chosen.Keep();
                ShowPage(ThemePage());
            }
        };
        Place(looks, 0, 1);

        // The applications typed in, the list filled again as it opens: the keyboard adds to it.
        var apps = new ComboBox { IsEnabled = perApp };
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

        // As wide as the choices' list above it, whatever the application's name.
        looks.Loaded += (_, _) => apps.Width = looks.Width;
        Place(new TextBlock { Text = Texts.Get(Resource.String.app_list), Margin = new Thickness(0, 0, RowGapDp, 0) }, 1, 0);
        Place(apps, 1, 1);

        // De la app: the application chosen takes its window's colours.
        var own = new CheckBox { Content = Texts.Get(Resource.String.app_own), IsChecked = appColor?.Look == AppLook.Own, IsEnabled = appColor is not null, MinWidth = 0, Margin = new Thickness(RowGapDp, 0, 0, 0) };
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
        Place(own, 1, 2);

        // The two colours: the application's chosen, else Personalizado's for every application.
        void Choose(Func<AppColor, AppColor> forApp, Func<KeyboardColors, KeyboardColors> forTema)
        {
            if (app is not null && appColor is not null)
            {
                AppColors.Set(app, forApp(appColor) with { Look = AppLook.Custom, Before = new(appColor.KeyColor, appColor.AccentColor) });
                ShowPage(ThemePage());
            }
            else
            {
                KeepColors(keptNow => forTema(keptNow.Colors));
            }
        }

        // Each colour as the keyboard shows it: the application's, or Tema's. On with Personalizado, or, Por aplicación,
        // for an application chosen that does not take its window's.
        var showing = app is not null && appColor is not null ? Palette.Read(kept.ColorsIn(app, Palette.Parse(appColor.WindowColor))) : general;
        var colorsOn = custom && (!perApp || appColor is { Look: not AppLook.Own });
        var colours = new StackPanel { Orientation = Orientation.Horizontal };
        colours.Children.Add(ColorButton(Resource.String.look_keyboard, showing.KeyColor ?? showing.Ground, colorsOn, chosen => Choose(color => color with { KeyColor = chosen }, colors => colors with { KeyColor = chosen })));
        colours.Children.Add(ColorButton(Resource.String.look_accent, showing.AccentColor ?? showing.Primary, colorsOn, chosen => Choose(color => color with { AccentColor = chosen }, colors => colors with { AccentColor = chosen })));


        // Deshacer, beside the two colours it takes back: those shown as they were before the last one was chosen, one step
        // back; off once used, with none, or where the colours are off.
        var undo = new Button { Content = Texts.Get(Resource.String.undo), Margin = new Thickness(RowGapDp, 0, 0, 0), IsEnabled = colorsOn && (appColor is not null ? appColor.Before is not null : kept.Before is not null) };
        undo.Click += (_, _) =>
        {
            if (app is not null && appColor is { Before: { } appBefore })
            {
                AppColors.Set(app, appColor with { KeyColor = appBefore.KeyColor, AccentColor = appBefore.AccentColor, Before = null });
            }
            else if (app is null && KeyboardPreferences.Read() is { Before: { } before } keptNow)
            {
                (keptNow.With(before) with { Before = null }).Keep();
            }

            ShowPage(ThemePage());
        };
        colours.Children.Add(undo);
        Place(colours, 0, 2);
        return rows;
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
    /// it; one chosen is told as it is kept, #RRGGBB; off where it does not apply.
    /// </summary>
    private Button ColorButton(int name, Color shown, bool enabled, Action<string> chosen)
    {
        const double OffOpacity = 0.35;
        // Off, its colour and its words faded, so it reads as off: Fluent fades a button's frame alone, and the colour shown
        // in full looked as if it could be chosen (the owner, 2026-10-25).
        var face = new StackPanel { Orientation = Orientation.Horizontal, Opacity = enabled ? 1 : OffOpacity };
        face.Children.Add(new Border { Width = 14, Height = 14, Background = new SolidColorBrush(shown), BorderBrush = SystemColors.ControlTextBrush, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, RowGapDp, 0), VerticalAlignment = VerticalAlignment.Center });
        face.Children.Add(new TextBlock { Text = Texts.Get(name), VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = face, IsEnabled = enabled, Margin = new Thickness(RowGapDp, 0, 0, 0) };
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

    /// <summary>
    /// Spelling, what goes for every language: all of it on and off at once
    /// (the toolbar's button too), where each language's is (its page under
    /// Languages), Download again, which fetches again whole what every
    /// language's choices turned on fetched, and that on Windows only what the
    /// keys typed whole is corrected.
    /// </summary>
    private FrameworkElement SpellingPage()
    {
        var preferences = KeyboardPreferences.Read();
        var form = Form(Resource.String.spelling);
        // As the toolbar's button: off while no language has anything downloaded to correct with, and said so when turned on then.
        bool CanCorrect(KeyboardPreferences kept) => StandardKeys.Languages.Any(language => kept.CanCorrect(language.Code));
        var correcting = new CheckBox { Content = Texts.Get(Resource.String.spelling_on), IsChecked = preferences.SpellingOn && CanCorrect(preferences) };
        correcting.Click += (_, _) =>
        {
            var now = KeyboardPreferences.Read();
            if (correcting.IsChecked == true && !CanCorrect(now))
            {
                correcting.IsChecked = false;
                Log.Write("Spelling: nothing downloaded to correct with, so Correct as I type stays off");
                MessageBox.Show(Texts.Get(Resource.String.spelling_nothing_downloaded), Texts.Get(Resource.String.spelling), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            (now with { SpellingOn = correcting.IsChecked == true }).Keep();
        };
        form.Children.Add(Field(null, correcting, Resource.String.spelling_on_summary));
        var capitals = new CheckBox { Content = Texts.Get(Resource.String.spelling_capitalize), IsChecked = preferences.CapitalizeSentences };
        capitals.Click += (_, _) => (KeyboardPreferences.Read() with { CapitalizeSentences = capitals.IsChecked == true }).Keep();
        form.Children.Add(Field(null, capitals, Resource.String.spelling_capitalize_summary));
        form.Children.Add(Field(null, LanguageLines(choosing: false, SpellingPage), Resource.String.spelling_by_language));

        // What the languages' choices turned on fetched, each once, one after another in one line; with nothing turned on, hidden in its place.
        var turnedOn = StandardKeys.Languages
            .SelectMany(language => LanguageOptions.SpellingChoices(language.Code, Texts.Get)
                .Where(choice => choice.Fetches is not null && choice.Get(preferences.SpellingIn(language.Code)))
                .Select(choice => choice.Fetches!))
            .Concat(StandardKeys.Languages.Where(language => preferences.GlidesIn(language.Code)).Select(LanguageOptions.GlideOf))
            .DistinctBy(fetched => fetched.Named)
            .ToList();
        var again = new Button
        {
            Content = Texts.Get(Resource.String.spelling_download_again),
            HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = turnedOn.Count > 0 ? Visibility.Visible : Visibility.Hidden,
        };
        var state = new TextBlock { Height = 20 };
        again.Click += async (_, _) =>
        {
            again.IsEnabled = false;
            foreach (var fetched in turnedOn)
            {
                await FetchAsync(fetched, state);
            }

            again.IsEnabled = true;
        };
        form.Children.Add(Field(null, again, Resource.String.spelling_download_again_summary));
        form.Children.Add(state);
        form.Children.Add(Field(null, new TextBlock { Text = Texts.Get(Resource.String.spelling_windows_note), TextWrapping = TextWrapping.Wrap }, null));
        return form;
    }

    /// <summary>
    /// A choice's check box made to keep what it is set to (<paramref name="keep"/>);
    /// where it fetches files, off until turned on and then fetched once (the
    /// keyboard never carries them), its line under it, returned to be put
    /// there, of one height: the files here, how much of them while they
    /// come, or, turned on without them, that they are missing with Download
    /// beside it, hidden in its place otherwise. Nothing is ever fetched but
    /// by turning it on or by Download.
    /// </summary>
    private static FrameworkElement? Choosing(CheckBox box, Fetched? fetches, Action<bool> keep)
    {
        if (fetches is null)
        {
            box.Click += (_, _) => keep(box.IsChecked == true);
            return null;
        }

        var state = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var download = new Button { Content = Texts.Get(Resource.String.download), Margin = new Thickness(FieldGapDp / 2, 0, 0, 0) };
        void Show()
        {
            var missing = box.IsChecked == true && !fetches.Downloaded();
            state.Text = fetches.Downloaded() ? Texts.Get(fetches.Done) : missing ? Texts.Get(Resource.String.spelling_not_downloaded) : "";
            download.Visibility = missing ? Visibility.Visible : Visibility.Hidden;
        }

        async Task Fetch()
        {
            box.IsEnabled = false;
            download.Visibility = Visibility.Hidden;
            await FetchAsync(fetches, state);
            box.IsEnabled = true;
        }

        box.Click += async (_, _) =>
        {
            if (box.IsChecked == true && !fetches.Downloaded())
            {
                await Fetch();
                box.IsChecked = fetches.Downloaded();
            }

            keep(box.IsChecked == true);
            Show();
        };
        download.Click += async (_, _) =>
        {
            await Fetch();
            Show();
        };
        Show();
        var line = new StackPanel { Orientation = Orientation.Horizontal, Height = 26 };
        line.Children.Add(state);
        line.Children.Add(download);
        return line;
    }

    /// <summary>
    /// What a choice fetches fetched, how much shown in its line as it goes;
    /// a failure in a window and the log, the files there before kept until
    /// new ones are checked. Whether they are on this computer now.
    /// </summary>
    private static async Task<bool> FetchAsync(Fetched what, TextBlock state)
    {
        try
        {
            await what.Download(new Progress<(long Done, long Total)>(at =>
                state.Text = Texts.Get(what.Downloading, Megabytes(at.Done), Megabytes(Math.Max(at.Total, at.Done)))), CancellationToken.None);
            state.Text = Texts.Get(what.Done);
            return true;
        }
        catch (Exception failure) when (failure is System.Net.Http.HttpRequestException or System.IO.IOException or System.IO.InvalidDataException or TaskCanceledException)
        {
            Log.Write($"Spelling: {what.Named} could not be downloaded: {failure.Message}");
            state.Text = what.Downloaded() ? Texts.Get(what.Done) : "";
            MessageBox.Show(Texts.Get(what.Failed, failure.Message), Texts.Get(Resource.String.spelling), MessageBoxButton.OK, MessageBoxImage.Warning);
            return what.Downloaded();
        }
    }

    private static string Megabytes(long bytes) => (bytes / 1_000_000.0).ToString("0.0", CultureInfo.CurrentCulture);

    /// <summary>How tall Troubleshooting's log viewer is, whatever the log holds.</summary>
    private const double LogViewerDp = 220;

    /// <summary>How tall the dictionary's list of words is, whatever it holds, so nothing under it moves.</summary>
    private const double WordListDp = 110;

    /// <summary>A word's line in the dictionary, shown or being written again: a text box's height.</summary>
    private const double WordLineDp = 24;

    /// <summary>The red of a word's ✕, the one that deletes it (<see cref="KeyboardLooks.DeleteRed"/>).</summary>
    private static readonly SolidColorBrush DeleteRed = new(Color.FromArgb(
        (byte)((KeyboardLooks.DeleteRed >> 24) & 0xFF), (byte)((KeyboardLooks.DeleteRed >> 16) & 0xFF), (byte)((KeyboardLooks.DeleteRed >> 8) & 0xFF), (byte)(KeyboardLooks.DeleteRed & 0xFF)));

    /// <summary>
    /// Dictionary (<see cref="PersonalWords"/>), what goes for every language:
    /// learning new words as you type and from the words chosen for a swipe,
    /// each a choice; the words chosen forgotten; every language's words
    /// exported; and where each language's words are (its page under
    /// Languages, <see cref="WordsPart"/>). Each change kept at once, the
    /// keyboard taking it through the file.
    /// </summary>
    private FrameworkElement DictionaryPage()
    {
        var personal = new PersonalWords(KeyboardWindow.PersonalFile);
        personal.Reload();
        var form = Form(Resource.String.dictionary);
        var typed = new CheckBox { Content = Texts.Get(Resource.String.dictionary_learn_typed), IsChecked = personal.LearnTyped };
        var chosen = new CheckBox { Content = Texts.Get(Resource.String.dictionary_learn_chosen), IsChecked = personal.LearnChosen };
        typed.Click += (_, _) => personal.SetLearning(typed.IsChecked == true, chosen.IsChecked == true);
        chosen.Click += (_, _) => personal.SetLearning(typed.IsChecked == true, chosen.IsChecked == true);
        var forget = new Button { Content = Texts.Get(Resource.String.dictionary_forget_chosen), HorizontalAlignment = HorizontalAlignment.Left };
        forget.Click += (_, _) => personal.ForgetChosen();
        var export = new Button { Content = Texts.Get(Resource.String.dictionary_export), HorizontalAlignment = HorizontalAlignment.Left };
        export.Click += (_, _) =>
        {
            var save = new Microsoft.Win32.SaveFileDialog { FileName = $"berpiztu-dictionary-{DateTime.Now:yyyy-MM-dd_HHmm}.json", Filter = "JSON|*.json" };
            if (save.ShowDialog() != true)
            {
                return;
            }

            try
            {
                System.IO.File.WriteAllText(save.FileName, personal.Export());
                MessageBox.Show(Texts.Get(Resource.String.dictionary_exported, personal.Count), Texts.Get(Resource.String.dictionary));
            }
            catch (Exception failure) when (failure is System.IO.IOException or UnauthorizedAccessException)
            {
                DictionaryFailed(failure);
            }
        };

        form.Children.Add(Field(null, typed, Resource.String.dictionary_learn_typed_summary));
        form.Children.Add(Field(null, chosen, Resource.String.dictionary_learn_chosen_summary));
        form.Children.Add(Field(null, forget, null));
        form.Children.Add(Field(null, export, Resource.String.dictionary_export_summary));
        form.Children.Add(Field(null, LanguageLines(choosing: false, DictionaryPage), Resource.String.dictionary_by_language));
        form.Children.Add(Field(Resource.String.dictionary_addresses, WordsPart(PersonalWords.Shared), Resource.String.dictionary_addresses_summary));
        return form;
    }

    /// <summary>A dictionary's file that could not be read or written, or what it holds: told in a window and written to the log.</summary>
    private static void DictionaryFailed(Exception failure)
    {
        Log.Write($"Dictionary: the file could not be read or written: {failure.Message}");
        MessageBox.Show(Texts.Get(Resource.String.dictionary_file_failed, failure.Message), Texts.Get(Resource.String.dictionary), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>
    /// A language's words, on its page: a word added with Add or Enter; your
    /// words in a box of one size that scrolls, each with ✏ that writes it
    /// again and a red ✕ that deletes it on its line; and a dictionary
    /// imported, its words without a language of their own into this one.
    /// </summary>
    private static FrameworkElement WordsPart(string code)
    {
        var personal = new PersonalWords(KeyboardWindow.PersonalFile);
        personal.Reload();
        var part = new StackPanel();
        var lines = new StackPanel();
        var words = new Border
        {
            Height = WordListDp,
            BorderBrush = SystemColors.ActiveBorderBrush,
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = lines },
        };
        var word = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
        var add = new Button { Content = Texts.Get(Resource.String.dictionary_add), Margin = new Thickness(FieldGapDp / 2, 0, 0, 0) };

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
            foreach (var kept in personal.Words(code))
            {
                var delete = LineButton("✕", DeleteRed);
                delete.Click += (_, _) =>
                {
                    personal.Remove(code, kept);
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
                            personal.Rename(code, kept, editing.Text);
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
            // The addresses' list takes only addresses: anything else belongs in a language's words.
            if (code == PersonalWords.Shared && !PersonalWords.IsAddress(word.Text))
            {
                Log.Write("Dictionary: an address not added, for it has no @");
                MessageBox.Show(Texts.Get(Resource.String.dictionary_not_address), Texts.Get(Resource.String.dictionary_addresses), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            personal.Add(code, word.Text);
            word.Clear();
            Fill();
        }

        add.Click += (_, _) => Add();
        word.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Add();
                e.Handled = true;
            }
        };
        var addresses = code == PersonalWords.Shared;
        var import = new Button { Content = Texts.Get(addresses ? Resource.String.dictionary_import_addresses : Resource.String.dictionary_import), HorizontalAlignment = HorizontalAlignment.Left };
        import.Click += (_, _) =>
        {
            var open = new Microsoft.Win32.OpenFileDialog { Filter = "JSON, TXT|*.json;*.txt|*.*|*.*" };
            if (open.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var added = personal.Import(System.IO.File.ReadAllText(open.FileName), code);
                Fill();
                MessageBox.Show(Texts.Get(addresses ? Resource.String.dictionary_addresses_imported : Resource.String.dictionary_imported, added), Texts.Get(Resource.String.dictionary));
            }
            catch (Exception failure) when (failure is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.IO.InvalidDataException)
            {
                DictionaryFailed(failure);
            }
        };

        var adding = new DockPanel();
        DockPanel.SetDock(add, Dock.Right);
        adding.Children.Add(add);
        adding.Children.Add(word);

        // Import (and the addresses' Export) first, then adding one by hand, then the list, the user's order.
        part.Children.Add(Field(null, import, addresses ? Resource.String.dictionary_import_addresses_summary : Resource.String.dictionary_import_summary));
        if (addresses)
        {
            part.Children.Add(Field(null, ExportAddresses(personal), Resource.String.dictionary_export_addresses_summary));
        }

        part.Children.Add(Field(addresses ? null : Resource.String.dictionary_words, adding, addresses ? null : Resource.String.dictionary_words_summary));
        part.Children.Add(Field(null, words, null));
        Fill();
        return part;
    }

    /// <summary>Export the addresses: every e-mail address to a file, to take them to another device.</summary>
    private static Button ExportAddresses(PersonalWords personal)
    {
        var export = new Button { Content = Texts.Get(Resource.String.dictionary_export_addresses), HorizontalAlignment = HorizontalAlignment.Left };
        export.Click += (_, _) =>
        {
            var save = new Microsoft.Win32.SaveFileDialog { FileName = $"berpiztu-addresses-{DateTime.Now:yyyy-MM-dd_HHmm}.json", Filter = "JSON|*.json" };
            if (save.ShowDialog() != true)
            {
                return;
            }

            try
            {
                System.IO.File.WriteAllText(save.FileName, personal.Export(PersonalWords.Shared));
                MessageBox.Show(Texts.Get(Resource.String.dictionary_addresses_exported, personal.Addresses.Count), Texts.Get(Resource.String.dictionary));
            }
            catch (Exception failure) when (failure is System.IO.IOException or UnauthorizedAccessException)
            {
                DictionaryFailed(failure);
            }
        };
        return export;
    }

    /// <summary>Glide typing, as Android's: swiping over the letters, its trail and ⌫ deleting a swiped word whole; FUTO Swipe's notice in sight, as its licence asks.</summary>
    private FrameworkElement GlidePage()
    {
        var preferences = KeyboardPreferences.Read();
        var form = Form(Resource.String.glide);
        form.Children.Add(Field(null, LanguageLines(choosing: false, GlidePage), Resource.String.glide_by_language));
        var trail = new CheckBox { Content = Texts.Get(Resource.String.glide_trail), IsChecked = preferences.GlideTrail };
        trail.Click += (_, _) => (KeyboardPreferences.Read() with { GlideTrail = trail.IsChecked == true }).Keep();
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
        var done = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 4, 0, 0) };
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

        // Windows run as administrator: why the keyboard is kept out of them, and the keyboard run so too (Elevation).
        var admin = new StackPanel();
        admin.Children.Add(new TextBlock { Text = Texts.Get(Resource.String.troubleshooting_admin_why), TextWrapping = TextWrapping.Wrap });
        var asAdministrator = new CheckBox { Content = Texts.Get(Resource.String.troubleshooting_admin_start), IsChecked = KeyboardPreferences.Read().AsAdministrator, Margin = new Thickness(0, FieldGapDp / 2, 0, 0) };
        asAdministrator.Click += (_, _) => (KeyboardPreferences.Read() with { AsAdministrator = asAdministrator.IsChecked == true }).Keep();
        admin.Children.Add(asAdministrator);
        form.Children.Add(Field(Resource.String.troubleshooting_admin, admin, Resource.String.troubleshooting_admin_start_summary));
        form.Children.Add(Field(Resource.String.troubleshooting_log, new TextBox { Text = Log.File, IsReadOnly = true }, Resource.String.troubleshooting_log_summary));
        var detailed = new CheckBox { Content = Texts.Get(Resource.String.troubleshooting_detailed_log), IsChecked = KeyboardPreferences.Read().DetailedLog };
        detailed.Click += (_, _) =>
        {
            Log.Detailed = detailed.IsChecked == true;
            (KeyboardPreferences.Read() with { DetailedLog = Log.Detailed }).Keep();
        };
        form.Children.Add(Field(null, detailed, Resource.String.troubleshooting_detailed_log_summary));

        // What the log keeps, word for word, in a box of one size: what would be sent is what is seen.
        var viewer = new TextBox
        {
            IsReadOnly = true,
            Height = LogViewerDp,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        void ShowLog()
        {
            viewer.Text = Log.Read();
            viewer.ScrollToEnd();
        }

        var refresh = new Button { Content = Texts.Get(Resource.String.troubleshooting_log_refresh) };
        refresh.Click += (_, _) => ShowLog();
        var clear = new Button { Content = Texts.Get(Resource.String.troubleshooting_log_clear), Margin = new Thickness(FieldGapDp / 2, 0, 0, 0) };
        clear.Click += (_, _) =>
        {
            Log.Clear();
            ShowLog();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, FieldGapDp / 4, 0, 0) };
        buttons.Children.Add(refresh);
        buttons.Children.Add(clear);
        var logPart = new StackPanel();
        logPart.Children.Add(viewer);
        logPart.Children.Add(buttons);
        form.Children.Add(Field(Resource.String.troubleshooting_log_view, logPart, Resource.String.troubleshooting_log_view_summary));
        ShowLog();
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

        // Every change made on what is kept now: the copy read as the page opened would undo what another page changed since.
        void Change(KeyboardPreferences changed)
        {
            var now = KeyboardPreferences.Read();
            preferences = now with
            {
                ShowKeyboard = changed.ShowKeyboard,
                FocusLost = changed.FocusLost,
                KeyboardIcon = changed.KeyboardIcon,
                AvoidCaret = changed.AvoidCaret,
                AvoidMenus = changed.AvoidMenus,
                EmojiRow = changed.EmojiRow,
            };
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
        var emojiRow = new CheckBox { Content = Texts.Get(Resource.String.emoji_row), IsChecked = preferences.EmojiRow };
        emojiRow.Click += (_, _) => Change(preferences with { EmojiRow = emojiRow.IsChecked == true });
        form.Children.Add(Field(null, emojiRow, Resource.String.emoji_row_summary));
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
        new() { Text = Texts.Get(name), FontSize = TitleSize, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, FieldGapDp) };

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
    private static FrameworkElement Field(int? name, FrameworkElement control, int? hint, double gapAbove = 0) =>
        FieldSaying(name, control, hint is { } said ? Texts.Get(said) : null, gapAbove);

    /// <summary>A field whose hint is a text made for it (one with a language's dictionary named in it).</summary>
    private static FrameworkElement FieldSaying(int? name, FrameworkElement control, string? hint, double gapAbove = 0)
    {
        var field = new StackPanel { Margin = new Thickness(0, gapAbove, 0, gapAbove > 0 ? 0 : FieldGapDp) };
        if (name is { } named)
        {
            field.Children.Add(new TextBlock { Text = Texts.Get(named), Margin = new Thickness(0, 0, 0, 4) });
        }

        field.Children.Add(control);
        if (hint is { } said)
        {
            field.Children.Add(new TextBlock { Text = said, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        }

        return field;
    }

    /// <summary>A part of a page under its own heading: Spelling and Dictionary on a language's page.</summary>
    private static TextBlock Section(int name) =>
        new() { Text = Texts.Get(name), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, FieldGapDp / 2, 0, FieldGapDp / 2) };
}
