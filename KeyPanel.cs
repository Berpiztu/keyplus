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
/// The keys, as a small window: a title bar that moves it, with ABC to turn
/// it into a whole Spanish keyboard and Fn to turn it back, ⤓ to dock it
/// above the keyboard again (while it floats) and ✕ to close it into the ⌨
/// icon, which a tap opens again and a drag moves; the keys, the shortcuts'
/// row and the terminal's two or the whole keyboard; and a grip at each
/// corner that resizes it, across for its width and up or down for its keys'
/// size. Where the window stands is
/// <see cref="KeyPlusService"/>'s: the title bar, the icon and the grips only
/// say how far they were dragged.
/// Ctrl, Alt and AltGr are held as Termux holds them: pressed once they stay
/// down, filled, for the next key, here or typed on the phone's keyboard
/// (<see cref="TakeHeldForTyped"/>), and are let go after it; pressed again
/// they let go at once. Shift stays down for the strip's keys, so Shift and
/// the arrows select, and is let go after a character typed; Caps Lock stays
/// until pressed again. A dead key (´ ` ^ ¨) waits for the next character
/// and puts its accent on it. Keys act on the touch, not on its release, and
/// the arrows, the pages and the deleting keys repeat while held.
/// </summary>
public sealed class KeyPanel
{
    /// <summary>The phase of a drag on the title bar, the icon or a grip.</summary>
    public enum Drag
    {
        Start,
        Move,
        End,
    }

    /// <summary>The corner a grip resizes from; the opposite one stays where it is.</summary>
    [Flags]
    public enum Corner
    {
        BottomRight = 0,
        Left = 1,
        Top = 2,
        BottomLeft = Left,
        TopRight = Top,
        TopLeft = Left | Top,
    }

    private const string PreferencesName = "keyplus";
    private const string FoldedPreference = "folded";
    private const string WholeKeyboardPreference = "wholeKeyboard";
    private const int RepeatDelayMs = 400;
    private const int RepeatIntervalMs = 50;

    private static readonly Color PanelColor = Color.Argb(0xEE, 0x1E, 0x1E, 0x1E);
    private static readonly Color KeyColor = Color.Argb(0xFF, 0x37, 0x37, 0x37);
    private static readonly Color HeldColor = Color.Argb(0xFF, 0x4F, 0xC3, 0xF7);
    private static readonly Color HeldTextColor = Color.Argb(0xFF, 0x10, 0x10, 0x10);
    private static readonly Color RippleColor = Color.Argb(0x55, 0xFF, 0xFF, 0xFF);
    private static readonly Color DimTextColor = Color.Argb(0xFF, 0x9E, 0x9E, 0x9E);

    private readonly Context context;
    private readonly Action<Keycode, MetaKeyStates> send;
    private readonly Action<string> type;
    private readonly ISharedPreferences preferences;
    private readonly Handler handler = new(Looper.MainLooper!);
    private readonly List<(Held Modifier, TextView View)> modifierKeys = [];
    private readonly List<(PanelKey Key, TextView View)> characterKeys = [];
    private readonly List<TextView> keys = [];
    private readonly FrameLayout root;
    private readonly LinearLayout strip;
    private readonly LinearLayout terminal;
    private readonly LinearLayout wholeKeyboard;
    private readonly TextView tab;
    private readonly TextView dock;
    private readonly TextView switchKeys;
    private Java.Lang.Runnable? repeat;
    private Held held;
    private string? deadAccent;
    private float scale = 1f;

