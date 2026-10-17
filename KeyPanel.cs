using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Orientation = Android.Widget.Orientation;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The strip of keys, as views: the terminal's two rows, and a narrow column
/// at the right with the strip's own controls: ✥ moves it (a tap docks it
/// again), ⇲ resizes it (across for its width, down for its keys' size), ▾
/// folds it into a small tab, which unfolds it again. Where the strip stands
/// is its window's, <see cref="KeyPlusService"/>'s: the controls only say how
/// far they were dragged.
/// Ctrl and Alt are held as Termux holds them: pressed once they stay down,
/// filled, for the next key, here or typed on the phone's keyboard
/// (<see cref="TakeHeldForTyped"/>), and are let go after it; pressed again
/// they let go at once. Shift is locked until it is pressed again. Keys act on
/// the touch, not on its release, and the arrows and pages repeat while held.
/// </summary>
public sealed class KeyPanel
{
    /// <summary>The phase of a drag on one of the strip's controls.</summary>
    public enum Drag
    {
        Start,
        Move,
        End,
    }

    private const string PreferencesName = "keyplus";
    private const string FoldedPreference = "folded";
    private const int RepeatDelayMs = 400;
    private const int RepeatIntervalMs = 50;

    private static readonly Color PanelColor = Color.Argb(0xEE, 0x1E, 0x1E, 0x1E);
    private static readonly Color KeyColor = Color.Argb(0xFF, 0x37, 0x37, 0x37);
    private static readonly Color HeldColor = Color.Argb(0xFF, 0x4F, 0xC3, 0xF7);
    private static readonly Color HeldTextColor = Color.Argb(0xFF, 0x10, 0x10, 0x10);
    private static readonly Color RippleColor = Color.Argb(0x55, 0xFF, 0xFF, 0xFF);

    private readonly Context context;
    private readonly Action<Keycode, MetaKeyStates> send;
    private readonly ISharedPreferences preferences;
    private readonly Handler handler = new(Looper.MainLooper!);
    private readonly Dictionary<Held, TextView> modifierKeys = [];
    private readonly List<TextView> keys = [];
    private readonly FrameLayout root;
    private readonly LinearLayout strip;
    private readonly TextView tab;
    private Java.Lang.Runnable? repeat;
    private Held held;
    private float scale = 1f;

    /// <param name="send">Sends a key with the meta state of the modifiers held for it.</param>
    public KeyPanel(Context context, Action<Keycode, MetaKeyStates> send)
    {
        this.context = context;
        this.send = send;
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

        var terminal = Rows(PanelKeys.Terminal);

        var move = ControlView("✥", "Move the keys; tap to dock them above the keyboard");
        Draggable(move, (drag, dx, dy) => Moving?.Invoke(drag, dx, dy), () => DockRequested?.Invoke());
        var resize = ControlView("⇲", "Resize the keys");
        Draggable(resize, (drag, dx, dy) => Resizing?.Invoke(drag, dx, dy), tapped: null);
        var fold = ControlView("▾", "Fold the keys");
        fold.Click += (_, _) => SetFolded(true);

        var controls = new LinearLayout(context) { Orientation = Orientation.Vertical };
        foreach (var control in new[] { move, resize, fold })
        {
            controls.AddView(control, KeyMargins(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f)));
        }

        var bottom = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        bottom.AddView(terminal, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        bottom.AddView(controls, new LinearLayout.LayoutParams(Dp(32), ViewGroup.LayoutParams.MatchParent));

        strip = new LinearLayout(context) { Orientation = Orientation.Vertical };
        strip.SetBackgroundColor(PanelColor);
        strip.SetPadding(Dp(2), Dp(2), Dp(2), Dp(2));
        strip.AddView(bottom);

        tab = KeyView("⌨");
        tab.ContentDescription = "Show the keys";
        tab.SetPadding(Dp(16), Dp(6), Dp(16), Dp(6));
        tab.Click += (_, _) => SetFolded(false);

        root = new FrameLayout(context);
        root.AddView(strip);
        root.AddView(tab);
        ShowFolded(preferences.GetBoolean(FoldedPreference, false));
    }

    /// <summary>The panel's view, for the window that shows it.</summary>
    public View Root => root;

    /// <summary>Whether the panel is folded into its tab.</summary>
    public bool Folded { get; private set; }

    /// <summary>The panel was folded or unfolded: its window changes shape.</summary>
    public event Action? FoldedChanged;

    /// <summary>✥ is dragged, by so many pixels across and down since the drag started.</summary>
    public event Action<Drag, float, float>? Moving;

    /// <summary>⇲ is dragged, by so many pixels across and down since the drag started.</summary>
    public event Action<Drag, float, float>? Resizing;

    /// <summary>✥ was tapped, not dragged: the strip goes back above the keyboard.</summary>
    public event Action? DockRequested;

    /// <summary>The keys' size, 1 for the usual one; the strip grows or shrinks with it.</summary>
    public float Scale
    {
        get => scale;
        set
        {
            scale = value;
            foreach (var key in keys)
            {
                key.SetTextSize(ComplexUnitType.Sp, 15 * scale);
                key.SetPadding(0, Dp(10 * scale), 0, Dp(10 * scale));
            }
        }
    }

    /// <summary>Whether Ctrl or Alt is held, waiting for the next key.</summary>
    public bool HoldsCtrlOrAlt => (held & (Held.Ctrl | Held.Alt)) != Held.None;

    /// <summary>
    /// The meta state of the modifiers held, for a key typed on the phone's
    /// keyboard instead of here; Ctrl and Alt are let go after it, as after a
    /// key here.
    /// </summary>
    public MetaKeyStates TakeHeldForTyped()
    {
        var meta = PanelKeys.MetaOf(held);
        SetHeld(held & Held.Shift);
        return meta;
    }

