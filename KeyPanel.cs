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
    private const float LargestLabelScale = 5f;
    private const int RepeatDelayMs = 400;
    private const int RepeatIntervalMs = 50;

    /// <summary>How long a touch is held still before it moves or resizes the window.</summary>
    private const int LongPressMs = 450;

    /// <summary>How near a corner of the window a long press starts to resize it rather than move it.</summary>
    private const int CornerReachDp = 48;

    /// <summary>The icon's label, in sp.</summary>
    private const float LabelSize = 16f;

    /// <summary>
    /// The room around a key, and inside it around its labels, in dp: one
    /// each, so the labels have the key almost whole.
    /// </summary>
    private const int KeyMarginDp = 1;
    private const int KeyPaddingDp = 1;

    /// <summary>The most of its key's width the word of a key that types no character takes: three quarters, the rest room at its sides.</summary>
    private const float WordShare = 0.75f;

    /// <summary>The least the words' one size is of the legends': below it, a word that does not fit is made smaller alone.</summary>
    private const float WordFloor = 0.7f;


    /// <summary>The least room between two legends of a key, in dp.</summary>
    private const int LegendGapDp = 2;

    /// <summary>The title bar's height, in dp: enough to hold and move the window by, no more.</summary>
    private const int TitleBarDp = 32;

    /// <summary>The least the title bar's height goes down to with the keys, in dp: room for a finger to hold it.</summary>
    private const int LeastTitleBarDp = 24;

    // Opaque, the keys well apart from the ground: the window looks the same
    // over a white screen as over a black one.
    private static readonly Color PanelColor = Color.Argb(0xFF, 0x12, 0x12, 0x12);
    private static readonly Color KeyColor = Color.Argb(0xFF, 0x42, 0x42, 0x42);
    private static readonly Color HeldColor = Color.Argb(0xFF, 0x4F, 0xC3, 0xF7);
    private static readonly Color HeldTextColor = Color.Argb(0xFF, 0x10, 0x10, 0x10);
    private static readonly Color RippleColor = Color.Argb(0x55, 0xFF, 0xFF, 0xFF);
    private static readonly Color DimTextColor = Color.Argb(0xFF, 0x9E, 0x9E, 0x9E);

    /// <summary>A key's legend it sends now, strong, and the ones it does not, weak.</summary>
    private static readonly Color SentLegendColor = Color.White;
    private static readonly Color OtherLegendColor = Color.Argb(0xFF, 0xA8, 0xA8, 0xA8);

    private readonly Context context;
    private readonly Action<Keycode, MetaKeyStates> send;
    private readonly Action<string> type;
    private readonly ISharedPreferences preferences;
    private readonly Handler handler = new(Looper.MainLooper!);
    private readonly int touchSlop;
    private readonly List<(Held Modifier, TextView View)> modifierKeys = [];
    /// <summary>The keys that type characters, whose legends are drawn on the table of three by three.</summary>
    private readonly List<(PanelKey Key, KeyLegends Legends, TextView View)> characterKeys = [];

    /// <summary>The keys that type no character but read another label with Fn held: the arrows.</summary>
    private readonly List<(PanelKey Key, TextView View)> fnKeys = [];

    /// <summary>The keys that type no character, whose one label is sized as the legends are, and how much larger it is drawn.</summary>
    private readonly List<(TextView View, float Factor, string Label)> labelKeys = [];

    /// <summary>The title bar's words and signs, and the largest each is drawn, in sp: never larger than the keys' words.</summary>
    private readonly List<(TextView View, int LargestSp)> titleTexts = [];

    /// <summary>The title bar's pieces but the title, and each one's width in dp at its full size.</summary>
    private readonly List<(View View, int WidthDp)> titleParts = [];

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

    /// <summary>The question shown in place of the title bar, and what Sí does.</summary>
    private readonly TextView question;
    private Action? asked;
    private bool iconOnly;
    private readonly TextView dock;
    private readonly TextView switchKeys;
    private Java.Lang.Runnable? repeat;
    private Held held;
    private string? deadAccent;
    private float scale = 1f;

    /// <summary>The labels' size the user chose with A− and A+, 1 for the usual one.</summary>
    private float labelScale;

    /// <summary>The legends' one size, in pixels, as the keys in sight allow (<see cref="FitLegends"/>); none until they are laid out.</summary>
    private float legendSize;

    /// <summary>A fitting is waiting for the keys to be laid out.</summary>
    private bool fitPending;

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

        // No name in the title bar, so its buttons have the room: what is
        // left between them is the place to hold and move the window by.
        var title = new View(context) { ContentDescription = "Hold to move the keys" };
        HoldToMove(title, tapped: null, resizes: false);

        dock = TitleButton("⤓", "Docked: the keys show while a keyboard is open, where you left them; pressed again, they float, in sight all the time", () => DockRequested?.Invoke());
        var minimize = TitleButton("–", "Minimize the keys into an icon", () => SetFolded(true));
        var close = TitleButton("✕", "Close KeyPlus", () => Ask("¿Cerrar KeyPlus?", () => CloseRequested?.Invoke()));
        var smaller = TitleButton("A−", "Smaller labels", () => SetLabelScale(labelScale - LabelScaleStep));
        var larger = TitleButton("A+", "Larger labels", () => SetLabelScale(labelScale + LabelScaleStep));
        // A↺ undoes every size made by hand, so it asks first, as ✕ does.
        var usual = TitleButton("A↺", "Keys and labels at their usual size", () => Ask("¿Tamaño normal?", () =>
        {
            SetLabelScale(1f);
            UsualSizeRequested?.Invoke();
        }));
        switchKeys = TitleButton("ABC", "Switch between the keys and a whole keyboard", () => SetWholeKeyboard(wholeKeyboard!.Visibility != ViewStates.Visible));

        titleBar = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        AddToTitleBar(Grip("◤", Corner.TopLeft), 24);
        titleBar.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        AddToTitleBar(smaller, 36);
        AddToTitleBar(larger, 36);
        AddToTitleBar(usual, 36);
        AddToTitleBar(switchKeys, 44);
        AddToTitleBar(dock, 36);
        AddToTitleBar(minimize, 36);
        AddToTitleBar(close, 36);

        // A window narrower than the title bar's buttons: they all narrow
        // alike, and their words with them, rather than any going out of sight.
        titleBar.LayoutChange += (_, e) =>
        {
            if (e.Right - e.Left != e.OldRight - e.OldLeft)
            {
                FitTitleBar(e.Right - e.Left);
            }
        };

        question = new TextView(context) { Gravity = GravityFlags.CenterVertical };
        question.SetTextColor(Color.White);
        Shrinkable(question, 13);
        question.SetPadding(Dp(4), 0, Dp(4), 0);
        var yes = TitleButton("Sí", "Yes", () =>
        {
            var confirmed = asked;
            Ask(null, null);
            confirmed?.Invoke();
        });
        var no = TitleButton("No", "No", () => Ask(null, null));
        closeBar = new LinearLayout(context) { Orientation = Orientation.Horizontal, Visibility = ViewStates.Gone };
        closeBar.AddView(question, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        closeBar.AddView(yes, new LinearLayout.LayoutParams(Dp(36), ViewGroup.LayoutParams.MatchParent));
        closeBar.AddView(no, new LinearLayout.LayoutParams(Dp(36), ViewGroup.LayoutParams.MatchParent));
        AddToTitleBar(Grip("◥", Corner.TopRight), 24);

        var bottomBar = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        bottomBar.AddView(Grip("◣", Corner.BottomLeft), new LinearLayout.LayoutParams(Dp(24), ViewGroup.LayoutParams.MatchParent));
        bottomBar.AddView(new View(context), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        bottomBar.AddView(Grip("◢", Corner.BottomRight), new LinearLayout.LayoutParams(Dp(24), ViewGroup.LayoutParams.MatchParent));

        strip = new LinearLayout(context) { Orientation = Orientation.Vertical };
        strip.SetBackgroundColor(PanelColor);
        strip.SetPadding(Dp(KeyMarginDp), 0, Dp(KeyMarginDp), 0);
        strip.AddView(titleBar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(TitleBarDp)));
        strip.AddView(closeBar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(TitleBarDp)));
        terminal = Rows(PanelKeys.Terminal);
        wholeKeyboard = Rows(PanelKeys.Spanish, PanelKeys.SpanishSide);
        strip.AddView(terminal);
        strip.AddView(wholeKeyboard);

        // Between the keys, a long press moves or resizes the window as on a key.
        HoldToMove(strip);
        strip.AddView(bottomBar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(14)));

        tab = KeyView("⌨");
        tab.SetTextSize(ComplexUnitType.Sp, LabelSize);
        tab.ContentDescription = "Show the keys";
        tab.SetPadding(Dp(16), Dp(6), Dp(16), Dp(6));
        Draggable(tab, (drag, dx, dy) => Moving?.Invoke(drag, dx, dy), OnIconTapped);

        root = new FrameLayout(context);
        root.AddView(strip);
        root.AddView(tab);
        ShowFolded(preferences.GetBoolean(FoldedPreference, false));
        ShowWholeKeyboard(preferences.GetBoolean(WholeKeyboardPreference, false));
        ApplySizes();
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

    /// <summary>ABC or Keys pressed: the window turned into the other mode, which keeps a layout of its own.</summary>
    public event Action? ModeChanged;

    /// <summary>The mode the window is in: the strip's keys or the whole keyboard (<see cref="PanelPlacement.KeysMode"/>, <see cref="PanelPlacement.WholeKeyboardMode"/>).</summary>
    public string Mode => wholeKeyboard.Visibility == ViewStates.Visible ? PanelPlacement.WholeKeyboardMode : PanelPlacement.KeysMode;

    /// <summary>Whether the window floats, which is when ⤓ can dock it.</summary>
    public bool Floating
    {
        // ⤓ filled while docked, as a modifier is while held.
        set
        {
            dock.Background = value
                ? new RippleDrawable(ColorStateList.ValueOf(RippleColor), null, new ColorDrawable(Color.White))
                : KeyBackground(HeldColor);
            dock.SetTextColor(value ? Color.White : HeldTextColor);
        }
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
    /// <summary>
    /// Shows a question in place of the title bar, which Sí answers by doing
    /// what was asked and No by leaving everything as it is; none, the title
    /// bar again.
    /// </summary>
    private void Ask(string? text, Action? yes)
    {
        asked = yes;
        question.Text = text ?? "";
        titleBar.Visibility = text is null ? ViewStates.Visible : ViewStates.Gone;
        closeBar.Visibility = text is null ? ViewStates.Gone : ViewStates.Visible;
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
        ModeChanged?.Invoke();
    }

    private void ShowWholeKeyboard(bool whole)
    {
        StopRepeat();
        terminal.Visibility = whole ? ViewStates.Gone : ViewStates.Visible;
        wholeKeyboard.Visibility = whole ? ViewStates.Visible : ViewStates.Gone;

        // Each mode keeps its own labels' size, as it keeps its own place
        // and keys' size; where it has none yet, the one kept before.
        labelScale = preferences.GetFloat(PanelPlacement.Keyed(Mode, LabelScalePreference), preferences.GetFloat(LabelScalePreference, 1f));
        SizeLabels();
        FitLegendsSoon();
        switchKeys.Text = whole ? "Keys" : "ABC";
    }

    private void ShowFolded(bool folded)
    {
        Folded = folded;
        StopRepeat();
        Ask(null, null);
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
            column.AddView(view, KeyMargins(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, RowHeight - (2 * Dp(KeyMarginDp)))));
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
        TextView view;
        if (key.IsCharacter)
        {
            // Its legends are drawn over it, each in its cell; the view itself reads nothing.
            view = KeyView("");
            view.ContentDescription = key.Label;
            var legends = new KeyLegends(Dp(KeyPaddingDp), Dp(LegendGapDp), SentLegendColor, OtherLegendColor);
            view.Foreground = legends;
            characterKeys.Add((key, legends, view));

            // The keys made larger or smaller, by a corner or a turned screen: the legends fitted to them again.
            view.LayoutChange += (_, e) =>
            {
                if (e.Right - e.Left != e.OldRight - e.OldLeft || e.Bottom - e.Top != e.OldBottom - e.OldTop)
                {
                    FitLegendsSoon();
                }
            };
        }
        else
        {
            view = KeyView(key.Label);
            view.SetPadding(Dp(KeyPaddingDp), Dp(KeyPaddingDp), Dp(KeyPaddingDp), Dp(KeyPaddingDp));
            labelKeys.Add((view, PanelKeys.GlyphFactor(key.Label), key.Label));
            if (key.FnLabel is not null)
            {
                fnKeys.Add((key, view));
            }
        }

        if (key.IsModifier)
        {
            modifierKeys.Add((key.Modifier, view));
        }

        // Every key acts on the touch, and one that types or sends repeats
        // while held, as a keyboard's does (PanelKey.RepeatsWhenHeld); the
        // window is moved from its title bar.
        view.Touch += (_, e) =>
        {
            e.Handled = true;
            switch (e.Event!.ActionMasked)
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

    /// <summary>
    /// A view held still a moment moves the window, or, where it
    /// <paramref name="resizes"/>, resizes it from the corner it is near; a
    /// tap on it does what <paramref name="tapped"/> says, if anything.
    /// </summary>
    private void HoldToMove(View view, Action? tapped = null, bool resizes = true)
    {
        var hold = new LongPress(this, view, resizes);
        view.Touch += (_, e) =>
        {
            var motion = e.Event!;
            e.Handled = true;
            switch (motion.ActionMasked)
            {
                case MotionEventActions.Down:
                    view.Pressed = tapped is not null;
                    hold.Begin(motion);
                    break;
                case MotionEventActions.Move:
                    hold.Track(motion);
                    break;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    view.Pressed = false;
                    if (hold.End(motion) && motion.ActionMasked == MotionEventActions.Up)
                    {
                        tapped?.Invoke();
                    }

                    break;
            }
        };
    }

    /// <summary>
    /// A long press that moves the window, or resizes it from the corner it
    /// starts near: held still past <see cref="LongPressMs"/>, the touch takes
    /// the window along, with a buzz to say so; let go before, it was a tap;
    /// moved before, neither.
    /// </summary>
    private sealed class LongPress(KeyPanel panel, View view, bool resizes = true)
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
            corner = resizes ? panel.CornerAt(startX, startY) : null;
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
        if (key.RepeatsWhenHeld)
        {
            StartRepeat(() => send(code, meta));
        }
    }

    /// <summary>
    /// Types a character key's text for the modifiers held, or keeps a dead
    /// key's accent for the next one; Shift and AltGr are let go after it.
    /// Held, the key types its text again and again, as it was typed: the
    /// same character with Shift or AltGr let go meanwhile, and no accent.
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
            return;
        }
        else
        {
            type(text);
        }

        if (key.RepeatsWhenHeld)
        {
            StartRepeat(() => type(text));
        }
    }

    /// <summary>Does it again while the key is held: after a pause, then every <see cref="RepeatIntervalMs"/>.</summary>
    private void StartRepeat(Action again)
    {
        StopRepeat();
        repeat = new Java.Lang.Runnable(() =>
        {
            again();
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

        foreach (var (key, legends, _) in characterKeys)
        {
            legends.Show(Legends(key, held));
        }

        foreach (var (key, view) in fnKeys)
        {
            view.Text = held.HasFlag(Held.Fn) ? key.FnLabel : key.Label;
        }
    }

    /// <summary>
    /// What a character key reads for the modifiers held, on its table of
    /// three by three: with Fn held the key it becomes, alone in the middle;
    /// with Shift or Alt Gr held only what it types now, in the middle, and
    /// nothing where it types nothing; else its shifted character at the top
    /// left (a letter has it as its own, upper case with Caps Lock), its
    /// plain one in the middle, strong, and its Alt Gr one at the bottom right.
    /// </summary>
    private static IReadOnlyList<KeyLegends.Legend> Legends(PanelKey key, Held held)
    {
        if (held.HasFlag(Held.Fn) && key.FnLabel is { } fnLabel)
        {
            return [new(fnLabel, KeyLegends.Plain, Sent: true)];
        }

        if ((held & (Held.Shift | Held.AltGr)) != Held.None)
        {
            return key.TextFor(held) is { } typed && typed.Trim().Length > 0 ? [new(typed, KeyLegends.Plain, Sent: true)] : [];
        }

        var legends = new List<KeyLegends.Legend>();
        if (!key.IsLetter && key.ShiftText is { } shifted && shifted != key.Text)
        {
            legends.Add(new(shifted, KeyLegends.Shifted, Sent: false));
        }

        legends.Add(new(key.IsLetter ? key.TextFor(held)! : key.Label, KeyLegends.Plain, Sent: true));
        if (key.AltGrText is { } altGr)
        {
            legends.Add(new(altGr, KeyLegends.AltGr, Sent: false));
        }

        return legends;
    }

    /// <summary>
    /// A button of the title bar: a tap does what it is for; held still a
    /// moment it moves the window, as the whole title bar does, so the bar is
    /// all room to move it by, never to resize it.
    /// </summary>
    private TextView TitleButton(string label, string description, Action pressed)
    {
        var view = new TextView(context)
        {
            Text = label,
            Gravity = GravityFlags.Center,
            ContentDescription = description,
            Background = new RippleDrawable(ColorStateList.ValueOf(RippleColor), null, new ColorDrawable(Color.White)),
        };
        view.SetTextColor(Color.White);
        Shrinkable(view, 14);
        HoldToMove(view, pressed, resizes: false);
        return view;
    }

    /// <summary>A title bar's piece at its width in dp, which narrows when the window is too narrow for every piece (<see cref="FitTitleBar"/>).</summary>
    private void AddToTitleBar(View view, int widthDp)
    {
        titleBar.AddView(view, new LinearLayout.LayoutParams(Dp(widthDp), ViewGroup.LayoutParams.MatchParent));
        titleParts.Add((view, widthDp));
    }

    /// <summary>The title bar's pieces at their widths, or, where the bar is narrower than they, all narrower alike.</summary>
    private void FitTitleBar(int width)
    {
        var wanted = Dp(titleParts.Sum(part => part.WidthDp));
        var share = Math.Min(1f, width / (float)wanted);
        foreach (var (view, widthDp) in titleParts)
        {
            view.LayoutParameters!.Width = (int)(Dp(widthDp) * share);
        }

        // Not in the middle of the layout pass that told the width.
        handler.Post(titleBar.RequestLayout);
    }

    /// <summary>
    /// A label of the title bar on one line, as large as given, smaller where
    /// its room is narrower, never cut; and never larger than the keys' words
    /// (<see cref="SizeLabels"/>), so the bar never outweighs the keys.
    /// </summary>
    private void Shrinkable(TextView view, int largestSp)
    {
        view.SetMaxLines(1);
        view.SetAutoSizeTextTypeUniformWithConfiguration(4, largestSp, 1, (int)ComplexUnitType.Sp);
        titleTexts.Add((view, largestSp));
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

    private TextView KeyView(string label)
    {
        var view = new TextView(context)
        {
            Text = label,
            Gravity = GravityFlags.Center,
            Background = KeyBackground(KeyColor),
        };
        view.SetTextColor(Color.White);

        // One line at most, not a single line: a single line scrolls across a
        // width without end, so auto-size never finds a word too wide to fit
        // (it was cut instead of made smaller), and the scroll carries the
        // legends drawn over a character key out of the key.
        view.SetMaxLines(1);
        view.SetHorizontallyScrolling(false);
        view.SetIncludeFontPadding(false);
        view.SetPadding(0, 0, 0, 0);
        return view;
    }

    /// <summary>Sets the labels' size the user chose, kept across restarts, and sizes the character keys' labels to it.</summary>
    private void SetLabelScale(float value)
    {
        labelScale = Math.Clamp((float)Math.Round(value, 1), SmallestLabelScale, LargestLabelScale);
        preferences.Edit()!.PutFloat(PanelPlacement.Keyed(Mode, LabelScalePreference), labelScale)!.Apply();
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
            key.LayoutParameters!.Height = RowHeight - (2 * Dp(KeyMarginDp));
            key.RequestLayout();
        }

        // The title bar as tall as the keys make it, between its least
        // height, still enough to hold and move the window by, and its usual
        // one: a keyboard made small keeps a title bar near its size.
        foreach (var bar in new View[] { titleBar, closeBar })
        {
            bar.LayoutParameters!.Height = Dp(Math.Clamp(TitleBarDp * scale, LeastTitleBarDp, TitleBarDp));
            bar.RequestLayout();
        }

        SizeLabels();
        FitLegendsSoon();
    }

    /// <summary>Fits the legends once the keys are laid out, after whatever made them change size.</summary>
    private void FitLegendsSoon()
    {
        if (fitPending)
        {
            return;
        }

        fitPending = true;
        handler.Post(() =>
        {
            fitPending = false;
            FitLegends();
        });
    }

    /// <summary>
    /// The three legends every key's size is measured with, as a key of the
    /// digits' row has them, in the key's height and its width up to that
    /// height: the one size is what every key in sight could hold them at,
    /// so the strip's keys (Ctrl+A, Home…), which hold one word, are labelled
    /// as large as the whole keyboard's keys of their height, not as large as
    /// the key.
    /// </summary>
    private static readonly IReadOnlyList<KeyLegends.Legend> MeasuringLegends =
    [
        new("\"", KeyLegends.Shifted, Sent: false),
        new("2", KeyLegends.Plain, Sent: true),
        new("@", KeyLegends.AltGr, Sent: false),
    ];

    /// <summary>
    /// The one legend size: the largest at which every key in sight could
    /// hold three legends (<see cref="MeasuringLegends"/>), and every
    /// character key its own, whole and apart, a letter's capital too; it
    /// grows as the keys do, by a corner, a wider screen or the whole keyboard
    /// left for the strip, and shrinks with them.
    /// </summary>
    private void FitLegends()
    {
        var shown = new List<(IReadOnlyList<KeyLegends.Legend> Legends, RectF Room)>();
        var inset = Dp(KeyPaddingDp);
        foreach (var view in characterKeys.Select(key => key.View).Concat(labelKeys.Select(key => key.View)))
        {
            if (view.IsShown && view.Width > 0 && view.Height > 0)
            {
                // The key's own height, and its width up to that height: a
                // wide key (the strip's Ctrl+A) would hold three legends far
                // larger across its width, and its word would fill it; a
                // narrow, tall one (a phone held upright) has its height.
                var across = Math.Min(view.Width, view.Height) - (2 * inset);
                shown.Add((MeasuringLegends, new RectF(inset, inset, inset + across, view.Height - inset)));
            }
        }

        foreach (var (key, legends, view) in characterKeys)
        {
            if (!view.IsShown || view.Width == 0 || view.Height == 0)
            {
                continue;
            }

            var room = legends.Room(view.Width, view.Height);
            shown.Add((Legends(key, Held.None), room));
            if (key.IsLetter)
            {
                shown.Add((Legends(key, Held.Shift), room));
            }
        }

        if (shown.Count == 0)
        {
            return;
        }

        legendSize = KeyLegends.Fitting(shown, Dp(LegendGapDp), shown.Max(key => key.Room.Height()));
        SizeLabels();
    }

    /// <summary>
    /// Every label at the legends' size, as A− and A+ make it: the character
    /// keys' legends, and Ctrl, Mayús, Entrar and the like as large, an arrow
    /// larger, and smaller where the word does not fit its key: smaller,
    /// never cut. A− and A+ size them all alike, so they keep their proportion.
    /// </summary>
    private void SizeLabels()
    {
        if (legendSize <= 0)
        {
            return;
        }

        foreach (var (_, legends, _) in characterKeys)
        {
            legends.TextSize = legendSize * labelScale;
            legends.BaseSize = legendSize;
            legends.InvalidateSelf();
        }

        // The words (Esc, Ctrl+A, Home…) at one size: the largest at which
        // every word in sight fits WordShare of its key, never larger than the
        // legends, and never under WordFloor of them, for a long word in a
        // narrow key (ImpPnt) would take every other down with it; such a word
        // alone is made smaller, never cut. An arrow is drawn larger.
        var legendsSize = legendSize * labelScale;
        var layouts = labelKeys.Where(key => key.View.IsShown && key.View.Width > 0)
            .ToDictionary(key => key.View, key => key.Factor == 1f ? WordLayout(key.View, key.Label) : (Text: key.View.Text ?? "", Fit: WordFit(key.View, key.View.Text ?? "")));
        var words = labelKeys.Where(key => key.Factor == 1f && layouts.ContainsKey(key.View)).Select(key => layouts[key.View].Fit).DefaultIfEmpty(legendsSize);
        // Never larger than the letters are drawn: A+ grows the words as far
        // as it grows the letters, which stop where their keys stop them.
        var letters = characterKeys.Where(key => key.View.IsShown && key.View.Width > 0)
            .Select(key => key.Legends.LetterSize(key.View.Width, key.View.Height)).DefaultIfEmpty(legendsSize).Min();
        var common = Math.Clamp(words.Min(), Math.Min(letters, legendsSize) * WordFloor, Math.Min(letters, legendsSize));
        foreach (var (view, factor, _) in labelKeys)
        {
            var side = Math.Max(Dp(KeyPaddingDp), (int)(view.Width * (1 - WordShare) / 2));
            view.SetPadding(side, Dp(KeyPaddingDp), side, Dp(KeyPaddingDp));
            var size = factor == 1f ? common : legendsSize * factor;
            var fits = layouts.TryGetValue(view, out var layout);
            if (fits && factor == 1f && view.Text != layout.Text)
            {
                view.Text = layout.Text;
                view.SetMaxLines(layout.Text.Contains('\n') ? 2 : 1);
            }

            var largest = Math.Max(2, (int)(fits ? Math.Min(size, layout.Fit) : size));
            view.SetAutoSizeTextTypeUniformWithConfiguration(1, largest, 1, (int)ComplexUnitType.Px);
            view.RequestLayout();
        }

        // The title bar's words and signs no larger than the keys' words:
        // the bar keeps the height to hold it by, its words the keys' proportion.
        foreach (var (view, largestSp) in titleTexts)
        {
            var largest = Math.Max(2, (int)Math.Min(TypedValue.ApplyDimension(ComplexUnitType.Sp, largestSp, context.Resources!.DisplayMetrics), common));
            view.SetAutoSizeTextTypeUniformWithConfiguration(1, largest, 1, (int)ComplexUnitType.Px);
        }
    }

    /// <summary>
    /// How a key's word is laid out, and the largest size it fits at: on one
    /// line, or, where it can be broken (Ctrl+A after its +, Bloq May at its
    /// space) and two lines fit it larger, as a tall, narrow key does, on two.
    /// </summary>
    private (string Text, float Fit) WordLayout(TextView view, string label)
    {
        var one = (Text: label, Fit: WordFit(view, label));
        var plus = label.IndexOf('+');
        var space = label.IndexOf(' ');
        var broken = plus > 0 && plus < label.Length - 1 ? label[..(plus + 1)] + "\n" + label[(plus + 1)..]
            : space > 0 && space < label.Length - 1 ? label[..space] + "\n" + label[(space + 1)..]
            : null;
        if (broken is null)
        {
            return one;
        }

        var two = (Text: broken, Fit: WordFit(view, broken));
        return two.Fit > one.Fit ? two : one;
    }

    /// <summary>The largest size, in pixels, at which a key's word, on its one or two lines, fits WordShare of its width and the room of its height.</summary>
    private float WordFit(TextView view, string text)
    {
        using var paint = new Paint(view.Paint) { TextSize = 100f };
        var lines = text.Split('\n');
        var widest = lines.Max(line => paint.MeasureText(line));
        var across = text.Length == 0 ? float.MaxValue : view.Width * WordShare * 100f / Math.Max(1f, widest);
        var metrics = paint.GetFontMetrics()!;
        var down = (view.Height - (2 * Dp(KeyPaddingDp))) * 100f / ((metrics.Descent - metrics.Ascent) * lines.Length);
        return Math.Min(across, down);
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
        var margin = Dp(KeyMarginDp);
        layout.SetMargins(margin, margin, margin, margin);
        return layout;
    }

    private int Dp(float value) => (int)(value * context.Resources!.DisplayMetrics!.Density + 0.5f);
}
