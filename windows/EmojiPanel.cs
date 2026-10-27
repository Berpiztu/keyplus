using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The emoji on Windows, in the keys' place, the toolbar over it: a bar with ← in a grey circle, a tab for each group,
/// the one in sight marked, and ⌫ at its right; the emoji under each group's grey name, those typed lately first,
/// scrolling. No bar under them, as Gboard's ABC, ☺ and ⌫: ← is ABC and the toolbar has ☺, and that bar's height kept
/// a keyboard of a window's height from showing a row of emoji whole. Sizes are the Android panel's, one dp a WPF unit;
/// colours <see cref="Palette"/>'s.
/// </summary>
public sealed class EmojiPanel : DockPanel
{
    private const double PixelsPerDp = 2.25;
    private const double BarDp = 48;
    private const double BackDp = 70 / PixelsPerDp;
    private const double TabDp = 72 / PixelsPerDp;
    private const double TabIconDp = 35.0 / (800.0 / 960) / PixelsPerDp;

    private readonly Palette colors;
    private readonly EmojiSheet sheet;
    private readonly StackPanel tabs = new() { Orientation = Orientation.Horizontal };
    private readonly DispatcherTimer repeat = new();

    public EmojiPanel(Palette colors)
    {
        this.colors = colors;
        Background = new SolidColorBrush(colors.Ground);
        sheet = new EmojiSheet(colors);
        sheet.Chosen += emoji =>
        {
            EmojiArt.Used(emoji);
            Typed?.Invoke(emoji);
        };
        sheet.SectionShown += MarkTab;

        var top = new DockPanel { Height = BarDp };
        var back = Round(Resource.Drawable.ic_tool_back, colors.Function, BackDp, () => Closed?.Invoke());
        back.Margin = new Thickness(8, 0, 6, 0);
        DockPanel.SetDock(back, Dock.Left);
        top.Children.Add(back);
        var delete = Delete();
        delete.Margin = new Thickness(6, 0, 8, 0);
        DockPanel.SetDock(delete, Dock.Right);
        top.Children.Add(delete);
        top.Children.Add(new ScrollViewer { Content = tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalContentAlignment = VerticalAlignment.Center, Focusable = false });
        SetDock(top, Dock.Top);
        Children.Add(top);
        Children.Add(sheet);

        repeat.Tick += (_, _) =>
        {
            repeat.Interval = TimeSpan.FromMilliseconds(50);
            Deleted?.Invoke();
        };
    }

    public event Action<string>? Typed;

    public event Action? Deleted;

    /// <summary>←: the keys again.</summary>
    public event Action? Closed;

    /// <summary>The emoji as they are now, those typed lately first, from the top.</summary>
    public void Fill()
    {
        var recent = EmojiArt.Recent();
        var groups = EmojiArt.Groups;
        sheet.Sections = [(Texts.Get(Resource.String.emoji_recent), recent), .. groups.Select(group => (group.Name, group.Items))];
        tabs.Children.Clear();
        int[] icons = [Resource.Drawable.ic_emoji_recent, .. groups.Select(group => group.Icon)];
        for (var i = 0; i < icons.Length; i++)
        {
            var section = i;
            tabs.Children.Add(Round(icons[i], Colors.Transparent, TabDp, () => sheet.ShowSection(section)));
        }

        MarkTab(recent.Count > 0 ? 0 : 1);
    }

    /// <summary>The tab of the group in sight on the modifiers' tone, its sign darker; the others plain.</summary>
    private void MarkTab(int section)
    {
        for (var i = 0; i < tabs.Children.Count; i++)
        {
            var tab = (SignCircle)tabs.Children[i];
            tab.Ground = i == section ? colors.On : Colors.Transparent;
            tab.Glyph = i == section ? colors.Glyph : colors.Outline;
        }
    }

    private SignCircle Round(int sign, Color ground, double size, Action pressed)
    {
        var round = new SignCircle { Sign = sign, Ground = ground, Glyph = colors.Glyph, SignSize = TabIconDp, Width = size, Height = size };
        round.MouseLeftButtonUp += (_, e) =>
        {
            pressed();
            e.Handled = true;
        };
        round.MouseLeftButtonDown += (_, e) => e.Handled = true;
        return round;
    }

    /// <summary>⌫ as ←, a circle of its size: held, it deletes again and again.</summary>
    private SignCircle Delete()
    {
        var delete = new SignCircle { Sign = Resource.Drawable.ic_key_backspace, Ground = Colors.Transparent, Glyph = colors.Glyph, SignSize = TabIconDp, Width = BackDp, Height = BackDp };
        delete.MouseLeftButtonDown += (_, e) =>
        {
            Deleted?.Invoke();
            repeat.Interval = TimeSpan.FromMilliseconds(400);
            repeat.Start();
            delete.CaptureMouse();
            e.Handled = true;
        };
        delete.MouseLeftButtonUp += (_, e) =>
        {
            repeat.Stop();
            delete.ReleaseMouseCapture();
            e.Handled = true;
        };

        // Let go of without its release (the panel hidden, the window gone): ⌫ deletes no more.
        delete.LostMouseCapture += (_, _) => repeat.Stop();
        return delete;
    }
}