    /// <summary>Lets go of a held key's repeat, when the panel goes out of sight.</summary>
    public void StopRepeat()
    {
        if (repeat is not null)
        {
            handler.RemoveCallbacks(repeat);
            repeat = null;
        }
    }

    private void SetFolded(bool folded)
    {
        ShowFolded(folded);
        preferences.Edit()!.PutBoolean(FoldedPreference, folded)!.Apply();
        FoldedChanged?.Invoke();
    }

    private void ShowFolded(bool folded)
    {
        Folded = folded;
        StopRepeat();
        strip.Visibility = folded ? ViewStates.Gone : ViewStates.Visible;
        tab.Visibility = folded ? ViewStates.Visible : ViewStates.Gone;
    }

    private LinearLayout Rows(IReadOnlyList<IReadOnlyList<PanelKey>> layout)
    {
        var rows = new LinearLayout(context) { Orientation = Orientation.Vertical };
        foreach (var keys in layout)
        {
            var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
            foreach (var key in keys)
            {
                row.AddView(PanelKeyView(key), KeyMargins(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)));
            }

            rows.AddView(row);
        }

        return rows;
    }

    private TextView PanelKeyView(PanelKey key)
    {
        var view = KeyView(key.Label);
        keys.Add(view);
        if (key.IsModifier)
        {
            modifierKeys[key.Modifier] = view;
        }

        view.Touch += (_, e) =>
        {
            e.Handled = true;
            switch (e.Event?.ActionMasked)
            {
                case MotionEventActions.Down:
                    view.Pressed = true;
                    view.PerformHapticFeedback(FeedbackConstants.KeyboardTap);
                    Press(key);
                    break;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    view.Pressed = false;
                    StopRepeat();
                    break;
            }
        };
        return view;
    }

    private void Press(PanelKey key)
    {
        if (key.IsModifier)
        {
            SetHeld(held ^ key.Modifier);
            return;
        }

        var meta = PanelKeys.MetaOf(held | key.With);
        send(key.Code, meta);
        SetHeld(held & Held.Shift);
        if (key.Repeats)
        {
            StartRepeat(key.Code, meta);
        }
    }

    private void StartRepeat(Keycode code, MetaKeyStates meta)
    {
        StopRepeat();
        repeat = new Java.Lang.Runnable(() =>
        {
            send(code, meta);
            handler.PostDelayed(repeat!, RepeatIntervalMs);
        });
        handler.PostDelayed(repeat, RepeatDelayMs);
    }

    private void SetHeld(Held value)
    {
        held = value;
        foreach (var (modifier, view) in modifierKeys)
        {
            var isHeld = held.HasFlag(modifier);
            view.Background = KeyBackground(isHeld ? HeldColor : KeyColor);
            view.SetTextColor(isHeld ? HeldTextColor : Color.White);
        }
    }

    private TextView ControlView(string label, string description)
    {
        var view = KeyView(label);
        view.ContentDescription = description;
        view.SetTextSize(ComplexUnitType.Sp, 13);
        view.SetPadding(0, 0, 0, 0);
        return view;
    }

    /// <summary>
    /// Makes a control draggable: past the touch slop it reports the drag, in
    /// pixels from where it started; a touch that never got that far is a tap.
    /// </summary>
    private void Draggable(TextView view, Action<Drag, float, float> drag, Action? tapped)
    {
        var slop = ViewConfiguration.Get(context)!.ScaledTouchSlop;
        float startX = 0, startY = 0;
        var dragging = false;
        view.Touch += (_, e) =>
        {
            e.Handled = true;
            var motion = e.Event!;
            var dx = motion.RawX - startX;
            var dy = motion.RawY - startY;
            switch (motion.ActionMasked)
            {
                case MotionEventActions.Down:
                    view.Pressed = true;
                    (startX, startY, dragging) = (motion.RawX, motion.RawY, false);
                    break;
                case MotionEventActions.Move:
                    if (!dragging && Math.Sqrt((dx * dx) + (dy * dy)) > slop)
                    {
                        dragging = true;
                        drag(Drag.Start, 0, 0);
                    }

                    if (dragging)
                    {
                        drag(Drag.Move, dx, dy);
                    }

                    break;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    view.Pressed = false;
                    if (dragging)
                    {
                        drag(Drag.End, dx, dy);
                    }
                    else if (motion.ActionMasked == MotionEventActions.Up)
                    {
                        tapped?.Invoke();
                    }

                    break;
            }
        };
    }

    private TextView KeyView(string label)
    {
        var view = new TextView(context)
        {
            Text = label,
            Gravity = GravityFlags.Center,
            Background = KeyBackground(KeyColor),
        };
        view.SetTextColor(Color.White);
        view.SetTextSize(ComplexUnitType.Sp, 15);
        view.SetSingleLine(true);
        view.SetPadding(0, Dp(10), 0, Dp(10));
        return view;
    }

    private RippleDrawable KeyBackground(Color color)
    {
        var shape = new GradientDrawable();
        shape.SetColor(color);
        shape.SetCornerRadius(Dp(6));
        return new RippleDrawable(ColorStateList.ValueOf(RippleColor), shape, null);
    }

    private LinearLayout.LayoutParams KeyMargins(LinearLayout.LayoutParams layout)
    {
        layout.SetMargins(Dp(2), Dp(2), Dp(2), Dp(2));
        return layout;
    }

    private int Dp(float value) => (int)(value * context.Resources!.DisplayMetrics!.Density + 0.5f);
}