    /// <param name="send">Sends a key with the meta state of the modifiers held for it.</param>
    /// <param name="type">Types text, as the phone's keyboard does.</param>
    public KeyPanel(Context context, Action<Keycode, MetaKeyStates> send, Action<string> type)
    {
        this.context = context;
        this.send = send;
        this.type = type;
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

        var title = new TextView(context) { Text = "KeyPlus", Gravity = GravityFlags.CenterVertical };
        title.SetTextColor(DimTextColor);
        title.SetTextSize(ComplexUnitType.Sp, 12);
        title.SetPadding(Dp(6), 0, Dp(6), 0);
        title.ContentDescription = "Move the keys";
        Draggable(title, (drag, dx, dy) => Moving?.Invoke(drag, dx, dy), tapped: null);

        dock = TitleButton("⤓", "Dock the keys above the keyboard");
        dock.Click += (_, _) => DockRequested?.Invoke();
        var close = TitleButton("✕", "Close the keys into an icon");
        close.Click += (_, _) => SetFolded(true);
        switchKeys = TitleButton("ABC", "Switch between the keys and a whole keyboard");
        switchKeys.Click += (_, _) => SetWholeKeyboard(wholeKeyboard!.Visibility != ViewStates.Visible);

        var titleBar = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        titleBar.AddView(Grip("◤", Corner.TopLeft), new LinearLayout.LayoutParams(Dp(24), ViewGroup.LayoutParams.MatchParent));
        titleBar.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        titleBar.AddView(switchKeys, new LinearLayout.LayoutParams(Dp(44), ViewGroup.LayoutParams.MatchParent));
        titleBar.AddView(dock, new LinearLayout.LayoutParams(Dp(36), ViewGroup.LayoutParams.MatchParent));
        titleBar.AddView(close, new LinearLayout.LayoutParams(Dp(36), ViewGroup.LayoutParams.MatchParent));
        titleBar.AddView(Grip("◥", Corner.TopRight), new LinearLayout.LayoutParams(Dp(24), ViewGroup.LayoutParams.MatchParent));

        var bottomBar = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        bottomBar.AddView(Grip("◣", Corner.BottomLeft), new LinearLayout.LayoutParams(Dp(24), ViewGroup.LayoutParams.MatchParent));
        bottomBar.AddView(new View(context), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        bottomBar.AddView(Grip("◢", Corner.BottomRight), new LinearLayout.LayoutParams(Dp(24), ViewGroup.LayoutParams.MatchParent));

        strip = new LinearLayout(context) { Orientation = Orientation.Vertical };
        strip.SetBackgroundColor(PanelColor);
        strip.SetPadding(Dp(2), 0, Dp(2), 0);
        strip.AddView(titleBar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(28)));
        terminal = Rows(PanelKeys.Terminal);
        wholeKeyboard = Rows(PanelKeys.Spanish);
        strip.AddView(terminal);
        strip.AddView(wholeKeyboard);
        strip.AddView(bottomBar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(14)));

        tab = KeyView("⌨");
        tab.ContentDescription = "Show the keys";
        tab.SetPadding(Dp(16), Dp(6), Dp(16), Dp(6));
        Draggable(tab, (drag, dx, dy) => Moving?.Invoke(drag, dx, dy), () => SetFolded(false));

