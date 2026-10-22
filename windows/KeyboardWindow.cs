using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
/// for Estándar PC), a tap the other, and ✕; under them Gboard's handle, which
/// moves it, and at its right a grip that makes it wider or narrower. Where it was left,
/// how wide, which keyboard and language, kept for the next time.
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
    private const double GripDp = 28;
    private const double MinWidthDp = 360;

    private static readonly string Kept = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Berpiztu", "Keyboard", "window.json");

    private readonly Palette colors = Palette.Read();
    private readonly KeyboardSurface keys;
    private readonly SignButton switcher;
    private readonly TextBlock notice;
    private readonly DispatcherTimer noticeTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private Point? dragFrom;
    private (double Left, double Top, double Width) dragStart;
    private bool resizing;

    /// <summary>What the window keeps between runs.</summary>
    private sealed record State(double Left, double Top, double Width, bool Pc, string Language);

    public KeyboardWindow()
    {
        Title = "Berpiztu AI Keyboard";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = true;
        SizeToContent = SizeToContent.Height;

        keys = new KeyboardSurface(colors);
        keys.NextLanguage += NextLanguage;
        keys.NotMade += what => Notice($"{what}: todavía no está hecho");

        switcher = new SignButton(colors) { Width = BarDp, Height = BarDp, Size = SignDp };
        switcher.Clicked += () =>
        {
            keys.Pc = !keys.Pc;
            ShowSwitcher();
            Keep();
        };
        var close = new SignButton(colors) { Width = BarDp, Height = BarDp, Size = SignDp, Sign = Resource.Drawable.ic_tool_close };
        close.Clicked += Close;
        notice = new TextBlock { Foreground = new SolidColorBrush(colors.Glyph), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 13 };
        noticeTimer.Tick += (_, _) =>
        {
            noticeTimer.Stop();
            notice.Text = "";
        };

        var bar = new DockPanel { Height = BarDp, Background = Brushes.Transparent };
        DockPanel.SetDock(switcher, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        bar.Children.Add(switcher);
        bar.Children.Add(close);
        bar.Children.Add(notice);
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

        // The grip at the handle's right, clear of the keys: the window wider or narrower.
        var grip = new Border { Width = GripDp, Background = Brushes.Transparent, Cursor = Cursors.SizeWE, Child = new TextBlock { Text = "⇔", Foreground = new SolidColorBrush(colors.Handle), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        Resizes(grip);
        var bottom = new DockPanel { Height = HandleBandDp };
        DockPanel.SetDock(grip, Dock.Right);
        bottom.Children.Add(grip);
        bottom.Children.Add(handle);

        var card = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        card.Children.Add(bar);
        card.Children.Add(bottom);
        card.Children.Add(keys);

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
        Closing += (_, _) => Keep();
        LocationChanged += (_, _) => KeepSoon();
    }

    /// <summary>A click on the window never takes the focus from the window typed in: no activation, and a taskbar button all the same.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, ExtendedStyle).ToInt64();
        SetWindowLongPtr(handle, ExtendedStyle, new IntPtr(style | NoActivate | AppWindow));
    }

    private void ShowSwitcher()
    {
        switcher.Sign = keys.Pc ? Resource.Drawable.ic_grid_pc : Resource.Drawable.ic_grid_phone;
        switcher.ToolTip = keys.Pc ? "PC" : "Estándar";
    }

    private void NextLanguage()
    {
        var languages = StandardKeys.Languages;
        keys.KeyLanguage = languages[(languages.ToList().IndexOf(keys.KeyLanguage) + 1) % languages.Count];
        Keep();
    }

    private void Notice(string text)
    {
        notice.Text = text;
        noticeTimer.Stop();
        noticeTimer.Start();
    }

    /// <summary>A part that moves the window as it is dragged.</summary>
    private void Moves(FrameworkElement part)
    {
        part.MouseLeftButtonDown += (_, e) => StartDrag(part, e.GetPosition(this), resize: false);
        part.MouseMove += (_, e) => Drag(e.GetPosition(this));
        part.MouseLeftButtonUp += (_, _) => EndDrag(part);
    }

    /// <summary>The grip: dragged, the window wider or narrower, its left where it is.</summary>
    private void Resizes(FrameworkElement part)
    {
        part.MouseLeftButtonDown += (_, e) => StartDrag(part, e.GetPosition(this), resize: true);
        part.MouseMove += (_, e) => Drag(e.GetPosition(this));
        part.MouseLeftButtonUp += (_, _) => EndDrag(part);
    }

    private void StartDrag(FrameworkElement part, Point at, bool resize)
    {
        dragFrom = PointToScreenDips(at);
        dragStart = (Left, Top, Width);
        resizing = resize;
        part.CaptureMouse();
    }

    private void Drag(Point at)
    {
        if (dragFrom is not { } from)
        {
            return;
        }

        var now = PointToScreenDips(at);
        if (resizing)
        {
            Width = Math.Max(MinWidthDp, dragStart.Width + (now.X - from.X));
        }
        else
        {
            Left = dragStart.Left + (now.X - from.X);
            Top = dragStart.Top + (now.Y - from.Y);
        }
    }

    private void EndDrag(FrameworkElement part)
    {
        dragFrom = null;
        part.ReleaseMouseCapture();
        Keep();
    }

    /// <summary>A point of the window on the screen, in the units Left and Top are in.</summary>
    private Point PointToScreenDips(Point at)
    {
        var screen = PointToScreen(at);
        return PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.Transform(screen) ?? screen;
    }

    /// <summary>Where it was left, as wide, with the keyboard and language it had; the first time, three fifths of the screen wide at its bottom's middle.</summary>
    private void Restore()
    {
        var area = SystemParameters.WorkArea;
        State? state = null;
        try
        {
            state = File.Exists(Kept) ? JsonSerializer.Deserialize<State>(File.ReadAllText(Kept)) : null;
        }
        catch (JsonException unreadable)
        {
            System.Diagnostics.Debug.WriteLine($"The window's state could not be read: {unreadable.Message}");
        }

        Width = Math.Max(MinWidthDp, state?.Width ?? area.Width * 0.6);
        keys.Pc = state?.Pc ?? true;
        keys.KeyLanguage = StandardKeys.Languages.FirstOrDefault(language => language.Code == state?.Language) ?? StandardKeys.Spanish;
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
        File.WriteAllText(Kept, JsonSerializer.Serialize(new State(Left, Top, Width, keys.Pc, keys.KeyLanguage.Code)));
    }

    private const int ExtendedStyle = -20;
    private const long NoActivate = 0x08000000;
    private const long AppWindow = 0x00040000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}

/// <summary>A toolbar's sign, the Android keyboard's drawable, in the keys' letters' colour; a click, what it does.</summary>
public sealed class SignButton(Palette colors) : FrameworkElement
{
    private int sign;
    private bool down;

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
        if (down)
        {
            context.DrawEllipse(new SolidColorBrush(colors.Function), null, new Point(RenderSize.Width / 2, RenderSize.Height / 2), RenderSize.Height * 0.42, RenderSize.Height * 0.42);
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