/// <summary>A sign on a circle (or a pill), the Android keyboard's drawable in a colour.</summary>
public sealed class SignCircle : FrameworkElement
{
    private Color ground;
    private Color glyph;

    public int Sign { get; init; }

    public double SignSize { get; init; } = 20;

    public bool Pill { get; init; }

    public Color Ground
    {
        get => ground;
        set
        {
            ground = value;
            InvalidateVisual();
        }
    }

    public Color Glyph
    {
        get => glyph;
        set
        {
            glyph = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext context)
    {
        var box = new Rect(RenderSize);
        context.DrawRectangle(Brushes.Transparent, null, box);
        var radius = Math.Min(box.Width, box.Height) / 2;
        context.DrawRoundedRectangle(new SolidColorBrush(ground), null, box, Pill ? radius : box.Width / 2, radius);
        Icons.Draw(context, Sign, new Point(box.Width / 2, box.Height / 2), SignSize, glyph);
    }
}

/// <summary>
/// The emoji's list, drawn on one element, only the rows in sight: each
/// section's grey name, then its emoji across, 4 apart across and down; the wheel
/// or a drag scrolls it, a click types one.
/// </summary>
public sealed class EmojiSheet : FrameworkElement
{
    private const double PixelsPerDp = 2.25;
    private const double HeaderSize = 20 / 0.711 / PixelsPerDp;
    private const double SideDp = 12 / PixelsPerDp;

    /// <summary>An emoji's size, the owner's (2026-10-25): Gboard's 37 dp left two rows in a keyboard of a window's height, this three.</summary>
    private const double EmojiDp = 30;

    /// <summary>The room between two emoji, across and down, the owner's (2026-10-25): Gboard's left 9 between them.</summary>
    private const double EmojiGapDp = 4;

    /// <summary>A row's height, and a column's least width.</summary>
    private const double RowDp = EmojiDp + EmojiGapDp;
    private const double CellDp = EmojiDp + EmojiGapDp;

    /// <summary>
    /// A section's name nearer its own emoji than the row above it, so each name is read with its emoji: 6 from a row's
    /// emoji to the next name's capitals and 3 from the name's lowest letters to its emoji, as drawn (the owner's,
    /// 2026-10-25; Gboard's, measured on the Fold, is the other way round).
    /// </summary>
    private const double AboveNameDp = 6;
    private const double UnderNameDp = 3;

    /// <summary>The room a row leaves above and under its emoji.</summary>
    private const double RowGapDp = (RowDp - EmojiDp) / 2;

    private const double HeaderCapTopDp = AboveNameDp - RowGapDp;
    private const double HeaderGapUnderDp = UnderNameDp - RowGapDp;

    private static readonly Typeface HeaderFont = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    /// <summary>A name's ink: its capitals' height over the baseline and its lowest letters' depth under it, as drawn, not the font's line.</summary>
    private static readonly (double Caps, double Descent) NameInk = MeasureNameInk();

    /// <summary>A section's name's box: its room above, its ink and its room under.</summary>
    private static readonly double HeaderDp = HeaderCapTopDp + NameInk.Caps + NameInk.Descent + HeaderGapUnderDp;

    private static (double Caps, double Descent) MeasureNameInk()
    {
        FormattedText Text(string text) => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, HeaderFont, HeaderSize, Brushes.Black, 1.0);
        var caps = Text("H");
        var low = Text("gjpqy");
        return (caps.Baseline - caps.BuildGeometry(new Point()).Bounds.Top, low.BuildGeometry(new Point()).Bounds.Bottom - low.Baseline);
    }

    private readonly Palette colors;
    private IReadOnlyList<(string Title, IReadOnlyList<string> Items)> sections = [];
    private List<double> tops = [];
    private double length;
    private double offset;
    private int columns = 1;
    private double cell;
    private Point? down;
    private double downOffset;
    private bool dragged;

    public EmojiSheet(Palette colors)
    {
        this.colors = colors;
        ClipToBounds = true;
    }

    public event Action<string>? Chosen;

    /// <summary>The section at the top of what is in sight, for its tab.</summary>
    public event Action<int>? SectionShown;

    public IReadOnlyList<(string Title, IReadOnlyList<string> Items)> Sections
    {
        set
        {
            sections = value;
            offset = 0;
            Lay();
        }
    }

