using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace Berpiztu.KeyPlus;

/// <summary>
/// What Mover and Redimensionar, in Berpiztu AI Keyboard's ⚙, lay over the
/// keyboard, as Gboard's Resize does: the keys dimmed under it and out of
/// reach until Hecho. Moving, the keyboard follows a finger held anywhere on
/// it, ✥ in the middle to say so. Resizing, a bar at each edge that can be
/// dragged, the top and bottom ones for the height (the keys' size), the side
/// ones for the width, which only a keyboard in a window has; Restablecer
/// takes it back to its usual size.
/// </summary>
public sealed class KeyboardFrame : FrameLayout
{
    /// <summary>What a finger holds: the whole keyboard, to move it, or one of its edges, to size it.</summary>
    public enum Part
    {
        Whole,
        Top,
        Bottom,
        Left,
        Right,
    }

    /// <summary>How thick an edge's bar is to take hold of, in dp.</summary>
    private const int EdgeDp = 36;

    private static readonly Color Dim = Color.Argb(0xC0, 0x12, 0x12, 0x12);

    private readonly Context context;
    private readonly LinearLayout buttons;
    private readonly TextView reset;
    private readonly View whole;
    private readonly View[] edges;

    public KeyboardFrame(Context context)
        : base(context)
    {
        this.context = context;
        Visibility = ViewStates.Gone;

        // The keys under it out of reach: a touch on it that nothing in it takes stops here.
        Clickable = true;
        var outline = new GradientDrawable();
        outline.SetColor(Dim);
        outline.SetStroke(Dp(2), KeyPanel.HeldColor);
        outline.SetCornerRadius(Dp(12));
        Background = outline;

        whole = new View(context);
        Hold(whole, Part.Whole);
        AddView(whole, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        edges =
        [
            Edge(Part.Top, GravityFlags.Top, horizontal: true),
            Edge(Part.Bottom, GravityFlags.Bottom, horizontal: true),
            Edge(Part.Left, GravityFlags.Left, horizontal: false),
            Edge(Part.Right, GravityFlags.Right, horizontal: false),
        ];

        // ✥ over Restablecer and Hecho: they fit a keyboard in a narrow window.
        buttons = new LinearLayout(context) { Orientation = Orientation.Vertical };
        buttons.SetGravity(GravityFlags.Center);
        var move = Button("✥", null);
        move.SetTextSize(ComplexUnitType.Sp, 28);
        Hold(move, Part.Whole);
        buttons.AddView(move, Spaced());
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        reset = Button("↺ Restablecer", () => ResetRequested?.Invoke());
        row.AddView(reset, Spaced());
        row.AddView(Button("✓ Hecho", () => DoneRequested?.Invoke()), Spaced());
        buttons.AddView(row, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
        AddView(buttons, new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Center));
    }

    /// <summary>A finger dragging the keyboard or one of its edges: how far it has gone since it went down, in pixels.</summary>
    public event Action<Part, KeyPanel.Drag, float, float>? Dragged;

    public event Action? ResetRequested;

    public event Action? DoneRequested;

    /// <summary>
    /// The frame over the keyboard: to move it (<paramref name="resizing"/>
    /// false), or to size it, with the side bars where the keyboard is in a
    /// window (<paramref name="sides"/>).
    /// </summary>
    public void Show(bool resizing, bool sides)
    {
        whole.Visibility = resizing ? ViewStates.Gone : ViewStates.Visible;
        reset.Visibility = resizing ? ViewStates.Visible : ViewStates.Gone;
        edges[0].Visibility = edges[1].Visibility = resizing ? ViewStates.Visible : ViewStates.Gone;
        edges[2].Visibility = edges[3].Visibility = resizing && sides ? ViewStates.Visible : ViewStates.Gone;
        Visibility = ViewStates.Visible;
        BringToFront();
    }

    public void Hide() => Visibility = ViewStates.Gone;

    public bool Showing => Visibility == ViewStates.Visible;

    /// <summary>An edge's bar: the whole edge to take hold of, a short line in its middle to show it is there.</summary>
    private View Edge(Part part, GravityFlags gravity, bool horizontal)
    {
        var edge = new FrameLayout(context);
        var line = new View(context) { Background = Rounded(KeyPanel.HeldColor) };
        edge.AddView(line, horizontal
            ? new LayoutParams(Dp(56), Dp(6), GravityFlags.Center)
            : new LayoutParams(Dp(6), Dp(56), GravityFlags.Center));
        Hold(edge, part);
        AddView(edge, horizontal
            ? new LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(EdgeDp), gravity)
            : new LayoutParams(Dp(EdgeDp), ViewGroup.LayoutParams.MatchParent, gravity));
        return edge;
    }

    /// <summary>A view a finger drags the keyboard or an edge by, from where it went down: the screen's own place, for the keyboard moves under the finger.</summary>
    private void Hold(View view, Part part)
    {
        float startX = 0, startY = 0;
        view.Touch += (_, e) =>
        {
            e.Handled = true;
            var motion = e.Event!;
            var (dx, dy) = (motion.RawX - startX, motion.RawY - startY);
            switch (motion.ActionMasked)
            {
                case MotionEventActions.Down:
                    (startX, startY) = (motion.RawX, motion.RawY);
                    view.Pressed = true;
                    Dragged?.Invoke(part, KeyPanel.Drag.Start, 0, 0);
                    break;
                case MotionEventActions.Move:
                    Dragged?.Invoke(part, KeyPanel.Drag.Move, dx, dy);
                    break;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    view.Pressed = false;
                    Dragged?.Invoke(part, KeyPanel.Drag.End, dx, dy);
                    break;
            }
        };
    }

    private TextView Button(string label, Action? pressed)
    {
        var button = new TextView(context) { Text = label, Gravity = GravityFlags.Center, Background = Rounded(KeyPanel.KeyColor) };
        button.SetTextColor(Color.White);
        button.SetTextSize(ComplexUnitType.Sp, 16);
        button.SetMaxLines(1);
        button.SetPadding(Dp(16), Dp(8), Dp(16), Dp(8));
        if (pressed is not null)
        {
            button.Click += (_, _) => pressed();
        }

        return button;
    }

    private LinearLayout.LayoutParams Spaced()
    {
        var layout = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(48)) { Gravity = GravityFlags.CenterHorizontal };
        layout.SetMargins(Dp(6), Dp(4), Dp(6), Dp(4));
        return layout;
    }

    private GradientDrawable Rounded(Color color)
    {
        var shape = new GradientDrawable();
        shape.SetColor(color);
        shape.SetCornerRadius(Dp(24));
        return shape;
    }

    private int Dp(float value) => (int)((value * context.Resources!.DisplayMetrics!.Density) + 0.5f);
}
