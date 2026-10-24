using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The emoji on Windows, in the keys' place, as the Android keyboard's
/// (EmojiPanel.cs): over them ← in a grey circle and a tab for each group,
/// the one in sight marked; the emoji under each group's grey name, those
/// typed lately first, scrolling; under them ABC, the emoji (in use) and ⌫.
/// Sizes are the Android panel's, one dp a WPF unit; colours <see cref="Palette"/>'s.
/// </summary>
public sealed class EmojiPanel : DockPanel
{
    private const double PixelsPerDp = 2.25;
    private const double BarDp = 48;
    private const double BackDp = 70 / PixelsPerDp;
    private const double TabDp = 72 / PixelsPerDp;
    private const double TabIconDp = 35.0 / (800.0 / 960) / PixelsPerDp;
    private const double BottomDp = 114 / PixelsPerDp;
    private const double PillWidthDp = 122 / PixelsPerDp;
    private const double PillHeightDp = 82 / PixelsPerDp;
    private const double PillIconDp = 43.0 / (800.0 / 960) / PixelsPerDp;
    private const double AbcSize = 24 / 0.711 / PixelsPerDp;

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
        top.Children.Add(new ScrollViewer { Content = tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalContentAlignment = VerticalAlignment.Center, Focusable = false });
        SetDock(top, Dock.Top);
        Children.Add(top);

        var bottom = BottomBar();
        SetDock(bottom, Dock.Bottom);
        Children.Add(bottom);
        Children.Add(sheet);

        repeat.Tick += (_, _) =>
        {
            repeat.Interval = TimeSpan.FromMilliseconds(50);
            Deleted?.Invoke();
        };
    }

    public event Action<string>? Typed;

    public event Action? Deleted;

    /// <summary>← or ABC: the keys again.</summary>
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

    /// <summary>ABC, the emoji in use on the modifiers' tone, ⌫; spread across.</summary>
    private UniformGrid BottomBar()
    {
        var bar = new UniformGrid { Rows = 1, Height = BottomDp };
        var abc = new TextBlock { Text = "ABC", FontSize = AbcSize, Foreground = new SolidColorBrush(colors.Glyph), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
        abc.MouseLeftButtonUp += (_, e) =>
        {
            Closed?.Invoke();
            e.Handled = true;
        };
        abc.MouseLeftButtonDown += (_, e) => e.Handled = true;
        bar.Children.Add(abc);
        bar.Children.Add(new SignCircle { Sign = Resource.Drawable.ic_key_emoji, Ground = colors.On, Glyph = colors.Glyph, SignSize = PillIconDp, Width = PillWidthDp, Height = PillHeightDp, Pill = true });
        var delete = new SignCircle { Sign = Resource.Drawable.ic_key_backspace, Ground = Colors.Transparent, Glyph = colors.Glyph, SignSize = PillIconDp, Width = PillWidthDp, Height = PillHeightDp };
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
        bar.Children.Add(delete);
        return bar;
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
/// section's grey name, then its emoji across in cells of at least 100 px of
/// the Fold, rows 113 px apart, each emoji 85 px wide; the wheel or a drag
/// scrolls it, a click types one.
/// </summary>
public sealed class EmojiSheet : FrameworkElement
{
    private const double PixelsPerDp = 2.25;
    private const double CellDp = 100 / PixelsPerDp;
    private const double RowDp = 113 / PixelsPerDp;
    private const double HeaderDp = 87 / PixelsPerDp;
    private const double HeaderSize = 20 / 0.711 / PixelsPerDp;
    private const double SideDp = 12 / PixelsPerDp;
    private const double EmojiShare = 85.0 / 100;

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
        var pixels = (int)Math.Round(cell * EmojiShare * dpi.DpiScaleX);
        var size = cell * EmojiShare;
        if (pixels < 1)
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
                var header = new FormattedText(title, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), HeaderSize, new SolidColorBrush(colors.Outline), dpi.PixelsPerDip);
                context.DrawText(header, new Point(SideDp + 8, top + ((HeaderDp - header.Height) / 2)));
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
