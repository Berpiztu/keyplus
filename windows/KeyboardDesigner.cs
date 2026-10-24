using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Diseñar on Windows, as Android's, the keyboard's and the settings'
/// preview's alike: over a <see cref="KeyboardSurface"/>, the designer's pills
/// (<see cref="Bar"/>, in a toolbar's place: −, +, + Tecla, + Fila, + Columna,
/// Quitar, Cancelar, Hecho) and + Tecla's list (<see cref="ExtraKeys"/>, in
/// the keys' place), which its host puts where they go. Started with a
/// keyboard of the list, from its design or the keyboard as made; Hecho saves
/// a built-in one as a new keyboard of the language designed in, chosen for it
/// at once, and a saved one in its place, docked as its docked design; Cancelar leaves it as it was; either way <see cref="Ended"/>.
/// </summary>
public sealed class KeyboardDesigner
{
    /// <summary>The designer's buttons, Android's toolbar pills: 89 px tall on the Fold, 2 dp apart.</summary>
    private const double PillDp = 89 / 2.625;
    private const double PillGapDp = 2;

    /// <summary>+ Tecla's list, as Android's: five keys across, 40 dp tall.</summary>
    private const int ExtrasAcross = 5;
    private const double ExtraKeyDp = 40;

    private readonly Palette colors;
    private readonly KeyboardSurface keys;
    private KeyboardEntry? keyboard;

    public KeyboardDesigner(Palette colors, KeyboardSurface keys)
    {
        this.colors = colors;
        this.keys = keys;
        FillBar();
    }

    /// <summary>The designer's pills, in sight while designing.</summary>
    public Grid Bar { get; } = new() { Height = KeyboardWindow.BarDp, Visibility = Visibility.Collapsed };

    /// <summary>+ Tecla's list, over the keys, as tall, while chosen.</summary>
    public ScrollViewer ExtraKeys { get; } = new() { Visibility = Visibility.Collapsed, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };

    /// <summary>Whether the keyboard is designed as it shows docked: its docked design made and kept, its floating one left as it is.</summary>
    public bool Docked { get; set; }

    /// <summary>The design ended: the keyboard as it now is (the one saved, or as it was), whether Hecho kept it, and the rows' share of Gboard's as they were, for a host to keep their height.</summary>
    public event Action<KeyboardEntry, bool, double>? Ended;

    /// <summary>A keyboard of the list designed, from its design or the keyboard as made, the pills in sight.</summary>
    public void Start(KeyboardEntry shown)
    {
        keyboard = shown;
        var made = StandardKeys.Page(keys.Pc ? StandardPage.Pc : StandardPage.Letters, keys.KeyLanguage);
        keys.Design = StandardDesign.FromText(shown.DesignFor(Docked)) ?? StandardDesign.From(made);
        keys.Designing = true;
        Bar.Visibility = Visibility.Visible;
        Log.Write($"Designing {shown.Name}");
    }

    /// <summary>
    /// Hecho: the design kept, a keyboard the keyboard comes with saved as a
    /// new one, which the keyboard then shows (renamed in Tema); a saved one
    /// keeps it in its place. Cancelar: as it was.
    /// </summary>
    private void End(bool keep)
    {
        if (keyboard is not { } shown)
        {
            return;
        }

        var scale = keys.RowScale;
        keys.Designing = false;
        ExtraKeys.Visibility = Visibility.Collapsed;
        keys.Visibility = Visibility.Visible;
        Bar.Visibility = Visibility.Collapsed;
        if (keep && keys.Design is { } designed)
        {
            // The keyboard shown is then the one saved, as it now is: designed again, it opens as saved, and Hecho keeps it in its place.
            // Docked, the design is the keyboard's docked one: a new keyboard made from a built-in one floats as that one does, its design kept.
            if (shown.BuiltIn)
            {
                shown = Docked
                    ? Keyboards.SaveAs(Keyboards.NewName(shown), shown.Base, shown.Design, keys.KeyLanguage.Code, designed.Text)
                    : Keyboards.SaveAs(Keyboards.NewName(shown), shown.Base, designed.Text, keys.KeyLanguage.Code);
                Keyboards.Select(keys.KeyLanguage.Code, shown.Id);
            }
            else
            {
                Keyboards.SetDesign(shown.Id, designed.Text, Docked);
                shown = Docked ? shown with { DockedDesign = designed.Text } : shown with { Design = designed.Text };
            }

            Log.Write($"Design kept as {shown.Name}");
        }
        else
        {
            keys.Design = StandardDesign.FromText(shown.DesignFor(Docked));
        }

        keyboard = null;
        Ended?.Invoke(shown, keep, scale);
    }

    /// <summary>The pills, Gboard's: −, +, + Tecla, + Fila, + Columna, Quitar, Cancelar, Hecho in blue; a sign's pill narrower than a word's.</summary>
    private void FillBar()
    {
        (string Label, Action Pressed, bool Done)[] buttons =
        [
            ("−", () => keys.ResizeMarked(wider: false), false),
            ("+", () => keys.ResizeMarked(wider: true), false),
            (Texts.Get(Resource.String.add_key), ToggleExtraKeys, false),
            (Texts.Get(Resource.String.add_row), keys.AddRow, false),
            (Texts.Get(Resource.String.add_column), keys.AddColumn, false),
            (Texts.Get(Resource.String.remove), keys.RemoveMarked, false),
            (Texts.Get(Resource.String.cancel), () => End(keep: false), false),
            (Texts.Get(Resource.String.done), () => End(keep: true), true),
        ];
        foreach (var (label, pressed, done) in buttons)
        {
            var pill = Pill(colors, label, done);
            pill.Clicked += pressed;
            Bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(label.Length <= 1 ? 0.7 : 1, GridUnitType.Star) });
            Grid.SetColumn(pill, Bar.Children.Count);
            Bar.Children.Add(pill);
        }
    }

    /// <summary>A pill of the designer's bar, as its host's Diseñar is drawn too, where the bar then goes.</summary>
    public static PillButton Pill(Palette colors, string label, bool done) =>
        new(colors, label, done, 16) { Height = PillDp, Margin = new Thickness(PillGapDp, 0, PillGapDp, 0), CornerRadius = new CornerRadius(PillDp / 2) };

    /// <summary>+ Tecla: the keys a design can add and the keyboard lacks, in the keys' place, by group, as Android's; one clicked comes loose over the keys, to be dragged to its place; again, the keys.</summary>
    private void ToggleExtraKeys()
    {
        if (ExtraKeys.Visibility == Visibility.Visible)
        {
            ExtraKeys.Visibility = Visibility.Collapsed;
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

        ExtraKeys.Content = list;
        ExtraKeys.Height = keys.ActualHeight;
        ExtraKeys.Background = new SolidColorBrush(colors.Ground);
        keys.Visibility = Visibility.Hidden;
        ExtraKeys.Visibility = Visibility.Visible;
    }
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
