using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace Berpiztu.KeyPlus;

/// <summary>
/// What Mover and Redimensionar, in Berpiztu AI Keyboard's ⚙, lay over the
/// keyboard, as Gboard's Resize does, and KeyPlus's title bar's resize over
/// its keys: the keys dimmed under it and out of
/// reach until Hecho. Moving, the keyboard follows a finger held anywhere on
/// it, ✥ in the middle to say so. Resizing, a bar at each edge that can be
/// dragged, the top and bottom ones for the height (the keys' size), the side
/// ones for the width, which only a keyboard in a window has; Restablecer
/// takes it back to its usual size, Cancelar to what it was when the frame
/// opened.
/// </summary>
public sealed class KeyboardFrame : FrameLayout
{
    /// <summary>What a finger holds: the whole keyboard, to move it, or its edges, one or a corner's two, to size it.</summary>
    [Flags]
    public enum Part
    {
        Whole = 1,
        Top = 2,
        Bottom = 4,
        Left = 8,
        Right = 16,
    }

    /// <summary>
    /// The frame's sizes in dp and sp, an edge's bar to take hold of, its line, the buttons and the room between them:
    /// the keyboard's, a finger's over a whole keyboard; the popup's small, for its keys and its two rows are shorter
    /// than the keyboard's frame's buttons, and the frame is no smaller than they.
    /// </summary>
    private readonly record struct Sizes(int Edge, int EdgeLine, int EdgeThickness, int ButtonHeight, int ButtonSp, int MoveSp, int PadAcross, int PadDown, int GapAcross, int GapDown);

    private static readonly Sizes Large = new(36, 56, 6, 48, 16, 28, 16, 8, 6, 4);
    private static readonly Sizes Small = new(20, 40, 4, 30, 13, 18, 10, 2, 3, 2);

    private readonly Sizes sizes;

    private static readonly Color Dim = Color.Argb(0xC0, 0x12, 0x12, 0x12);

    private readonly Context context;
    private readonly LinearLayout buttons;
    private readonly TextView reset;
    private readonly TextView move;
    private readonly TextView dock;
    private readonly LinearLayout top;
    private readonly View whole;
    private readonly View[] edges;

    /// <param name="small">The popup's frame, its buttons and edges smaller (<see cref="Small"/>).</param>
    public KeyboardFrame(Context context, bool small = false)
        : base(context)
    {
        this.context = context;
        sizes = small ? Small : Large;
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

        // 📌 Dock and ✥ over Restablecer, Cancelar and Hecho: they fit a keyboard in a narrow window.
        buttons = new LinearLayout(context) { Orientation = Orientation.Vertical };
        buttons.SetGravity(GravityFlags.Center);
        top = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        dock = Button("📌 " + context.GetString(Resource.String.dock)!, () => DockToggled?.Invoke());
        top.AddView(dock, Spaced());
        move = Button("✥", null);
        move.SetTextSize(ComplexUnitType.Sp, sizes.MoveSp);
        Hold(move, Part.Whole);
        top.AddView(move, Spaced());
        buttons.AddView(top, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { Gravity = GravityFlags.CenterHorizontal });
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        reset = Button("↺ " + context.GetString(Resource.String.reset_size)!, () => ResetRequested?.Invoke());
        row.AddView(reset, Spaced());
        row.AddView(Button("✕ " + context.GetString(Resource.String.cancel)!, () => CancelRequested?.Invoke()), Spaced());
        row.AddView(Button("✓ " + context.GetString(Resource.String.done)!, () => DoneRequested?.Invoke()), Spaced());
        buttons.AddView(row, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
        AddView(buttons, new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Center));
    }

    /// <summary>A finger dragging the keyboard or one of its edges: how far it has gone since it went down, in pixels.</summary>
    public event Action<Part, KeyPanel.Drag, float, float>? Dragged;

    public event Action? ResetRequested;

    /// <summary>📌 Dock tapped: the window stuck to the keyboard, or left where it is.</summary>
    public event Action? DockToggled;

    /// <summary>Cancelar: the keyboard as the frame found it.</summary>
    public event Action? CancelRequested;

    public event Action? DoneRequested;

    /// <summary>
    /// The frame over the keyboard: to move it (<paramref name="resizing"/>
    /// false), or to size it, with the side bars where the keyboard is in a
    /// window (<paramref name="sides"/>). A window stuck to the keyboard
    /// (<paramref name="docked"/>) is not moved by hand: no ✥; with 📌 Dock
    /// where the user may stick it or let it go (<paramref name="canDock"/>),
    /// lit while it is stuck, else neither row's place.
    /// </summary>
    public void Show(bool resizing, bool sides, bool canDock = false, bool docked = false)
    {
        whole.Visibility = resizing ? ViewStates.Gone : ViewStates.Visible;
        dock.Visibility = canDock ? ViewStates.Visible : ViewStates.Gone;
        dock.Background = Rounded(docked ? KeyPanel.HeldColor : KeyPanel.KeyColor);
        dock.SetTextColor(docked ? KeyPanel.HeldTextColor : Color.White);
        move.Visibility = docked ? ViewStates.Invisible : ViewStates.Visible;
        top.Visibility = canDock || !docked ? ViewStates.Visible : ViewStates.Gone;
        reset.Visibility = resizing ? ViewStates.Visible : ViewStates.Gone;
        edges[0].Visibility = edges[1].Visibility = resizing ? ViewStates.Visible : ViewStates.Gone;
        edges[2].Visibility = edges[3].Visibility = resizing && sides ? ViewStates.Visible : ViewStates.Gone;
        Visibility = ViewStates.Visible;
        BringToFront();
    }

    public void Hide() => Visibility = ViewStates.Gone;

    /// <summary>How tall the frame needs to be for its buttons and its top and bottom edges, in pixels.</summary>
    public int NeededHeight
    {
        get
        {
            buttons.Measure(MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified), MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified));
            return buttons.MeasuredHeight + (2 * Dp(sizes.Edge));
        }
    }

    public bool Showing => Visibility == ViewStates.Visible;

    /// <summary>An edge's bar: the whole edge to take hold of, a short line in its middle to show it is there.</summary>
    private View Edge(Part part, GravityFlags gravity, bool horizontal)
    {
        var edge = new FrameLayout(context);
        var line = new View(context) { Background = Rounded(KeyPanel.HeldColor) };
        edge.AddView(line, horizontal
            ? new LayoutParams(Dp(sizes.EdgeLine), Dp(sizes.EdgeThickness), GravityFlags.Center)
            : new LayoutParams(Dp(sizes.EdgeThickness), Dp(sizes.EdgeLine), GravityFlags.Center));
        Hold(edge, part);
        AddView(edge, horizontal
            ? new LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(sizes.Edge), gravity)
            : new LayoutParams(Dp(sizes.Edge), ViewGroup.LayoutParams.MatchParent, gravity));
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
        button.SetTextSize(ComplexUnitType.Sp, sizes.ButtonSp);
        button.SetMaxLines(1);
        button.SetPadding(Dp(sizes.PadAcross), Dp(sizes.PadDown), Dp(sizes.PadAcross), Dp(sizes.PadDown));
        if (pressed is not null)
        {
            button.Click += (_, _) => pressed();
        }

        return button;
    }

    private LinearLayout.LayoutParams Spaced()
    {
        var layout = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(sizes.ButtonHeight)) { Gravity = GravityFlags.CenterHorizontal };
        layout.SetMargins(Dp(sizes.GapAcross), Dp(sizes.GapDown), Dp(sizes.GapAcross), Dp(sizes.GapDown));
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