        root = new FrameLayout(context);
        root.AddView(strip);
        root.AddView(tab);
        ShowFolded(preferences.GetBoolean(FoldedPreference, false));
        ShowWholeKeyboard(preferences.GetBoolean(WholeKeyboardPreference, false));
        ShowHeld();
    }

    /// <summary>The panel's view, for the window that shows it.</summary>
    public View Root => root;

    /// <summary>Whether the panel is folded into its tab.</summary>
    public bool Folded { get; private set; }

    /// <summary>The window was closed into its icon or opened again: it changes shape.</summary>
    public event Action? FoldedChanged;

    /// <summary>The title bar or the icon is dragged, by so many pixels across and down since the drag started.</summary>
    public event Action<Drag, float, float>? Moving;

    /// <summary>A corner's grip is dragged, by so many pixels across and down since the drag started.</summary>
    public event Action<Corner, Drag, float, float>? Resizing;

    /// <summary>⤓ was pressed: the window goes back above the keyboard.</summary>
    public event Action? DockRequested;

    /// <summary>Whether the window floats, which is when ⤓ can dock it.</summary>
    public bool Floating
    {
        set => dock.Visibility = value ? ViewStates.Visible : ViewStates.Gone;
    }

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
        SetHeld(held & (Held.Shift | Held.CapsLock));
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

    private void SetWholeKeyboard(bool whole)
    {
        ShowWholeKeyboard(whole);
        preferences.Edit()!.PutBoolean(WholeKeyboardPreference, whole)!.Apply();
    }

    private void ShowWholeKeyboard(bool whole)
    {
        StopRepeat();
        terminal.Visibility = whole ? ViewStates.Gone : ViewStates.Visible;
        wholeKeyboard.Visibility = whole ? ViewStates.Visible : ViewStates.Gone;
        switchKeys.Text = whole ? "Fn" : "ABC";
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
                row.AddView(PanelKeyView(key), KeyMargins(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, key.Weight)));
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
            modifierKeys.Add((key.Modifier, view));
        }

        if (key.IsCharacter)
        {
            characterKeys.Add((key, view));
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

        if (key.IsCharacter && ((held & (Held.Ctrl | Held.Alt)) == Held.None || key.Code == Keycode.Unknown))
        {
            TypeCharacter(key);
            return;
        }

        var meta = PanelKeys.MetaOf(held | key.With);
        send(key.Code, meta);
        SetHeld(held & (Held.Shift | Held.CapsLock));
        if (key.Repeats)
        {
            StartRepeat(key.Code, meta);
        }
    }

    /// <summary>
    /// Types a character key's text for the modifiers held, or keeps a dead
    /// key's accent for the next one; Shift and AltGr are let go after it.
    /// </summary>
    private void TypeCharacter(PanelKey key)
    {
        var text = key.TextFor(held);
        SetHeld(held & Held.CapsLock);
        if (text is null)
        {
            return;
        }

        if (deadAccent is not null)
        {
            var accent = deadAccent;
            deadAccent = null;
            type(PanelKeys.Compose(accent, text));
        }
        else if (PanelKeys.IsDead(text))
        {
            deadAccent = text;
        }
        else
        {
            type(text);
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
        ShowHeld();
    }

    /// <summary>Fills the modifiers held, and labels each character key with what it types now.</summary>
    private void ShowHeld()
    {
        foreach (var (modifier, view) in modifierKeys)
        {
            var isHeld = held.HasFlag(modifier);
            view.Background = KeyBackground(isHeld ? HeldColor : KeyColor);
            view.SetTextColor(isHeld ? HeldTextColor : Color.White);
        }

        foreach (var (key, view) in characterKeys)
        {
            var text = key.TextFor(held);
            view.Text = text is null or " " ? key.Label : text;
        }
    }

    private TextView TitleButton(string label, string description)
    {
        var view = new TextView(context)
        {
            Text = label,
            Gravity = GravityFlags.Center,
            ContentDescription = description,
            Background = new RippleDrawable(ColorStateList.ValueOf(RippleColor), null, new ColorDrawable(Color.White)),
        };
        view.SetTextColor(Color.White);
        view.SetTextSize(ComplexUnitType.Sp, 14);
        return view;
    }

    /// <summary>A corner's grip: drawn faintly, resizing from that corner when dragged.</summary>
    private TextView Grip(string label, Corner corner)
    {
        var view = new TextView(context) { Text = label, ContentDescription = "Resize the keys" };
        view.Gravity = ((corner & Corner.Top) != 0 ? GravityFlags.Top : GravityFlags.Bottom)
            | ((corner & Corner.Left) != 0 ? GravityFlags.Left : GravityFlags.Right);
        view.SetTextColor(DimTextColor);
        view.SetTextSize(ComplexUnitType.Sp, 10);
        Draggable(view, (drag, dx, dy) => Resizing?.Invoke(corner, drag, dx, dy), tapped: null);
        return view;
    }

    /// <summary>
    /// Makes a view draggable: past the touch slop it reports the drag, in
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
