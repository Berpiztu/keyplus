using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Text;
using Android.Text.Style;
using Android.Util;
using Android.Views;
using Android.Widget;
using Orientation = Android.Widget.Orientation;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The keys, as a small window: a title bar that moves it, with ABC to turn
/// it into a whole Spanish keyboard and Keys to turn it back, A− and A+ for
/// smaller or larger labels on the keys that type characters and A↺ for the
/// keys and labels at their usual size, ⤓ to dock it
/// above the keyboard again (while it floats), – to minimize it into the ⌨
/// icon, which a tap opens again and a drag moves, and ✕ to close KeyPlus,
/// once the title bar has asked whether to; the keys, the shortcuts'
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

    private const string FoldedPreference = "folded";
    private const string WholeKeyboardPreference = "wholeKeyboard";
    private const string LabelScalePreference = "labelScale";

    /// <summary>How much A− and A+ change the labels' size at a press, and how far.</summary>
    private const float LabelScaleStep = 0.1f;
    private const float SmallestLabelScale = 0.5f;
    private const float LargestLabelScale = 2.5f;
    private const int RepeatDelayMs = 400;
    private const int RepeatIntervalMs = 50;

    /// <summary>How long a touch is held still before it moves or resizes the window.</summary>
    private const int LongPressMs = 450;

    /// <summary>How near a corner of the window a long press starts to resize it rather than move it.</summary>
    private const int CornerReachDp = 48;

    /// <summary>The icon's label, in sp.</summary>
    private const float LabelSize = 16f;

    /// <summary>
    /// A key's label, in sp at the usual size: as large as fits in the key, up
    /// to the largest, down to the smallest for a long label in a narrow key.
    /// </summary>
    private const int LargestLabelSize = 18;

    /// <summary>The largest label, in sp at the usual size, of a key that types no character: Ctrl, Alt, Entrar and the like.</summary>
    private const int OtherLabelSize = 16;
    private const int SmallestLabelSize = 8;

    /// <summary>The share of it a secondary legend takes: the shifted character above, the Alt Gr one at the right.</summary>
    private const float SecondaryLabelShare = 0.7f;

    /// <summary>The share of the room left of its legend a key with three legends gives up.</summary>
    private const float ThreeLegendsLeftRoomGivenUp = 0.5f;

    // Opaque, the keys well apart from the ground: the window looks the same
    // over a white screen as over a black one.
    private static readonly Color PanelColor = Color.Argb(0xFF, 0x12, 0x12, 0x12);
    private static readonly Color KeyColor = Color.Argb(0xFF, 0x42, 0x42, 0x42);
    private static readonly Color HeldColor = Color.Argb(0xFF, 0x4F, 0xC3, 0xF7);
    private static readonly Color HeldTextColor = Color.Argb(0xFF, 0x10, 0x10, 0x10);
    private static readonly Color RippleColor = Color.Argb(0x55, 0xFF, 0xFF, 0xFF);
    private static readonly Color DimTextColor = Color.Argb(0xFF, 0x9E, 0x9E, 0x9E);

    private readonly Context context;
    private readonly Action<Keycode, MetaKeyStates> send;
    private readonly Action<string> type;
    private readonly ISharedPreferences preferences;
    private readonly Handler handler = new(Looper.MainLooper!);
    private readonly int touchSlop;
    private readonly List<(Held Modifier, TextView View)> modifierKeys = [];
    private readonly List<(PanelKey Key, TextView View)> labelledKeys = [];
    private readonly Dictionary<TextView, CornerLabel> corners = [];
    /// <summary>Every key, and whether it types a character, the keys whose labels A− and A+ size.</summary>
    private readonly List<(TextView View, bool IsCharacter, float Factor)> keys = [];

    /// <summary>The rows and the side column's keys, whose heights follow <see cref="Scale"/> and nothing else.</summary>
    private readonly List<View> rowViews = [];
    private readonly List<View> sideKeys = [];
    private readonly FrameLayout root;
    private readonly LinearLayout strip;
    private readonly LinearLayout terminal;
    private readonly LinearLayout wholeKeyboard;
    private readonly TextView tab;
    private readonly LinearLayout titleBar;
    private readonly LinearLayout closeBar;
    private bool iconOnly;
    private readonly TextView dock;
    private readonly TextView switchKeys;
    private Java.Lang.Runnable? repeat;
    private Held held;
    private string? deadAccent;
    private float scale = 1f;

    /// <summary>The labels' size the user chose with A− and A+, 1 for the usual one.</summary>
    private float labelScale;

    /// <param name="send">Sends a key with the meta state of the modifiers held for it.</param>
    /// <param name="type">Types text, as the phone's keyboard does.</param>
    /// <param name="preferences">The layout of the display the panel is on.</param>
    public KeyPanel(Context context, ISharedPreferences preferences, Action<Keycode, MetaKeyStates> send, Action<string> type)
    {
        this.context = context;
        this.send = send;
        this.type = type;
        this.preferences = preferences;
        touchSlop = ViewConfiguration.Get(context)!.ScaledTouchSlop;
        labelScale = preferences.GetFloat(LabelScalePreference, 1f);

        var title = new TextView(context) { Text = "KeyPlus", Gravity = GravityFlags.CenterVertical };
        title.SetTextColor(DimTextColor);
        title.SetTextSize(ComplexUnitType.Sp, 12);
        title.SetPadding(Dp(6), 0, Dp(6), 0);
        title.ContentDescription = "Move the keys";
        Draggable(title, (drag, dx, dy) => Moving?.Invoke(drag, dx, dy), tapped: null);

        dock = TitleButton("⤓", "Dock the keys above the keyboard");
        dock.Click += (_, _) => DockRequested?.Invoke();
        var minimize = TitleButton("–", "Minimize the keys into an icon");
        minimize.Click += (_, _) => SetFolded(true);
        var close = TitleButton("✕", "Close KeyPlus");
        close.Click += (_, _) => AskToClose(true);
        var smaller = TitleButton("A−", "Smaller labels");
        smaller.Click += (_, _) => SetLabelScale(labelScale - LabelScaleStep);
        var larger = TitleButton("A+", "Larger labels");
        larger.Click += (_, _) => SetLabelScale(labelScale + LabelScaleStep);
        var usual = TitleButton("A↺", "Keys and labels at their usual size");
        usual.Click += (_, _) =>
        {
            SetLabelScale(1f);
            UsualSizeRequested?.Invoke();
        };
        switchKeys = TitleButton("ABC", "Switch between the keys and a whole keyboard");
        switchKeys.Click += (_, _) => SetWholeKeyboard(wholeKeyboard!.Visibility != ViewStates.Visible);

        titleBar = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        titleBar.AddView(Grip("◤", Corner.TopLeft), new LinearLayout.LayoutParams(Dp(24), ViewGroup.LayoutParams.MatchParent));
        titleBar.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        titleBar.AddView(smaller, new LinearLayout.LayoutParams(Dp(36), ViewGroup.LayoutParams.MatchParent));
        titleBar.AddView(larger, new LinearLayout.LayoutParams(Dp(36), ViewGroup.LayoutParams.MatchParent));
        titleBar.AddView(usual, new LinearLayout.LayoutParams(Dp(36), ViewGroup.LayoutParams.MatchParent));
        titleBar.AddView(switchKeys, new LinearLayout.LayoutParams(Dp(44), ViewGroup.LayoutParams.MatchParent));
        titleBar.AddView(dock, new LinearLayout.LayoutParams(Dp(36), ViewGroup.LayoutParams.MatchParent));
        titleBar.AddView(minimize, new LinearLayout.LayoutParams(Dp(36), ViewGroup.LayoutParams.MatchParent));
        titleBar.AddView(close, new LinearLayout.LayoutParams(Dp(36), ViewGroup.LayoutParams.MatchParent));

        var question = new TextView(context) { Text = "¿Quieres cerrar KeyPlus?", Gravity = GravityFlags.CenterVertical };
        question.SetTextColor(Color.White);
        question.SetTextSize(ComplexUnitType.Sp, 13);
        question.SetPadding(Dp(8), 0, Dp(8), 0);
        var yes = TitleButton("Sí", "Close KeyPlus");
        yes.Click += (_, _) => CloseRequested?.Invoke();
        var no = TitleButton("No", "Keep KeyPlus open");
        no.Click += (_, _) => AskToClose(false);
        closeBar = new LinearLayout(context) { Orientation = Orientation.Horizontal, Visibility = ViewStates.Gone };
        closeBar.AddView(question, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        closeBar.AddView(yes, new LinearLayout.LayoutParams(Dp(48), ViewGroup.LayoutParams.MatchParent));
        closeBar.AddView(no, new LinearLayout.LayoutParams(Dp(48), ViewGroup.LayoutParams.MatchParent));
        titleBar.AddView(Grip("◥", Corner.TopRight), new LinearLayout.LayoutParams(Dp(24), ViewGroup.LayoutParams.MatchParent));

        var bottomBar = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        bottomBar.AddView(Grip("◣", Corner.BottomLeft), new LinearLayout.LayoutParams(Dp(24), ViewGroup.LayoutParams.MatchParent));
        bottomBar.AddView(new View(context), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        bottomBar.AddView(Grip("◢", Corner.BottomRight), new LinearLayout.LayoutParams(Dp(24), ViewGroup.LayoutParams.MatchParent));

        strip = new LinearLayout(context) { Orientation = Orientation.Vertical };
        strip.SetBackgroundColor(PanelColor);
        strip.SetPadding(Dp(2), 0, Dp(2), 0);
        strip.AddView(titleBar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(28)));
        strip.AddView(closeBar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(28)));
        terminal = Rows(PanelKeys.Terminal);
        wholeKeyboard = Rows(PanelKeys.Spanish, PanelKeys.SpanishSide);
        strip.AddView(terminal);
        strip.AddView(wholeKeyboard);

        // Between the keys, a long press moves or resizes the window as on a key.
        var groundHold = new LongPress(this, strip);
        strip.Touch += (_, e) =>
        {
            var motion = e.Event!;
            e.Handled = true;
            switch (motion.ActionMasked)
            {
                case MotionEventActions.Down:
                    groundHold.Begin(motion);
                    break;
                case MotionEventActions.Move:
                    groundHold.Track(motion);
                    break;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    groundHold.End(motion);
                    break;
            }
        };
        strip.AddView(bottomBar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(14)));

        tab = KeyView("⌨");
        tab.ContentDescription = "Show the keys";
        tab.SetPadding(Dp(16), Dp(6), Dp(16), Dp(6));
        Draggable(tab, (drag, dx, dy) => Moving?.Invoke(drag, dx, dy), OnIconTapped);

        root = new FrameLayout(context);
        root.AddView(strip);
        root.AddView(tab);
        ShowFolded(preferences.GetBoolean(FoldedPreference, false));
        ShowWholeKeyboard(preferences.GetBoolean(WholeKeyboardPreference, false));
        ShowHeld();
    }

    /// <summary>The panel's view, for the window that shows it.</summary>
    public View Root => root;

    /// <summary>Whether the user minimized the window into its icon.</summary>
    public bool Folded { get; private set; }

    /// <summary>
    /// Whether only the icon is shown, the window being docked with no
    /// keyboard to stand above: the icon is always in sight.
    /// </summary>
    public bool IconOnly
    {
        get => iconOnly;
        set
        {
            // Set on every windows change: showing it again would also take
            // back the title bar's question whether to close.
            if (iconOnly != value)
            {
                iconOnly = value;
                ShowFolded(Folded);
            }
        }
    }

    /// <summary>Whether the icon is what is shown, minimized or for want of a keyboard.</summary>
    public bool ShowsIcon => Folded || iconOnly;

    /// <summary>The window was minimized into its icon or opened again: it changes shape.</summary>
    public event Action? FoldedChanged;

    /// <summary>The icon was tapped while only the icon can be shown: the window opens floating where it is.</summary>
    public event Action? OpenRequested;

    /// <summary>The user said yes to closing KeyPlus.</summary>
    public event Action? CloseRequested;

    /// <summary>The title bar or the icon is dragged, by so many pixels across and down since the drag started.</summary>
    public event Action<Drag, float, float>? Moving;

    /// <summary>A corner's grip is dragged, by so many pixels across and down since the drag started.</summary>
    public event Action<Corner, Drag, float, float>? Resizing;

    /// <summary>⤓ was pressed: the window goes back above the keyboard.</summary>
    public event Action? DockRequested;

    /// <summary>A↺ was pressed: the keys go back to their usual size, as the labels have.</summary>
    public event Action? UsualSizeRequested;

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
            ApplySizes();
        }
    }

    /// <summary>Whether Ctrl, Alt or Windows is held, waiting for the next key.</summary>
    public bool HoldsKeyModifier => (held & KeyModifiers) != Held.None;

    /// <summary>The modifiers that make a character key go out as a key, not as its text.</summary>
    private const Held KeyModifiers = Held.Ctrl | Held.Alt | Held.Windows;

    /// <summary>
    /// The meta state of the modifiers held, for a key typed on the phone's
    /// keyboard instead of here; Ctrl and Alt are let go after it, as after a
    /// key here.
    /// </summary>
    public MetaKeyStates TakeHeldForTyped()
    {
        var meta = PanelKeys.MetaOf(held);
        SetHeld(held & (Held.Shift | Held.CapsLock | Held.Fn));
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

    /// <summary>Opens the window out of its icon.</summary>
    public void Unfold() => SetFolded(false);

    private void OnIconTapped()
    {
        if (iconOnly)
        {
            OpenRequested?.Invoke();
        }
        else
        {
            SetFolded(false);
        }
    }

    /// <summary>Shows the title bar's question whether to close KeyPlus, or the title bar again.</summary>
    private void AskToClose(bool ask)
    {
        titleBar.Visibility = ask ? ViewStates.Gone : ViewStates.Visible;
        closeBar.Visibility = ask ? ViewStates.Visible : ViewStates.Gone;
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
        switchKeys.Text = whole ? "Keys" : "ABC";
    }

    private void ShowFolded(bool folded)
    {
        Folded = folded;
        StopRepeat();
        AskToClose(false);
        strip.Visibility = ShowsIcon ? ViewStates.Gone : ViewStates.Visible;
        tab.Visibility = ShowsIcon ? ViewStates.Visible : ViewStates.Gone;
    }

    /// <summary>
    /// A row's height: fixed, following the keys' size alone, so no legend
    /// (two lines, or one with Fn, Shift or Alt Gr held) changes the
    /// keyboard's height, and the side column's keys stand level with the rows.
    /// </summary>
    private int RowHeight => Dp(56 * scale);

    /// <param name="side">Keys in a column at the right, one beside each row.</param>
    private LinearLayout Rows(IReadOnlyList<KeyBlock> layout, IReadOnlyList<PanelKey>? side = null)
    {
        var blocks = new LinearLayout(context) { Orientation = Orientation.Vertical };
        foreach (var block in layout)
        {
            var rows = new LinearLayout(context) { Orientation = Orientation.Vertical };
            foreach (var keys in block.Rows)
            {
                var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
                foreach (var key in keys)
                {
                    row.AddView(PanelKeyView(key), KeyMargins(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, key.Weight)));
                }

                rows.AddView(row, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, RowHeight));
                rowViews.Add(row);
            }

            if (block.Tall is not { } tall)
            {
                blocks.AddView(rows);
                continue;
            }

            var withTall = new LinearLayout(context) { Orientation = Orientation.Horizontal };
            withTall.AddView(rows, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, block.RowsWeight));
            withTall.AddView(PanelKeyView(tall), KeyMargins(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, tall.Weight)));
            blocks.AddView(withTall);
        }

        if (side is null)
        {
            return blocks;
        }

        var column = new LinearLayout(context) { Orientation = Orientation.Vertical };
        foreach (var key in side)
        {
            var view = PanelKeyView(key);
            column.AddView(view, KeyMargins(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, RowHeight - Dp(4))));
            sideKeys.Add(view);
        }

        var withSide = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        withSide.AddView(blocks, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 15.5f));
        // As wide as one key of the rows, which weigh 15.5.
        withSide.AddView(column, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        return withSide;
    }

    private TextView PanelKeyView(PanelKey key)
    {
        var view = KeyView(key.Label);
        view.SetPadding(Dp(2), 0, Dp(2), 0);
        var factor = PanelKeys.GlyphFactor(key.Label);
        FitLabel(view, key.IsCharacter, factor);
        keys.Add((view, key.IsCharacter, factor));
        if (key.IsModifier)
        {
            modifierKeys.Add((key.Modifier, view));
        }

        if (key.IsCharacter || key.FnLabel is not null)
        {
            labelledKeys.Add((key, view));
            if (key.AltGrText is not null)
            {
                var corner = new CornerLabel(view, SecondaryLabelShare, Dp(5));
                view.Foreground = corner;
                corners[view] = corner;
            }

            view.SetSingleLine(false);
            view.SetMaxLines(2);

            // No room above and below the lines for accents a legend does not
            // have, so the legends grow as far as they can; a margin inside
            // the key's edges, which the corner's Alt Gr character keeps too.
            view.SetIncludeFontPadding(false);
            view.SetPadding(Dp(6), Dp(3), Dp(6), Dp(3));
        }

        // A key that repeats acts on the touch, and repeats while held; any
        // other acts on its release, for held long it moves the window, or
        // resizes it from a corner it is near.
        var hold = key.Repeats ? null : new LongPress(this, view);
        view.Touch += (_, e) =>
        {
            e.Handled = true;
            var motion = e.Event!;
            switch (motion.ActionMasked)
            {
                case MotionEventActions.Down:
                    view.Pressed = true;
                    view.PerformHapticFeedback(FeedbackConstants.KeyboardTap);
                    if (hold is null)
                    {
                        Press(key);
                    }
                    else
                    {
                        hold.Begin(motion);
                    }

                    break;
                case MotionEventActions.Move:
                    hold?.Track(motion);
                    break;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    view.Pressed = false;
                    StopRepeat();
                    if (hold?.End(motion) == true && motion.ActionMasked == MotionEventActions.Up)
                    {
                        Press(key);
                    }

                    break;
            }
        };
        return view;
    }

    /// <summary>
    /// A long press that moves the window, or resizes it from the corner it
    /// starts near: held still past <see cref="LongPressMs"/>, the touch takes
    /// the window along, with a buzz to say so; let go before, it was a tap;
    /// moved before, neither.
    /// </summary>
    private sealed class LongPress(KeyPanel panel, View view)
    {
        private float startX;
        private float startY;
        private bool held;
        private bool slid;
        private Corner? corner;
        private Java.Lang.Runnable? waiting;

        public void Begin(MotionEvent motion)
        {
            (startX, startY, held, slid, corner) = (motion.RawX, motion.RawY, false, false, null);
            waiting = new Java.Lang.Runnable(Hold);
            panel.handler.PostDelayed(waiting, LongPressMs);
        }

        public void Track(MotionEvent motion)
        {
            var (dx, dy) = (motion.RawX - startX, motion.RawY - startY);
            if (held)
            {
                Report(Drag.Move, dx, dy);
            }
            else if (!slid && Math.Sqrt((dx * dx) + (dy * dy)) > panel.touchSlop)
            {
                slid = true;
                Cancel();
            }
        }

        /// <summary>Ends the touch; whether it was a tap.</summary>
        public bool End(MotionEvent motion)
        {
            Cancel();
            if (held)
            {
                Report(Drag.End, motion.RawX - startX, motion.RawY - startY);
                return false;
            }

            return !slid;
        }

        private void Hold()
        {
            waiting = null;
            held = true;
            view.PerformHapticFeedback(FeedbackConstants.LongPress);
            corner = panel.CornerAt(startX, startY);
            Report(Drag.Start, 0, 0);
        }

        private void Report(Drag drag, float dx, float dy)
        {
            if (corner is { } from)
            {
                panel.Resizing?.Invoke(from, drag, dx, dy);
            }
            else
            {
                panel.Moving?.Invoke(drag, dx, dy);
            }
        }

        private void Cancel()
        {
            if (waiting is not null)
            {
                panel.handler.RemoveCallbacks(waiting);
                waiting = null;
            }
        }
    }

    /// <summary>The corner of the window a point on the screen is near, if any.</summary>
    private Corner? CornerAt(float rawX, float rawY)
    {
        var location = new int[2];
        root.GetLocationOnScreen(location);
        var (x, y) = (rawX - location[0], rawY - location[1]);
        var reach = Dp(CornerReachDp);
        var left = x < reach;
        var right = x > root.Width - reach;
        var top = y < reach;
        var bottom = y > root.Height - reach;
        if (!(left || right) || !(top || bottom))
        {
            return null;
        }

        return (left ? Corner.Left : 0) | (top ? Corner.Top : 0);
    }

    private void Press(PanelKey key)
    {
        if (key.IsModifier)
        {
            // Windows pressed twice, nothing between: Windows alone, the Start menu.
            if (key.Modifier == Held.Windows && held.HasFlag(Held.Windows))
            {
                send(key.Code, MetaKeyStates.None);
            }

            SetHeld(held ^ key.Modifier);
            return;
        }

        var code = held.HasFlag(Held.Fn) && key.FnCode is { } fnCode ? fnCode : key.Code;
        if (code == key.Code && key.IsCharacter && ((held & KeyModifiers) == Held.None || key.Code == Keycode.Unknown))
        {
            TypeCharacter(key);
            return;
        }

        var meta = PanelKeys.MetaOf(held | key.With);
        send(code, meta);
        SetHeld(held & (Held.Shift | Held.CapsLock | Held.Fn));
        if (key.Repeats)
        {
            StartRepeat(code, meta);
        }
    }

    /// <summary>
    /// Types a character key's text for the modifiers held, or keeps a dead
    /// key's accent for the next one; Shift and AltGr are let go after it.
    /// </summary>
    private void TypeCharacter(PanelKey key)
    {
        var text = key.TextFor(held);
        SetHeld(held & (Held.CapsLock | Held.Fn));
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

    /// <summary>Fills the modifiers held, and labels each character key and each key Fn changes for the modifiers held.</summary>
    private void ShowHeld()
    {
        foreach (var (modifier, view) in modifierKeys)
        {
            var isHeld = held.HasFlag(modifier);
            view.Background = KeyBackground(isHeld ? HeldColor : KeyColor);
            view.SetTextColor(isHeld ? HeldTextColor : Color.White);
        }

        foreach (var (key, view) in labelledKeys)
        {
            view.TextFormatted = Legend(key);
            if (corners.TryGetValue(view, out var corner))
            {
                // The Alt Gr character in the corner only while the key reads all it types.
                var plain = (held & (Held.AltGr | Held.Shift)) == Held.None && !(held.HasFlag(Held.Fn) && key.FnLabel is not null);
                corner.Text = plain ? key.AltGrText : null;
                corner.InvalidateSelf();

                // Three legends: the two above one another give up half the
                // room left of them, to stand apart from the corner's.
                ((KeyLabel)view).LeftRoomGivenUp = plain && key.HasThreeLegends ? ThreeLegendsLeftRoomGivenUp : 0f;
                view.Invalidate();
            }
        }
    }

    /// <summary>
    /// What a key reads, much as Windows' touch keyboard labels it: with Fn
    /// held the key it becomes; with Alt Gr or Shift held only what it types
    /// now, nothing where it types nothing; else the character it types with
    /// its shifted one small above it, both in the middle, closer to the left
    /// when the key also has an Alt Gr character, its <see cref="CornerLabel"/>.
    /// </summary>
    private Java.Lang.ICharSequence Legend(PanelKey key)
    {
        if (held.HasFlag(Held.Fn) && key.FnLabel is { } fnLabel)
        {
            return new Java.Lang.String(fnLabel);
        }

        if (!key.IsCharacter)
        {
            return new Java.Lang.String(key.Label);
        }

        if ((held & (Held.AltGr | Held.Shift)) != Held.None)
        {
            var typed = key.TextFor(held);
            var only = new SpannableStringBuilder();
            AppendLine(only, typed is null or " " ? key.Label == key.Text ? "" : key.Label : typed, secondary: false);
            return only;
        }

        var legend = new SpannableStringBuilder();
        if (!key.IsLetter && key.ShiftText is { } shifted && shifted != key.Text)
        {
            AppendLine(legend, shifted, secondary: true);
        }

        AppendLine(legend, key.IsLetter ? key.TextFor(held & ~Held.AltGr)! : key.Label, secondary: false);
        return legend;
    }

    /// <summary>Adds a line to a legend, a secondary one smaller, a small glyph larger.</summary>
    private static void AppendLine(SpannableStringBuilder legend, string text, bool secondary)
    {
        if (legend.Length() > 0)
        {
            legend.Append("\n");
        }

        var start = legend.Length();
        legend.Append(text);
        if (secondary)
        {
            legend.SetSpan(new RelativeSizeSpan(SecondaryLabelShare), start, legend.Length(), SpanTypes.ExclusiveExclusive);
        }

        if (PanelKeys.GlyphFactor(text) is var factor and > 1f)
        {
            legend.SetSpan(new BigGlyphSpan(factor), start, legend.Length(), SpanTypes.ExclusiveExclusive);
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
        var slop = touchSlop;
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

    private KeyLabel KeyView(string label)
    {
        var view = new KeyLabel(context)
        {
            Text = label,
            Gravity = GravityFlags.Center,
            Background = KeyBackground(KeyColor),
        };
        view.SetTextColor(Color.White);
        view.SetTextSize(ComplexUnitType.Sp, LabelSize);
        view.SetSingleLine(true);
        view.SetPadding(0, 0, 0, 0);
        return view;
    }

    /// <summary>Sets the labels' size the user chose, kept across restarts, and sizes the character keys' labels to it.</summary>
    private void SetLabelScale(float value)
    {
        labelScale = Math.Clamp((float)Math.Round(value, 1), SmallestLabelScale, LargestLabelScale);
        preferences.Edit()!.PutFloat(LabelScalePreference, labelScale)!.Apply();
        ApplySizes();
    }

    /// <summary>Sizes the rows, the side column's keys and every label for the keys' size and the labels' size.</summary>
    private void ApplySizes()
    {
        foreach (var row in rowViews)
        {
            row.LayoutParameters!.Height = RowHeight;
            row.RequestLayout();
        }

        foreach (var key in sideKeys)
        {
            key.LayoutParameters!.Height = RowHeight - Dp(4);
            key.RequestLayout();
        }

        foreach (var (view, isCharacter, factor) in keys)
        {
            FitLabel(view, isCharacter, factor);
        }
    }

    /// <summary>
    /// Sizes a key's label to the largest that fits in it, for the keys' size
    /// and, for a key that types a character, the labels' size the user chose:
    /// A− and A+ leave Ctrl, Alt, Enter and the like as they are.
    /// </summary>
    /// <param name="factor">How much larger a key that types no character draws its label: an arrow's, half as large again.</param>
    private void FitLabel(TextView key, bool isCharacter, float factor)
    {
        var size = isCharacter ? LargestLabelSize * scale * labelScale : OtherLabelSize * scale * factor;
        var largest = Math.Max(SmallestLabelSize + 1, (int)Math.Round(size));
        key.SetAutoSizeTextTypeUniformWithConfiguration(SmallestLabelSize, largest, 1, (int)ComplexUnitType.Sp);
        key.RequestLayout();
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