    /// <summary>A section brought to the top, its tab tapped.</summary>
    public void ShowSection(int section)
    {
        if (section < tops.Count)
        {
            Scroll(tops[section] - offset);
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        Lay();
    }

    private void Lay()
    {
        var width = ActualWidth - (2 * SideDp);
        columns = Math.Max(1, (int)(width / CellDp));
        cell = width / columns;
        tops = [];
        length = 0;
        foreach (var (_, items) in sections)
        {
            tops.Add(length);
            if (items.Count > 0)
            {
                length += HeaderDp + (((items.Count + columns - 1) / columns) * RowDp);
            }
        }

        Scroll(0);
    }

    private void Scroll(double by)
    {
        offset = Math.Clamp(offset + by, 0, Math.Max(0, length - ActualHeight));
        var shown = 0;
        for (var i = 0; i < tops.Count; i++)
        {
            if (sections[i].Items.Count > 0 && tops[i] <= offset + 1)
            {
                shown = i;
            }
        }

        SectionShown?.Invoke(shown);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        context.DrawRectangle(new SolidColorBrush(colors.Ground), null, new Rect(RenderSize));
        var dpi = VisualTreeHelper.GetDpi(this);
        var pixels = (int)Math.Round(EmojiDp * dpi.DpiScaleX);
        const double size = EmojiDp;
        if (cell <= 0)
        {
            // Not laid out yet: no room to draw in.
            return;
        }

        for (var s = 0; s < sections.Count; s++)
        {
            var (title, items) = sections[s];
            if (items.Count == 0)
            {
                continue;
            }

            var top = tops[s] - offset;
            if (top + HeaderDp >= 0 && top <= ActualHeight)
            {
                var header = new FormattedText(title, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, HeaderFont, HeaderSize, new SolidColorBrush(colors.Outline), dpi.PixelsPerDip);

                // The capitals' top at its room above: the baseline under it by the capitals' ink.
                var baseline = top + HeaderCapTopDp + NameInk.Caps;
                context.DrawText(header, new Point(SideDp + 8, baseline - header.Baseline));
            }

            var rowsTop = top + HeaderDp;
            for (var i = 0; i < items.Count; i++)
            {
                var y = rowsTop + ((i / columns) * RowDp);
                if (y + RowDp < 0 || y > ActualHeight)
                {
                    continue;
                }

                var x = SideDp + ((i % columns) * cell);
                context.DrawImage(EmojiArt.Draw(items[i], pixels), new Rect(x + ((cell - size) / 2), y + ((RowDp - size) / 2), size, size));
            }
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Scroll(-e.Delta / 120.0 * RowDp);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        down = e.GetPosition(this);
        downOffset = offset;
        dragged = false;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (down is not { } from || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var dy = e.GetPosition(this).Y - from.Y;
        if (dragged || Math.Abs(dy) > SystemParameters.MinimumVerticalDragDistance * 2)
        {
            dragged = true;
            offset = downOffset;
            Scroll(-dy);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        if (down is not null && !dragged && EmojiAt(e.GetPosition(this)) is { } chosen)
        {
            Chosen?.Invoke(chosen);
        }

        down = null;
        e.Handled = true;
    }

    private string? EmojiAt(Point point)
    {
        var y = point.Y + offset;
        for (var s = sections.Count - 1; s >= 0; s--)
        {
            if (sections[s].Items.Count == 0 || y < tops[s])
            {
                continue;
            }

            var rowsTop = tops[s] + HeaderDp;
            if (y < rowsTop)
            {
                return null;
            }

            var column = (int)((point.X - SideDp) / cell);
            var at = ((int)((y - rowsTop) / RowDp) * columns) + column;
            return column >= 0 && column < columns && at < sections[s].Items.Count ? sections[s].Items[at] : null;
        }

        return null;
    }
}

/// <summary>
/// The emoji typed lately in a row, over the toolbar (Preferences' Show the emoji row), as the Android keyboard's: each in
/// its cell from the left, as many as fit, Gboard's sizes on the Fold (cells 83 px, the emoji 48 of them); a click types one.
/// </summary>
public sealed class EmojiStrip : FrameworkElement
{
    private const double PixelsPerDp = 2.25;
    private const double CellDp = 83 / PixelsPerDp;
    private const double EmojiDp = CellDp * 48 / 83;
    private const double SideDp = 12 / PixelsPerDp;

    private IReadOnlyList<string> items = [];

    public EmojiStrip() => ClipToBounds = true;

    public event Action<string>? Chosen;

    public IReadOnlyList<string> Items
    {
        set
        {
            items = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext context)
    {
        context.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var pixels = (int)Math.Round(EmojiDp * VisualTreeHelper.GetDpi(this).DpiScaleX);
        var top = (RenderSize.Height - EmojiDp) / 2;
        for (var i = 0; i < items.Count && SideDp + ((i + 1) * CellDp) <= RenderSize.Width; i++)
        {
            context.DrawImage(EmojiArt.Draw(items[i], pixels), new Rect(SideDp + (i * CellDp) + ((CellDp - EmojiDp) / 2), top, EmojiDp, EmojiDp));
        }
    }

    /// <summary>An emoji pressed is the strip's; the room beside them is left to the row, which moves the window as the toolbar does.</summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) => e.Handled = At(e) >= 0;

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (At(e) is var at and >= 0)
        {
            Chosen?.Invoke(items[at]);
            e.Handled = true;
        }
    }

    /// <summary>The emoji under the mouse, by its place in the list; -1, none.</summary>
    private int At(MouseButtonEventArgs e)
    {
        var at = (int)Math.Floor((e.GetPosition(this).X - SideDp) / CellDp);
        return at >= 0 && at < items.Count && SideDp + ((at + 1) * CellDp) <= RenderSize.Width ? at : -1;
    }
}
