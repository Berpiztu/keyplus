using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The keyboard as its ⌨ icon, as on Android, while it is hidden and the
/// preferences ask for it: a finger's size, over every window and never
/// taking the focus; in a light theme the app's own icon, in a dark one a
/// white round key with the keyboard in black, to stand out over any app;
/// raised by a shadow. A tap opens the keyboard; dragged, it moves, and
/// where it was left is kept.
/// </summary>
public sealed class KeyboardIconWindow : Window
{
    /// <summary>Android's: the icon a finger's size, the room around it its shadow is drawn in, in dp.</summary>
    public const double IconDp = 48;
    public const double ShadowDp = 12;

    /// <summary>How far the pointer goes before a press is a drag, not a tap.</summary>
    private const double DragReachDp = 4;

    private Point? pressedAt;
    private Point startedAt;
    private bool dragged;

    public KeyboardIconWindow(Palette colors)
    {
        Title = "Berpiztu AI Keyboard ⌨";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        Width = Height = IconDp + (2 * ShadowDp);
        Content = new IconFace(colors.Dark)
        {
            Margin = new Thickness(ShadowDp),
            Effect = new DropShadowEffect { BlurRadius = ShadowDp * 1.5, ShadowDepth = 2, Opacity = 0.3 },
            Cursor = Cursors.Hand,
        };
    }

    /// <summary>Tapped: the keyboard asked for.</summary>
    public event Action? Tapped;

    /// <summary>Let go after a drag, where it now is.</summary>
    public event Action? Moved;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NeverActivated.Apply(this, taskbar: false);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        pressedAt = PointToScreen(e.GetPosition(this));
        startedAt = new Point(Left, Top);
        dragged = false;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (pressedAt is not { } from || PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
        {
            return;
        }

        var moved = target.TransformFromDevice.Transform((Point)(PointToScreen(e.GetPosition(this)) - from));
        if (dragged || Math.Abs(moved.X) > DragReachDp || Math.Abs(moved.Y) > DragReachDp)
        {
            dragged = true;
            Left = startedAt.X + moved.X;
            Top = startedAt.Y + moved.Y;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (pressedAt is null)
        {
            return;
        }

        pressedAt = null;
        ReleaseMouseCapture();
        e.Handled = true;
        if (dragged)
        {
            Moved?.Invoke();
        }
        else
        {
            Tapped?.Invoke();
        }
    }

    /// <summary>The icon's face, as Android draws it.</summary>
    private sealed class IconFace(bool dark) : FrameworkElement
    {
        protected override void OnRender(DrawingContext context)
        {
            var centre = new Point(RenderSize.Width / 2, RenderSize.Height / 2);
            if (!dark)
            {
                Icons.DrawOwn(context, Resource.Drawable.ic_keyplus, centre, RenderSize.Width);
                return;
            }

            context.DrawEllipse(Brushes.White, null, centre, RenderSize.Width / 2, RenderSize.Height / 2);
            var sign = new FormattedText("⌨", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Symbol"),
                RenderSize.Height / 2, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            context.DrawText(sign, new Point(centre.X - (sign.Width / 2), centre.Y - (sign.Height / 2)));
        }
    }
}
