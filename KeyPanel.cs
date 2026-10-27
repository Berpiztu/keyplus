using System.Globalization;
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
/// row and the terminal's two or the whole keyboard; the window resized with
/// the frame its resize lays over the keys (<see cref="KeyboardFrame"/>), or
/// a long press near a corner. Where the window stands is
/// the popup's service's (KeyPlusService): the title bar, the icon and the corners only
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
    internal const int RepeatDelayMs = 400;
    internal const int RepeatIntervalMs = 50;

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

    /// <summary>A swipe's trail's width where the finger is, in dp: Gboard's line.</summary>
    private const int SwipeTrailDp = 5;

    // Opaque, the keys well apart from the ground: the window looks the same
    // over a white screen as over a black one.
    internal static readonly Color PanelColor = Color.Argb(0xFF, 0x12, 0x12, 0x12);
    internal static readonly Color KeyColor = Color.Argb(0xFF, 0x42, 0x42, 0x42);
    internal static readonly Color HeldColor = Color.Argb(0xFF, 0x4F, 0xC3, 0xF7);
    internal static readonly Color HeldTextColor = Color.Argb(0xFF, 0x10, 0x10, 0x10);
    internal static readonly Color RippleColor = Color.Argb(0x55, 0xFF, 0xFF, 0xFF);
    private static readonly Color DimTextColor = Color.Argb(0xFF, 0x9E, 0x9E, 0x9E);

    /// <summary>The copied things' cards, the menu held on one and the editor: a ground a little lighter than the window's.</summary>
    private static readonly Color CardColor = Color.Argb(0xFF, 0x2C, 0x2C, 0x2C);

    /// <summary>The title bar's height where what was copied stands in it as chips, in dp: Gboard's, room for a chip to read and tap.</summary>
    private const int ChipBarDp = 44;

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

    /// <summary>Keys: the terminal's two rows; empty until their mode is first shown (<see cref="MakeKeys"/>).</summary>
    private LinearLayout terminal;

    /// <summary>The whole keyboard (ABC): as made, or as the screen's design has it, made again when it changes; empty until first shown.</summary>
    private LinearLayout wholeKeyboard;

    /// <summary>Which of the modes' keys are made: Keys, the whole keyboard, two rows.</summary>
    private bool terminalMade, wholeKeyboardMade, compactMade;

    /// <summary>The keyboard of the list this screen shows in the keyboard's language (Ajustes › Tema); none in the popup.</summary>
    private KeyboardEntry? keyboard;

    /// <summary>The screen the keyboards of the list are chosen for; none in the popup and the headset's box.</summary>
    private readonly KeyPlusPreferences.Screen? keyboardScreen;

    /// <summary>Whether the keyboard floats: its floating design shown, and designed.</summary>
    public bool ShowsFloating { get; set; }

    /// <summary>The keyboard of the list shown, as it is now; none in the popup.</summary>
    public KeyboardEntry? Keyboard => keyboard;

    /// <summary>The whole keyboard's design: a keyboard saved keeps its own; the popup's, the screen's.</summary>
    private KeyboardDesign? ReadDesign() =>
        keyboard is null ? KeyboardDesign.Read(preferences) : KeyboardDesign.FromText(keyboard.Design);

    /// <summary>The design kept where it belongs: in the keyboard saved; the popup's, the screen's. None, the keyboard as made again.</summary>
    private void KeepDesign(KeyboardDesign? kept)
    {
        if (keyboard is { } made)
        {
            Keyboards.SetDesign(context, made.Id, kept?.Text, floating: false);
        }
        else if (kept is not null)
        {
            kept.Save(preferences);
        }
        else
        {
            KeyboardDesign.Forget(preferences);
        }
    }

    /// <summary>The screen's design of the whole keyboard; none, the keyboard as made (KeyboardDesign).</summary>
    private KeyboardDesign? design;

    /// <summary>Whether the whole keyboard's keys are being dragged into place (⚙ → Diseñar) rather than typed with.</summary>
    private bool designing;

    /// <summary>Every key and hole the design shows, with its id (a hole's negative), its row and its place in it: Entrar standing tall is there for both of its rows; the keys aside in row -1.</summary>
    private readonly List<(int Id, int Row, int Index, View View)> designKeys = [];

    /// <summary>The design's rows as shown, each with its index, to find the row a key is let go on: Entrar's two rows each by their own views.</summary>
    private readonly List<(int Row, View View)> designRows = [];

    /// <summary>
    /// Each key of the design made once and moved from row to row as it is
    /// dragged: made again at every drop, seventy keys a time, the views and
    /// their listeners left for the collector between Java and .NET held the
    /// keyboard still past Android's five seconds.
    /// </summary>
    private readonly Dictionary<int, TextView> designViews = [];

    /// <summary>While designing, above the keys: what to do, Restablecer and Hecho.</summary>
    private readonly LinearLayout designBar;

    /// <summary>The key being dragged, the picture of it that follows the finger or pointer, and where it was taken within it.</summary>
    private (int Id, View Key, ImageView Picture, float GrabX, float GrabY)? dragged;

    /// <summary>Whether the key held has moved further than a tap: let go, it is put there; else it is marked.</summary>
    private bool dragMoved;

    /// <summary>Where the key held was touched, on the screen.</summary>
    private (float X, float Y) dragStart;

    /// <summary>The key marked by a tap while designing, which − and + make narrower or wider.</summary>
    private int? selectedKey;

    /// <summary>Keys made small: two rows, no title bar; empty until first shown.</summary>
    private LinearLayout compact;

    /// <summary>
    /// The standard keyboard, Gboard's (Berpiztu AI Keyboard on a phone),
    /// beside PC, which is the strip's keys and the whole keyboard with its
    /// designs; none in the popup and on a headset.
    /// </summary>
    private readonly StandardKeyboard? standard;
    private readonly TextView makeSmall;

    /// <summary>The title bar's resize, the keyboard's sign: the frame over the keys to size them (<see cref="ResizeRequested"/>), where the window shows it (<see cref="ShowsResizeButton"/>).</summary>
    private readonly ImageView resizeButton;

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
    private readonly View clipboardButton;
    private readonly ScrollView clipboardView;

    /// <summary>
    /// A thing copied seen whole in the keys' place (its menu's Preview), an image or a text that scrolls, and which one
    /// it is.
    /// </summary>
    private readonly FrameLayout previewView;
    private readonly ImageView imageViewer;
    private readonly ScrollView textPreview;
    private readonly TextView previewText;
    private ClipboardHistory.Entry? previewed;
    private readonly LinearLayout clipboardRows;

    /// <summary>The copied texts side by side in one row above the keys (<see cref="ClipboardAbove"/>).</summary>
    private readonly HorizontalScrollView clipboardLine;
    private readonly LinearLayout clipboardItems;

    /// <summary>The keys a view stands in place of (the copied texts, the settings), with the button that shows it; none while the keys show.</summary>
    private (View Keys, View Cover, View Button)? covered;

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

    /// <summary>Test on a headset: ⌨ hides the keyboard and shows it again; the line says what Android tells of the keyboard's window.</summary>
    private readonly TextView keyboardButton;
    private readonly TextView infoLine;
    private Action? keyboardToggle;

    /// <summary>✕, which closes the popup once asked; a keyboard has none (<see cref="Settings"/>).</summary>
    private readonly TextView closeButton;

    /// <summary>⚙, and the settings it shows in the keys' place, each a row that does what it says.</summary>
    private readonly TextView settingsButton;
    private readonly ScrollView settingsView;
    private readonly LinearLayout settingsRows;
    private IReadOnlyList<Setting> settings = [];

    /// <summary>
    /// What was copied as chips in the title bar's middle (<see cref="CopiedInTitleBar"/>),
    /// a bar of its own in the title bar's place, with ⚙ at its right.
    /// </summary>
    private readonly LinearLayout chipBar;
    private readonly LinearLayout chipItems;

    /// <summary>ABC or Keys in the chips' bar, as in the title bar.</summary>
    private readonly TextView chipSwitchKeys;
    private readonly HorizontalScrollView chipScroll;
    private bool copiedInTitleBar;

    /// <summary>The chips wait to be made again, what was copied having changed while they were out of sight.</summary>
    private bool chipsStale = true;

    /// <summary>
    /// Berpiztu AI Keyboard on a phone (<see cref="CopiedInTitleBar"/>), as
    /// Gboard's toolbar: ⚙ at the left and the rest of it what was copied
    /// last while it is recent, else 📋, or a swipe's other words; one height
    /// and one size of words, whatever the keys' size, and nothing drawn over
    /// anything else. Keys and ABC switch from ⚙'s tiles.
    /// </summary>
    private readonly LinearLayout toolbar;

    /// <summary>⚙'s tiles or the copied things in the keys' place: ✕ and 📋 across the toolbar's place.</summary>
    private readonly LinearLayout menuBar;

    /// <summary>The toolbar's middle: the recent copy's chip, or 📋.</summary>
    private readonly FrameLayout toolbarMiddle;
    private readonly View toolbarClipboard;

    /// <summary>The toolbar's and the menu bar's height, in dp: Gboard's.</summary>
    internal const int ToolbarDp = 48;

    /// <summary>
    /// The standard keyboard's toolbar, as Gboard's on the emulator (pixels
    /// over 2.625): each end's sign 59 px in, so its place 118 px wide; a
    /// circle 89 px across; ⊞ drawn 40 px wide, of the 720 its 960 box fills;
    /// the words 19 sp (a capital 35 px tall over Roboto's 0.711); the lines
    /// between them 3 px wide and 63 px tall.
    /// </summary>
    private readonly LinearLayout standardBar;
    private readonly StandardColors? standardColors;

    /// <summary>
    /// The standard keyboard floating in a window, as Gboard's (on the
    /// emulator, pixels over 2.625): a card with corners 55 px round and a
    /// shadow, under the keys a bar 104 px tall with ∨ (hide) at its left,
    /// its centre 90 px in, drawn 29 px wide (480 of its 960), and in the
    /// middle a handle 188 × 10 px that moves the window.
    /// </summary>
    private readonly FrameLayout? floatingBar;
    private bool inWindow;
    private const float CardCornerDp = 55 / 2.625f;
    private const float CardShadowDp = 8;
    private const float FloatingBarDp = 104 / 2.625f;
    private const float HideCentreDp = 90 / 2.625f;
    private const float HideDp = 29 / (480f / 960) / 2.625f;
    private const float HandleWidthDp = 188 / 2.625f;
    private const float HandleHeightDp = 10 / 2.625f;

    /// <summary>
    /// The floating card being resized, as Gboard's, a tap on its bar beside
    /// the handle: its outline 4 px, its corners 8 px and 38 px past their
    /// curve; a corner held anywhere within <see cref="CornerReachDp"/>;
    /// Reset, 207 × 74 px, 105 px from the card's right. It ends by itself
    /// <see cref="CardResizeMs"/> after the last touch, or as the card is moved.
    /// </summary>
    private bool cardResizing;
    private readonly List<View> cardCorners = [];
    private View? cardReset;
    private Java.Lang.Runnable? endCardResize;
    private const float CardLineDp = 4 / 2.625f;
    private const float CardMarkDp = 8 / 2.625f;
    private const float CardMarkReachDp = 38 / 2.625f;
    private const float ResetWidthDp = 207 / 2.625f;
    private const float ResetHeightDp = 74 / 2.625f;
    private const float ResetRightDp = 105 / 2.625f;
    private const int CardResizeMs = 3000;

    /// <summary>Berpiztu AI Keyboard's ⌨ icon, in dp: a finger's; and the room around it its shadow is drawn in.</summary>
    private const int IconDp = 48;
    private const int IconShadowDp = 12;

    /// <summary>The standard keyboard's grid (⊞), Gboard's, in the keys' place; none without the standard keyboard.</summary>
    private readonly StandardGrid? standardGrid;

    /// <summary>The standard keyboard's emoji (the comma held, ⊞'s Emojis), in the keys' and the toolbar's place; and those typed lately, over the toolbar.</summary>
    private readonly EmojiPanel? emojiPanel;
    private readonly LinearLayout emojiRow;
    private readonly LinearLayout emojiRowItems;

    /// <summary>The row of those typed lately, as Gboard's on the Fold: 100 px tall, each 83 px apart, 48 px wide.</summary>
    private const float EmojiRowDp = 100 / 2.25f;
    private const float EmojiRowCellDp = 83 / 2.25f;
    private const float EmojiRowShare = 48f / 83;

    /// <summary>The grid's buttons the keyboard gives, Gboard's, before the panel's own (the other standard keyboard, Minimizar).</summary>
    private IReadOnlyList<GridTile> gridTiles = [];

    /// <summary>The toolbar's own buttons, Gboard's, in Gboard's order: where they stand until placed elsewhere (⊞ › ✏).</summary>
    private static readonly string[] BarFirst = ["clipboard", "resize", "settings"];

    private const string OtherStandardId = "otherStandard";

    /// <summary>
    /// Customising, on the Fold: the toolbar's buttons in a blue outline 85 px
    /// tall, Gboard's 8 px inside it above and below, each a circle as tall as
    /// that leaves (<see cref="ToolDiameter"/>); side by side they all but
    /// touch, <see cref="ToolGapDp"/> apart, as the glyph inside each circle
    /// keeps its own margin, so that as many fit as can be.
    /// </summary>
    private const float EditBarDp = 85 / 2.25f;
    private const float EditToolGapDp = 8 / 2.25f;
    private const float ToolGapDp = 1;

    /// <summary>While the grid is customised (⊞ › ✏): the toolbar's buttons and the grid's, by their ids, as they are being placed.</summary>
    private bool editingTools;
    private List<string> barTools = [];
    private List<string> gridTools = [];

    /// <summary>
    /// The button dragged while customising: it and where it was taken from,
    /// its picture under the finger, and where it would go, on the toolbar
    /// or in the grid (-1, neither).
    /// </summary>
    private (GridTile Tool, bool FromBar, int From)? lifted;
    private View? liftedView;
    private int barGap = -1;
    private int gridGap = -1;

    /// <summary>The toolbar's places while customising, in order, a gap's among them.</summary>
    private List<View> barSlots = [];
    private const float ToolSlotDp = 118 / 2.625f;
    internal const float ToolCircleDp = 89 / 2.625f;
    private const float ToolIconDp = 40 / (720f / 960) / 2.625f;
    private const float SuggestionSp = 35 / 0.711f / 2.625f;
    private const float SuggestionLineDp = 3 / 2.625f;
    private const float SuggestionLineHeightDp = 63 / 2.625f;

    /// <summary>How long a copy shows as a chip in the toolbar, as Gboard shows the one just copied.</summary>
    private static readonly TimeSpan RecentCopyTime = TimeSpan.FromMinutes(5);

    /// <summary>What was copied last, by its text or image, and when: the toolbar's chip while it is recent.</summary>
    private (string? Text, string? Image)? recentCopy;
    private DateTime recentCopyAt;

    /// <summary>Whether 📋 asked for the things copied in the line over the toolbar, until it is tapped again.</summary>
    private bool copiesAsked;

    /// <summary>How far the chips of the things copied are scrolled across, from the start once they show anew.</summary>
    private int copiesScroll;

    /// <summary>What the clipboard's list held, to tell a new copy from a pin, an edit or a removal.</summary>
    private HashSet<(string? Text, string? Image)> knownCopies = [];

    /// <summary>The whole keyboard's letters a swipe is read over, in the order their centres are handed with it: Spanish's.</summary>
    private static string SwipeLetters => StandardKeys.Spanish.SwipeLetters;

    /// <summary>
    /// A swipe over the whole keyboard's letters: its points and their
    /// times in milliseconds from its start, and each letter's centre (in
    /// <see cref="SwipeLetters"/>' order), all from 0 to 1 over the room the
    /// letters take, as FUTO Swipe reads them; and the language it is read in.
    /// </summary>
    public sealed record SwipePath(float[] X, float[] Y, float[] T, float[] KeyX, float[] KeyY, StandardLanguage Language);

    /// <summary>
    /// Reads a swipe over the letters as words, the likeliest first
    /// (Berpiztu AI Keyboard's FUTO Swipe); none, the letters are only
    /// tapped and type on the touch.
    /// </summary>
    public Func<SwipePath, Task<IReadOnlyList<string>>>? Swiping { get; set; }

    /// <summary>Whether a language is swiped in: its Glide typing on, with its word list and the model downloaded (the keyboard's LanguageChoices); none, every language is.</summary>
    public Func<StandardLanguage, bool>? GlidesFor { get; set; }

    /// <summary>A swipe's words in the order to offer them, what was learnt first (<see cref="PersonalWords.Ordered"/>); none, as read.</summary>
    public Func<StandardLanguage, IReadOnlyList<string>, IReadOnlyList<string>>? OrderSwiped { get; set; }

    /// <summary>A word of the toolbar taken in place of a swiped one: the language, the word the swipe read first, the word taken (<see cref="PersonalWords.Chose"/>).</summary>
    public Action<StandardLanguage, string, string>? SwipeChosen { get; set; }

    /// <summary>The text just before the caret, to tell whether a swiped word needs a space before it.</summary>
    public Func<string?>? TextBeforeCaret { get; set; }

    /// <summary>The text just before the caret, the sentence being written, for Spelling to mend it (<see cref="SpanishMarks"/>).</summary>
    public Func<string?>? RecentText { get; set; }

    /// <summary>
    /// The text right before the caret, so many characters, replaced with
    /// another in one edit of the field: ⌫ keys and the text typed after
    /// them reach the field out of order (the text first), so the keys took
    /// the new word away. None, ⌫ keys then the text.
    /// </summary>
    public Action<int, string>? ReplaceBeforeCaret { get; set; }

    /// <summary>A finger on a letter while swiping is on: its points on the screen and times, and whether it left the letter's touch.</summary>
    private sealed class SwipeTrace(PanelKey key, long start)
    {
        public PanelKey Key { get; } = key;
        public long Start { get; } = start;
        public List<(float X, float Y, float T)> Points { get; } = [];
        public bool Moved { get; set; }
    }

    private SwipeTrace? swipeTrace;

    /// <summary>The line the swiping finger draws over the whole keyboard, once it left its first letter.</summary>
    private SwipeTrail? swipeTrail;

    /// <summary>What the last swipe typed, which a word in the toolbar replaces; none once anything else is typed.</summary>
    private string? swipedText;

    /// <summary>The last swipe's words, the likeliest first, in the toolbar's middle as Gboard's suggestions.</summary>
    private IReadOnlyList<string> swipeWords = [];

    /// <summary>Which of the last swipe's words is typed now: the toolbar's centre.</summary>
    private string? swipedWord;

    /// <summary>The words offered for the word being typed letter by letter, the likeliest first, in the toolbar's middle as Gboard's; and that word as it stands before the caret, which the one taken replaces.</summary>
    private IReadOnlyList<string> typingWords = [];
    private string typingWord = "";

    /// <summary>Whether the toolbar's middle shows words: a swipe's, or those offered for the word being typed.</summary>
    private bool OffersWords => swipeWords.Count > 0 || typingWords.Count > 0 || learnable;

    /// <summary>Whether the word being typed is one no list has, offered as typed at the toolbar's left to be learnt with a tap (<see cref="Learnable"/>).</summary>
    private bool learnable;

    /// <summary>The words to offer for a word being typed in a language, the likeliest first (<see cref="WordSuggestions"/>); none, nothing is offered as letters are typed.</summary>
    public Func<StandardLanguage, string, IReadOnlyList<string>>? Suggesting { get; set; }

    /// <summary>The letters right before the caret, the word being typed; none or empty where there is none.</summary>
    public Func<string?>? WordBeforeCaret { get; set; }

    /// <summary>The settings ⚙ shows now: on a phone, the panel's own tiles first.</summary>
    private IReadOnlyList<Setting> shownSettings = [];

    /// <summary>The editor's card stands outside the panel, where its host puts it (<see cref="EditorOutside"/>).</summary>
    private bool editorOutside;

    /// <summary>The question the title bar's place asks (<see cref="Ask"/>); none, the bar.</summary>
    private string? asking;

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

    /// <summary>The window is being moved or resized, and the labels wait for it to end to be fitted (<see cref="NoteDrag"/>).</summary>
    private bool dragging;
    private bool fitAfterDrag;

    /// <summary>A headset (Android XR), whose touches give their raw place from the window (<see cref="OnScreen"/>).</summary>
    private readonly bool onHeadset;

    /// <param name="send">Sends a key with the meta state of the modifiers held for it.</param>
    /// <param name="type">Types text, as the phone's keyboard does.</param>
    /// <param name="preferences">The layout of the display the panel is on.</param>
    /// <param name="clipboard">The texts copied lately, which 📋 lists.</param>
    /// <param name="lettersFirst">Whether a screen with no mode kept opens on the whole keyboard (ABC), as a keyboard opens on its letters, rather than on Keys.</param>
    /// <param name="keyboardScreen">Berpiztu AI Keyboard on a phone's screen: the keyboards of the list (Estándar, Estándar PC, those saved), the one the screen shows in the keyboard's language; none, the popup's and the headset's keys.</param>
    /// <param name="floating">Whether the keyboard floats, which shows its floating design.</param>
    public KeyPanel(Context context, ISharedPreferences preferences, ClipboardHistory clipboard, Action<Keycode, MetaKeyStates> send, Action<string> type, bool lettersFirst = false, KeyPlusPreferences.Screen? keyboardScreen = null, bool floating = false)
    {
        this.context = context;
        this.clipboard = clipboard;
#if KEYBOARD
        onHeadset = KeyPlusPreferences.HeadsetKeyboard(context);
#else
        onHeadset = KeyPlusPreferences.IsHeadset(context);
#endif
        sendOut = send;
        typeOut = type;
        this.preferences = preferences;
        touchSlop = ViewConfiguration.Get(context)!.ScaledTouchSlop;

        // No name in the title bar, so its buttons have the room: what is
        // left between them is the place to drag the window by.
        var title = new View(context) { ContentDescription = "Drag to move the keys" };
        // The bar's free room is dragged at once, as a window's title is: a
        // headset's pinch (Galaxy XR) never holds still enough for a long press.
        Draggable(title, ReportMoving, tapped: null);

        dock = TitleButton("⤓", "Docked: the keys show while a keyboard is open, where you left them; pressed again, they float, in sight all the time", () => DockRequested?.Invoke());
        var minimize = TitleButton("–", "Minimize the keys into an icon", () => SetFolded(true));
        closeButton = TitleButton("✕", "Close KeyPlus", () => Ask(context.GetString(Resource.String.close_keyplus_ask)!, () => CloseRequested?.Invoke()));
        var smaller = TitleButton("A−", "Smaller labels", () => SetLabelScale(labelScale - LabelScaleStep));
        var larger = TitleButton("A+", "Larger labels", () => SetLabelScale(labelScale + LabelScaleStep));
        // A↺ undoes every size made by hand, so it asks first, as ✕ does.
        var usual = TitleButton("A↺", "Keys and labels at their usual size", () => Ask(context.GetString(Resource.String.usual_size_ask)!, () =>
        {
            SetLabelScale(1f);
            UsualSizeRequested?.Invoke();
        }));
        switchKeys = TitleButton("ABC", "Switch between the keys and a whole keyboard", () => SetWholeKeyboard(wholeKeyboard!.Visibility != ViewStates.Visible));
        clipboardButton = ClipboardTitleButton("Texts copied lately: tap one to paste it");
        settingsButton = TitleButton("⚙", "Settings", () => Toggle(settingsView!, ShowSettings));
        keyboardButton = TitleButton("⌨", "Hide the keyboard, or show it again", () => keyboardToggle?.Invoke());
        keyboardButton.Visibility = ViewStates.Gone;
        settingsButton.Visibility = ViewStates.Gone;
        makeSmall = TitleButton("▭", "Two rows: the keys made small, with no title bar; hold PgUp to open them again", () => SetCompact(true));
        resizeButton = TitleIcon(Resource.Drawable.ic_tool_resize, "Resize the keys", () => ResizeRequested?.Invoke());
        resizeButton.Visibility = ViewStates.Gone;

        titleBar = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        titleBar.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        AddToTitleBar(resizeButton, 36);
        AddToTitleBar(smaller, 36);
        AddToTitleBar(larger, 36);
        AddToTitleBar(usual, 36);
        AddToTitleBar(makeSmall, 36);
        AddToTitleBar(keyboardButton, 36);
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
        var yes = TitleButton(context.GetString(Resource.String.yes)!, "Yes", () =>
        {
            var confirmed = asked;
            Ask(null, null);
            confirmed?.Invoke();
        });
        var no = TitleButton(context.GetString(Resource.String.no)!, "No", () => Ask(null, null));
        closeBar = new LinearLayout(context) { Orientation = Orientation.Horizontal, Visibility = ViewStates.Gone };
        closeBar.AddView(question, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        closeBar.AddView(yes, new LinearLayout.LayoutParams(Dp(36), ViewGroup.LayoutParams.MatchParent));
        closeBar.AddView(no, new LinearLayout.LayoutParams(Dp(36), ViewGroup.LayoutParams.MatchParent));

        // As Gboard's: buttons at both ends, what was copied between them. The
        // room the chips leave free is held, as the buttons are, to move the
        // window.
        chipItems = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        HoldToMove(chipItems, resizes: false);
        chipScroll = new HorizontalScrollView(context) { HorizontalScrollBarEnabled = false, FillViewport = true };
        chipScroll.AddView(chipItems, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent));
        chipSwitchKeys = TitleButton("ABC", "Switch between the keys and a whole keyboard", () => SetWholeKeyboard(wholeKeyboard!.Visibility != ViewStates.Visible));
        chipBar = new LinearLayout(context) { Orientation = Orientation.Horizontal, Visibility = ViewStates.Gone };
        chipBar.AddView(ClipboardTitleButton("Everything copied lately"), new LinearLayout.LayoutParams(Dp(44), ViewGroup.LayoutParams.MatchParent));
        chipBar.AddView(chipScroll, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        chipBar.AddView(chipSwitchKeys, new LinearLayout.LayoutParams(Dp(52), ViewGroup.LayoutParams.MatchParent));
        chipBar.AddView(TitleButton("⚙", "Settings", () => Toggle(settingsView!, ShowSettings)), new LinearLayout.LayoutParams(Dp(44), ViewGroup.LayoutParams.MatchParent));

        toolbarClipboard = ToolIcon(Resource.Drawable.ic_clipboard, "Everything copied lately", () => Toggle(clipboardView!, ShowClipboard));
        toolbarMiddle = new FrameLayout(context);
        HoldToMove(toolbarMiddle, resizes: false);
        toolbarMiddle.AddView(toolbarClipboard, new FrameLayout.LayoutParams(Dp(56), ViewGroup.LayoutParams.MatchParent, GravityFlags.Center));
        toolbar = new LinearLayout(context) { Orientation = Orientation.Horizontal, Visibility = ViewStates.Gone };
        // ⚙ as wide as its sign and a finger's margin: the room left is the words'.
        var toolbarSettings = ToolButton("⚙", "Settings: sizes, moving, designing, the keyboard's app", 20, () => Toggle(settingsView!, ShowSettings));
        toolbarSettings.SetPadding(Dp(8), 0, Dp(8), 0);
        toolbar.AddView(toolbarSettings, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent));
        toolbar.AddView(toolbarMiddle, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        var standardKeys = keyboardScreen is not null;
        standardColors = standardKeys ? StandardColors.Read(context) : null;
        standardBar = new LinearLayout(context) { Orientation = Orientation.Horizontal, Visibility = ViewStates.Gone };

        // A keyboard made narrower or wider (a window resized): its toolbar's places again for that width.
        standardBar.LayoutChange += (_, e) =>
        {
            if (e.Right - e.Left != e.OldRight - e.OldLeft)
            {
                standardBar.Post(ShowBars);
            }
        };
        menuBar = new LinearLayout(context) { Orientation = Orientation.Horizontal, Visibility = ViewStates.Gone };
        foreach (var button in new View[]
        {
            ToolButton("✕", "The keys again", 20, Uncover),
            ToolIcon(Resource.Drawable.ic_clipboard, "Everything copied lately", () => Toggle(clipboardView!, ShowClipboard)),
        })
        {
            menuBar.AddView(button, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        }

        strip = new LinearLayout(context) { Orientation = Orientation.Vertical };
        strip.SetBackgroundColor(PanelColor);
        strip.SetPadding(Dp(KeyMarginDp), 0, Dp(KeyMarginDp), 0);
        infoLine = new TextView(context) { Visibility = ViewStates.Gone };
        infoLine.SetTextColor(Color.Yellow);
        infoLine.SetTextSize(ComplexUnitType.Sp, 11);
        strip.AddView(infoLine);
        strip.AddView(titleBar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(TitleBarDp)));
        strip.AddView(closeBar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(TitleBarDp)));
        strip.AddView(chipBar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(TitleBarDp)));
        strip.AddView(toolbar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(ToolbarDp)));
        emojiRow = new LinearLayout(context) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        emojiRowItems = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        emojiRow.AddView(emojiRowItems, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
        if (standardColors is not null)
        {
            emojiRow.AddView(new View(context) { Background = new ColorDrawable(standardColors.Divider) }, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 1));
        }

        strip.AddView(emojiRow, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(EmojiRowDp)));
        strip.AddView(standardBar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(ToolbarDp)));
        strip.AddView(menuBar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(ToolbarDp)));
        clipboardRows = new LinearLayout(context) { Orientation = Orientation.Vertical };
        clipboardView = new ScrollView(context) { Visibility = ViewStates.Gone };
        clipboardView.AddView(clipboardRows);
        strip.AddView(clipboardView, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0));
        previewView = new FrameLayout(context) { Visibility = ViewStates.Gone };
        imageViewer = new ImageView(context) { ContentDescription = "The image copied" };
        imageViewer.SetScaleType(ImageView.ScaleType.FitCenter);
        imageViewer.Click += (_, _) =>
        {
            if (previewed is { } image)
            {
                ViewImage?.Invoke(image);
            }
        };
        previewView.AddView(imageViewer, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        // The text fills the room, so a tap anywhere in it, under a short text too, opens it in the editor.
        previewText = new TextView(context) { ContentDescription = "The text copied" };
        previewText.SetTextSize(ComplexUnitType.Sp, CardTextSp);
        previewText.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        previewText.Click += (_, _) =>
        {
            if (previewed is { } text)
            {
                Edit(text);
            }
        };
        textPreview = new ScrollView(context) { FillViewport = true };
        textPreview.AddView(previewText, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        previewView.AddView(textPreview, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        strip.AddView(previewView, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0));
        clipboardItems = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        clipboardLine = new HorizontalScrollView(context) { Visibility = ViewStates.Gone };
        clipboardLine.AddView(clipboardItems, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent));

        // The cards are as wide as a third of the panel at most: filled again
        // once the panel has its width, which it has not yet when the row is
        // first asked for, before the keyboard is laid out.
        clipboardLine.LayoutChange += (_, change) =>
        {
            if (change.Right - change.Left != change.OldRight - change.OldLeft && clipboardLine.Visibility == ViewStates.Visible)
            {
                clipboardLine.Post(FillClipboardLine);
            }
        };
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
        editorButtons.AddView(WordButton(context.GetString(Resource.String.cancel)!, "Leave the copied text as it was", DimTextColor, () => EndEditing(save: false)));
        editorButtons.AddView(WordButton(context.GetString(Resource.String.save)!, "Keep the copied text as edited", HeldColor, () => EndEditing(save: true)));
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
        if (standardColors is not null)
        {
            standardGrid = new StandardGrid(context, standardColors, StartEditingTools) { Visibility = ViewStates.Gone };
            strip.AddView(standardGrid, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0));
            emojiPanel = new EmojiPanel(context, standardColors)
            {
                Visibility = ViewStates.Gone,
                Typed = TypeEmoji,
                Deleted = () => Send(Keycode.Del, MetaKeyStates.None),
                Closed = Uncover,
                NotMade = NotMadeYet,
            };
            strip.AddView(emojiPanel, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0));
            extraKeysView = new ScrollView(context) { Visibility = ViewStates.Gone, VerticalScrollBarEnabled = false };
            extraKeysView.SetBackgroundColor(standardColors.Ground);
            strip.AddView(extraKeysView, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0));
            Emojis.Prepare(context);
        }
        knownCopies = [.. clipboard.Entries.Select(CopyKey)];
        clipboard.Changed += NoteCopy;
        clipboard.Changed += RefillClipboard;
        asideKeys = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        designBar = DesignBar();
        strip.AddView(designBar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        terminal = new LinearLayout(context);
        ShowsFloating = floating;
        this.keyboardScreen = keyboardScreen;
        keyboard = keyboardScreen is { } screen ? Keyboards.Selected(context, preferences, screen, KeyPlusPreferences.Language(context).Code) : null;
        design = ReadDesign();
        wholeKeyboard = new LinearLayout(context);
        compact = new LinearLayout(context);
        strip.AddView(terminal);
        strip.AddView(wholeKeyboard);
        strip.AddView(compact);
        if (standardKeys)
        {
            standard = MakeStandard();
            designer = new StandardDesigner(context, standardColors!, standard);
            ReadSettings();
            strip.AddView(standard, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
            floatingBar = FloatingBar();
            strip.AddView(floatingBar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(FloatingBarDp)));
        }

        // Between the keys, a long press moves or resizes the window as on a key.
        HoldToMove(strip);

        tab = KeyView("⌨");
        tab.SetTextSize(ComplexUnitType.Sp, LabelSize);
        tab.ContentDescription = "Show the keys";
        tab.SetPadding(Dp(16), Dp(6), Dp(16), Dp(6));
        Draggable(tab, ReportMoving, OnIconTapped);
        if (standardColors is not null)
        {
            // Berpiztu AI Keyboard's icon, to stand out over any app (the owner's eyes): in a light theme
            // the app's own icon, dark; in a dark one a white round key with the keyboard in black. Raised
            // over the app, with room around it for its shadow.
            tab.SetPadding(0, 0, 0, 0);
            tab.Gravity = GravityFlags.Center;
            tab.Elevation = Dp(CardShadowDp);
            if ((context.Resources!.Configuration!.UiMode & UiMode.NightMask) == UiMode.NightYes)
            {
                var round = new GradientDrawable();
                round.SetShape(ShapeType.Oval);
                round.SetColor(Color.White);
                tab.Background = new RippleDrawable(ColorStateList.ValueOf(RippleColor), round, null);
                tab.SetTextColor(Color.Black);
            }
            else
            {
                tab.Text = "";
                tab.Background = new RippleDrawable(ColorStateList.ValueOf(RippleColor), context.GetDrawable(Resource.Drawable.ic_keyplus), null);
            }
        }

        root = new PanelFrame(this, context);
        root.AddView(strip);
        root.AddView(tab, standardColors is not null
            ? new FrameLayout.LayoutParams(Dp(IconDp), Dp(IconDp)) { LeftMargin = Dp(IconShadowDp), TopMargin = Dp(IconShadowDp), RightMargin = Dp(IconShadowDp), BottomMargin = Dp(IconShadowDp) }
            : new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
        ShowFolded(preferences.GetBoolean(FoldedPreference, false));
        var standardFirst = standard is not null && keyboard is { Standard: true };
        ShowMode(preferences.GetBoolean(WholeKeyboardPreference, lettersFirst), preferences.GetBoolean(CompactPreference, false), makeKeys: !standardFirst);
        if (standardFirst)
        {
            ShowStandard();
        }

        ApplySizes();
        ShowHeld();
    }

    /// <summary>The panel's view, for the window that shows it.</summary>
    public View Root => root;

    /// <summary>The keyboard as its ⌨ icon for this field, or opened again: not kept, as Minimizar's is.</summary>
    public void ShowAsIcon(bool icon) => ShowFolded(icon);

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
        ShownKeys == standard ? PanelPlacement.StandardMode
        : ShownKeys == wholeKeyboard ? PanelPlacement.WholeKeyboardMode
        : ShownKeys == compact ? PanelPlacement.CompactMode
        : PanelPlacement.KeysMode;

    /// <summary>The keys of the mode the window is in, in sight or under the copied texts.</summary>
    private View ShownKeys =>
        covered?.Keys
        ?? (standard is { Visibility: ViewStates.Visible } ? standard
            : wholeKeyboard.Visibility == ViewStates.Visible ? wholeKeyboard
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

    /// <summary>Test on a headset: what ⌨ does, hiding the keyboard or showing it again; none, no ⌨.</summary>
    public Action? KeyboardToggle
    {
        set
        {
            keyboardToggle = value;
            keyboardButton.Visibility = value is null ? ViewStates.Gone : ViewStates.Visible;
            if (titleBar.Width > 0)
            {
                FitTitleBar(titleBar.Width);
            }
        }
    }

    /// <summary>Test on a headset: a line above the title bar with what Android tells of the keyboard's window; none, no line.</summary>
    public string? InfoText
    {
        set
        {
            infoLine.Text = value;
            infoLine.Visibility = value is null ? ViewStates.Gone : ViewStates.Visible;
        }
    }

    /// <summary>
    /// Whether the whole keyboard has its column at the right: not where its
    /// width is short (Berpiztu AI Keyboard on a headset), so the other keys
    /// are wider; Fn and the arrows are Inicio, Fin, RePág and AvPág there.
    /// </summary>
    public bool SideKeys
    {
        set
        {
            // A design has no column: its keys are in its rows.
            if (sideColumn is not null)
            {
                sideColumn.Visibility = value ? ViewStates.Visible : ViewStates.Gone;
            }
        }
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

    /// <summary>Opens an image copied in the phone's own editor, where the panel can (Berpiztu AI Keyboard); none, its menu has no Edit.</summary>
    public Action<ClipboardHistory.Entry>? EditImage { get; set; }

    /// <summary>Opens an image copied in the phone's own viewer, its preview tapped, where the panel can (Berpiztu AI Keyboard).</summary>
    public Action<ClipboardHistory.Entry>? ViewImage { get; set; }

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
    /// The panel let go, another made in its place: no longer told of copies, nothing of its own left to run. Held by the
    /// clipboard's history, which lives as long as the keyboard, every panel made again (another keyboard, language or
    /// colours) stayed alive with all its views, until Java's references ran short, every one more cost a full collection
    /// and the keyboard stopped answering.
    /// </summary>
    public void Release()
    {
        clipboard.Changed -= NoteCopy;
        clipboard.Changed -= RefillClipboard;
        repeat = null;
        handler.RemoveCallbacksAndMessages(null);
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
        asking = text;
        question.Text = text ?? "";

        // In the bars' letters' colour: the standard keyboard's on its light bar, white on the title bar's dark one.
        for (var i = 0; i < closeBar.ChildCount; i++)
        {
            (closeBar.GetChildAt(i) as TextView)?.SetTextColor(ToolGlyph);
        }

        ShowBars();
    }

    /// <summary>
    /// The toolbar's middle: the chip of what was copied last while it is
    /// recent, as Gboard shows it, else 📋; and once it is no longer recent,
    /// 📋 again by itself.
    /// </summary>
    private void FillToolbar()
    {
        while (toolbarMiddle.ChildCount > 1)
        {
            toolbarMiddle.RemoveViewAt(1);
        }

        if (OffersWords)
        {
            toolbarClipboard.Visibility = ViewStates.Gone;
            toolbarMiddle.AddView(SwipeWordsRow(16, divider: null), new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            return;
        }

        var recent = RecentCopy();
        toolbarClipboard.Visibility = recent is null ? ViewStates.Visible : ViewStates.Gone;
        if (recent is not null)
        {
            toolbarMiddle.AddView(RecentChip(recent), RecentChipPlace());
        }
    }

    /// <summary>
    /// The last swipe's words as Gboard's suggestions: the word typed in the
    /// centre, in bold, the next likeliest on each side, a tap on one taking
    /// its place and ending the suggestions; a long word smaller, whole, never cut; lines between them
    /// where the look has them.
    /// </summary>
    private LinearLayout SwipeWordsRow(float sp, Color? divider)
    {
        // Typing, the likeliest in the centre and the next on each side, each taken with a tap; after a swipe, its word typed in the centre.
        var typing = swipeWords.Count == 0;
        var others = swipeWords.Where(word => word != swipedWord).Take(2).ToList();
        var words = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        words.SetGravity(GravityFlags.CenterVertical);

        // A word no list has, as typed at the left in quotes, as Gboard's: a tap writes it and learns it; the others after it.
        var learning = typing && learnable;
        var rest = typingWords.Where(word => !learning || !word.Equals(typingWord, StringComparison.OrdinalIgnoreCase)).ToList();
        string?[] shown = learning ? [typingWord, rest.ElementAtOrDefault(0), rest.ElementAtOrDefault(1)]
            : typing ? [typingWords.ElementAtOrDefault(1), typingWords.ElementAtOrDefault(0), typingWords.ElementAtOrDefault(2)]
            : [others.ElementAtOrDefault(0), swipedWord, others.ElementAtOrDefault(1)];
        for (var i = 0; i < shown.Length; i++)
        {
            var word = shown[i];
            var typed = i == 1 && !typing;
            var learnt = learning && i == 0;
            if (divider is { } line && i > 0)
            {
                words.AddView(Divider(line), new LinearLayout.LayoutParams(Dp(SuggestionLineDp), Dp(SuggestionLineHeightDp)));
            }

            var button = ToolButton(learnt ? $"\"{word}\"" : word ?? "", typed ? $"{word}, written" : $"Write {word} instead", sp, () =>
            {
                if (word is not null && learnt)
                {
                    var language = TypingLanguage;
                    TakeSuggestion(word);
                    Learn?.Invoke(language, word);
                }
                else if (word is not null && typing)
                {
                    TakeSuggestion(word);
                }
                else if (word is not null && !typed)
                {
                    ReplaceSwiped(word);
                }
            });
            button.SetPadding(Dp(2), 0, Dp(2), 0);

            // An address in two lines in its room, as Gboard's, made as small as both need.
            var address = word is not null && !learnt && PersonalWords.IsAddress(word);
            if (address)
            {
                button.SetMaxLines(2);
                button.Text = PersonalWords.InTwoLines(word!);
            }

            button.SetAutoSizeTextTypeUniformWithConfiguration(4, (int)Math.Round(address ? sp * PersonalWords.AddressTypeShare : sp), 1, (int)ComplexUnitType.Sp);
            if (i == 1)
            {
                button.SetTypeface(null, TypefaceStyle.Bold);
            }

            words.AddView(button, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        }

        return words;
    }

    private View Divider(Color color)
    {
        var line = new View(context);
        line.SetBackgroundColor(color);
        return line;
    }

    /// <summary>What was copied last as a chip, which shows while it is recent, ✕ at its end: the bars made again once it is not.</summary>
    private LinearLayout RecentChip(ClipboardHistory.Entry recent)
    {
        ExpireCopyLater();
        return Chip(recent, () => AskDeleteCopy(recent));
    }

    /// <summary>The bars made again as the last copy stops being recent, its chips gone.</summary>
    private void ExpireCopyLater()
    {
        if (!CopiedLately)
        {
            return;
        }

        var left = RecentCopyTime - (DateTime.UtcNow - recentCopyAt);
        handler.PostDelayed(ShowBars, (long)left.TotalMilliseconds + 100);
    }

    /// <summary>A chip's ✕: asked first in the toolbar's place, as a ✕ is easily touched by mistake; Yes deletes it (<see cref="DeleteCopy"/>).</summary>
    private void AskDeleteCopy(ClipboardHistory.Entry entry) => Ask(context.GetString(Resource.String.delete_copy_ask)!, () => DeleteCopy(entry));

    /// <summary>That thing copied deleted from the clipboard's list, as its menu's Delete; the last one gone, the emoji or the buttons back.</summary>
    private void DeleteCopy(ClipboardHistory.Entry entry)
    {
        if (CopiesToShow().All(shown => shown == entry))
        {
            copiesAsked = false;
        }

        clipboard.Remove(entry);
        ShowBars();
    }

    /// <summary>
    /// 📋: with the keys in sight, the things copied, every one, in the line over the toolbar where it shows, else in the
    /// toolbar, or gone from there again, the emoji or the buttons back; with something in the keys' place (⊞), the
    /// clipboard there, or gone from there.
    /// </summary>
    private void ClipboardTapped()
    {
        if (covered is not null)
        {
            Toggle(clipboardView!, ShowClipboard);
            return;
        }

        var shown = CopiesShown;
        copiesAsked = !shown;
        if (shown)
        {
            recentCopy = null;
        }
        else
        {
            copiesScroll = 0;
        }

        ShowBars();
    }

    /// <summary>Whether the things copied show, in the line over the toolbar or in the toolbar: 📋 lit.</summary>
    private bool CopiesShown => !OffersWords && (copiesAsked || CopiesToShow().Count > 0);

    /// <summary>
    /// The things copied as chips that scroll across, each with ✕ (<see cref="CopiesToShow"/>), or that nothing is copied
    /// where 📋 asked for them; none, the emoji or the buttons in their place.
    /// </summary>
    private View? CopiesView()
    {
        if (copiesAsked && clipboard.Entries.Count == 0)
        {
            return NothingCopied();
        }

        var copies = CopiesToShow();
        if (copies.Count == 0)
        {
            return null;
        }

        ExpireCopyLater();
        var chips = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        foreach (var entry in copies)
        {
            var place = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent);
            place.SetMargins(Dp(ChipApartDp), Dp(6), Dp(ChipApartDp), Dp(6));
            // The one previewed has its ✕ hidden in its place, which would read as closing the preview.
            chips.AddView(Chip(entry, () => AskDeleteCopy(entry), closeShown: !IsPreviewed(entry)), place);
        }

        // Made again (a preview opened, a chip deleted), the chips stay where they were scrolled to, set before they are drawn.
        var across = new HorizontalScrollView(context) { HorizontalScrollBarEnabled = false };
        across.AddView(chips, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent));
        var kept = copiesScroll;
        void Restore(object? sender, View.LayoutChangeEventArgs e)
        {
            across.LayoutChange -= Restore;
            across.ScrollTo(kept, 0);
            across.ScrollChange += (_, scrolled) => copiesScroll = scrolled.ScrollX;
        }

        across.LayoutChange += Restore;
        return across;
    }

    /// <summary>
    /// The things copied shown while the last copy is recent, or 📋 asked for them: the last copy first, then the
    /// pinned, then the others from the newest, as the clipboard's list keeps them.
    /// </summary>
    private List<ClipboardHistory.Entry> CopiesToShow()
    {
        if (!CopiedLately && !copiesAsked)
        {
            return [];
        }

        var entries = clipboard.Entries;
        var last = entries.FirstOrDefault(entry => CopyKey(entry) == recentCopy);
        return last is null ? [.. entries] : [last, .. entries.Where(entry => entry != last)];
    }

    private FrameLayout.LayoutParams RecentChipPlace()
    {
        var layout = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent, GravityFlags.Center);
        layout.SetMargins(0, Dp(6), 0, Dp(6));
        return layout;
    }

    /// <summary>
    /// The standard keyboard's toolbar, Gboard's as the owner's Fold has it:
    /// ⊞ (the grid), 📋, stickers, resize, the app's settings, one-handed
    /// and 🎤 in its circle, spread across; while words are suggested, or
    /// something was copied lately, ⊞ circled, the words or the copy, and 🎤;
    /// while the grid or the copied things are in the keys' place, ⊞ is ✕
    /// in a function key's circle. Parts not made yet say so.
    /// </summary>
    private void FillStandardBar()
    {
        standardBar.RemoveAllViews();
        barRoomShown = -1;

        // The line over the toolbar first: the words offered go there while it shows, and the toolbar keeps its buttons.
        ShowEmojiRow();
        if (editingTools)
        {
            FillEditingBar();
            return;
        }

        if (designingStandard)
        {
            FillDesignBar();
            return;
        }

        var colors = standardColors!;
        var wordsInBar = OffersWords && !EmojiRowShown;
        var copies = OffersWords || EmojiRowShown ? null : CopiesView();
        var suggesting = wordsInBar || copies is not null;
        var first = covered is not null
            ? StandardTool(Resource.Drawable.ic_tool_close, "The keys again", Uncover, colors.Function)
            : StandardTool(Resource.Drawable.ic_tool_grid, "Tools and settings", () => Toggle(standardGrid!, ShowGrid), suggesting ? colors.GridCircle : null);
        standardBar.AddView(first, new LinearLayout.LayoutParams(ToolSlot, ViewGroup.LayoutParams.MatchParent));
        if (offer is { } offered)
        {
            (offered.Parent as ViewGroup)?.RemoveView(offered);
            standardBar.AddView(offered, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        }
        else if (wordsInBar)
        {
            standardBar.AddView(SwipeWordsRow(SuggestionSp, colors.Divider), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        }
        else if (copies is not null)
        {
            // 📋 lit before them, as it stands in the buttons' place: a tap takes them all away.
            standardBar.AddView(StandardTool(Resource.Drawable.ic_clipboard, context.GetString(Resource.String.clipboard), ClipboardTapped,
                colors.Primary, colors.OnPrimary, diameter: ToolDiameter), new LinearLayout.LayoutParams(ToolDiameter, ViewGroup.LayoutParams.MatchParent));
            standardBar.AddView(copies, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        }
        else
        {
            var tools = Tools();
            barRoomShown = BarRoom;
            foreach (var tool in Arranged().Bar.Select(id => tools[id]).Select(tool => StandardTool(tool.Icon, tool.Label, tool.Chosen,
                tool.On ? colors.Primary : null, tool.On ? colors.OnPrimary : null, diameter: ToolDiameter)))
            {
                standardBar.AddView(new View(context), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
                standardBar.AddView(tool, new LinearLayout.LayoutParams(ToolDiameter, ViewGroup.LayoutParams.MatchParent));
            }

            standardBar.AddView(new View(context), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        }

        standardBar.AddView(MicTool(), new LinearLayout.LayoutParams(ToolSlot, ViewGroup.LayoutParams.MatchParent));
    }

    /// <summary>🎤 tapped: a dictation started, or the one going on stopped.</summary>
    public event Action? VoiceTapped;

    /// <summary>The standard keyboard's language as a dictation asks for it, by its locale ("es-ES", "en-US").</summary>
    public string DictationLocale => (standard?.Language ?? StandardKeys.Spanish).Locale;

    /// <summary>The kind of field typed in, for the standard keyboard to suit it (<see cref="StandardKeyboard.Field"/>).</summary>
    public void FieldIs(StandardField kind)
    {
        if (standard is not null)
        {
            standard.Field = kind;
        }
    }

    /// <summary>Whether the caret stands where a sentence begins (Spelling's Capital at the start of a sentence): the standard keyboard's Shift on by itself, or off again.</summary>
    public void SentenceStart(bool begins) => standard?.SentenceStart(begins);

    /// <summary>🎤's circle, lit while listening and as wide as the voice is loud; its sign.</summary>
    private View? micHalo;
    private ImageView? micSign;
    private bool listening;
    private double listeningLevel;

    /// <summary>🎤's sign while it listens: the voice coming in (<see cref="VoiceBars"/>).</summary>
    private BarsSign? bars;

    /// <summary>
    /// 🎤 as the Windows keyboard's: in the keys' tone, and while it listens
    /// on a circle in the primary tone that beats with the voice's level, its
    /// sign in the tone on it; a tap starts or stops the dictation.
    /// </summary>
    private FrameLayout MicTool()
    {
        var place = StandardTool(Resource.Drawable.ic_tool_mic, context.GetString(Resource.String.voice_typing), () => VoiceTapped?.Invoke(), standardColors!.Key);
        var halo = new View(context);
        var round = new GradientDrawable();
        round.SetShape(ShapeType.Oval);
        round.SetColor(standardColors.Primary);
        halo.Background = round;
        place.AddView(halo, 0, new FrameLayout.LayoutParams(Dp(ToolCircleDp), Dp(ToolCircleDp), GravityFlags.Center));
        micHalo = halo;
        micSign = (ImageView)place.GetChildAt(1)!;
        ShowListening(listening, listeningLevel);
        return place;
    }

    /// <summary>The circle's size as a share of 🎤's, in silence and at the loudest: Windows' 0.36 and 0.5 of the button's height over the pressed circle's 0.42.</summary>
    private const float QuietHalo = 0.36f / 0.42f;
    private const float LoudHalo = 0.5f / 0.42f;

    /// <summary>Whether a dictation is listening, and its level, 0 to 1: 🎤 lit and beating, or as it was.</summary>
    public void ShowListening(bool on, double level)
    {
        listening = on;
        listeningLevel = level;
        if (micHalo is null || micSign is null || standardColors is null)
        {
            return;
        }

        micHalo.Visibility = on ? ViewStates.Visible : ViewStates.Invisible;
        var scale = QuietHalo + ((LoudHalo - QuietHalo) * (float)level);
        micHalo.ScaleX = micHalo.ScaleY = scale;
        micSign.ImageTintList = ColorStateList.ValueOf(on ? standardColors.OnPrimary : standardColors.Glyph);
        if (on)
        {
            bars ??= new BarsSign();
            bars.Show(standardColors.OnPrimary, level);
            if (micSign.Drawable != bars)
            {
                micSign.SetImageDrawable(bars);
            }
        }
        else if (micSign.Drawable == bars)
        {
            micSign.SetImageResource(Resource.Drawable.ic_tool_mic);
        }

        // The sign's own circle, in the keys' tone, out of the way while the lit one beats behind it.
        if (micSign.Background is { } own)
        {
            own.Alpha = on ? 0 : 255;
        }
    }

    /// <summary>The voice coming in, as Gboard's, in 🎤's place: the bars rounded, in the tone on the lit circle, each as tall as the level and its beat make it.</summary>
    private sealed class BarsSign : Drawable
    {
        private readonly Paint paint = new() { AntiAlias = true };
        private double level;

        public void Show(Color color, double heard)
        {
            paint.Color = color;
            level = heard;
            InvalidateSelf();
        }

        public override void Draw(Canvas canvas)
        {
            var box = Bounds;
            var size = Math.Min(box.Width(), box.Height());
            var (left, middle) = (box.Left + ((box.Width() - size) / 2f), box.ExactCenterY());
            var width = (float)(VoiceBars.WidthShare * size);
            var seconds = System.Environment.TickCount64 / 1000.0;
            for (var bar = 0; bar < VoiceBars.Count; bar++)
            {
                var height = Math.Max(width, (float)(VoiceBars.Height(bar, level, seconds) * size));
                var x = left + (float)VoiceBars.Left(bar, size);
                canvas.DrawRoundRect(x, middle - (height / 2), x + width, middle + (height / 2), width / 2, width / 2, paint);
            }
        }

        public override void SetAlpha(int alpha) => paint.Alpha = alpha;

        public override void SetColorFilter(ColorFilter? colorFilter)
        {
        }

        public override int Opacity => (int)Format.Translucent;
    }

    /// <summary>A toolbar sign's place: Gboard's, or, where seven of them do not fit (a narrow window), a seventh of the bar.</summary>
    private int ToolSlot => standardBar.Width > 0 ? Math.Min(Dp(ToolSlotDp), standardBar.Width / 7) : Dp(ToolSlotDp);

    /// <summary>
    /// A sign of the standard keyboard's toolbar, centred in its place, in a
    /// circle where it has one (its outline, where it has one), in the keys'
    /// letters' colour or its own; held still a moment, it moves the keyboard
    /// as the bar does. Its circle Gboard's, or <paramref name="diameter"/> pixels across.
    /// </summary>
    private FrameLayout StandardTool(int icon, string description, Action pressed, Color? circle = null, Color? glyph = null, Color? ring = null, int? diameter = null)
    {
        var across = diameter ?? Dp(ToolCircleDp);
        // Its name shown as the mouse rests on it, as the owner asked.
        var sign = new ImageView(context) { ContentDescription = description, TooltipText = description };
        sign.SetImageResource(icon);
        sign.ImageTintList = ColorStateList.ValueOf(glyph ?? standardColors!.Glyph);
        sign.SetScaleType(ImageView.ScaleType.FitCenter);
        var inset = (across - Dp(ToolIconDp)) / 2;
        sign.SetPadding(inset, inset, inset, inset);
        var ground = new GradientDrawable();
        ground.SetShape(ShapeType.Oval);
        ground.SetColor(circle ?? Color.Transparent);
        if (ring is { } line)
        {
            ground.SetStroke(Dp(StandardGrid.OutlineDp), line);
        }

        sign.Background = new RippleDrawable(ColorStateList.ValueOf(RippleColor), ground, null);
        HoldToMove(sign, pressed, resizes: false);
        var place = new FrameLayout(context);
        place.AddView(sign, new FrameLayout.LayoutParams(across, across, GravityFlags.Center));
        return place;
    }

    /// <summary>The standard keyboard's resize, or the title bar's, was pressed: the frame to size the keys with, where the window has one.</summary>
    public event Action? ResizeRequested;

    /// <summary>Whether the title bar has the resize button: the popup's window, sized with the frame.</summary>
    public bool ShowsResizeButton
    {
        set
        {
            resizeButton.Visibility = value ? ViewStates.Visible : ViewStates.Gone;
            if (titleBar.Width > 0)
            {
                FitTitleBar(titleBar.Width);
            }
        }
    }

    /// <summary>
    /// Whether the title bar offers ABC, the whole keyboard: not in KeyPlus's windows, where the whole keyboard is
    /// Berpiztu AI Keyboard's; there the keys are kept, a window left on the whole keyboard back on them.
    /// </summary>
    public bool OffersWholeKeyboard
    {
        set
        {
            switchKeys.Visibility = value ? ViewStates.Visible : ViewStates.Gone;
            chipSwitchKeys.Visibility = switchKeys.Visibility;
            if (!value && wholeKeyboard.Visibility == ViewStates.Visible)
            {
                SetWholeKeyboard(false);
            }

            if (titleBar.Width > 0)
            {
                FitTitleBar(titleBar.Width);
            }
        }
    }

    /// <summary>A view laid over the whole window, above the keys and the bars (the frame that sizes it, <see cref="KeyboardFrame"/>).</summary>
    public void AddOverlay(View over) =>
        root.AddView(over, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

    /// <summary>The grid's buttons the keyboard gives (Gboard's), which come before the panel's own.</summary>
    public IReadOnlyList<GridTile> GridTiles
    {
        set
        {
            gridTiles = value;

            // A button that shows a state (Flotante) changed: the toolbar and the grid in sight show it.
            if (standardBar.Visibility == ViewStates.Visible)
            {
                FillStandardBar();
            }

            if (covered?.Cover == standardGrid && !editingTools)
            {
                ShowGrid();
            }
        }
    }

    /// <summary>
    /// The emoji in the keys' place, the toolbar over it, as on Windows: those
    /// typed lately first; ← or the toolbar's ✕ the keys again. The window keeps its size.
    /// </summary>
    public void ShowEmojis()
    {
        if (emojiPanel is null)
        {
            return;
        }

        // In the keys' place, the toolbar and the row of those typed lately kept over it, as on Windows: the keyboard
        // keeps its height.
        emojiPanel.Fill();
        Cover(emojiPanel, settingsButton);
    }

    /// <summary>An emoji typed, from the panel or the row of those typed lately.</summary>
    private void TypeEmoji(string emoji)
    {
        EndSwipe(letter: false);
        Type(emoji);
    }

    /// <summary>
    /// Over the toolbar, while it shows and Preferencias' Extra line for suggestions and recent emoji is on, a line of
    /// one height whatever it holds, as the Windows keyboard's: the words offered while typing, else, after a copy, the
    /// things copied as chips (<see cref="CopiesView"/>), so the toolbar keeps its buttons, else the emoji typed lately,
    /// as Gboard's on the Fold, as many as fit, a tap typing one. Off, no line, the words and the things copied in the toolbar.
    /// </summary>
    private void ShowEmojiRow()
    {
        var shown = EmojiRowShown;
        emojiRow.Visibility = shown ? ViewStates.Visible : ViewStates.Gone;
        emojiRowItems.RemoveAllViews();
        if (!shown)
        {
            return;
        }

        if (OffersWords)
        {
            emojiRowItems.AddView(SwipeWordsRow(SuggestionSp, standardColors!.Divider), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
            return;
        }

        if (CopiesView() is { } copies)
        {
            emojiRowItems.AddView(copies, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
            return;
        }

        var recent = Emojis.Recent(context);
        var width = strip.Width > 0 ? strip.Width : context.Resources!.DisplayMetrics!.WidthPixels;
        var cell = Dp(EmojiRowCellDp);
        var measure = new Paint { TextSize = 100 };
        var size = 100 * cell * EmojiRowShare / Math.Max(1, measure.MeasureText("😀"));
        foreach (var emoji in recent.Take(Math.Max(1, width / cell)))
        {
            var item = new TextView(context) { Text = emoji, Gravity = GravityFlags.Center, Clickable = true };
            item.SetTextSize(ComplexUnitType.Px, size);
            item.Click += (_, _) =>
            {
                Emojis.Used(context, emoji);
                TypeEmoji(emoji);
                ShowEmojiRow();
            };
            // Each in its cell from the left, as Gboard's, however few.
            emojiRowItems.AddView(item, new LinearLayout.LayoutParams(cell, ViewGroup.LayoutParams.MatchParent));
        }
    }

    /// <summary>Whether the line over the toolbar shows: the option on, the standard keyboard's toolbar in sight, not while designing.</summary>
    private bool EmojiRowShown => emojiRowOn && emojiPanel is not null && standardBar.Visibility == ViewStates.Visible && !designingStandard;

    /// <summary>The grid in the keys' place: its buttons as placed.</summary>
    private void ShowGrid()
    {
        var tools = Tools();
        standardGrid!.Tiles = [.. Arranged().Grid.Select(id => tools[id])];
        Cover(standardGrid, settingsButton);
    }

    /// <summary>
    /// Every button the toolbar and the grid can hold, by its id: the
    /// toolbar's own, the keyboard's, then the panel's (the other standard
    /// keyboard, Minimizar).
    /// </summary>
    private Dictionary<string, GridTile> Tools()
    {
        GridTile[] all =
        [
            new("clipboard", context.GetString(Resource.String.clipboard), Resource.Drawable.ic_clipboard, ClipboardTapped, On: CopiesShown),
            new("resize", context.GetString(Resource.String.resize), Resource.Drawable.ic_tool_resize, () => ResizeRequested?.Invoke()),
            new("settings", context.GetString(Resource.String.keyboard_settings), Resource.Drawable.ic_tool_settings, OpenApp),
            .. gridTiles,
            // One button either way, as the owner asked: it shows the keyboard in sight, Estándar a phone, PC a computer, and switches to the other.
            keyboard?.Id == Keyboards.StandardId
                ? new(OtherStandardId, context.GetString(Resource.String.keyboard_standard), Resource.Drawable.ic_grid_phone, () => Choose(OtherStandard))
                : new(OtherStandardId, "PC", Resource.Drawable.ic_grid_pc, () => Choose(OtherStandard)),
            new(DesignId, context.GetString(Resource.String.design), Resource.Drawable.ic_grid_design, Design),
        ];
        return all.ToDictionary(tool => tool.Id);
    }

    private const string DesignId = "design";

    /// <summary>Whether the standard keyboard's keys are being designed (⊞ › Diseñar).</summary>
    private bool designingStandard;

    /// <summary>The keys a design can add (+ Tecla), in the keys' place while chosen.</summary>
    private readonly ScrollView? extraKeysView;

    /// <summary>Diseñar's pills, + Tecla's list and Hecho's saving, shared with Tema's preview.</summary>
    private StandardDesigner? designer;

    /// <summary>
    /// Diseñar on a standard keyboard, as the PC's designer: a tap marks a
    /// key in blue, − and + make it narrower or wider, a drag moves it;
    /// + Tecla, + Fila and Quitar over the keys, in the toolbar's place, with
    /// Cancelar and Hecho. Its design for the mode it shows in, or the
    /// keyboard as made, to start from.
    /// </summary>
    private void StartStandardDesign()
    {
        if (keyboard is null || designer is null)
        {
            return;
        }

        Uncover();
        designer.Start(keyboard, ShowsFloating);
        designingStandard = true;
        ShowBars();
    }

    /// <summary>Hecho or Cancelar (<see cref="StandardDesigner.End"/>): the keyboard shown as it now is, the toolbar back.</summary>
    private void EndStandardDesign(bool keep)
    {
        designingStandard = false;
        Uncover();
        keyboard = designer?.End(keep, preferences) ?? keyboard;
        ShowBars();
    }

    /// <summary>The designer's pills in the toolbar's place.</summary>
    private void FillDesignBar() => designer!.FillBar(standardBar, () => Toggle(extraKeysView!, ShowExtraKeys), EndStandardDesign);

    /// <summary>+ Tecla's list in the keys' place.</summary>
    private void ShowExtraKeys()
    {
        extraKeysView!.RemoveAllViews();
        extraKeysView.AddView(designer!.ExtraKeys(Uncover));
        Cover(extraKeysView, settingsButton);
    }

    /// <summary>The toolbar's buttons and the grid's as placed; those the toolbar has no room for in the grid, first.</summary>
    private (List<string> Bar, List<string> Grid) Arranged()
    {
        var (bar, grid) = StandardTools.Read(context, BarFirst, [.. gridTiles.Select(tile => tile.Id), OtherStandardId, DesignId]);
        var room = BarRoom;
        return ([.. bar.Take(room)], [.. bar.Skip(room), .. grid]);
    }

    /// <summary>
    /// How many buttons the toolbar holds: as many circles (<see cref="ToolDiameter"/>), <see cref="ToolGapDp"/> apart, as
    /// fit between ⊞ and 🎤 inside the customising outline; not laid out yet, at the width it was measured at (<see cref="FitBar"/>).
    /// </summary>
    private int BarRoom => BarMiddle > 0
        ? Math.Max(1, (BarMiddle - (2 * EditSideInset) + Dp(ToolGapDp)) / (ToolDiameter + Dp(ToolGapDp)))
        : BarFirst.Length;

    /// <summary>The toolbar between ⊞ and 🎤, in pixels; 0 while its width is not known.</summary>
    private int BarMiddle => (standardBar.Width > 0 ? standardBar.Width : standardBar.MeasuredWidth) is var width and > 0
        ? width - (2 * ToolSlot)
        : 0;

    /// <summary>The customising outline's line and the gap inside it: above and below its circles, and at their sides.</summary>
    private int EditInset => Dp(StandardGrid.OutlineDp + EditToolGapDp);
    private int EditSideInset => Dp(StandardGrid.OutlineDp + ToolGapDp);

    /// <summary>
    /// The circle of each of the toolbar's own buttons, customising or not, in pixels: as tall as the customising outline
    /// leaves inside it, and as wide, so it is round and as many fit as can be.
    /// </summary>
    private int ToolDiameter => Dp(EditBarDp) - (2 * EditInset);

    /// <summary>The toolbar's room its buttons were placed for; -1 while it shows no buttons (words, a copy, customising).</summary>
    private int barRoomShown = -1;

    /// <summary>
    /// The keyboard about to be laid out at a width: the toolbar measured at it and its buttons placed again where as many
    /// do not fit, before it is drawn. Made again (another keyboard, another language), the toolbar was first drawn with
    /// five buttons, as its width was not known, and then with all of them.
    /// </summary>
    public void FitBar(int width)
    {
        if (barRoomShown < 0 || standardBar.Visibility != ViewStates.Visible)
        {
            return;
        }

        Root.Measure(
            View.MeasureSpec.MakeMeasureSpec(width, MeasureSpecMode.Exactly),
            View.MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified));
        if (BarRoom != barRoomShown)
        {
            FillStandardBar();
        }
    }

    /// <summary>✏: the grid customised, as Gboard's.</summary>
    private void StartEditingTools()
    {
        (barTools, gridTools) = Arranged();
        editingTools = true;
        barGap = gridGap = -1;
        FillStandardBar();
        ShowEditingGrid();
    }

    private void ShowEditingGrid()
    {
        var tools = Tools();
        standardGrid!.ShowEditing([.. gridTools.Select(id => tools[id])], gridGap, Lift);
    }

    /// <summary>✓: customising done, the grid's pages again with its buttons where they were placed.</summary>
    private void EndEditingTools()
    {
        editingTools = false;
        FillStandardBar();
        ShowGrid();
    }

    /// <summary>
    /// The toolbar while customising, as Gboard's: ✓ in a blue circle, the
    /// toolbar's buttons each on a pill in a blue outline, a gap where the
    /// one dragged would go (the last left out where the toolbar is full,
    /// for it goes to the grid), 🎤 ringed in blue.
    /// </summary>
    private void FillEditingBar()
    {
        var colors = standardColors!;
        standardBar.AddView(StandardTool(Resource.Drawable.ic_tool_check, context.GetString(Resource.String.done), EndEditingTools, colors.Primary, colors.OnPrimary),
            new LinearLayout.LayoutParams(ToolSlot, ViewGroup.LayoutParams.MatchParent));

        var outline = new GradientDrawable();
        outline.SetColor(colors.Dim);
        outline.SetCornerRadius(Dp(EditBarDp) / 2f);
        outline.SetStroke(Dp(StandardGrid.OutlineDp), colors.Primary);
        var group = new LinearLayout(context) { Orientation = Orientation.Horizontal, Background = outline };
        group.SetPadding(EditSideInset, EditInset, EditSideInset, EditInset);
        var tools = Tools();
        var shown = barTools.Take(barGap >= 0 ? BarRoom - 1 : BarRoom).Select(id => (GridTile?)tools[id]).ToList();
        if (barGap >= 0)
        {
            shown.Insert(Math.Min(barGap, shown.Count), null);
        }

        barSlots = [];
        foreach (var tool in shown)
        {
            var slot = tool is null ? new View(context) : EditingTool(tool);
            group.AddView(slot, new LinearLayout.LayoutParams(ToolDiameter, ViewGroup.LayoutParams.MatchParent) { LeftMargin = barSlots.Count > 0 ? Dp(ToolGapDp) : 0 });
            barSlots.Add(slot);
        }

        var middle = new FrameLayout(context);
        middle.AddView(group, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(EditBarDp), GravityFlags.Center));
        standardBar.AddView(middle, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        standardBar.AddView(StandardTool(Resource.Drawable.ic_tool_mic, "Voice typing", () => NotMadeYet(context.GetString(Resource.String.voice_typing)), colors.Key, ring: colors.Primary),
            new LinearLayout.LayoutParams(ToolSlot, ViewGroup.LayoutParams.MatchParent));
    }

    /// <summary>A toolbar button while customising: its sign on a pill in the keys' colour, its name as the mouse rests on it; held, lifted.</summary>
    private FrameLayout EditingTool(GridTile tool)
    {
        var colors = standardColors!;
        var sign = new ImageView(context) { ContentDescription = tool.Label, ImageTintList = ColorStateList.ValueOf(colors.Glyph) };
        sign.SetImageResource(tool.Icon);
        sign.SetScaleType(ImageView.ScaleType.FitCenter);
        var ground = new GradientDrawable();
        ground.SetColor(colors.Key);
        ground.SetCornerRadius(Dp(EditBarDp) / 2f);
        var pill = new FrameLayout(context) { Background = ground, TooltipText = tool.Label };
        pill.AddView(sign, new FrameLayout.LayoutParams(Dp(ToolIconDp), Dp(ToolIconDp), GravityFlags.Center));
        StandardGrid.HoldToDrag(pill, (x, y) => Lift(tool, x, y));
        return pill;
    }

    /// <summary>A button held while customising: taken from where it was, a gap there, its picture under the finger.</summary>
    private void Lift(GridTile tool, float x, float y)
    {
        var fromBar = barTools.IndexOf(tool.Id);
        lifted = (tool, fromBar >= 0, fromBar >= 0 ? fromBar : gridTools.IndexOf(tool.Id));
        barTools.Remove(tool.Id);
        gridTools.Remove(tool.Id);
        barGap = fromBar;
        gridGap = fromBar >= 0 ? -1 : lifted.Value.From;
        FillStandardBar();
        ShowEditingGrid();
        MoveLifted(x, y);
    }

    /// <summary>
    /// The button dragged, under the finger, its sign and its name: over the
    /// toolbar, the toolbar opening where it would go; over the list, the
    /// list opening; elsewhere, neither.
    /// </summary>
    private void MoveLifted(float x, float y)
    {
        if (lifted is not { } now)
        {
            return;
        }

        var overBar = StandardGrid.Contains(standardBar, x, y);
        var overList = !overBar && standardGrid!.OverList(x, y);
        var bar = overBar ? InsertAt(barSlots, barGap, x, y) : -1;
        var grid = overList ? InsertAt(standardGrid!.Cells, gridGap, x, y) : -1;
        if (bar != barGap)
        {
            barGap = bar;
            FillStandardBar();
        }

        if (grid != gridGap)
        {
            gridGap = grid;
            ShowEditingGrid();
        }

        // Always the whole pill, its sign and its name, over the toolbar too: the owner's wish, to see which button it is.
        if (liftedView is null)
        {
            var (width, height) = standardGrid!.PillSize;
            liftedView = standardGrid.Pill(now.Tool);
            liftedView.Elevation = Dp(CardShadowDp);
            root.AddView(liftedView, new FrameLayout.LayoutParams(width, height));
        }

        var at = new int[2];
        root.GetLocationOnScreen(at);
        liftedView.TranslationX = x - at[0] - (liftedView.LayoutParameters!.Width / 2f);
        liftedView.TranslationY = y - at[1] - (liftedView.LayoutParameters.Height / 2f);
    }

    /// <summary>Where among some places, a gap's among them, the button dragged would go: the nearest place, or past it where nothing is open yet and the finger is past its middle.</summary>
    private static int InsertAt(IReadOnlyList<View> places, int gap, float x, float y)
    {
        var nearest = StandardGrid.Nearest(places, x, y);
        if (nearest < 0)
        {
            return 0;
        }

        var at = new int[2];
        places[nearest].GetLocationOnScreen(at);
        return gap < 0 && x > at[0] + (places[nearest].Width / 2f) ? nearest + 1 : nearest;
    }

    /// <summary>
    /// The button let go: where the gap is (on a full toolbar, the toolbar's
    /// last to the grid's start), or back where it was; the places kept.
    /// </summary>
    private void DropLifted()
    {
        if (lifted is not { } now)
        {
            return;
        }

        if (barGap >= 0)
        {
            barTools.Insert(Math.Min(barGap, barTools.Count), now.Tool.Id);
            if (barTools.Count > BarRoom)
            {
                gridTools.Insert(0, barTools[^1]);
                barTools.RemoveAt(barTools.Count - 1);
            }
        }
        else if (gridGap >= 0)
        {
            gridTools.Insert(Math.Min(gridGap, gridTools.Count), now.Tool.Id);
        }
        else
        {
            var from = now.FromBar ? barTools : gridTools;
            from.Insert(Math.Min(now.From, from.Count), now.Tool.Id);
        }

        lifted = null;
        barGap = gridGap = -1;
        if (liftedView is not null)
        {
            root.RemoveView(liftedView);
            liftedView = null;
        }

        StandardTools.Save(context, barTools, gridTools);
        FillStandardBar();
        ShowEditingGrid();
    }

    /// <summary>A button lifted: the rest of the touch is the drag's, wherever the finger goes.</summary>
    private bool Dragging(MotionEvent motion)
    {
        if (lifted is null)
        {
            return false;
        }

        MoveLifted(motion.RawX, motion.RawY);
        if (motion.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel)
        {
            DropLifted();
        }

        return true;
    }

    /// <summary>What was copied last, as the clipboard's list has it now, if it was copied within <see cref="RecentCopyTime"/>.</summary>
    private ClipboardHistory.Entry? RecentCopy() =>
        CopiedLately && recentCopy is { } key
            ? clipboard.Entries.FirstOrDefault(entry => CopyKey(entry) == key)
            : null;

    private bool CopiedLately => recentCopy is not null && DateTime.UtcNow - recentCopyAt < RecentCopyTime;

    private static (string? Text, string? Image) CopyKey(ClipboardHistory.Entry entry) => (entry.Text, entry.Image);

    /// <summary>
    /// The clipboard's list changed: a thing it did not hold is a new copy,
    /// the toolbar's chip from now on; a pin, an edit's old text gone or a
    /// removal is not.
    /// </summary>
    private void NoteCopy()
    {
        var now = clipboard.Entries;
        var fresh = now.FirstOrDefault(entry => !knownCopies.Contains(CopyKey(entry)));
        if (fresh is not null)
        {
            (recentCopy, recentCopyAt) = (CopyKey(fresh), DateTime.UtcNow);
            copiesScroll = 0;
        }

        knownCopies = [.. now.Select(CopyKey)];
    }

    /// <summary>A toolbar's button: one size of words or signs whatever the keys' size; held still a moment, it moves the keyboard as the bar does.</summary>
    private TextView ToolButton(string label, string description, float sp, Action pressed)
    {
        var view = new TextView(context)
        {
            Text = label,
            Gravity = GravityFlags.Center,
            ContentDescription = description,
            Background = new RippleDrawable(ColorStateList.ValueOf(RippleColor), null, new ColorDrawable(Color.White)),
        };
        view.SetTextColor(ToolGlyph);
        view.SetTextSize(ComplexUnitType.Sp, sp);
        view.SetMaxLines(1);
        HoldToMove(view, pressed, resizes: false);
        return view;
    }

    /// <summary>A toolbar's button drawn as an icon, white as its words are; held still a moment, it moves the keyboard as the bar does.</summary>
    private ImageView ToolIcon(int drawable, string description, Action pressed)
    {
        var view = new ImageView(context)
        {
            ContentDescription = description,
            Background = new RippleDrawable(ColorStateList.ValueOf(RippleColor), null, new ColorDrawable(Color.White)),
        };
        view.SetImageResource(drawable);
        view.ImageTintList = ColorStateList.ValueOf(ToolGlyph);
        view.SetScaleType(ImageView.ScaleType.Center);
        HoldToMove(view, pressed, resizes: false);
        return view;
    }

    /// <summary>Ajustes de la app, among ⚙'s tiles: the keyboard's own app, where its settings are.</summary>
    private void OpenApp()
    {
        Uncover();
        context.StartActivity(new Intent(context, typeof(MainActivity)).AddFlags(ActivityFlags.NewTask));
    }

    /// <summary>
    /// ⚙'s own tiles on a phone, before the keyboard's: the labels' size
    /// (A−, A+, A↺, which asks first), Keys in two rows where Keys show, and
    /// the keys made an icon, and the keyboard's app.
    /// </summary>
    private IEnumerable<Setting> PanelTiles()
    {
        yield return new(context.GetString(Resource.String.labels_smaller)!, () => SetLabelScale(labelScale - LabelScaleStep), "A−");
        yield return new(context.GetString(Resource.String.labels_larger)!, () => SetLabelScale(labelScale + LabelScaleStep), "A+");
        yield return new(context.GetString(Resource.String.usual_size)!, () => Ask(context.GetString(Resource.String.usual_size_ask)!, () =>
        {
            SetLabelScale(1f);
            UsualSizeRequested?.Invoke();
        }), "A↺");
        // A saved PC keyboard's own modes; from it, back to a standard one.
        var standardShown = standard is { Visibility: ViewStates.Visible };
        if (standard is not null)
        {
            yield return new(OtherStandardName, () => Choose(OtherStandard), OtherStandard == Keyboards.StandardPcId ? "PC" : "⌨");
        }

        if (!standardShown && wholeKeyboard.Visibility != ViewStates.Visible)
        {
            yield return new(context.GetString(Resource.String.two_rows)!, () => SetCompact(true), "▭");
        }

        // Keys or the whole keyboard: the keyboard opens on its letters, so the switch is a tile, not the bar's.
        var whole = wholeKeyboard.Visibility == ViewStates.Visible;
        if (!standardShown)
        {
            yield return new(whole ? context.GetString(Resource.String.keys)! : "ABC", () => SetWholeKeyboard(!whole), whole ? "⌨" : "ABC");
        }

        yield return new(context.GetString(Resource.String.minimize)!, () => SetFolded(true), "–");
        yield return new(context.GetString(Resource.String.app_settings)!, OpenApp, "⚙");
    }

    /// <summary>
    /// The bar in the title bar's place: a question asked, the chips of what
    /// was copied (<see cref="CopiedInTitleBar"/>) while there is any and
    /// nothing else is in the keys' place, or the title bar's buttons; on a
    /// phone, the toolbar, the standard keyboard's or PC's. Made small, the
    /// window has none of them.
    /// </summary>
    private void ShowBars()
    {
        var small = compact.Visibility == ViewStates.Visible;
        if (copiedInTitleBar)
        {
            // Gboard's: the toolbar, or its menu while something is in the
            // keys' place, or the question asked; sizing is ⚙'s.
            titleBar.Visibility = ViewStates.Gone;
            chipBar.Visibility = ViewStates.Gone;
            closeBar.Visibility = asking is not null && !small ? ViewStates.Visible : ViewStates.Gone;
            var standardShown = covered?.Keys == standard || standard is { Visibility: ViewStates.Visible };
            menuBar.Visibility = asking is null && !small && covered is not null && !standardShown ? ViewStates.Visible : ViewStates.Gone;
            toolbar.Visibility = asking is null && !small && covered is null && !standardShown ? ViewStates.Visible : ViewStates.Gone;
            standardBar.Visibility = asking is null && standardShown ? ViewStates.Visible : ViewStates.Gone;
            ShowEmojiRow();
            if (toolbar.Visibility == ViewStates.Visible)
            {
                FillToolbar();
            }

            if (standardBar.Visibility == ViewStates.Visible)
            {
                FillStandardBar();
            }

            return;
        }

        var chips = copiedInTitleBar && asking is null && covered is null && edited is null && clipboard.Entries.Count > 0;
        titleBar.Visibility = asking is null && !small && !chips ? ViewStates.Visible : ViewStates.Gone;
        chipBar.Visibility = chips && !small ? ViewStates.Visible : ViewStates.Gone;
        closeBar.Visibility = asking is not null && !small ? ViewStates.Visible : ViewStates.Gone;
        if (chipBar.Visibility == ViewStates.Visible && chipsStale)
        {
            FillChips();
        }
    }

    /// <summary>
    /// What was copied as chips in the title bar's middle, as Gboard has it:
    /// while there is something copied they stand between 📋 at the left and
    /// ABC and ⚙ at the right, in place of the title bar's other buttons,
    /// which ⚙ brings back with the settings; with nothing copied, the
    /// buttons (Berpiztu AI Keyboard on a phone). The bars are then as tall
    /// as Gboard's.
    /// </summary>
    public bool CopiedInTitleBar
    {
        set
        {
            copiedInTitleBar = value;
            ApplySizes();
            ShowBars();
        }
    }

    /// <summary>
    /// The editor's card, Editar: for a keyboard that shows it above itself,
    /// over the app, as Gboard does (<see cref="EditorOutside"/>).
    /// </summary>
    public View EditorCard => editorBar;

    /// <summary>
    /// The editor's card out of the panel, for its host to show above the
    /// keys, over the app (Berpiztu AI Keyboard on a phone): the keys keep
    /// their place and size while a copied text is edited, and the host is
    /// told when it shows or goes (<see cref="EditingChanged"/>).
    /// </summary>
    public bool EditorOutside
    {
        set
        {
            editorOutside = value;
            if (value)
            {
                strip.RemoveView(editorBar);
            }
        }
    }

    /// <summary>The editor's card showed or went.</summary>
    public event Action? EditingChanged;

    /// <summary>A chip for each thing copied, the pinned first, then the newest; the row scrolls across.</summary>
    private void FillChips()
    {
        chipsStale = false;
        chipItems.RemoveAllViews();
        foreach (var entry in clipboard.Entries)
        {
            var chip = Chip(entry);
            var layout = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent);
            layout.SetMargins(Dp(ChipApartDp), Dp(5), Dp(ChipApartDp), Dp(5));
            chipItems.AddView(chip, layout);
        }

        chipScroll.ScrollTo(0, 0);
    }

    /// <summary>A chip's 📋 and ✕, the room before its text and after its end, and how far ✕'s touch reaches past its glyph.</summary>
    private const float ChipSignDp = 18;
    private const float ChipGapDp = 8;
    private const float ChipEndDp = 14;
    private const float ChipCloseReachDp = 6;

    /// <summary>The room at each side of a chip in a row of them.</summary>
    private const float ChipApartDp = 4;

    /// <summary>
    /// A thing copied as Gboard's chip: rounded, 📋 (📌 when pinned) and its
    /// text's first line cut short, or the image small, and ✕ at its end where
    /// it can be deleted. A tap pastes it, a long press opens its menu (Pegar,
    /// Fijar, Editar, Eliminar).
    /// </summary>
    private LinearLayout Chip(ClipboardHistory.Entry entry, Action? dismissed = null, bool closeShown = true)
    {
        var chip = new LinearLayout(context)
        {
            Orientation = Orientation.Horizontal,
            ContentDescription = entry.IsImage ? "Paste the image" : $"Paste: {entry.Text}",
        };

        // Its signs and words, painted with its ground: in the bars' letters' colour, white on the marked one's dark ground.
        var sign = entry.IsImage || entry.Pinned ? null : new ImageView(context);
        var text = entry.IsImage ? null : new TextView(context);
        ImageView? close = null;
        void Paint(bool marked)
        {
            chip.Background = Pill(marked ? SelectedChipColor : ChipGround);
            var glyph = ColorStateList.ValueOf(marked ? Color.White : ToolGlyph);
            sign?.ImageTintList = glyph;
            text?.SetTextColor(glyph);
            close?.ImageTintList = glyph;
        }

        chip.SetGravity(GravityFlags.CenterVertical);
        chip.SetPadding(Dp(12), 0, Dp(ChipEndDp), 0);
        if (entry.IsImage)
        {
            var image = new ImageView(context) { Background = Card(), ClipToOutline = true };
            image.SetScaleType(ImageView.ScaleType.CenterCrop);
            image.SetImageBitmap(Thumbnail(clipboard.PathOf(entry), Dp(28)));
            chip.AddView(image, new LinearLayout.LayoutParams(Dp(44), Dp(28)));
        }
        else
        {
            if (entry.Pinned)
            {
                var pin = new TextView(context) { Text = "📌" };
                pin.SetTextSize(ComplexUnitType.Sp, 14);
                chip.AddView(pin);
            }
            else
            {
                sign!.SetImageResource(Resource.Drawable.ic_clipboard);
                chip.AddView(sign, new LinearLayout.LayoutParams(Dp(ChipSignDp), Dp(ChipSignDp)));
            }

            text!.Text = entry.Text!.ReplaceLineEndings(" ").Trim();
            text.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
            text.SetTextSize(ComplexUnitType.Sp, 14);
            text.SetMaxLines(1);
            text.SetMaxWidth(Dp(180));
            var spaced = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
            spaced.SetMargins(Dp(ChipGapDp), 0, 0, 0);
            chip.AddView(text, spaced);
        }

        // ✕ across from the clipboard's sign, its glyph where the chip's end was, its touch wider than the glyph.
        if (dismissed is not null)
        {
            var reach = Dp(ChipCloseReachDp);
            close = new ImageView(context)
            {
                ContentDescription = "Delete",
                Clickable = true,
                Visibility = closeShown ? ViewStates.Visible : ViewStates.Invisible,
            };
            close.SetImageResource(Resource.Drawable.ic_tool_close);
            close.SetPadding(reach, reach, reach, reach);
            close.Click += (_, _) => dismissed();
            var place = new LinearLayout.LayoutParams(Dp(ChipSignDp + (2 * ChipCloseReachDp)), Dp(ChipSignDp + (2 * ChipCloseReachDp)));
            place.SetMargins(Dp(ChipGapDp - ChipCloseReachDp), 0, 0, 0);
            chip.AddView(close, place);
            chip.SetPadding(Dp(12), 0, Dp(ChipEndDp - ChipCloseReachDp), 0);
        }

        Paint(IsPreviewed(entry));

        // While a preview shows, a tap previews this one instead; else it pastes it.
        chip.Click += (_, _) =>
        {
            if (previewed is not null)
            {
                Preview(entry);
            }
            else
            {
                Paste(entry);
            }
        };
        chip.LongClick += (_, e) =>
        {
            e.Handled = true;
            // The chip held marked while its menu is open (and its preview, which marks it again as the bar is made anew).
            Paint(true);
            ShowMenu(chip, entry, () => Paste(entry), closed: () => Paint(IsPreviewed(entry)));
        };
        return chip;
    }

    /// <summary>
    /// A chip's ground: the standard keyboard's keys', whether the keys are in sight or something stands in their place
    /// (a preview), else the cards'; the one previewed or held, <see cref="SelectedChipColor"/>.
    /// </summary>
    private Color ChipGround => StandardKeysShown ? standardColors!.Key : CardColor;

    private bool IsPreviewed(ClipboardHistory.Entry entry) => previewed is { } shown && CopyKey(shown) == CopyKey(entry);

    private Color SelectedChipColor => StandardKeysShown ? CardColor : HeldColor;

    /// <summary>Whether the standard keyboard is the one in use, its keys in sight or covered.</summary>
    private bool StandardKeysShown => standard is { } keys && (keys.Visibility == ViewStates.Visible || covered?.Keys == keys);

    /// <summary>A ground rounded at both ends, as Gboard's chips and settings are.</summary>
    private GradientDrawable Pill(Color color)
    {
        var shape = new GradientDrawable();
        shape.SetColor(color);
        shape.SetCornerRadius(Dp(100));
        return shape;
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

    /// <summary>The whole keyboard, Keys made small, or Keys in full, with the title bar where it has one; its keys made first unless the standard keyboard is shown right after.</summary>
    private void ShowMode(bool whole, bool small, bool makeKeys = true)
    {
        StopRepeat();
        Uncover();
        if (!whole)
        {
            EndDesign();
        }

        small &= !whole;
        if (makeKeys)
        {
            MakeKeys(whole, small);
        }

        if (standard is not null)
        {
            standard.Visibility = ViewStates.Gone;
        }

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
        chipSwitchKeys.Text = switchKeys.Text;
        ApplyLook();
    }

    /// <summary>
    /// The keys of the mode shown made, the first time it is: Berpiztu AI Keyboard shows the standard keyboard, and made
    /// with every panel the popup's three cost it some 136 text views each time it was made again (another language,
    /// keyboard or colours), most of the 0.7 s it took on a headset.
    /// </summary>
    private void MakeKeys(bool whole, bool small)
    {
        if (whole && !wholeKeyboardMade)
        {
            wholeKeyboard = InPlaceOf(wholeKeyboard, WholeKeyboard());
            wholeKeyboardMade = true;
        }
        else if (small && !compactMade)
        {
            compact = InPlaceOf(compact, Rows(PanelKeys.Compact));
            compactMade = true;
        }
        else if (!whole && !small && !terminalMade)
        {
            terminal = InPlaceOf(terminal, Rows(PanelKeys.Terminal));
            terminalMade = true;
        }
        else
        {
            return;
        }

        // Made at the keys' size now; their legends and modifiers as held now.
        ShowHeld();
    }

    /// <summary>A view put in the strip where another was.</summary>
    private LinearLayout InPlaceOf(LinearLayout old, LinearLayout made)
    {
        var at = strip.IndexOfChild(old);
        strip.RemoveViewAt(at);
        strip.AddView(made, at);
        return made;
    }

    /// <summary>The standard keyboard ⊞ switches to: Estándar PC from Estándar (on a headset, Estándar XR), the other from any other.</summary>
    private string OtherStandard => keyboard?.Id is Keyboards.StandardId or Keyboards.StandardXrId
        ? Keyboards.StandardPcId
        : KeyPlusPreferences.IsHeadset(context) ? Keyboards.StandardXrId : Keyboards.StandardId;

    private string OtherStandardName => context.GetString(OtherStandard switch
    {
        Keyboards.StandardPcId => Resource.String.keyboard_standard_pc,
        Keyboards.StandardXrId => Resource.String.keyboard_standard_xr,
        _ => Resource.String.keyboard_standard,
    });

    /// <summary>A keyboard of the list chosen for this screen in the keyboard's language: the keyboard made again with it.</summary>
    private void Choose(string id) => Keyboards.Select(preferences, standard?.Language.Code ?? KeyPlusPreferences.Language(context).Code, id);

    /// <summary>The standard keyboard in the keys' place, in the device's colours.</summary>
    private void ShowStandard()
    {
        StopRepeat();
        Uncover();
        EndDesign();
        Ask(null, null);
        terminal.Visibility = ViewStates.Gone;
        wholeKeyboard.Visibility = ViewStates.Gone;
        compact.Visibility = ViewStates.Gone;
        standard!.Visibility = ViewStates.Visible;
        ApplyLook();
    }

    /// <summary>The ground under the keys: the device's, Gboard's, under the standard keyboard; the panel's own under PC.</summary>
    public Color GroundColor => standard is { Visibility: ViewStates.Visible } ? standardColors!.Ground : PanelColor;

    /// <summary>Whether a standard keyboard is in sight (Standard XR, Standard PC…), and its keys' height as last measured.</summary>
    public bool ShowsStandard => standard is { Visibility: ViewStates.Visible };

    public int StandardHeight => standard?.MeasuredHeight ?? 0;

    /// <summary>The toolbar's words and signs: the device's, on the standard keyboard; white on PC.</summary>
    private Color ToolGlyph => StandardKeysShown ? standardColors!.Glyph : Color.White;

    /// <summary>
    /// The ground and the toolbars in the look of the keys in sight: Gboard's,
    /// in the device's colours, or PC's; the standard keyboard in a window a
    /// card with its bar under the keys.
    /// </summary>
    private void ApplyLook()
    {
        var card = inWindow && standardColors is not null;
        if (card)
        {
            var ground = new GradientDrawable();
            ground.SetColor(GroundColor);
            ground.SetCornerRadius(Dp(CardCornerDp));
            strip.Background = ground;
            strip.OutlineProvider = ViewOutlineProvider.Background;
            strip.ClipToOutline = true;
            strip.Elevation = Dp(CardShadowDp);
        }
        else
        {
            strip.SetBackgroundColor(GroundColor);
            strip.ClipToOutline = false;
            strip.Elevation = 0;
        }

        if (floatingBar is not null)
        {
            floatingBar.Visibility = card ? ViewStates.Visible : ViewStates.Gone;
            TintFloatingBar();
        }

        if (!card && cardResizing)
        {
            ShowCardResizing(false);
        }

        Tint(toolbar, ToolGlyph);
        Tint(menuBar, ToolGlyph);
        ShowBars();
    }

    /// <summary>Whether the keyboard stands in a window of its own (Modo ventana, Flotante): the standard keyboard then a card, as Gboard's floating one.</summary>
    public bool InWindow
    {
        set
        {
            if (inWindow != value)
            {
                inWindow = value;
                ApplyLook();
            }
        }
    }

    /// <summary>∨ in the floating keyboard's bar: the keyboard hidden.</summary>
    public event Action? HideRequested;

    /// <summary>The floating bar's parts whose colours follow the keys in sight: Gboard's on the standard keyboard, PC's own on PC.</summary>
    private ImageView? floatingHide;
    private GradientDrawable? floatingHandle;
    private GradientDrawable? resetGround;

    private void TintFloatingBar()
    {
        var standardShown = standard is { Visibility: ViewStates.Visible };
        floatingHide?.SetColorFilter(ToolGlyph, PorterDuff.Mode.SrcIn!);
        floatingHandle?.SetColor(standardShown ? standardColors!.Handle : DimTextColor);
        resetGround?.SetColor(standardShown ? standardColors!.ResetGround : CardColor);
        if (cardReset is not null)
        {
            Tint(cardReset, ToolGlyph);
        }
    }

    /// <summary>Reset while the floating keyboard is resized: its usual size and place.</summary>
    public event Action? ResetSizeRequested;

    /// <summary>The floating card resized, or no longer: its outline, its corners to hold and Reset; it ends by itself a while after the last touch.</summary>
    private void ShowCardResizing(bool resizing)
    {
        cardResizing = resizing && inWindow && standardColors is not null;
        strip.Foreground = cardResizing
            ? new CardOutline(standardColors!.Accent, Dp(CardLineDp), Dp(CardMarkDp), Dp(CardCornerDp), Dp(CardMarkReachDp))
            : null;
        if (cardCorners.Count == 0 && cardResizing)
        {
            foreach (var (corner, gravity) in new[]
            {
                (Corner.TopLeft, GravityFlags.Top | GravityFlags.Left),
                (Corner.TopRight, GravityFlags.Top | GravityFlags.Right),
                (Corner.BottomLeft, GravityFlags.Bottom | GravityFlags.Left),
                (Corner.BottomRight, GravityFlags.Bottom | GravityFlags.Right),
            })
            {
                // Above the card, which stands raised for its shadow: touches go to the highest view first.
                var hold = new View(context) { ContentDescription = "Drag to resize the keyboard", Elevation = Dp(CardShadowDp) + 1 };
                Draggable(hold, (drag, dx, dy) =>
                {
                    StayResizing();
                    Resizing?.Invoke(corner, drag, dx, dy);
                }, tapped: null);
                root.AddView(hold, new FrameLayout.LayoutParams(Dp(CornerReachDp), Dp(CornerReachDp), gravity));
                cardCorners.Add(hold);
            }
        }

        foreach (var hold in cardCorners)
        {
            hold.Visibility = cardResizing ? ViewStates.Visible : ViewStates.Gone;
        }

        if (cardReset is not null)
        {
            cardReset.Visibility = cardResizing ? ViewStates.Visible : ViewStates.Gone;
        }

        StayResizing();
    }

    /// <summary>The card stays resized a while longer, from this touch: else it ends by itself.</summary>
    private void StayResizing()
    {
        if (endCardResize is not null)
        {
            handler.RemoveCallbacks(endCardResize);
        }

        if (cardResizing)
        {
            endCardResize ??= new Java.Lang.Runnable(() => ShowCardResizing(false));
            handler.PostDelayed(endCardResize, CardResizeMs);
        }
    }

    /// <summary>The floating keyboard's bar: ∨ at the left, which hides it, and the handle in the middle, dragged to move the window.</summary>
    private FrameLayout FloatingBar()
    {
        var bar = new FrameLayout(context) { Visibility = ViewStates.Gone, Clickable = true };
        // A tap on the bar beside the handle resizes the card, as Gboard's.
        bar.Click += (_, _) => ShowCardResizing(!cardResizing);
        var hide = new ImageView(context) { ContentDescription = "Hide the keyboard" };
        floatingHide = hide;
        hide.SetImageResource(Resource.Drawable.ic_tool_hide);
        hide.SetScaleType(ImageView.ScaleType.FitCenter);
        hide.Click += (_, _) => HideRequested?.Invoke();
        bar.AddView(hide, new FrameLayout.LayoutParams(Dp(HideDp), Dp(HideDp), GravityFlags.Left | GravityFlags.CenterVertical) { LeftMargin = Dp(HideCentreDp - (HideDp / 2)) });

        var handle = new View(context) { ContentDescription = "Drag to move the keyboard" };
        var pill = new GradientDrawable();
        pill.SetCornerRadius(Dp(HandleHeightDp));
        floatingHandle = pill;
        handle.Background = pill;
        // Held anywhere around the handle, not on it alone: it is thin.
        var reach = new FrameLayout(context);
        reach.AddView(handle, new FrameLayout.LayoutParams(Dp(HandleWidthDp), Dp(HandleHeightDp), GravityFlags.Center));
        Draggable(reach, (drag, dx, dy) =>
        {
            // Moved, the card is no longer resized, as Gboard's.
            ShowCardResizing(false);
            ReportMoving(drag, dx, dy);
        }, tapped: null);
        bar.AddView(reach, new FrameLayout.LayoutParams(Dp(HandleWidthDp * 2), ViewGroup.LayoutParams.MatchParent, GravityFlags.Center));

        var reset = new LinearLayout(context) { Orientation = Orientation.Horizontal, Visibility = ViewStates.Gone, Clickable = true, ContentDescription = context.GetString(Resource.String.reset_size) };
        reset.SetGravity(GravityFlags.Center);
        var pillGround = new GradientDrawable();
        pillGround.SetCornerRadius(Dp(ResetHeightDp) / 2f);
        reset.Background = pillGround;
        resetGround = pillGround;
        var sign = new ImageView(context);
        sign.SetImageResource(Resource.Drawable.ic_tool_reset);
        reset.AddView(sign, new LinearLayout.LayoutParams(Dp(18), Dp(18)) { RightMargin = Dp(6) });
        var words = new TextView(context) { Text = context.GetString(Resource.String.reset_size) };
        words.SetTextSize(ComplexUnitType.Sp, 14);
        reset.AddView(words);
        reset.Click += (_, _) =>
        {
            ShowCardResizing(false);
            ResetSizeRequested?.Invoke();
        };
        var resetPlace = new FrameLayout.LayoutParams(Dp(ResetWidthDp), Dp(ResetHeightDp), GravityFlags.Right | GravityFlags.CenterVertical) { RightMargin = Dp(ResetRightDp) };
        bar.AddView(reset, resetPlace);

        // Reset in the middle of the room right of the handle, no further in than Gboard's: a narrow card has little.
        bar.LayoutChange += (_, e) =>
        {
            var room = ((e.Right - e.Left) / 2) - Dp(HandleWidthDp / 2);
            var margin = Math.Clamp((room - Dp(ResetWidthDp)) / 2, 0, Dp(ResetRightDp));
            if (resetPlace.RightMargin != margin)
            {
                resetPlace.RightMargin = margin;
                reset.Post(reset.RequestLayout);
            }
        };
        cardReset = reset;
        return bar;
    }

    /// <summary>Every word and sign within a bar the colour given.</summary>
    private static void Tint(View view, Color color)
    {
        switch (view)
        {
            case TextView words:
                words.SetTextColor(color);
                break;
            case ImageView sign:
                sign.ImageTintList = ColorStateList.ValueOf(color);
                break;
            case ViewGroup group:
                for (var i = 0; i < group.ChildCount; i++)
                {
                    Tint(group.GetChildAt(i)!, color);
                }

                break;
        }
    }

    /// <summary>
    /// The standard keyboard, its keys doing what PC's do: typing as the
    /// panel types, a letter after a swiped word with its space first; ⌫ and
    /// Enter as keys, Enter as the field asks where the keyboard says how
    /// (<see cref="Enter"/>); its swipes read as the whole keyboard's.
    /// </summary>
    private StandardKeyboard MakeStandard() => new(context, standardColors!)
    {
        SelectionCase = () => SelectionCase?.Invoke() == true,
        Typed = text =>
        {
            var letter = text.Length == 1 && char.IsLetter(text[0]);
            EndOffer();
            if (EndSwipe(letter))
            {
                Type(" ");
            }

            // @ ends no word, as Gboard's: an address goes on.
            var fixing = letter || text == "@" ? null : FixWord();
            Type(text);
            OfferWords();
            if (fixing is { } offered)
            {
                OfferFix(offered, text);
            }

            if (text is "." or "?" or "!")
            {
                CorrectClosed();
            }
        },
        Deleted = () =>
        {
            EndOffer();
            if (!DeleteSwiped())
            {
                EndSwipe(letter: false);
                Send(Keycode.Del, MetaKeyStates.None);
            }

            OfferWordsSoon();
        },
        KeyPressed = (code, meta) =>
        {
            EndOffer();
            EndSwipe(letter: false);
            Send(code, meta);
            OfferWordsSoon();
        },
        Pc = keyboard?.Base == Keyboards.StandardPcBase,
        Xr = keyboard?.Base == Keyboards.StandardXrBase,
        Design = keyboard is { Standard: true } ? StandardDesign.FromText(keyboard.DesignFor(docked: !ShowsFloating)) : null,
        EmojisAsked = ShowEmojis,
        Entered = () =>
        {
            EndOffer();
            EndSwipe(letter: false);
            CorrectBeforeEnter();
            ForgetTyping();
            if (Enter is { } enter)
            {
                enter();
            }
            else
            {
                Send(Keycode.Enter, MetaKeyStates.None);
            }
        },
        Language = KeyPlusPreferences.Language(context),
        NextLanguage = NextLanguage,
        NotMade = NotMadeYet,
        Swiped = (path, capital, capitals) => ReadSwipe(path, capital, capitals),
        GlidesIn = language => Swiping is not null && GlidesFor?.Invoke(language) != false,
        Visibility = ViewStates.Gone,
    };

    /// <summary>The app's settings the standard keyboard follows, read again as it opens: they may have changed meanwhile.</summary>
    public void ReadSettings()
    {
        var kept = KeyPlusPreferences.Global(context);
        vibrates = kept.GetBoolean(KeyPlusPreferences.VibrateKey, true);
        sounds = kept.GetBoolean(KeyPlusPreferences.SoundKey, false);
        showsTrail = kept.GetBoolean(KeyPlusPreferences.SwipeTrailKey, true);
        deletesSwipedWord = kept.GetBoolean(KeyPlusPreferences.SwipeDeleteWordKey, true);
        var rowBefore = emojiRowOn;
        emojiRowOn = kept.GetBoolean(KeyPlusPreferences.EmojiRowKey, true);
        if (emojiRowOn != rowBefore && strip.IsAttachedToWindow)
        {
            ShowBars();
        }

        if (standard is not null)
        {
            standard.LongPressMs = kept.GetInt(KeyPlusPreferences.LongPressKey, KeyPlusPreferences.LongPressDefault);
            standard.Vibrates = vibrates;
            standard.Sounds = sounds;
            standard.ShowsTrail = showsTrail;
        }
    }

    /// <summary>The app's settings PC follows too: a key's touch felt and heard, a swipe's trail drawn.</summary>
    private bool vibrates = true;
    private bool sounds;
    private bool showsTrail = true;

    /// <summary>Glide typing › Delete a swiped word whole: ⌫ right after a swipe takes all it typed.</summary>
    private bool deletesSwipedWord = true;

    /// <summary>Preferencias' Extra line for suggestions and recent emoji (<see cref="KeyPlusPreferences.EmojiRowKey"/>).</summary>
    private bool emojiRowOn = true;

    /// <summary>🌐, or a swipe along the space bar: the standard keyboard in its next language, kept for every screen, the kind of keyboard in sight kept in it (Estándar, Estándar PC); where the screen shows another keyboard in it, the keyboard is made again with that one (KeyPlusKeyboard).</summary>
    private void NextLanguage()
    {
        EndSwipe(letter: false);
        var languages = StandardKeys.Languages;
        var next = languages[(languages.ToList().IndexOf(standard!.Language) + 1) % languages.Count];
        var remade = false;
        if (keyboard is { } shown && keyboardScreen is { } screen)
        {
            Keyboards.Carry(context, preferences, screen, next.Code, shown);
            remade = Keyboards.Selected(context, preferences, screen, next.Code) != shown;
        }

        // Another keyboard in the new language (Standard PC's design is its language's) is made again whole: these keys are
        // left as they are until then, or the new language's letters were drawn on them first and the keyboard twice.
        if (!remade)
        {
            standard.Language = next;
        }

        KeyPlusPreferences.Global(context).Edit()!.PutString(KeyPlusPreferences.LanguageKey, next.Code)!.Apply();
        LanguageChanged?.Invoke(next);
    }

    /// <summary>The standard keyboard's language changed (🌐): what goes by language, its tools lit or not, made again.</summary>
    public event Action<StandardLanguage>? LanguageChanged;

    /// <summary>Enter as the field asks (Go, Search, Send…), where the keyboard can say; none, the Enter key.</summary>
    public Action? Enter { get; set; }

    /// <summary>A key or button whose part is not made yet: said so, for a moment.</summary>
    public void NotMadeYet(string what) =>
        Toast.MakeText(context, context.GetString(Resource.String.not_made_yet, new Java.Lang.String(what)), ToastLength.Short)!.Show();

    /// <summary>
    /// A setting ⚙ shows: its words and what it does; with a sign, it shows
    /// as Gboard's (<see cref="ShowSettings"/>), the one in use marked.
    /// </summary>
    public sealed record Setting(string Label, Action Chosen, string? Sign = null, bool InUse = false);

    /// <summary>
    /// The settings ⚙ shows in the keys' place, each a row that does what it
    /// says (Berpiztu AI Keyboard: choose another keyboard); ⚙ stands at the
    /// right in ✕'s place, for a keyboard is not closed but changed for
    /// another. None, no ⚙, and ✕ closes the popup.
    /// </summary>
    public IReadOnlyList<Setting> Settings
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
    private void Cover(View cover, View button)
    {
        Uncover();
        var keys = ShownKeys;
        cover.LayoutParameters!.Height = Math.Max(keys.Height, RowHeight);
        keys.Visibility = ViewStates.Gone;
        cover.Visibility = ViewStates.Visible;
        covered = (keys, cover, button);
        MarkTitleButton(button, held: true);
        ShowBars();
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
        if (now.Cover == previewView)
        {
            imageViewer.SetImageDrawable(null);
            previewed = null;
        }

        MarkTitleButton(now.Button, held: false);
        covered = null;
        ShowBars();
    }

    /// <summary>
    /// A thing copied previewed whole in the keys' place, nothing outside the keyboard used: an image as large as their
    /// room lets it, a tap opening it in the phone's own viewer (<see cref="ViewImage"/>); a text whole, scrolling, a tap
    /// opening it in the editor (<see cref="Edit"/>). While one shows, a tap on a chip previews that one; ✕ the keys again.
    /// </summary>
    private void Preview(ClipboardHistory.Entry entry)
    {
        // Which one it is known before the bars are made again, so its chip is the one marked.
        Uncover();
        previewed = entry;
        Cover(previewView, clipboardButton);
        imageViewer.Visibility = entry.IsImage ? ViewStates.Visible : ViewStates.Gone;
        textPreview.Visibility = entry.IsImage ? ViewStates.Gone : ViewStates.Visible;
        if (entry.IsImage)
        {
            imageViewer.SetImageBitmap(Thumbnail(clipboard.PathOf(entry), previewView.LayoutParameters!.Height));
            return;
        }

        previewText.Text = entry.Text;
        previewText.SetTextColor(ToolGlyph);
        textPreview.ScrollTo(0, 0);
    }

    /// <summary>The keyboard shown again: the thing previewed read again, as the list has it now, an image edited meanwhile changed.</summary>
    public void PreviewAgain()
    {
        if (previewed is { } shown && clipboard.Entries.FirstOrDefault(entry => CopyKey(entry) == CopyKey(shown)) is { } now)
        {
            Preview(now);
        }
    }

    /// <summary>The copied things in the keys' place, a list that scrolls.</summary>
    private void ShowClipboard()
    {
        FillClipboard();
        clipboardView.ScrollTo(0, 0);
        Cover(clipboardView, clipboardButton);
    }

    /// <summary>
    /// The settings in the keys' place, from the first: a tap does it and
    /// brings the keys back. With their signs, as Gboard shows them: rounded
    /// tiles, a sign and the words, two or three to a row as the width
    /// allows, the one in use outlined; else a row each across.
    /// </summary>
    private void ShowSettings()
    {
        shownSettings = copiedInTitleBar ? [.. PanelTiles(), .. settings] : settings;
        settingsRows.RemoveAllViews();
        if (shownSettings.Count > 0 && shownSettings.All(setting => setting.Sign is not null))
        {
            ShowSettingTiles();
        }
        else
        {
            foreach (var setting in shownSettings)
            {
                var row = KeyView(setting.Label);
                row.Gravity = GravityFlags.CenterVertical | GravityFlags.Left;
                row.SetPadding(Dp(12), 0, Dp(12), 0);
                row.SetTextSize(ComplexUnitType.Px, RowHeight * 0.4f);
                row.Click += (_, _) => Choose(setting);
                settingsRows.AddView(row, KeyMargins(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, RowHeight - (2 * Dp(KeyMarginDp)))));
            }
        }

        settingsView.ScrollTo(0, 0);
        Cover(settingsView, settingsButton);
    }

    /// <summary>
    /// Gboard's settings: rows of rounded tiles, as many to a row as a
    /// tile's least width allows, two at least and three at most, each row as
    /// tall as the keys' place allows them all, up to Gboard's; the last
    /// row's tiles share its whole width, so their words are never cut.
    /// </summary>
    private void ShowSettingTiles()
    {
        var columns = Math.Clamp(strip.Width / Dp(180), 2, 3);
        var rows = (shownSettings.Count + columns - 1) / columns;
        var padding = Dp(6);
        var room = Math.Max(ShownKeys.Height, RowHeight) - (2 * padding);
        var lineHeight = Math.Min(Dp(52 + 12), room / rows);
        settingsRows.SetPadding(padding, padding, padding, padding);
        var wordsSp = SettingWordsSp(columns, padding);
        LinearLayout? line = null;
        for (var i = 0; i < shownSettings.Count; i++)
        {
            if (i % columns == 0)
            {
                line = new LinearLayout(context) { Orientation = Orientation.Horizontal };
                settingsRows.AddView(line, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, lineHeight));
            }

            line!.AddView(SettingTile(shownSettings[i], wordsSp), TileMargins());
        }

        LinearLayout.LayoutParams TileMargins()
        {
            var layout = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f);
            layout.SetMargins(Dp(6), Dp(4), Dp(6), Dp(4));
            return layout;
        }
    }

    /// <summary>
    /// The settings' words' one size, in sp: a list's, or, where a tile is
    /// too narrow for the longest, smaller a step at a time until it fits,
    /// so no tile's words are cut and all are alike. A tile's room for its
    /// words: its share of the row less its margins, padding and sign.
    /// </summary>
    private int SettingWordsSp(int columns, int padding)
    {
        // Each tile's own room: a full row's share, or the last row's, whose few tiles share it all.
        var full = shownSettings.Count - (shownSettings.Count % columns);
        var last = shownSettings.Count - full;
        var rooms = shownSettings.Select((_, i) => WordsRoom(i < full ? columns : last)).ToArray();
        var paint = new Android.Text.TextPaint(PaintFlags.AntiAlias);
        var size = (int)MenuTextSp;
        for (; size > SmallestSettingSp; size--)
        {
            paint.TextSize = TypedValue.ApplyDimension(ComplexUnitType.Sp, size, context.Resources!.DisplayMetrics);
            if (shownSettings.Select((setting, i) => paint.MeasureText(setting.Label) <= rooms[i]).All(fits => fits))
            {
                break;
            }
        }

        return size;

        int WordsRoom(int inRow)
        {
            var tileWidth = ((strip.Width - (2 * padding)) / inRow) - (2 * Dp(6));
            return tileWidth - Dp(TilePaddingDp + 12) - Dp(TileSignDp) - Dp(TileGapDp);
        }
    }

    /// <summary>The least the settings' words go down to, in sp.</summary>
    private const int SmallestSettingSp = 8;

    /// <summary>A setting tile's room before its sign, its sign's width and the gap before its words, in dp.</summary>
    private const int TilePaddingDp = 14;
    private const int TileSignDp = 24;
    private const int TileGapDp = 8;

    /// <summary>A setting as Gboard's tile: rounded, its sign and its words; the one in use outlined in the held keys' colour.</summary>
    private LinearLayout SettingTile(Setting setting, int wordsSp)
    {
        var ground = Pill(KeyColor);
        if (setting.InUse)
        {
            ground.SetStroke(Dp(2), HeldColor);
        }

        var tile = new LinearLayout(context) { Orientation = Orientation.Horizontal, Background = ground, ContentDescription = setting.Label };
        tile.SetGravity(GravityFlags.CenterVertical);
        tile.SetPadding(Dp(TilePaddingDp), 0, Dp(12), 0);
        var sign = new TextView(context) { Text = setting.Sign, Gravity = GravityFlags.Center };
        sign.SetTextColor(setting.InUse ? HeldColor : Color.White);
        sign.SetTextSize(ComplexUnitType.Sp, 20);
        tile.AddView(sign, new LinearLayout.LayoutParams(Dp(TileSignDp), ViewGroup.LayoutParams.WrapContent));
        var words = new TextView(context) { Text = setting.Label, Gravity = GravityFlags.CenterVertical };
        words.SetTextColor(Color.White);
        words.SetMaxLines(1);
        words.SetTextSize(ComplexUnitType.Sp, wordsSp);
        var spaced = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f);
        spaced.SetMargins(Dp(TileGapDp), 0, 0, 0);
        tile.AddView(words, spaced);
        tile.Click += (_, _) => Choose(setting);
        return tile;
    }

    /// <summary>A setting chosen: the keys back, and it done.</summary>
    private void Choose(Setting setting)
    {
        Uncover();
        setting.Chosen();
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
    /// and up to a third of the row wide, or twice as wide as the row is
    /// tall where that is more (a keyboard in a narrow window), each with a small ✏ (a text) over
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
                text.SetMaxWidth(Math.Max(2 * height, root.Width / 3));
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
            column.AddView(Small(WordButton("✏", "Edit it right here", DimTextColor, () => Edit(entry))), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
        }

        column.AddView(Small(WordButton("✕", "Take it out of the list", DimTextColor, () => clipboard.Remove(entry))), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
        return column;

        // A word button's padding is wider than this column: the sign alone, whole.
        static TextView Small(TextView button)
        {
            button.SetPadding(0, 0, 0, 0);
            return button;
        }
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

        chipsStale = true;
        ShowBars();
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
    /// A thing copied's menu, as Gboard has it: a card beside it with Paste,
    /// Pin or Unpin, Edit (a text) and Delete, each with its Material Symbols
    /// sign in the text's colour; a touch outside it closes it. It never takes
    /// the focus, so the field keeps it.
    /// </summary>
    private void ShowMenu(View anchor, ClipboardHistory.Entry entry, Action pasted, Action? closed = null)
    {
        var menu = new LinearLayout(context) { Orientation = Orientation.Vertical, Background = Card() };
        menu.SetPadding(0, Dp(4), 0, Dp(4));
        var popup = new PopupWindow(menu, ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, false)
        {
            OutsideTouchable = true,
            Elevation = Dp(8),
        };
        popup.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
        if (closed is not null)
        {
            popup.DismissEvent += (_, _) => closed();
        }

        void Item(int sign, string label, Action chosen)
        {
            var icon = context.GetDrawable(sign)!.Mutate()!;
            icon.SetTint(Color.White);
            var item = new TextView(context)
            {
                Text = label,
                Gravity = GravityFlags.CenterVertical,
                CompoundDrawablePadding = Dp(12),
                Background = new RippleDrawable(ColorStateList.ValueOf(RippleColor), null, new ColorDrawable(Color.White)),
            };
            item.SetCompoundDrawablesRelativeWithIntrinsicBounds(icon, null, null, null);
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

        Item(Resource.Drawable.ic_menu_paste, context.GetString(Resource.String.paste)!, pasted);
        Item(Resource.Drawable.ic_menu_view, context.GetString(Resource.String.preview)!, () => Preview(entry));

        Item(entry.Pinned ? Resource.Drawable.ic_menu_unpin : Resource.Drawable.ic_menu_pin,
            entry.Pinned ? context.GetString(Resource.String.unpin)! : context.GetString(Resource.String.pin)!, () => clipboard.SetPinned(entry, !entry.Pinned));
        if (!entry.IsImage)
        {
            Item(Resource.Drawable.ic_grid_edit, context.GetString(Resource.String.edit)!, () => Edit(entry));
        }
        else if (EditImage is { } editImage)
        {
            Item(Resource.Drawable.ic_grid_edit, context.GetString(Resource.String.edit)!, () => editImage(entry));
        }

        Item(Resource.Drawable.ic_menu_delete, context.GetString(Resource.String.erase)!, () => clipboard.Remove(entry));
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
        if (!editorOutside)
        {
            editorBar.LayoutParameters = CardMargins(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, inRow ? clipboardAbove - Dp(8) : ViewGroup.LayoutParams.WrapContent));
        }

        // As a card, one line that scrolls across, as Gboard's: the card never
        // grows with the text, nor moves the keys. In the row, its lines.
        editor.SetMaxLines(inRow ? 3 : 1);
        editor.SetHorizontallyScrolling(!inRow);
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
        editor.SetSelection(editor.Text?.Length ?? 0);
        editor.RequestFocus();
        ShowBars();
        EditingChanged?.Invoke();
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

        ShowBars();
        EditingChanged?.Invoke();
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
            if (text is "?" or "!")
            {
                MendSentence(text[0]);
            }

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

    /// <summary>The whole keyboard as the screen's design has it, or as made.</summary>
    private LinearLayout WholeKeyboard() =>
        design is null ? Rows(PanelKeys.Spanish, PanelKeys.SpanishSide) : DesignedRows(design);

    /// <summary>
    /// The whole keyboard's keys dragged into place (⚙ → Diseñar): the
    /// whole keyboard shown, as the screen's design has it, or as made.
    /// </summary>
    public void Design()
    {
        if (keyboard is { Standard: true } && standard is not null)
        {
            StartStandardDesign();
            return;
        }

        if (wholeKeyboard.Visibility != ViewStates.Visible)
        {
            SetWholeKeyboard(true);
        }

        design ??= KeyboardDesign.Usual();
        ShowDesign();
        designing = true;
        designBar.Visibility = ViewStates.Visible;
    }

    /// <summary>
    /// While designing, above the keys: the keys taken out, side by side,
    /// where a key dragged off the keys lands and whence it is dragged back;
    /// under them Ajustar, Restablecer and Hecho.
    /// </summary>
    private LinearLayout DesignBar()
    {
        var bar = new LinearLayout(context) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        var aside = new HorizontalScrollView(context) { HorizontalScrollBarEnabled = false };
        aside.AddView(asideKeys, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent));
        bar.AddView(aside, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(DesignAsideDp)));
        var buttons = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        buttons.AddView(new View(context), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        buttons.AddView(DesignButton("−", "The marked key a quarter narrower", () => ResizeSelected(wider: false)),
            new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent));
        buttons.AddView(DesignButton("+", "The marked key a quarter wider", () => ResizeSelected(wider: true)),
            new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent));
        buttons.AddView(DesignButton(context.GetString(Resource.String.add_row)!, "A new empty row at the top", AddDesignRow),
            new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent));
        buttons.AddView(DesignButton(context.GetString(Resource.String.spread_keys)!, "The holes closed, every key the same size, the longest row as wide as the keyboard", SpreadDesign),
            new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent));
        buttons.AddView(DesignButton(context.GetString(Resource.String.reset_size)!, "The keyboard as it was made, on this screen",
            () => Ask(context.GetString(Resource.String.design_reset_ask)!, ResetDesign)), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent));
        buttons.AddView(DesignButton(context.GetString(Resource.String.done)!, "Type with the keys again", EndDesign),
            new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent));
        bar.AddView(buttons, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(DesignButtonsDp)));
        return bar;
    }

    /// <summary>The keys taken out, side by side, while designing: the design's own row -1.</summary>
    private readonly LinearLayout asideKeys;

    /// <summary>The keys taken out, as small keys that drag back; none yet, what to do.</summary>
    private void FillAside()
    {
        asideKeys.RemoveAllViews();
        designKeys.RemoveAll(key => key.Row == AsideRow);
        if (design is not { Removed.Count: > 0 })
        {
            var hint = new TextView(context) { Text = context.GetString(Resource.String.aside_hint)!, Gravity = GravityFlags.CenterVertical };
            hint.SetTextColor(DimTextColor);
            hint.SetTextSize(ComplexUnitType.Sp, 13);
            hint.SetPadding(Dp(12), 0, Dp(12), 0);
            asideKeys.AddView(hint, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent));
            return;
        }

        foreach (var id in design.Removed)
        {
            var key = KeyboardDesign.Key(id);
            var view = KeyView(key.Label.Length > 0 ? key.Label : "Espacio");
            view.SetTextSize(ComplexUnitType.Sp, 13);
            view.SetPadding(Dp(8), 0, Dp(8), 0);
            view.Touch += (_, e) =>
            {
                e.Handled = true;
                DesignTouch(view, e.Event!);
            };
            asideKeys.AddView(view, KeyMargins(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent)));
            designKeys.Add((id, AsideRow, asideKeys.ChildCount - 1, view));
        }
    }

    /// <summary>The row the keys taken out stand in, while designing.</summary>
    private const int AsideRow = -1;

    private const int DesignAsideDp = 40;
    private const int DesignButtonsDp = 36;

    /// <summary>Repartir: the holes and empty rows gone, every key the same size, the longest row as wide as the keyboard; kept for the screen.</summary>
    private void SpreadDesign() => ChangeDesign(shown => shown.Spread());

    /// <summary>+ Fila: a new empty row at the top; kept for the screen.</summary>
    private void AddDesignRow() => ChangeDesign(shown => shown.AddRow());

    /// <summary>The design changed, kept for the screen and shown.</summary>
    private void ChangeDesign(Action<KeyboardDesign> change)
    {
        if (design is null)
        {
            return;
        }

        change(design);
        KeepDesign(design);
        ShowDesign();
    }

    private TextView DesignButton(string label, string description, Action pressed)
    {
        var button = new TextView(context)
        {
            Text = label,
            Gravity = GravityFlags.Center,
            ContentDescription = description,
            Background = new RippleDrawable(ColorStateList.ValueOf(RippleColor), null, new ColorDrawable(Color.White)),
        };
        button.SetTextColor(Color.White);
        button.SetTextSize(ComplexUnitType.Sp, 14);
        button.SetPadding(Dp(10), 0, Dp(10), 0);
        button.Click += (_, _) => pressed();
        return button;
    }

    private void EndDesign()
    {
        if (!designing)
        {
            return;
        }

        designing = false;
        designBar.Visibility = ViewStates.Gone;
        selectedKey = null;
        ShowSelection();
    }

    /// <summary>A key tapped while designing: marked, or no longer marked if it was.</summary>
    private void Select(int id)
    {
        selectedKey = selectedKey == id ? null : id;
        ShowSelection();
    }

    /// <summary>
    /// The marked key filled as a held key is, the others as keys are, after
    /// the modifiers are shown held or not: else a marked Mayús, Ctrl or Alt
    /// was painted over and did not look marked.
    /// </summary>
    private void ShowSelection()
    {
        ShowHeld();
        foreach (var (id, view) in designViews)
        {
            if (id == selectedKey)
            {
                view.Background = KeyBackground(HeldColor);
                view.SetTextColor(HeldTextColor);
            }
            else if (!KeyboardDesign.Key(id).IsModifier)
            {
                view.Background = KeyBackground(KeyColor);
                view.SetTextColor(Color.White);
            }
        }
    }

    /// <summary>− or +: the marked key a quarter narrower or wider, nothing else moved; kept for the screen.</summary>
    private void ResizeSelected(bool wider)
    {
        if (selectedKey is { } id)
        {
            ChangeDesign(shown => shown.Resize(id, wider));
        }
    }

    /// <summary>Restablecer: the screen's keyboard as made, still being designed.</summary>
    private void ResetDesign()
    {
        KeepDesign(null);
        design = KeyboardDesign.Usual();
        ShowDesign();
    }

    /// <summary>
    /// The whole keyboard made again as the design has it now: the old one's
    /// keys taken out of every list that sizes, marks or labels them.
    /// </summary>
    private void ShowDesign()
    {
        var old = wholeKeyboard;
        var kept = new HashSet<View>(designViews.Values);
        var gone = new HashSet<View>(Descendants(old).Where(view => !kept.Contains(view)));
        characterKeys.RemoveAll(key => gone.Contains(key.View));
        labelKeys.RemoveAll(key => gone.Contains(key.View));
        fnKeys.RemoveAll(key => gone.Contains(key.View));
        modifierKeys.RemoveAll(key => gone.Contains(key.View));
        rowViews.RemoveAll(gone.Contains);
        sideKeys.RemoveAll(gone.Contains);
        if (sideColumn is not null && gone.Contains(sideColumn))
        {
            sideColumn = null;
        }

        var at = strip.IndexOfChild(old);
        strip.RemoveViewAt(at);
        wholeKeyboard = WholeKeyboard();
        wholeKeyboard.Visibility = old.Visibility;
        strip.AddView(wholeKeyboard, at);
        ApplySizes();
        SizeLabels();
        ShowSelection();
        FitLegendsSoon();
    }

    private static IEnumerable<View> Descendants(View view)
    {
        yield return view;
        if (view is ViewGroup group)
        {
            for (var i = 0; i < group.ChildCount; i++)
            {
                foreach (var child in Descendants(group.GetChildAt(i)!))
                {
                    yield return child;
                }
            }
        }
    }

    /// <summary>
    /// The design's rows, each key as wide as its weight of
    /// <see cref="KeyboardDesign.Width"/> whatever was moved: a shorter row's
    /// room left empty at its right, a longer one running past the edge
    /// (<see cref="AtWidth"/>); Entrar as tall as two rows where both have
    /// it, the keys before it on its left and those after it on its right.
    /// The keys taken out, above, while designing.
    /// </summary>
    private LinearLayout DesignedRows(KeyboardDesign shown)
    {
        designKeys.Clear();
        designRows.Clear();
        var rows = new LinearLayout(context) { Orientation = Orientation.Vertical };
        for (var r = 0; r < shown.Rows.Count; r++)
        {
            if (shown.TallWith(r) is not { } pair)
            {
                var keys = shown.Rows[r];
                var row = AtWidth(DesignedRow(r, keys, 0), shown.WeightOf(keys), shown.Width, ViewGroup.LayoutParams.MatchParent);
                rows.AddView(row, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, RowHeight));
                rowViews.Add(row);
                designRows.Add((r, row));
                continue;
            }

            var tall = KeyboardDesign.TallId;
            var block = new LinearLayout(context) { Orientation = Orientation.Horizontal };
            if (pair.Before > 0)
            {
                block.AddView(TwoRows(r, pair.Upper[..pair.UpperAt], pair.Lower[..pair.LowerAt], pair.Before, 0, 0),
                    new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, pair.Before));
            }

            var tallView = DesignView(tall);
            block.AddView(tallView, KeyMargins(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, shown.WeightOf(tall))));
            designKeys.Add((tall, r, pair.UpperAt, tallView));
            designKeys.Add((tall, r + 1, pair.LowerAt, tallView));
            if (pair.After > 0)
            {
                block.AddView(TwoRows(r, pair.Upper[(pair.UpperAt + 1)..], pair.Lower[(pair.LowerAt + 1)..], pair.After, pair.UpperAt + 1, pair.LowerAt + 1),
                    new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, pair.After));
            }

            rows.AddView(AtWidth(block, pair.Weight, shown.Width, ViewGroup.LayoutParams.WrapContent));
            r++;
        }

        FillAside();
        return rows;
    }

    /// <summary>
    /// A row, or Entrar's two, as wide as its keys' <paramref name="weight"/>
    /// of the keyboard's <paramref name="width"/>: its room left empty at its
    /// right; or, wider than the keyboard, running past its edge and cut
    /// there, until Ajustar. The keys are never made smaller by it.
    /// </summary>
    private View AtWidth(LinearLayout content, float weight, float width, int height)
    {
        if (weight <= width + 0.01f)
        {
            AddRoom(content, width - weight);
            return content;
        }

        var frame = new FrameLayout(context);
        frame.AddView(content, new FrameLayout.LayoutParams(0, height));
        frame.LayoutChange += (_, e) =>
        {
            var wide = (int)Math.Round((e.Right - e.Left) * weight / width);
            if (content.LayoutParameters!.Width != wide)
            {
                content.LayoutParameters.Width = wide;
                content.Post(content.RequestLayout);
            }
        };
        return frame;
    }

    /// <summary>
    /// Two rows' cells on one side of Entrar standing tall, each row as wide
    /// as <paramref name="width"/>, a shorter one's room empty; the first of
    /// each at <paramref name="upperFrom"/> and <paramref name="lowerFrom"/> of its row.
    /// </summary>
    private LinearLayout TwoRows(int first, List<int> upper, List<int> lower, float width, int upperFrom, int lowerFrom)
    {
        var both = new LinearLayout(context) { Orientation = Orientation.Vertical };
        foreach (var (index, cells, from) in new[] { (first, upper, upperFrom), (first + 1, lower, lowerFrom) })
        {
            var row = DesignedRow(index, cells, from);
            AddRoom(row, width - design!.WeightOf(cells));
            both.AddView(row, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, RowHeight));
            rowViews.Add(row);
            designRows.Add((index, row));
        }

        return both;
    }

    /// <summary>One row's cells side by side: its keys, and its holes as empty room; the first at <paramref name="from"/> of the design's row.</summary>
    private LinearLayout DesignedRow(int index, List<int> cells, int from)
    {
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            View view;
            if (KeyboardDesign.IsHole(cell))
            {
                view = new View(context);
                row.AddView(view, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, design!.WeightOf(cell)));
            }
            else
            {
                view = DesignView(cell);
                row.AddView(view, KeyMargins(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, design!.WeightOf(cell))));
            }

            designKeys.Add((cell, index, from + i, view));
        }

        return row;
    }

    /// <summary>A row's room no key takes: empty, at its right.</summary>
    private void AddRoom(LinearLayout row, float weight)
    {
        if (weight > 0.01f)
        {
            row.AddView(new View(context), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, weight));
        }
    }

    /// <summary>
    /// While designing, a key of the design held: a picture of it follows the
    /// finger or pointer, and where it is let go it goes, in that row before
    /// the keys right of it; let go above the keys, it is taken out. False,
    /// the key types as ever.
    /// </summary>
    private bool DesignTouch(View view, MotionEvent motion)
    {
        if (!designing)
        {
            return false;
        }

        var held = designKeys.FirstOrDefault(key => key.View == view && !KeyboardDesign.IsHole(key.Id));
        if (held.View is null)
        {
            return false;
        }

        var origin = new int[2];
        root.GetLocationOnScreen(origin);
        switch (motion.ActionMasked)
        {
            case MotionEventActions.Down:
                var at = new int[2];
                view.GetLocationOnScreen(at);
                var bitmap = Android.Graphics.Bitmap.CreateBitmap(Math.Max(view.Width, 1), Math.Max(view.Height, 1), Android.Graphics.Bitmap.Config.Argb8888!)!;
                view.Draw(new Android.Graphics.Canvas(bitmap));
                var picture = new ImageView(context) { Alpha = 0.85f, Elevation = Dp(8) };
                picture.SetImageBitmap(bitmap);
                root.AddView(picture, new FrameLayout.LayoutParams(view.Width, view.Height));
                picture.SetX(at[0] - origin[0]);
                picture.SetY(at[1] - origin[1]);
                view.Alpha = 0.3f;
                dragged = (held.Id, view, picture, motion.RawX - at[0], motion.RawY - at[1]);
                (dragMoved, dragStart) = (false, (motion.RawX, motion.RawY));

                // The keys aside scroll across: the drag is the key's, not the scroll's.
                view.Parent?.RequestDisallowInterceptTouchEvent(true);
                break;
            case MotionEventActions.Move when dragged is { } moving:
                dragMoved |= Math.Abs(motion.RawX - dragStart.X) > Dp(8) || Math.Abs(motion.RawY - dragStart.Y) > Dp(8);
                moving.Picture.SetX(motion.RawX - moving.GrabX - origin[0]);
                moving.Picture.SetY(motion.RawY - moving.GrabY - origin[1]);
                moving.Picture.Alpha = OffTheKeys(motion.RawY) ? 0.4f : 0.85f;
                break;
            case MotionEventActions.Up when dragged is { } letGo:
                LetGo(letGo.Picture);
                dragged = null;
                if (!dragMoved)
                {
                    letGo.Key.Alpha = 1f;
                    Select(letGo.Id);
                    break;
                }

                // The keyboard is made again once this touch is over, not while it is handled.
                var (x, y) = (motion.RawX, motion.RawY);
                strip.Post(() => Drop(letGo.Id, x, y));
                break;
            case MotionEventActions.Cancel when dragged is { } cancelled:
                LetGo(cancelled.Picture);
                cancelled.Key.Alpha = 1f;
                dragged = null;
                break;
        }

        return true;
    }

    /// <summary>A design's key, made the first time and taken from the row it stood in after that.</summary>
    private TextView DesignView(int id)
    {
        if (designViews.TryGetValue(id, out var made))
        {
            (made.Parent as ViewGroup)?.RemoveView(made);
            made.Alpha = 1f;
            return made;
        }

        var view = PanelKeyView(KeyboardDesign.Key(id));
        designViews[id] = view;
        return view;
    }

    /// <summary>The dragged key's picture gone, and its bitmap freed at once, not left for the collector.</summary>
    private void LetGo(ImageView picture)
    {
        root.RemoveView(picture);
        var bitmap = (picture.Drawable as Android.Graphics.Drawables.BitmapDrawable)?.Bitmap;
        picture.SetImageDrawable(null);
        bitmap?.Recycle();
        picture.Dispose();
    }

    /// <summary>Whether a point on the screen is above the whole keyboard's keys, where a key let go is taken out.</summary>
    private bool OffTheKeys(float rawY)
    {
        var at = new int[2];
        wholeKeyboard.GetLocationOnScreen(at);
        return rawY < at[1];
    }

    /// <summary>
    /// The key let go: above the keys, put aside; on a hole (or on its own
    /// place), it takes it; else put in the row under the point before the
    /// cells right of it. Where it came from, a hole stays. Kept for the screen.
    /// </summary>
    private void Drop(int id, float rawX, float rawY)
    {
        if (design is null)
        {
            return;
        }

        if (OffTheKeys(rawY))
        {
            ChangeDesign(shown => shown.PutAside(id));
            return;
        }

        var row = RowAt(rawY);
        var cells = designKeys.Where(cell => cell.Row == row).ToList();
        var under = cells.FirstOrDefault(cell => (KeyboardDesign.IsHole(cell.Id) || cell.Id == id) && Within(cell.View, rawX));
        if (under.View is not null)
        {
            ChangeDesign(shown => shown.Put(id, row, under.Index, onHole: true));
            return;
        }

        var index = cells.Count(cell => CentreX(cell.View) < rawX);
        ChangeDesign(shown => shown.Put(id, row, index, onHole: false));
    }

    /// <summary>The design's row under a height on the screen, or the nearest one.</summary>
    private int RowAt(float rawY)
    {
        var best = 0;
        var nearest = float.MaxValue;
        foreach (var (row, view) in designRows)
        {
            var at = new int[2];
            view.GetLocationOnScreen(at);
            var distance = rawY < at[1] ? at[1] - rawY : rawY > at[1] + view.Height ? rawY - (at[1] + view.Height) : 0;
            if (distance < nearest)
            {
                (best, nearest) = (row, distance);
            }
        }

        return best;
    }

    /// <summary>Whether a point across the screen is within a view's width.</summary>
    private static bool Within(View view, float rawX)
    {
        var at = new int[2];
        view.GetLocationOnScreen(at);
        return rawX >= at[0] && rawX < at[0] + view.Width;
    }

    private static float CentreX(View view)
    {
        var at = new int[2];
        view.GetLocationOnScreen(at);
        return at[0] + (view.Width / 2f);
    }

    /// <summary>
    /// A letter of the whole keyboard touched while swiping is on, as
    /// Gboard's: it types when the finger lifts, unless the finger left it,
    /// when the path drawn is read as a word. False, the key acts as ever.
    /// </summary>
    private bool SwipeTouch(PanelKey key, View view, MotionEvent motion)
    {
        if (Swiping is null || GlidesFor?.Invoke(StandardKeys.Spanish) == false || designing || !key.IsLetter || !SwipeLetters.Contains(key.Text!) || !Within(view, wholeKeyboard))
        {
            return false;
        }

        var at = new int[2];
        view.GetLocationOnScreen(at);
        switch (motion.ActionMasked)
        {
            case MotionEventActions.Down:
                view.Pressed = true;
                if (vibrates)
                {
                    view.PerformHapticFeedback(FeedbackConstants.KeyboardTap);
                }

                if (sounds)
                {
                    StandardKeyboard.PlayKeySound(context, key.Code);
                }

                swipeTrace = new SwipeTrace(key, motion.EventTime);
                Track(swipeTrace, at[0] + motion.GetX(), at[1] + motion.GetY(), motion.EventTime);
                break;
            case MotionEventActions.Move when swipeTrace is { } trace:
                for (var i = 0; i < motion.HistorySize; i++)
                {
                    Track(trace, at[0] + motion.GetHistoricalX(i), at[1] + motion.GetHistoricalY(i), motion.GetHistoricalEventTime(i));
                }

                Track(trace, at[0] + motion.GetX(), at[1] + motion.GetY(), motion.EventTime);
                var (startX, startY, _) = trace.Points[0];
                if (!trace.Moved && Math.Max(Math.Abs(at[0] + motion.GetX() - startX), Math.Abs(at[1] + motion.GetY() - startY)) > Math.Min(view.Width, view.Height) * 0.6f)
                {
                    trace.Moved = true;
                    view.Pressed = false;
                    ShowTrail(trace);
                }

                break;
            case MotionEventActions.Up when swipeTrace is { } trace:
                view.Pressed = false;
                swipeTrace = null;
                HideTrail();
                if (trace.Moved)
                {
                    Track(trace, at[0] + motion.GetX(), at[1] + motion.GetY(), motion.EventTime);
                    ReadSwipe(trace);
                }
                else
                {
                    // Typed as the finger lifts, so nothing is held to repeat it.
                    Press(trace.Key);
                    StopRepeat();
                }

                break;
            case MotionEventActions.Cancel:
                view.Pressed = false;
                swipeTrace = null;
                HideTrail();
                break;
        }

        return true;
    }

    /// <summary>A point of a swipe, on the screen, kept with its time from the swipe's start, and drawn on its trail once there is one.</summary>
    private void Track(SwipeTrace trace, float x, float y, long time)
    {
        trace.Points.Add((x, y, time - trace.Start));
        swipeTrail?.Add(x, y, time);
    }

    /// <summary>A swipe's trail shown over the whole keyboard, from the points the finger already drew.</summary>
    private void ShowTrail(SwipeTrace trace)
    {
        if (!showsTrail)
        {
            return;
        }

        var at = new int[2];
        wholeKeyboard.GetLocationOnScreen(at);
        swipeTrail = new SwipeTrail(Dp(SwipeTrailDp), HeldColor, at[0], at[1]);
        swipeTrail.SetBounds(0, 0, wholeKeyboard.Width, wholeKeyboard.Height);
        foreach (var (x, y, time) in trace.Points)
        {
            swipeTrail.Add(x, y, trace.Start + (long)time);
        }

        wholeKeyboard.Overlay!.Add(swipeTrail);
    }

    private void HideTrail()
    {
        if (swipeTrail is null)
        {
            return;
        }

        wholeKeyboard.Overlay!.Remove(swipeTrail);
        swipeTrail = null;
    }

    /// <summary>Whether a view stands within another, at any depth.</summary>
    private static bool Within(View view, View group)
    {
        for (var parent = view.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent == group)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A swipe's path made FUTO Swipe's: over the room the letters take, with each letter's centre; none where a letter has no key in sight.</summary>
    private SwipePath? PathOf(SwipeTrace trace)
    {
        var centres = new List<(float X, float Y)>();
        float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
        foreach (var letter in SwipeLetters)
        {
            var view = characterKeys.FirstOrDefault(key => key.Key.Text == letter.ToString() && key.View.IsShown && Within(key.View, wholeKeyboard)).View;
            if (view is null || view.Width == 0)
            {
                return null;
            }

            var at = new int[2];
            view.GetLocationOnScreen(at);
            centres.Add((at[0] + (view.Width / 2f), at[1] + (view.Height / 2f)));
            (left, top) = (Math.Min(left, at[0]), Math.Min(top, at[1]));
            (right, bottom) = (Math.Max(right, at[0] + view.Width), Math.Max(bottom, at[1] + view.Height));
        }

        float X(float x) => (x - left) / (right - left);
        float Y(float y) => (y - top) / (bottom - top);
        return new SwipePath(
            [.. trace.Points.Select(point => X(point.X))],
            [.. trace.Points.Select(point => Y(point.Y))],
            [.. trace.Points.Select(point => point.T)],
            [.. centres.Select(centre => X(centre.X))],
            [.. centres.Select(centre => Y(centre.Y))],
            StandardKeys.Spanish);
    }

    /// <summary>A swipe read: its likeliest word typed, the others in the toolbar to take its place.</summary>
    private void ReadSwipe(SwipeTrace trace)
    {
        if (PathOf(trace) is not { } path)
        {
            Log.Write("A swipe was not read: a letter has no key in sight");
            return;
        }

        ReadSwipe(path, held.HasFlag(Held.Shift), held.HasFlag(Held.CapsLock));
    }

    /// <summary>A swipe's path read: its likeliest word typed, with a capital or in capitals as Shift or the capitals' lock had it, the others in the toolbar.</summary>
    private async void ReadSwipe(SwipePath path, bool capital, bool capitals)
    {
        if (Swiping is null)
        {
            return;
        }

        try
        {
            var read = await Swiping(path);
            if (read.Count == 0)
            {
                return;
            }

            var words = OrderSwiped?.Invoke(path.Language, read) ?? read;
            ForgetSwipe();
            typingWords = [];
            swipeRead = (path.Language, read[0]);
            TypeSwiped(words[0], capital, capitals);
            swipeWords = words;
            ShowBars();
        }
        catch (Exception failure)
        {
            Log.Write($"The swipe could not be read: {failure}");
        }
    }

    /// <summary>
    /// A swiped word typed with a space before it unless the caret follows a
    /// space or nothing, and none after, as Gboard's: ⌫ takes its last
    /// letter, and the next word brings its own space; in capitals with Bloq May, its
    /// first letter with Shift, which it lets go as a letter does.
    /// </summary>
    private void TypeSwiped(string word, bool capital, bool capitals)
    {
        var cased = capitals ? word.ToUpper(CultureInfo.CurrentCulture)
            : capital ? char.ToUpper(word[0], CultureInfo.CurrentCulture) + word[1..]
            : word;
        SetHeld(held & (Held.CapsLock | Held.Fn));
        var before = TextBeforeCaret?.Invoke();
        var text = (string.IsNullOrEmpty(before) || char.IsWhiteSpace(before[^1]) ? "" : " ") + cased;
        Type(text);
        swipedText = text;
        swipedWord = word;
    }

    /// <summary>A word of the toolbar taken in place of the one the swipe typed: the centre now, the one it replaced at a side.</summary>
    private void ReplaceSwiped(string word)
    {
        if (swipedText is not { } typed)
        {
            return;
        }

        var old = typed.Trim();
        var capital = old.Length > 0 && char.IsUpper(old[0]);
        var text = (typed.StartsWith(' ') ? " " : "") + (capital ? char.ToUpper(word[0], CultureInfo.CurrentCulture) + word[1..] : word);
        if (ReplaceBeforeCaret is { } replace)
        {
            replace(typed.Length, text);
        }
        else
        {
            foreach (var _ in typed)
            {
                Send(Keycode.Del, MetaKeyStates.None);
            }

            Type(text);
        }

        // Taken, as Gboard takes it: the words gone from the toolbar; the next word still starts with its space.
        swipedText = text;
        swipedWord = word;
        swipeWords = [];
        ShowBars();
        if (swipeRead is { } read)
        {
            SwipeChosen?.Invoke(read.Language, read.Word, word);
        }
    }

    /// <summary>The last swipe's language and the word it read first, before what was learnt put another in its place.</summary>
    private (StandardLanguage Language, string Word)? swipeRead;

    /// <summary>
    /// A sentence about to be closed with ? or !, in Spanish, mended as
    /// Spelling has it (<see cref="SpanishMarks.Opened"/>): its opening
    /// mark put at its start, written again in the field from where it
    /// begins.
    /// </summary>
    private void MendSentence(char closing)
    {
        if (SpellingIn?.Invoke(TypingLanguage).OpenMark == true
            && ReplaceBeforeCaret is { } replace && RecentText?.Invoke() is { } before
            && SpanishMarks.Opened(before, closing) is { } mended)
        {
            replace(mended.Replaced, mended.Text);
        }
    }

    /// <summary>
    /// ⌫ right after a swiped word, with Delete a swiped word whole on (as
    /// Gboard's): all the swipe typed deleted, the word and the space it put
    /// before it, and the swipe's words gone; ⌫ held goes on letter by letter.
    /// Whether it was.
    /// </summary>
    private bool DeleteSwiped()
    {
        if (!deletesSwipedWord || swipedText is not { } typed)
        {
            return false;
        }

        if (ReplaceBeforeCaret is { } replace)
        {
            replace(typed.Length, "");
        }
        else
        {
            foreach (var _ in typed)
            {
                Send(Keycode.Del, MetaKeyStates.None);
            }
        }

        ForgetSwipe();
        return true;
    }

    /// <summary>
    /// Something other than a swiped word typed: the swipe's words gone from
    /// the toolbar; whether a space is owed first, a letter right after a
    /// swiped word starting the next word, as Gboard's.
    /// </summary>
    private bool EndSwipe(bool letter)
    {
        var owed = swipedText is not null && letter;
        ForgetSwipe();
        return owed;
    }

    /// <summary>How long after a key the field is read again for the word before the caret: a key's event reaches the field a moment later than text.</summary>
    private const int FieldSettleMs = 80;

    /// <summary>
    /// The word before the caret read and the words starting so offered in
    /// the toolbar, as Gboard offers them while a word is typed; none where
    /// there is no word, or a swipe's words are offered.
    /// </summary>
    public void OfferWords()
    {
        var word = swipedText is null && Suggesting is not null && standard is not null ? WordBeforeCaret?.Invoke() ?? "" : "";

        // A piece of an address ("com" of someone@example.com) is no word: nothing offered for it, nor to learn.
        if (word.Length > 0 && InAddress(RecentText?.Invoke() ?? "", word.Length))
        {
            word = "";
        }

        IReadOnlyList<string> words = word.Length > 0 ? Suggesting!(standard!.Language, word) : [];
        var learning = word.Length >= 2 && Learnable?.Invoke(standard!.Language, word) == true;
        if (words.Count == 0 && typingWords.Count == 0 && !learning && !learnable)
        {
            return;
        }

        typingWords = words;
        typingWord = word;
        learnable = learning;
        ShowBars();
    }

    /// <summary>The words offered again once a key (⌫, an arrow) has reached the field.</summary>
    private void OfferWordsSoon()
    {
        handler.RemoveCallbacks(OfferWords);
        handler.PostDelayed(OfferWords, FieldSettleMs);
    }

    /// <summary>A word offered taken in place of the one being typed, with a space after it, as Gboard's: the words gone from the toolbar.</summary>
    private void TakeSuggestion(string word)
    {
        // An e-mail address is the field's whole, with nothing after it.
        var text = word.Contains('@') ? word : word + " ";
        if (ReplaceBeforeCaret is { } replace)
        {
            replace(typingWord.Length, text);
        }
        else
        {
            foreach (var _ in typingWord)
            {
                Send(Keycode.Del, MetaKeyStates.None);
            }

            Type(text);
        }

        ForgetTyping();
    }

    /// <summary>The words offered for the word being typed gone from the toolbar.</summary>
    public void ForgetTyping()
    {
        typingWord = "";
        if (typingWords.Count == 0 && !learnable)
        {
            return;
        }

        typingWords = [];
        learnable = false;
        ShowBars();
    }

    /// <summary>The last swipe's words gone from the toolbar, anything else having been typed.</summary>
    private void ForgetSwipe()
    {
        swipedText = null;
        swipedWord = null;
        if (swipeWords.Count == 0)
        {
            return;
        }

        swipeWords = [];
        ShowBars();
    }

    /// <summary>Spelling, the keyboard's as Windows': the words a word typed may have meant and whether it is misspelled (the keyboard's SpellingEngine); none, it is left as it is.</summary>
    public Func<StandardLanguage, string, WordFixes?>? Fixing { get; set; }

    /// <summary>A language's Spelling as it applies now: its Autocorrect, its opening marks, its accents.</summary>
    public Func<StandardLanguage, LanguageSpelling>? SpellingIn { get; set; }

    /// <summary>The accent model's sentence for one typed, read away from the main thread; none while it is not loaded.</summary>
    public Func<string, Task<string?>>? CorrectSentence { get; set; }

    /// <summary>The same at once, for Enter, which cannot wait; none while it is not loaded.</summary>
    public Func<string, string?>? CorrectSentenceNow { get; set; }

    /// <summary>The text right after the caret, to tell a word being written in the middle of another.</summary>
    public Func<string?>? TextAfterCaret { get; set; }

    /// <summary>Shift tapped twice: the field's selected text turned to its next case, still selected; false where none is selected.</summary>
    public Func<bool>? SelectionCase { get; set; }

    /// <summary>Whether a word being typed is one no list, no dictionary of the user's and no Hunspell has, offered as typed at the toolbar's left to be learnt; none, never.</summary>
    public Func<StandardLanguage, string, bool>? Learnable { get; set; }

    /// <summary>A word taken from the toolbar's left, learnt into the user's dictionary.</summary>
    public Action<StandardLanguage, string>? Learn { get; set; }

    private StandardLanguage TypingLanguage => standard?.Language ?? StandardKeys.Spanish;

    /// <summary>The last word or sentence Spelling corrected or offered: what is written, what was typed, and what followed it; taken back or changed from the toolbar while the text still ends so.</summary>
    private sealed record Correction(string Written, string Typed, string After);

    private Correction? correction;

    /// <summary>What the toolbar's middle shows in place of words: a correction, or what a tool lacks with Download; none, as ever.</summary>
    private View? offer;

    /// <summary>The toolbar's middle given to what is offered; none, back to words or tools.</summary>
    private void ShowOffer(View? shown)
    {
        offer = shown;
        ShowBars();
    }

    /// <summary>Something else typed: what was offered gone from the toolbar.</summary>
    private void EndOffer()
    {
        correction = null;
        if (offer is not null)
        {
            ShowOffer(null);
        }
    }

    /// <summary>The text right before the caret, so many characters, replaced with another, in one edit where the field allows it.</summary>
    private void Replace(int length, string text)
    {
        if (ReplaceBeforeCaret is { } replace)
        {
            replace(length, text);
            return;
        }

        for (var gone = 0; gone < length; gone++)
        {
            Send(Keycode.Del, MetaKeyStates.None);
        }

        typeOut(text);
    }

    /// <summary>
    /// The word typed letter by letter, ended now, before what ends it is
    /// typed: misspelled, with Autocorrect on, the likeliest word it may have
    /// meant written in its place; the word as typed, those words and whether
    /// one was written, for the toolbar. None for a word left as it is, a
    /// swiped one, or one being written in the middle of another (letters
    /// right after the caret).
    /// </summary>
    private (string Typed, IReadOnlyList<string> Fixes, bool Applied)? FixWord()
    {
        if (Fixing is null || swipedText is not null || WordBeforeCaret?.Invoke() is not { Length: > 0 } word
            || (TextAfterCaret?.Invoke() is { Length: > 0 } after && char.IsLetter(after[0]))
            || InAddress(RecentText?.Invoke() ?? "", word.Length)
            || Fixing(TypingLanguage, word) is not { Words.Count: > 0 } fixes)
        {
            return null;
        }

        var applied = SpellingIn?.Invoke(TypingLanguage).Autocorrect == true && fixes.Misspelled;
        if (applied)
        {
            Replace(word.Length, fixes.Words[0]);
        }

        return (word, fixes.Words, applied);
    }

    /// <summary>
    /// A word checked offered in the toolbar once what ended it is typed:
    /// written in its place, ↶ and the word as typed in the centre and the
    /// other words it may have meant on each side; else those words, ✓ on
    /// the likeliest in the centre. A tap writes it again with what ended it.
    /// </summary>
    private void OfferFix((string Typed, IReadOnlyList<string> Fixes, bool Applied) fixing, string ending)
    {
        correction = new(fixing.Applied ? fixing.Fixes[0] : fixing.Typed, fixing.Typed, ending);
        string?[] shown = fixing.Applied
            ? [fixing.Fixes.ElementAtOrDefault(1), fixing.Typed, fixing.Fixes.ElementAtOrDefault(2)]
            : [fixing.Fixes.ElementAtOrDefault(1), fixing.Fixes[0], fixing.Fixes.ElementAtOrDefault(2)];
        ShowOffer(OfferRow(shown, fixing.Applied ? "↶ " : "✓ ", TakeCorrection));
    }

    /// <summary>Three words in the toolbar's middle, as its suggestions are: the centre's in bold after its sign, each written with a tap.</summary>
    private LinearLayout OfferRow(string?[] shown, string centreSign, Action<string> taken)
    {
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        for (var i = 0; i < shown.Length; i++)
        {
            var word = shown[i];
            if (standardColors?.Divider is { } line && i > 0)
            {
                row.AddView(Divider(line), new LinearLayout.LayoutParams(Dp(SuggestionLineDp), Dp(SuggestionLineHeightDp)));
            }

            var button = ToolButton(word is null ? "" : i == 1 ? centreSign + word : word, word ?? "", SuggestionSp, () =>
            {
                if (word is not null)
                {
                    taken(word);
                }
            });
            button.SetTextColor(standardColors!.Glyph);
            button.SetPadding(Dp(2), 0, Dp(2), 0);
            button.SetAutoSizeTextTypeUniformWithConfiguration(4, (int)Math.Round(SuggestionSp), 1, (int)ComplexUnitType.Sp);
            if (i == 1)
            {
                button.SetTypeface(null, TypefaceStyle.Bold);
            }

            row.AddView(button, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        }

        return row;
    }

    /// <summary>A word or a sentence of the toolbar taken in place of the one corrected or offered, what followed it written again after it; only while the text still ends so.</summary>
    private void TakeCorrection(string word)
    {
        if (correction is { } fix && RecentText?.Invoke() is { } before && before.EndsWith(fix.Written + fix.After, StringComparison.Ordinal))
        {
            Replace(fix.Written.Length + fix.After.Length, word + fix.After);
        }

        EndOffer();
    }

    /// <summary>
    /// Whether the text before the caret ends in an e-mail or web address,
    /// the last <paramref name="word"/> characters aside (the word being
    /// ended, or the . that closed a sentence): what
    /// stands before it since the last space holds @ . / or :
    /// ("someone@example", "www.berpiztu"), so nothing in it is corrected, as
    /// Gboard's; a word after a sentence's . and a space is a word.
    /// </summary>
    private static bool InAddress(string recent, int word)
    {
        var before = recent[..Math.Max(0, recent.Length - word)];
        var token = before[(before.LastIndexOfAny([' ', '\n', '\t']) + 1)..];
        return token.IndexOfAny(['@', '.', '/', ':']) >= 0;
    }

    /// <summary>The sentence the text before the caret ends with: from after the last . ? ! or new line before its end; empty where there is none.</summary>
    private static string SentenceAt(string recent)
    {
        var start = recent.Length > 1 ? recent.LastIndexOfAny(['.', '?', '!', '\n'], recent.Length - 2) + 1 : 0;
        return recent[start..].TrimStart();
    }

    /// <summary>
    /// A sentence closed with . ? or !, its accents read by the model away
    /// from the main thread, as Windows': with Autocorrect on, written in its
    /// place, ↶ offering it back; off, the words it would change offered with
    /// ✓. Only while the text still ends with it, spaces aside.
    /// </summary>
    private async void CorrectClosed()
    {
        var language = TypingLanguage;
        if (CorrectSentence is null || SpellingIn?.Invoke(language) is not { Accents: true } spelling
            || RecentText?.Invoke() is not { } recent || SentenceAt(recent) is not { Length: > 1 } sentence
            || InAddress(recent, 1))
        {
            return;
        }

        if (await CorrectSentence(sentence) is not { } corrected || corrected == sentence
            || RecentText?.Invoke() is not { } now || now.LastIndexOf(sentence, StringComparison.Ordinal) is var at && at < 0)
        {
            return;
        }

        var after = now[(at + sentence.Length)..];
        if (after.Any(character => !char.IsWhiteSpace(character)))
        {
            return;
        }

        string Changed(string shown, string other) => string.Join(' ', shown.Split(' ').Zip(other.Split(' ')).Where(pair => pair.First != pair.Second).Select(pair => pair.First));
        if (spelling.Autocorrect)
        {
            Replace(sentence.Length + after.Length, corrected + after);
            correction = new(corrected, sentence, after);
            ShowOffer(OfferRow([null, Changed(sentence, corrected), null], "↶ ", _ => TakeCorrection(sentence)));
        }
        else
        {
            correction = new(sentence, sentence, after);
            ShowOffer(OfferRow([null, Changed(corrected, sentence), null], "✓ ", _ => TakeCorrection(corrected)));
        }
    }

    /// <summary>Enter closing what is typed, with Autocorrect on: its last word corrected, then its sentence's accents, before the line ends, for after Enter the field may add what it likes; nothing offered after.</summary>
    private void CorrectBeforeEnter()
    {
        if (SpellingIn?.Invoke(TypingLanguage) is not { Autocorrect: true } spelling)
        {
            return;
        }

        FixWord();
        if (spelling.Accents && CorrectSentenceNow is { } correct && RecentText?.Invoke() is { } recent
            && SentenceAt(recent) is { Length: > 1 } sentence && correct(sentence) is { } corrected && corrected != sentence)
        {
            Replace(sentence.Length, corrected);
        }
    }

    /// <summary>
    /// What a tool lacks to work, said in the toolbar's middle with Download
    /// beside it, in the keyboard's colours, as Windows': fetched there with
    /// a tap, how much said as it comes, and once it is, what it is for
    /// turned on (<paramref name="turnOn"/>) and the toolbar as it was; a
    /// failure said there. Nothing is fetched but by that tap.
    /// </summary>
    public void OfferDownload(string missing, Func<Exception, string> failed, Func<IProgress<(long Done, long Total)>, Task> download, Action turnOn)
    {
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        var said = ToolButton(missing, missing, SuggestionSp, () => { });
        said.SetAutoSizeTextTypeUniformWithConfiguration(4, (int)Math.Round(SuggestionSp), 1, (int)ComplexUnitType.Sp);
        TextView? fetch = null;
        fetch = ToolButton(context.GetString(Resource.String.download)!, context.GetString(Resource.String.download)!, SuggestionSp, async () =>
        {
            fetch!.Visibility = ViewStates.Gone;
            try
            {
                await download(new Progress<(long Done, long Total)>(at => said.Text = context.GetString(Resource.String.downloading_short,
                    new Java.Lang.String((at.Done / 1_000_000.0).ToString("0.0", CultureInfo.CurrentCulture)),
                    new Java.Lang.String((Math.Max(at.Total, at.Done) / 1_000_000.0).ToString("0.0", CultureInfo.CurrentCulture)))));
                turnOn();
                ShowOffer(null);
            }
            catch (Exception failure) when (failure is System.Net.Http.HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
            {
                Log.Write($"Downloads: {missing} could not be downloaded from the toolbar: {failure.Message}");
                said.Text = failed(failure);
            }
        });
        said.SetTextColor(standardColors!.Glyph);
        fetch.SetTextColor(standardColors.Glyph);
        fetch.SetTypeface(null, TypefaceStyle.Bold);
        fetch.SetPadding(Dp(8), 0, Dp(8), 0);
        row.AddView(said, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        row.AddView(fetch, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent));
        ShowOffer(row);
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
            if (key.FnLabel is not null || key.ShiftLabel is not null || key.CtrlLabel is not null)
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
            if (DesignTouch(view, e.Event!) || SwipeTouch(key, view, e.Event!))
            {
                return;
            }

            switch (e.Event!.ActionMasked)
            {
                case MotionEventActions.Down:
                    view.Pressed = true;
                    if (vibrates)
                    {
                        view.PerformHapticFeedback(FeedbackConstants.KeyboardTap);
                    }

                    if (sounds)
                    {
                        StandardKeyboard.PlayKeySound(context, key.Code);
                    }

                    if (key.OpensOnHold)
                    {
                        opened = false;
                        opening = new Java.Lang.Runnable(() =>
                        {
                            opened = true;
                            if (vibrates)
                            {
                                view.PerformHapticFeedback(FeedbackConstants.LongPress);
                            }

                            if (key.OpensFull)
                            {
                                SetCompact(false);
                            }
                            else
                            {
                                ResizeRequested?.Invoke();
                            }
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
            var (x, y) = panel.OnScreen(view, motion);
            (startX, startY, held, slid, corner) = (x, y, false, false, null);
            waiting = new Java.Lang.Runnable(Hold);
            panel.handler.PostDelayed(waiting, LongPressMs);
        }

        public void Track(MotionEvent motion)
        {
            var (x, y) = panel.OnScreen(view, motion);
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
                var (x, y) = panel.OnScreen(view, motion);
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
                panel.ReportResizing(from, drag, dx, dy);
            }
            else
            {
                panel.ReportMoving(drag, dx, dy);
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

    /// <summary>The panel's own frame: a drag of the window going on takes every touch, whatever view it began on.</summary>
    private sealed class PanelFrame(KeyPanel panel, Context context) : FrameLayout(context)
    {
        public override bool OnInterceptTouchEvent(MotionEvent? e) =>
            (e is not null && panel.Dragging(e)) || base.OnInterceptTouchEvent(e);

        public override bool OnTouchEvent(MotionEvent? e) =>
            (e is not null && panel.Dragging(e)) || base.OnTouchEvent(e);
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
        // A modifier (Shift for a capital) keeps the swiped word's suggestions and the space the next word brings.
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

        if (key.Code == Keycode.Del && (held & KeyModifiers) == Held.None && DeleteSwiped())
        {
            return;
        }

        // A letter right after a swiped word starts the next word, a space between, as Gboard's.
        var spaceFirst = EndSwipe(key.IsLetter);

        // With Shift held, a key that is another then (⌫ Supr) sends that one, without Shift; with Ctrl held, one that is
        // a shortcut then (the two rows' PgUp, Ctrl+C) sends that one, with Ctrl.
        var shifted = held.HasFlag(Held.Shift) && key.ShiftCode is not null;
        var code = held.HasFlag(Held.Fn) && key.FnCode is { } fnCode ? fnCode
            : held.HasFlag(Held.Ctrl) && key.CtrlCode is { } ctrlCode ? ctrlCode
            : shifted ? key.ShiftCode!.Value
            : key.Code;
        if (code == key.Code && key.IsCharacter && ((held & KeyModifiers) == Held.None || key.Code == Keycode.Unknown))
        {
            if (spaceFirst)
            {
                Type(" ");
            }

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

        var relabelled = false;
        foreach (var (key, view) in fnKeys)
        {
            var label = LabelNow(key);
            relabelled |= view.Text != label;
            view.Text = label;
        }

        // A word changed (PgUp to Ctrl+C) is laid out and sized again as the keys' words are.
        if (relabelled)
        {
            FitLegendsSoon();
        }
    }

    /// <summary>What a key that is another with Fn, Ctrl or Shift held reads now: that one's word while it is held, else its own.</summary>
    private string LabelNow(PanelKey key) =>
        held.HasFlag(Held.Fn) && key.FnLabel is { } fnLabel ? fnLabel
        : held.HasFlag(Held.Ctrl) && key.CtrlLabel is { } ctrlLabel ? ctrlLabel
        : held.HasFlag(Held.Shift) && key.ShiftLabel is { } shiftLabel ? shiftLabel
        : key.Label;

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

    /// <summary>
    /// A button of the title bar drawn as an icon, white as its words are, made smaller where the bar narrows; held still
    /// a moment it moves the window, as <see cref="TitleButton"/> does.
    /// </summary>
    private ImageView TitleIcon(int drawable, string description, Action pressed)
    {
        var view = new ImageView(context)
        {
            ContentDescription = description,
            Background = new RippleDrawable(ColorStateList.ValueOf(RippleColor), null, new ColorDrawable(Color.White)),
            ImageTintList = ColorStateList.ValueOf(Color.White),
        };
        view.SetImageResource(drawable);
        view.SetScaleType(ImageView.ScaleType.CenterInside);
        HoldToMove(view, pressed, resizes: false);
        return view;
    }

    /// <summary>The title bar's clipboard, which shows the things copied in the keys' place: Material's, the keyboard's toolbar's.</summary>
    private ImageView ClipboardTitleButton(string description) =>
        TitleIcon(Resource.Drawable.ic_clipboard, description, () => Toggle(clipboardView!, ShowClipboard));

    /// <summary>A title bar's button lit while what it shows is in the keys' place (the held keys' colour), or as the bar again.</summary>
    private void MarkTitleButton(View button, bool held)
    {
        button.Background = held ? KeyBackground(HeldColor) : new RippleDrawable(ColorStateList.ValueOf(RippleColor), null, new ColorDrawable(Color.White));
        var color = held ? HeldTextColor : Color.White;
        if (button is TextView words)
        {
            words.SetTextColor(color);
        }
        else if (button is ImageView icon)
        {
            icon.ImageTintList = ColorStateList.ValueOf(color);
        }
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

    /// <summary>
    /// Where a touch is on the screen: the event's raw place; on a headset
    /// (Galaxy XR), which gives the raw place from the window, so a dragged
    /// window shook where it was, the view's place on the screen and the
    /// touch's place in the view. Not that one elsewhere: the view's place
    /// lags behind a window being dragged, and the window danced.
    /// </summary>
    private (float X, float Y) OnScreen(View view, MotionEvent motion)
    {
        if (!onHeadset)
        {
            return (motion.RawX, motion.RawY);
        }

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
        if (standard is not null)
        {
            standard.Scale = scale;
        }

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
        // Gboard's toolbar keeps its height whatever the keys' size, so its
        // words are always read.
        var barDp = copiedInTitleBar ? ChipBarDp : TitleBarDp;
        foreach (var bar in new View[] { titleBar, closeBar, chipBar, toolbar, menuBar })
        {
            bar.LayoutParameters!.Height = copiedInTitleBar
                ? Dp(ToolbarDp)
                : Dp(Math.Clamp(barDp * scale, LeastTitleBarDp, barDp));
            bar.RequestLayout();
        }

        if (dragging)
        {
            fitAfterDrag = true;
            return;
        }

        SizeLabels();
        FitLegendsSoon();
    }

    /// <summary>
    /// A drag moving the window, its title bar's, its icon's or a long
    /// press's: told to whoever places the window, the labels left alone
    /// meanwhile (<see cref="NoteDrag"/>).
    /// </summary>
    private void ReportMoving(Drag drag, float dx, float dy)
    {
        NoteDrag(drag, before: true);
        Moving?.Invoke(drag, dx, dy);
        NoteDrag(drag, before: false);
    }

    /// <summary>A drag resizing the window from a corner, told likewise.</summary>
    private void ReportResizing(Corner corner, Drag drag, float dx, float dy)
    {
        NoteDrag(drag, before: true);
        Resizing?.Invoke(corner, drag, dx, dy);
        NoteDrag(drag, before: false);
    }

    /// <summary>
    /// While the window is moved or resized the labels are not fitted again
    /// at every step, which made the drag slow: once, when it ends, if
    /// anything asked for it meanwhile. Told before and after each step of
    /// the drag, by the panel's own corners and by whoever else sizes it
    /// (the frame of Berpiztu AI Keyboard's Redimensionar and of the popup's resize).
    /// </summary>
    public void NoteDrag(Drag drag, bool before)
    {
        if (drag == Drag.Start && before)
        {
            dragging = true;
        }
        else if (drag == Drag.End && !before)
        {
            dragging = false;
            if (fitAfterDrag)
            {
                fitAfterDrag = false;
                SizeLabels();
                FitLegendsSoon();
            }
        }
    }

    /// <summary>Fits the legends once the keys are laid out, after whatever made them change size; after a drag, once it ends.</summary>
    private void FitLegendsSoon()
    {
        if (dragging)
        {
            fitAfterDrag = true;
            return;
        }

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

        // A key that is another while a modifier is held is laid out with the word it reads now, not the one it was made with.
        var readNow = fnKeys.ToDictionary(key => key.View, key => LabelNow(key.Key));
        var layouts = labelKeys.Where(key => key.View.IsShown && key.View.Width > 0)
            .ToDictionary(key => key.View, key => key.Factor == 1f
                ? WordLayout(key.View, readNow.TryGetValue(key.View, out var now) ? now : key.Label)
                : (Text: key.View.Text ?? "", Fit: WordFit(key.View, key.View.Text ?? "")));
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
