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
/// the popup's service's (KeyPlusService): the title bar, the icon and the grips only
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
    private const string CompactPreference = "compact";
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

    /// <summary>The copied things' cards, the menu held on one and the editor: a ground a little lighter than the window's.</summary>
    private static readonly Color CardColor = Color.Argb(0xFF, 0x2C, 0x2C, 0x2C);

    /// <summary>The copied things', their menu's and the editor's words, in sp: a list's, not the keys'.</summary>
    private const float CardTextSp = 15f;
    private const float MenuTextSp = 16f;

    /// <summary>A key's legend it sends now, strong, and the ones it does not, weak.</summary>
    private static readonly Color SentLegendColor = Color.White;
    private static readonly Color OtherLegendColor = Color.Argb(0xFF, 0xA8, 0xA8, 0xA8);

    private readonly Context context;
    private readonly Action<Keycode, MetaKeyStates> sendOut;
    private readonly Action<string> typeOut;
    private readonly ISharedPreferences preferences;
    private readonly Handler handler = new(Looper.MainLooper!);
    private readonly int touchSlop;
    private readonly List<(Held Modifier, TextView View)> modifierKeys = [];
    /// <summary>The keys that type characters, whose legends are drawn on the table of three by three.</summary>
    private readonly List<(PanelKey Key, KeyLegends Legends, TextView View)> characterKeys = [];

    /// <summary>The keys that type no character but read another label with Fn held: the arrows.</summary>
    /// <summary>The keys that type no character but read another label with Fn or Shift held: the arrows, ⌫.</summary>
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

    /// <summary>The whole keyboard's column at its right (ImpPnt, Inicio, Fin, RePág, AvPág).</summary>
    private View? sideColumn;
    private readonly FrameLayout root;
    private readonly LinearLayout strip;
    private readonly LinearLayout terminal;
    private readonly LinearLayout wholeKeyboard;

    /// <summary>Keys made small: two rows, no title bar.</summary>
    private readonly LinearLayout compact;
    private readonly TextView makeSmall;

    /// <summary>The grips at the four corners, and the bar along the bottom that holds two of them.</summary>
    private readonly List<View> grips = [];
    private readonly LinearLayout bottomBar;
    private readonly TextView tab;
    private readonly LinearLayout titleBar;
    private readonly LinearLayout closeBar;

    /// <summary>The question shown in place of the title bar, and what Sí does.</summary>
    private readonly TextView question;
    private Action? asked;
    private bool iconOnly;
    private readonly TextView dock;
    private readonly TextView switchKeys;

    /// <summary>The texts copied lately, and 📋, which shows them in the keys' place to paste one.</summary>
    private readonly ClipboardHistory clipboard;
    private readonly TextView clipboardButton;
    private readonly ScrollView clipboardView;
    private readonly LinearLayout clipboardRows;

    /// <summary>The copied texts side by side in one row above the keys (<see cref="ClipboardAbove"/>).</summary>
    private readonly HorizontalScrollView clipboardLine;
    private readonly LinearLayout clipboardItems;

    /// <summary>The keys a view stands in place of (the copied texts, the settings), with the button that shows it; none while the keys show.</summary>
    private (View Keys, View Cover, TextView Button)? covered;

    /// <summary>
    /// The editor above the keys for a copied text, Cancelar and Guardar under
    /// it, and the text edited: the keys type into it meanwhile, not into the
    /// field; none, no editor.
    /// </summary>
    private readonly LinearLayout editorBar;
    private readonly TextView editorTitle;
    private readonly LinearLayout editorButtons;
    private readonly View editorGap;
    private readonly EditText editor;
    private ClipboardHistory.Entry? edited;

    /// <summary>✕, which closes the popup once asked; a keyboard has none (<see cref="Settings"/>).</summary>
    private readonly TextView closeButton;

    /// <summary>⚙, and the settings it shows in the keys' place, each a row that does what it says.</summary>
    private readonly TextView settingsButton;
    private readonly ScrollView settingsView;
    private readonly LinearLayout settingsRows;
    private IReadOnlyList<(string Label, Action Chosen)> settings = [];

    /// <summary>The height the copied texts keep above the keys all the time (<see cref="ClipboardAbove"/>); 0, none.</summary>
    private int clipboardAbove;
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
    /// <param name="clipboard">The texts copied lately, which 📋 lists.</param>
    public KeyPanel(Context context, ISharedPreferences preferences, ClipboardHistory clipboard, Action<Keycode, MetaKeyStates> send, Action<string> type)
    {
        this.context = context;
        this.clipboard = clipboard;
        sendOut = send;
        typeOut = type;
        this.preferences = preferences;
        touchSlop = ViewConfiguration.Get(context)!.ScaledTouchSlop;

        // No name in the title bar, so its buttons have the room: what is
        // left between them is the place to drag the window by.
        var title = new View(context) { ContentDescription = "Drag to move the keys" };
        // The bar's free room is dragged at once, as a window's title is: a
        // headset's pinch (Galaxy XR) never holds still enough for a long press.
        Draggable(title, (drag, dx, dy) => Moving?.Invoke(drag, dx, dy), tapped: null);

        dock = TitleButton("⤓", "Docked: the keys show while a keyboard is open, where you left them; pressed again, they float, in sight all the time", () => DockRequested?.Invoke());
        var minimize = TitleButton("–", "Minimize the keys into an icon", () => SetFolded(true));
        closeButton = TitleButton("✕", "Close KeyPlus", () => Ask("¿Cerrar KeyPlus?", () => CloseRequested?.Invoke()));
        var smaller = TitleButton("A−", "Smaller labels", () => SetLabelScale(labelScale - LabelScaleStep));
        var larger = TitleButton("A+", "Larger labels", () => SetLabelScale(labelScale + LabelScaleStep));
        // A↺ undoes every size made by hand, so it asks first, as ✕ does.
        var usual = TitleButton("A↺", "Keys and labels at their usual size", () => Ask("¿Tamaño normal?", () =>
        {
            SetLabelScale(1f);
            UsualSizeRequested?.Invoke();
        }));
        switchKeys = TitleButton("ABC", "Switch between the keys and a whole keyboard", () => SetWholeKeyboard(wholeKeyboard!.Visibility != ViewStates.Visible));
        clipboardButton = TitleButton("📋", "Texts copied lately: tap one to paste it", () => Toggle(clipboardView!, ShowClipboard));
        settingsButton = TitleButton("⚙", "Settings", () => Toggle(settingsView!, ShowSettings));
        settingsButton.Visibility = ViewStates.Gone;
        makeSmall = TitleButton("▭", "Two rows: the keys made small, with no title bar; hold PgUp to open them again", () => SetCompact(true));

        titleBar = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        AddToTitleBar(Grip("◤", Corner.TopLeft), 24);
        titleBar.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        AddToTitleBar(smaller, 36);
        AddToTitleBar(larger, 36);
        AddToTitleBar(usual, 36);
        AddToTitleBar(makeSmall, 36);
        AddToTitleBar(clipboardButton, 36);
        AddToTitleBar(switchKeys, 44);
        AddToTitleBar(dock, 36);
        AddToTitleBar(minimize, 36);
        AddToTitleBar(closeButton, 36);
        AddToTitleBar(settingsButton, 36);

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

        bottomBar = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        bottomBar.AddView(Grip("◣", Corner.BottomLeft), new LinearLayout.LayoutParams(Dp(24), ViewGroup.LayoutParams.MatchParent));
        bottomBar.AddView(new View(context), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        bottomBar.AddView(Grip("◢", Corner.BottomRight), new LinearLayout.LayoutParams(Dp(24), ViewGroup.LayoutParams.MatchParent));

        strip = new LinearLayout(context) { Orientation = Orientation.Vertical };
        strip.SetBackgroundColor(PanelColor);
        strip.SetPadding(Dp(KeyMarginDp), 0, Dp(KeyMarginDp), 0);
        strip.AddView(titleBar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(TitleBarDp)));
        strip.AddView(closeBar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(TitleBarDp)));
        clipboardRows = new LinearLayout(context) { Orientation = Orientation.Vertical };
        clipboardView = new ScrollView(context) { Visibility = ViewStates.Gone };
        clipboardView.AddView(clipboardRows);
        strip.AddView(clipboardView, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0));
        clipboardItems = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        clipboardLine = new HorizontalScrollView(context) { Visibility = ViewStates.Gone };
        clipboardLine.AddView(clipboardItems, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent));
        strip.AddView(clipboardLine, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0));
        // The editor as Gboard's: a card with its title, the text over a line,
        // and Cancelar and Guardar as words at its bottom right.
        editorTitle = new TextView(context) { Text = "Editar" };
        editorTitle.SetTextColor(Color.White);
        editorTitle.SetTextSize(ComplexUnitType.Sp, MenuTextSp);
        editor = new EditText(context) { ShowSoftInputOnFocus = false, ContentDescription = "The copied text, to edit" };
        editor.SetTextColor(Color.White);
        editor.SetTextSize(ComplexUnitType.Sp, MenuTextSp);
        editor.BackgroundTintList = ColorStateList.ValueOf(HeldColor);
        editor.SetMaxLines(3);
        editorButtons = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        editorGap = new View(context);
        editorButtons.AddView(editorGap, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        editorButtons.AddView(WordButton("Cancelar", "Leave the copied text as it was", DimTextColor, () => EndEditing(save: false)));
        editorButtons.AddView(WordButton("Guardar", "Keep the copied text as edited", HeldColor, () => EndEditing(save: true)));
        editorBar = new LinearLayout(context) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone, Background = Card() };
        editorBar.SetPadding(Dp(12), Dp(8), Dp(12), Dp(4));
        editorBar.AddView(editorTitle);
        editorBar.AddView(editor, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        editorBar.AddView(editorButtons, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        strip.AddView(editorBar, CardMargins(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)));
        settingsRows = new LinearLayout(context) { Orientation = Orientation.Vertical };
        settingsView = new ScrollView(context) { Visibility = ViewStates.Gone };
        settingsView.AddView(settingsRows);
        strip.AddView(settingsView, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0));
        clipboard.Changed += RefillClipboard;
        terminal = Rows(PanelKeys.Terminal);
        wholeKeyboard = Rows(PanelKeys.Spanish, PanelKeys.SpanishSide);
        compact = Rows(PanelKeys.Compact);
        strip.AddView(terminal);
        strip.AddView(wholeKeyboard);
        strip.AddView(compact);

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
        ShowMode(preferences.GetBoolean(WholeKeyboardPreference, false), preferences.GetBoolean(CompactPreference, false));
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
    public string Mode =>
        ShownKeys == wholeKeyboard ? PanelPlacement.WholeKeyboardMode
        : ShownKeys == compact ? PanelPlacement.CompactMode
        : PanelPlacement.KeysMode;

    /// <summary>The keys of the mode the window is in, in sight or under the copied texts.</summary>
    private View ShownKeys =>
        covered?.Keys
        ?? (wholeKeyboard.Visibility == ViewStates.Visible ? wholeKeyboard
            : compact.Visibility == ViewStates.Visible ? compact
            : terminal);

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

    /// <summary>
    /// Whether ⤓ is in the title bar: not in Berpiztu AI Keyboard
    /// (KeyPlusKeyboard), which stands where Android puts it, with no other
    /// keyboard to dock above.
    /// </summary>
    public bool Dockable
    {
        set
        {
            dock.Visibility = value ? ViewStates.Visible : ViewStates.Gone;

            // Not laid out yet, the bar is fitted once it is.
            if (titleBar.Width > 0)
            {
                FitTitleBar(titleBar.Width);
            }
        }
    }

    /// <summary>
    /// Whether the corners' grips resize the window: not where the keys fill
    /// the room they are given (Berpiztu AI Keyboard on a headset), which
    /// sets their size itself.
    /// </summary>
    public bool Resizable
    {
        set
        {
            foreach (var grip in grips)
            {
                grip.Visibility = value ? ViewStates.Visible : ViewStates.Gone;
            }

            bottomBar.Visibility = value ? ViewStates.Visible : ViewStates.Gone;
            if (titleBar.Width > 0)
            {
                FitTitleBar(titleBar.Width);
            }
        }
    }

    /// <summary>
    /// Whether the whole keyboard has its column at the right: not where its
    /// width is short (Berpiztu AI Keyboard on a headset), so the other keys
    /// are wider; Fn and the arrows are Inicio, Fin, RePág and AvPág there.
    /// </summary>
    public bool SideKeys
    {
        set => sideColumn!.Visibility = value ? ViewStates.Visible : ViewStates.Gone;
    }

    /// <summary>The height of the keys in sight, which grows with <see cref="Scale"/> alone; none while the window is its icon or 📋 shows the copied texts.</summary>
    public int KeysHeight => ShowsIcon || covered is not null ? 0 : ShownKeys.Height;

    /// <summary>
    /// The height of a row of the copied texts, side by side, above the keys
    /// and under the title bar, all the time, where a keyboard has room to
    /// spare (Berpiztu AI Keyboard on a headset); 📋 still shows them all in
    /// the keys' place, with their options, as Gboard does. 0, none.
    /// </summary>
    public int ClipboardAbove
    {
        get => clipboardAbove;
        set
        {
            if (value == clipboardAbove)
            {
                return;
            }

            Uncover();
            clipboardAbove = value;
            clipboardLine.LayoutParameters!.Height = value;
            clipboardLine.Visibility = value > 0 ? ViewStates.Visible : ViewStates.Gone;
            if (value > 0)
            {
                FillClipboardLine();
            }

            clipboardLine.RequestLayout();
            if (titleBar.Width > 0)
            {
                FitTitleBar(titleBar.Width);
            }
        }
    }

    /// <summary>Pastes an image copied into the field, where the panel can (Berpiztu AI Keyboard); none, the images are listed but not pasted.</summary>
    public Action<ClipboardHistory.Entry>? PasteImage { get; set; }

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

    /// <summary>
    /// The labels fitted to the keys again, once they are in sight: a
    /// keyboard's keys may be laid out while it is out of sight, and come
    /// into sight at the same size, which no layout change says.
    /// </summary>
    public void FitLabels() => FitLegendsSoon();

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
        // Made small, the window has no title bar, nor any question in its place.
        var small = compact.Visibility == ViewStates.Visible;
        titleBar.Visibility = text is null && !small ? ViewStates.Visible : ViewStates.Gone;
        closeBar.Visibility = text is not null && !small ? ViewStates.Visible : ViewStates.Gone;
    }

    private void SetFolded(bool folded)
    {
        ShowFolded(folded);
        preferences.Edit()!.PutBoolean(FoldedPreference, folded)!.Apply();
        FoldedChanged?.Invoke();
    }

    private void SetWholeKeyboard(bool whole)
    {
        ShowMode(whole, small: false);
        preferences.Edit()!.PutBoolean(WholeKeyboardPreference, whole)!.Apply();
        ModeChanged?.Invoke();
    }

    /// <summary>Keys made small (two rows, no title bar), or opened again in full, kept, and the mode's place and size taken.</summary>
    private void SetCompact(bool small)
    {
        ShowMode(whole: false, small);
        preferences.Edit()!.PutBoolean(CompactPreference, small)!.Apply();
        ModeChanged?.Invoke();
    }

    /// <summary>The whole keyboard, Keys made small, or Keys in full, with the title bar where it has one.</summary>
    private void ShowMode(bool whole, bool small)
    {
        StopRepeat();
        Uncover();
        small &= !whole;
        terminal.Visibility = !whole && !small ? ViewStates.Visible : ViewStates.Gone;
        wholeKeyboard.Visibility = whole ? ViewStates.Visible : ViewStates.Gone;
        compact.Visibility = small ? ViewStates.Visible : ViewStates.Gone;
        makeSmall.Visibility = whole ? ViewStates.Gone : ViewStates.Visible;
        Ask(null, null);

        // Each mode keeps its own labels' size, as it keeps its own place
        // and keys' size; where it has none yet, the one kept before.
        labelScale = preferences.GetFloat(PanelPlacement.Keyed(Mode, LabelScalePreference), preferences.GetFloat(LabelScalePreference, 1f));
        SizeLabels();
        FitLegendsSoon();
        switchKeys.Text = whole ? "Keys" : "ABC";
    }

    /// <summary>
    /// The settings ⚙ shows in the keys' place, each a row that does what it
    /// says (Berpiztu AI Keyboard: choose another keyboard); ⚙ stands at the
    /// right in ✕'s place, for a keyboard is not closed but changed for
    /// another. None, no ⚙, and ✕ closes the popup.
    /// </summary>
    public IReadOnlyList<(string Label, Action Chosen)> Settings
    {
        set
        {
            settings = value;
            settingsButton.Visibility = value.Count > 0 ? ViewStates.Visible : ViewStates.Gone;
            closeButton.Visibility = value.Count > 0 ? ViewStates.Gone : ViewStates.Visible;
            if (titleBar.Width > 0)
            {
                FitTitleBar(titleBar.Width);
            }
        }
    }

    /// <summary>A title bar's button that shows a view in the keys' place: pressed again, or with another one showing, the keys again.</summary>
    private void Toggle(View cover, Action show)
    {
        var showing = covered?.Cover == cover;
        Uncover();
        if (!showing)
        {
            show();
        }
    }

    /// <summary>
    /// A view in the keys' place, as tall as the keys were, so the window
    /// keeps its size; its button filled while it shows, as a held key is.
    /// </summary>
    private void Cover(View cover, TextView button)
    {
        Uncover();
        var keys = ShownKeys;
        cover.LayoutParameters!.Height = Math.Max(keys.Height, RowHeight);
        keys.Visibility = ViewStates.Gone;
        cover.Visibility = ViewStates.Visible;
        covered = (keys, cover, button);
        button.Background = KeyBackground(HeldColor);
        button.SetTextColor(HeldTextColor);
    }

    /// <summary>The keys again, in place of the copied texts or the settings.</summary>
    private void Uncover()
    {
        if (covered is not { } now)
        {
            return;
        }

        now.Keys.Visibility = ViewStates.Visible;
        now.Cover.Visibility = ViewStates.Gone;
        now.Cover.LayoutParameters!.Height = 0;
        now.Button.Background = new RippleDrawable(ColorStateList.ValueOf(RippleColor), null, new ColorDrawable(Color.White));
        now.Button.SetTextColor(Color.White);
        covered = null;
    }

    /// <summary>The copied things in the keys' place, a list that scrolls.</summary>
    private void ShowClipboard()
    {
        FillClipboard();
        clipboardView.ScrollTo(0, 0);
        Cover(clipboardView, clipboardButton);
    }

    /// <summary>The settings in the keys' place, one row each: a tap does it and brings the keys back.</summary>
    private void ShowSettings()
    {
        settingsRows.RemoveAllViews();
        foreach (var (label, chosen) in settings)
        {
            var row = KeyView(label);
            row.Gravity = GravityFlags.CenterVertical | GravityFlags.Left;
            row.SetPadding(Dp(12), 0, Dp(12), 0);
            row.SetTextSize(ComplexUnitType.Px, RowHeight * 0.4f);
            row.Click += (_, _) =>
            {
                Uncover();
                chosen();
            };
            settingsRows.AddView(row, KeyMargins(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, RowHeight - (2 * Dp(KeyMarginDp)))));
        }

        Cover(settingsView, settingsButton);
    }

    /// <summary>
    /// What was copied as Gboard shows it: cards in two columns, the pinned
    /// first, then the newest, a text on up to three lines at a list's size,
    /// an image cut to its card. A tap pastes it and brings the keys back; a
    /// long press opens its menu beside it.
    /// </summary>
    private void FillClipboard()
    {
        clipboardRows.RemoveAllViews();
        var entries = clipboard.Entries;
        if (entries.Count == 0)
        {
            clipboardRows.AddView(NothingCopied(), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, RowHeight));
            return;
        }

        const int Columns = 2;
        for (var first = 0; first < entries.Count; first += Columns)
        {
            var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
            for (var column = 0; column < Columns; column++)
            {
                if (first + column >= entries.Count)
                {
                    row.AddView(new View(context), new LinearLayout.LayoutParams(0, 1, 1f));
                    continue;
                }

                var entry = entries[first + column];
                var card = CopiedCard(entry, maxLines: 3, imageHeight: Dp(96), () =>
                {
                    Paste(entry);
                    Uncover();
                });
                row.AddView(card, CardMargins(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)));
            }

            clipboardRows.AddView(row);
        }
    }

    /// <summary>
    /// What was copied in one row above the keys, the pinned first, then the
    /// newest, at the left: cards as in the list, a text on up to two lines
    /// and up to a third of the row wide, each with a small ✏ (a text) over
    /// a small ✕ beside it, to edit it right there or take it out. A tap
    /// pastes it, a long press opens its menu; the row scrolls across.
    /// </summary>
    private void FillClipboardLine()
    {
        clipboardItems.RemoveAllViews();
        var height = clipboardAbove;
        var entries = clipboard.Entries;
        if (entries.Count == 0)
        {
            clipboardItems.AddView(NothingCopied(), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, height));
            return;
        }

        foreach (var entry in entries)
        {
            var card = CopiedCard(entry, maxLines: 2, imageHeight: ViewGroup.LayoutParams.MatchParent, () => Paste(entry));
            if (card is TextView text)
            {
                text.SetMaxWidth(Math.Max(height, root.Width / 3));
            }

            clipboardItems.AddView(card, CardMargins(new LinearLayout.LayoutParams(entry.IsImage ? height * 3 / 2 : ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent)));
            clipboardItems.AddView(EditOrRemove(entry), new LinearLayout.LayoutParams(Dp(28), ViewGroup.LayoutParams.MatchParent));
        }
    }

    /// <summary>A thing copied's small ✏ (a text) over its small ✕, beside its card in the row above the keys.</summary>
    private LinearLayout EditOrRemove(ClipboardHistory.Entry entry)
    {
        var column = new LinearLayout(context) { Orientation = Orientation.Vertical };
        if (!entry.IsImage)
        {
            column.AddView(WordButton("✏", "Edit it right here", DimTextColor, () => Edit(entry)), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
        }

        column.AddView(WordButton("✕", "Take it out of the list", DimTextColor, () => clipboard.Remove(entry)), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
        return column;
    }

    /// <summary>The copied things again, wherever they show, after a change.</summary>
    private void RefillClipboard()
    {
        if (clipboardView.Visibility == ViewStates.Visible)
        {
            FillClipboard();
        }

        if (clipboardLine.Visibility == ViewStates.Visible)
        {
            FillClipboardLine();
        }
    }

    private TextView NothingCopied()
    {
        var none = new TextView(context) { Text = "Nothing copied yet", Gravity = GravityFlags.CenterVertical };
        none.SetTextColor(DimTextColor);
        none.SetTextSize(ComplexUnitType.Sp, CardTextSp);
        none.SetPadding(Dp(12), 0, Dp(12), 0);
        return none;
    }

    /// <summary>
    /// A thing copied as a card that pastes it: its text at a list's size on
    /// up to so many lines, 📌 before it when pinned, or the image cut to the
    /// card. Held, its menu opens beside it.
    /// </summary>
    private View CopiedCard(ClipboardHistory.Entry entry, int maxLines, int imageHeight, Action pasted)
    {
        View view;
        if (entry.IsImage)
        {
            var image = new ImageView(context) { Background = Card(), ClipToOutline = true, ContentDescription = "Paste the image" };
            image.SetScaleType(ImageView.ScaleType.CenterCrop);
            image.SetImageBitmap(Thumbnail(clipboard.PathOf(entry), imageHeight > 0 ? imageHeight : RowHeight));
            image.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, imageHeight);
            view = image;
        }
        else
        {
            var text = new TextView(context)
            {
                Text = (entry.Pinned ? "📌 " : "") + entry.Text,
                Background = Card(),
                Gravity = GravityFlags.Top | GravityFlags.Left,
                Ellipsize = Android.Text.TextUtils.TruncateAt.End,
                ContentDescription = $"Paste: {entry.Text}",
            };
            text.SetTextColor(Color.White);
            text.SetTextSize(ComplexUnitType.Sp, CardTextSp);
            text.SetMaxLines(maxLines);
            text.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
            text.SetMinHeight(Dp(48));
            view = text;
        }

        view.Click += (_, _) => pasted();
        view.LongClick += (_, e) =>
        {
            e.Handled = true;
            ShowMenu(view, entry, pasted);
        };
        return view;
    }

    /// <summary>
    /// A thing copied's menu, as Gboard has it: a card beside it with Pegar,
    /// Fijar or Desfijar, Editar (a text) and Eliminar, each with its sign; a
    /// touch outside it closes it. It never takes the focus, so the field
    /// keeps it.
    /// </summary>
    private void ShowMenu(View anchor, ClipboardHistory.Entry entry, Action pasted)
    {
        var menu = new LinearLayout(context) { Orientation = Orientation.Vertical, Background = Card() };
        menu.SetPadding(0, Dp(4), 0, Dp(4));
        var popup = new PopupWindow(menu, ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, false)
        {
            OutsideTouchable = true,
            Elevation = Dp(8),
        };
        popup.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));

        void Item(string sign, string label, Action chosen)
        {
            var item = new TextView(context)
            {
                Text = $"{sign}   {label}",
                Gravity = GravityFlags.CenterVertical,
                Background = new RippleDrawable(ColorStateList.ValueOf(RippleColor), null, new ColorDrawable(Color.White)),
            };
            item.SetTextColor(Color.White);
            item.SetTextSize(ComplexUnitType.Sp, MenuTextSp);
            item.SetPadding(Dp(16), Dp(12), Dp(24), Dp(12));
            item.SetMinWidth(Dp(176));
            item.Click += (_, _) =>
            {
                popup.Dismiss();
                chosen();
            };
            menu.AddView(item);
        }

        Item("📋", "Pegar", pasted);
        Item("📌", entry.Pinned ? "Desfijar" : "Fijar", () => clipboard.SetPinned(entry, !entry.Pinned));
        if (!entry.IsImage)
        {
            Item("✏", "Editar", () => Edit(entry));
        }

        Item("🗑", "Eliminar", () => clipboard.Remove(entry));
        // Within the keyboard, which would cut it: beside the card if there
        // is room at its right, else at its left, at the card's height, kept
        // between the keyboard's top and bottom.
        menu.Measure(View.MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified), View.MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified));
        var card = new int[2];
        var keyboard = new int[2];
        anchor.GetLocationInWindow(card);
        root.GetLocationInWindow(keyboard);
        var right = card[0] + anchor.Width - Dp(8);
        var x = right + menu.MeasuredWidth <= keyboard[0] + root.Width ? right : Math.Max(keyboard[0], card[0] + Dp(8) - menu.MeasuredWidth);
        var y = Math.Clamp(card[1] + ((anchor.Height - menu.MeasuredHeight) / 2), keyboard[1], Math.Max(keyboard[1], keyboard[1] + root.Height - menu.MeasuredHeight));
        popup.ShowAtLocation(root, GravityFlags.NoGravity, x, y);
    }

    /// <summary>A button that is a word or a sign alone, as a dialog's are: Cancelar, Guardar, ✏, ✕.</summary>
    private TextView WordButton(string label, string description, Color color, Action pressed)
    {
        var button = new TextView(context)
        {
            Text = label,
            Gravity = GravityFlags.Center,
            ContentDescription = description,
            Background = new RippleDrawable(ColorStateList.ValueOf(RippleColor), null, new ColorDrawable(Color.White)),
        };
        button.SetTextColor(color);
        button.SetTextSize(ComplexUnitType.Sp, CardTextSp);
        button.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        button.Click += (_, _) => pressed();
        return button;
    }

    /// <summary>A card's ground: rounded, a little lighter than the window's.</summary>
    private GradientDrawable Card()
    {
        var shape = new GradientDrawable();
        shape.SetColor(CardColor);
        shape.SetCornerRadius(Dp(10));
        return shape;
    }

    /// <summary>The room around a card, so cards stand apart.</summary>
    private LinearLayout.LayoutParams CardMargins(LinearLayout.LayoutParams layout)
    {
        var margin = Dp(4);
        layout.SetMargins(margin, margin, margin, margin);
        return layout;
    }

    /// <summary>
    /// A copied text in the editor, the keys in sight to edit it: they type
    /// into the editor, not into the field, until Cancelar or Guardar. Where
    /// the copied things have a row above the keys (<see cref="ClipboardAbove"/>)
    /// the editor stands in that row, Cancelar and Guardar at its right, so
    /// nothing else moves; else as a card above the keys, as Gboard's.
    /// </summary>
    private void Edit(ClipboardHistory.Entry entry)
    {
        Uncover();
        edited = entry;
        editor.Text = entry.Text;
        editor.SetSelection(editor.Text?.Length ?? 0);
        var inRow = clipboardAbove > 0;
        editorBar.Orientation = inRow ? Orientation.Horizontal : Orientation.Vertical;
        editorBar.LayoutParameters = CardMargins(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, inRow ? clipboardAbove - Dp(8) : ViewGroup.LayoutParams.WrapContent));
        editorTitle.Visibility = inRow ? ViewStates.Gone : ViewStates.Visible;
        editor.LayoutParameters = inRow
            ? new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f)
            : new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        editorButtons.LayoutParameters = inRow
            ? new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent)
            : new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        editorGap.Visibility = inRow ? ViewStates.Gone : ViewStates.Visible;
        clipboardLine.Visibility = inRow ? ViewStates.Gone : clipboardLine.Visibility;
        editorBar.Visibility = ViewStates.Visible;
        editor.RequestFocus();
    }

    /// <summary>The editor closed: the text edited kept in its place in the list (Guardar), or left as it was (Cancelar); the keys type into the field again.</summary>
    private void EndEditing(bool save)
    {
        if (edited is null)
        {
            return;
        }

        if (save)
        {
            clipboard.Replace(edited, editor.Text ?? "");
        }

        edited = null;
        editor.ClearFocus();
        editorBar.Visibility = ViewStates.Gone;
        if (clipboardAbove > 0)
        {
            clipboardLine.Visibility = ViewStates.Visible;
            FillClipboardLine();
        }
    }

    /// <summary>A key sent to the field, or, while a copied text is edited, to the editor, which moves, selects and deletes as a field does.</summary>
    private void Send(Keycode code, MetaKeyStates meta)
    {
        if (edited is null)
        {
            sendOut(code, meta);
            return;
        }

        KeySender.Send(key => editor.DispatchKeyEvent(key), code, meta);
    }

    /// <summary>Text typed into the field, or, while a copied text is edited, into the editor, in place of what is selected there.</summary>
    private void Type(string text)
    {
        if (edited is null)
        {
            typeOut(text);
            return;
        }

        var start = Math.Max(0, Math.Min(editor.SelectionStart, editor.SelectionEnd));
        var end = Math.Max(0, Math.Max(editor.SelectionStart, editor.SelectionEnd));
        editor.EditableText!.Replace(start, end, new Java.Lang.String(text));
    }

    /// <summary>An image read no larger than it is shown, a row's height, so twenty of them cost little memory.</summary>
    private static Bitmap? Thumbnail(string path, int height)
    {
        var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeFile(path, bounds);
        var sample = 1;
        while (bounds.OutHeight / (sample * 2) >= height)
        {
            sample *= 2;
        }

        return BitmapFactory.DecodeFile(path, new BitmapFactory.Options { InSampleSize = sample });
    }

    /// <summary>A thing copied pasted into the field: a text typed, an image handed to the field by <see cref="PasteImage"/>.</summary>
    private void Paste(ClipboardHistory.Entry entry)
    {
        if (entry.IsImage)
        {
            PasteImage?.Invoke(entry);
        }
        else
        {
            typeOut(entry.Text!);
        }
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
    public int RowHeight => Dp(56 * scale);

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
        sideColumn = column;
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
            if (key.FnLabel is not null || key.ShiftLabel is not null)
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
        // window is moved from its title bar. A key that opens the full
        // window (PgUp in two rows) acts on its release instead: held still a
        // moment, it opens the window, and sends nothing.
        Java.Lang.Runnable? opening = null;
        var opened = false;
        view.Touch += (_, e) =>
        {
            e.Handled = true;
            switch (e.Event!.ActionMasked)
            {
                case MotionEventActions.Down:
                    view.Pressed = true;
                    view.PerformHapticFeedback(FeedbackConstants.KeyboardTap);
                    if (key.OpensFull)
                    {
                        opened = false;
                        opening = new Java.Lang.Runnable(() =>
                        {
                            opened = true;
                            view.PerformHapticFeedback(FeedbackConstants.LongPress);
                            SetCompact(false);
                        });
                        handler.PostDelayed(opening, LongPressMs);
                    }
                    else
                    {
                        Press(key);
                    }

                    break;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    view.Pressed = false;
                    StopRepeat();
                    if (opening is not null)
                    {
                        handler.RemoveCallbacks(opening);
                        opening = null;
                        if (!opened && e.Event.ActionMasked == MotionEventActions.Up)
                        {
                            Press(key);
                        }
                    }

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
            var (x, y) = OnScreen(view, motion);
            (startX, startY, held, slid, corner) = (x, y, false, false, null);
            waiting = new Java.Lang.Runnable(Hold);
            panel.handler.PostDelayed(waiting, LongPressMs);
        }

        public void Track(MotionEvent motion)
        {
            var (x, y) = OnScreen(view, motion);
            var (dx, dy) = (x - startX, y - startY);
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
                var (x, y) = OnScreen(view, motion);
                Report(Drag.End, x - startX, y - startY);
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
                Send(key.Code, MetaKeyStates.None);
            }

            SetHeld(held ^ key.Modifier);
            return;
        }

        // With Shift held, a key that is another then (⌫ Supr) sends that one, without Shift.
        var shifted = held.HasFlag(Held.Shift) && key.ShiftCode is not null;
        var code = held.HasFlag(Held.Fn) && key.FnCode is { } fnCode ? fnCode
            : shifted ? key.ShiftCode!.Value
            : key.Code;
        if (code == key.Code && key.IsCharacter && ((held & KeyModifiers) == Held.None || key.Code == Keycode.Unknown))
        {
            TypeCharacter(key);
            return;
        }

        var meta = PanelKeys.MetaOf((shifted ? held & ~Held.Shift : held) | key.With);
        Send(code, meta);
        SetHeld(held & (Held.Shift | Held.CapsLock | Held.Fn));
        if (key.RepeatsWhenHeld)
        {
            StartRepeat(() => Send(code, meta));
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
            Type(PanelKeys.Compose(accent, text));
        }
        else if (PanelKeys.IsDead(text))
        {
            deadAccent = text;
            return;
        }
        else
        {
            Type(text);
        }

        if (key.RepeatsWhenHeld)
        {
            StartRepeat(() => Type(text));
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
            view.Text = held.HasFlag(Held.Fn) && key.FnLabel is { } fnLabel ? fnLabel
                : held.HasFlag(Held.Shift) && key.ShiftLabel is { } shiftLabel ? shiftLabel
                : key.Label;
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
        var wanted = Dp(titleParts.Where(part => part.View.Visibility != ViewStates.Gone).Sum(part => part.WidthDp));
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
        grips.Add(view);
        return view;
    }

    /// <summary>
    /// Where a touch is on the screen: the view's place on it and the touch's
    /// place in the view. Not the event's raw place, which a headset (Galaxy
    /// XR) gives from the window: the window moved under it, the raw place
    /// moved back as much, and a dragged window shook where it was.
    /// </summary>
    private static (float X, float Y) OnScreen(View view, MotionEvent motion)
    {
        var location = new int[2];
        view.GetLocationOnScreen(location);
        return (location[0] + motion.GetX(), location[1] + motion.GetY());
    }

    /// <summary>
    /// Makes a view draggable: past the touch slop it reports the drag, in
    /// pixels from where it started; a touch that never got that far is a tap.
    /// </summary>
    private void Draggable(View view, Action<Drag, float, float> drag, Action? tapped)
    {
        var slop = touchSlop;
        float startX = 0, startY = 0;
        var dragging = false;
        view.Touch += (_, e) =>
        {
            e.Handled = true;
            var motion = e.Event!;
            var (x, y) = OnScreen(view, motion);
            var dx = x - startX;
            var dy = y - startY;
            switch (motion.ActionMasked)
            {
                case MotionEventActions.Down:
                    view.Pressed = true;
                    (startX, startY, dragging) = (x, y, false);
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
