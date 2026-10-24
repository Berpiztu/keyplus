using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using System.Text;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Gboard's colours as Gboard takes them, from the device's own palette
/// (Material You), never written here: each part of the keyboard is the
/// system colour Gboard's matches on the owner's Fold (the ground
/// system_surface_container_light, a key system_surface_container_lowest_light,
/// its letters system_on_surface_light, a function key system_accent2_100 and
/// its sign system_accent2_700); before Android 14, which has no surface
/// colours, their neutral tones. The swipe's trail is system_accent1_600,
/// the toolbar's ⊞ circled system_accent1_200 while words are suggested and
/// the lines between the words system_neutral2_200, the grid's dots and ✏'s
/// outline system_outline_light (the dots not in sight at half), as on
/// Gboard on the emulator and the Fold. Dark (Tema's Oscuro, or Sistema with
/// the device dark), each part the same role's dark tone: the surfaces'
/// _dark ones, a key the highest container, so it stands out of the ground
/// as the light one does; a container's accent tone 30 for 90 and its sign
/// 90 for 30, the primary 80 for 40, a grey as far from the ground the other
/// way. Only the colours: the keys' places never change with them.
/// </summary>
public sealed class StandardColors(Context context, bool dark)
{
    /// <summary>The application typed in, as the keyboard last heard it (Por app); none in the keyboard's own app.</summary>
    public static string? App { get; set; }

    /// <summary>The keyboard's colours as Tema has them for the application typed in, the device's dark theme for Sistema.</summary>
    public static StandardColors Read(Context context) => Read(context, App);

    public static StandardColors Read(Context context, string? app)
    {
        var (dark, key, accent) = Chosen(context, app);
        var colors = new StandardColors(context, dark);

        // On a headset in the dark, Gboard's keys there: darker than the ground, the lowest container's tone (#101418 to its
        // #1D2024 on the Galaxy XR), as its light keys are the lowest one's white.
        if (dark && KeyPlusPreferences.IsHeadset(context))
        {
            colors.Key = Of(context, dark: true, 0,
                OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemSurfaceContainerLowestDark : Android.Resource.Color.SystemNeutral11000);
        }

        colors.Personalize(key, accent, headset: KeyPlusPreferences.IsHeadset(context));
        return colors;
    }

    /// <summary>What the colours are made from for an application: whether dark, and Personalizado's keyboard and accent colours.</summary>
    public static (bool Dark, Rgb? Key, Rgb? Accent) Chosen(Context context, string? app)
    {
        var global = KeyPlusPreferences.Global(context);
        var look = KeyPlusPreferences.Look(context);
        Rgb? key = null, accent = null;
        if (look == KeyboardLook.Custom)
        {
            (key, accent) = (Rgb.Parse(global.GetString(KeyPlusPreferences.KeyColorKey, null)), Rgb.Parse(global.GetString(KeyPlusPreferences.AccentColorKey, null)));

            // Por app: the application's theme's background with the device's accent, or its own colours, a keyboard colour
            // it has not chosen Tema's, an accent the device's.
            if (global.GetBoolean(KeyPlusPreferences.PerAppKey, false) && app is not null)
            {
                var color = AppThemes.For(context, app);
                if (color.Look == AppLook.Own && Rgb.Parse(color.WindowColor) is { } window)
                {
                    (key, accent) = (window, null);
                }
                else if (color.Look == AppLook.Custom)
                {
                    (key, accent) = (Rgb.Parse(color.KeyColor) ?? key, Rgb.Parse(color.AccentColor));
                }
            }
        }

        return (key is { } chosen ? chosen.Readable == Rgb.White : KeyboardLooks.IsDark(look, KeyPlusPreferences.DeviceDark(context)), key, accent);
    }

    /// <summary>
    /// Personalizado over the device's colours: its keyboard colour in the ground's place, its letters white or black,
    /// whichever reads, the keys, lines and greys made from the two as the Windows app makes its own (on a headset in the
    /// dark, as Gboard's there, the keys in it and the ground a little lighter); its accent in the accent's, its tones a
    /// quarter of the way to white or black a step (<see cref="Rgb.Tone"/>).
    /// </summary>
    private void Personalize(Rgb? key, Rgb? accent, bool headset)
    {
        if (key is { } ground)
        {
            var letters = ground.Readable;
            Glyph = Of(letters);
            Key = Of(dark ? ground.Mix(letters, 0.16) : ground);
            Ground = Of(ground.Mix(letters, dark ? 0.06 : 0.08));

            // On a headset in the dark, Gboard's there: the keys the colour chosen, the ground as much lighter as its #1D2024
            // is than its keys' #101418 (13 of the 239 steps to white).
            if (headset && dark)
            {
                Key = Of(ground);
                Ground = Of(ground.Mix(letters, 13.0 / 239));
            }

            Low = Ground;
            Dim = Of(ground.Mix(letters, 0.12));
            ResetGround = Dim;
            High = Of(ground.Mix(letters, 0.2));
            Divider = Of(ground.Mix(letters, 0.25));
            Handle = Of(ground.Mix(letters, 0.35));
            Outline = Of(ground.Mix(letters, 0.45));
        }

        if (accent is { } chosen)
        {
            Function = Of(chosen.Tone(dark ? -3 : 3));
            FunctionPressed = Of(chosen.Tone(dark ? -2 : 2));
            FunctionGlyph = Of(chosen.Tone(dark ? 3 : -2));
            GridCircle = Of(chosen.Tone(dark ? -1 : 2));
            Primary = Of(chosen);
            OnPrimary = Of(chosen.Readable);
            Trail = Primary;
            Accent = Primary;
        }
    }

    public bool Dark => dark;

    public Color Ground { get; private set; } = Of(context, dark,
        OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemSurfaceContainerLight : Android.Resource.Color.SystemNeutral1100,
        OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemSurfaceContainerDark : Android.Resource.Color.SystemNeutral1900);

    public Color Key { get; private set; } = Of(context, dark,
        OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemSurfaceContainerLowestLight : Android.Resource.Color.SystemNeutral110,
        OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemSurfaceContainerHighestDark : Android.Resource.Color.SystemNeutral1800);

    public Color Glyph { get; private set; } = Of(context, dark,
        OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemOnSurfaceLight : Android.Resource.Color.SystemNeutral1900,
        OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemOnSurfaceDark : Android.Resource.Color.SystemNeutral1100);

    public Color Function { get; private set; } = Of(context, dark, Android.Resource.Color.SystemAccent2100, Android.Resource.Color.SystemAccent2700);

    /// <summary>A function key while pressed: the next tone of its own, towards its sign.</summary>
    public Color FunctionPressed { get; private set; } = Of(context, dark, Android.Resource.Color.SystemAccent2200, Android.Resource.Color.SystemAccent2600);

    public Color FunctionGlyph { get; private set; } = Of(context, dark, Android.Resource.Color.SystemAccent2700, Android.Resource.Color.SystemAccent2100);

    public Color Trail { get; private set; } = Of(context, dark, Android.Resource.Color.SystemAccent1600, Android.Resource.Color.SystemAccent1200);

    public Color GridCircle { get; private set; } = Of(context, dark, Android.Resource.Color.SystemAccent1200, Android.Resource.Color.SystemAccent1700);

    public Color Divider { get; private set; } = Of(context, dark, Android.Resource.Color.SystemNeutral2200, Android.Resource.Color.SystemNeutral2700);

    /// <summary>The floating keyboard's outline while resized, its corners and, at 80 %, its edge.</summary>
    public Color Accent { get; private set; } = Of(context, dark, Android.Resource.Color.SystemAccent1600, Android.Resource.Color.SystemAccent1200);

    /// <summary>The floating keyboard's Reset while resized: Gboard's 223 grey, the palette's nearest tone.</summary>
    public Color ResetGround { get; private set; } = Of(context, dark, Android.Resource.Color.SystemNeutral1100, Android.Resource.Color.SystemNeutral1800);

    /// <summary>The floating keyboard's handle: Gboard's 168 grey, the palette's nearest tone.</summary>
    public Color Handle { get; private set; } = Of(context, dark, Android.Resource.Color.SystemNeutral1300, Android.Resource.Color.SystemNeutral1600);

    /// <summary>The grid customised (✏): ✓'s circle and the outlines, Gboard's blue, the palette's primary (#387AFF on the Fold); ✓ on it.</summary>
    public Color Primary { get; private set; } = Of(context, dark,
        OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemPrimaryLight : Android.Resource.Color.SystemAccent1600,
        OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemPrimaryDark : Android.Resource.Color.SystemAccent1200);

    public Color OnPrimary { get; private set; } = Of(context, dark,
        OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemOnPrimaryLight : Android.Resource.Color.SystemAccent10,
        OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemOnPrimaryDark : Android.Resource.Color.SystemAccent1800);

    /// <summary>The emoji's ground, system_surface_container_low_light (#F1F1F3 on the Fold), and their grey pills, system_surface_container_high_light (#CCCCCE).</summary>
    public Color Low { get; private set; } = Of(context, dark,
        OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemSurfaceContainerLowLight : Android.Resource.Color.SystemNeutral150,
        OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemSurfaceContainerLowDark : Android.Resource.Color.SystemNeutral1900);

    public Color High { get; private set; } = Of(context, dark,
        OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemSurfaceContainerHighLight : Android.Resource.Color.SystemNeutral1200,
        OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemSurfaceContainerHighDark : Android.Resource.Color.SystemNeutral1700);

    /// <summary>The grid customised: the ground inside its outlines, system_surface_dim_light (#DAD9E0 on the Fold).</summary>
    public Color Dim { get; private set; } = Of(context, dark,
        OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemSurfaceDimLight : Android.Resource.Color.SystemNeutral2100,
        OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemSurfaceDimDark : Android.Resource.Color.SystemNeutral2900);

    public Color Outline { get; private set; } = Of(context, dark,
        OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemOutlineLight : Android.Resource.Color.SystemNeutral1500,
        OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemOutlineDark : Android.Resource.Color.SystemNeutral1400);

    /// <summary>The outline at half: on the Fold, a dot not in sight 180 where the ground is 228 and the outline 132.</summary>
    public Color FaintOutline => Color.Argb(128, Outline.R, Outline.G, Outline.B);

    private static Color Of(Context context, bool dark, int light, int darkColor) => new(context.GetColor(dark ? darkColor : light));

    private static Color Of(Rgb color) => new(color.R, color.G, color.B);
}

/// <summary>
/// The standard keyboard, Gboard's copied (docs/tODO/settings-languages.md):
/// its pages (<see cref="StandardKeys"/>) drawn on one view as Gboard draws
/// them, in the device's colours (<see cref="StandardColors"/>). A key acts as
/// the finger lifts; held, a letter types its corner's symbol and ⌫ repeats;
/// Shift twice quickly locks the capitals; a swipe over the letters is read
/// as a word (<see cref="Swiped"/>) and a swipe along the space bar is the
/// next language. As Estándar PC (<see cref="Pc"/>), a PC's keys on one
/// page in the same look: Ctrl, Alt, ⊞ and Alt Gr on until the next key,
/// Bloq Mayús, the dead keys' accents on the next letter, the arrows going
/// on while held. Its sizes are Gboard's on a Pixel 7 (420 dpi): pixels
/// measured there over 2.625; its letters' size, signs and corners' symbols
/// are shares of a letter row's height, so they grow with the keys.
/// </summary>
public sealed class StandardKeyboard : View
{
    private const float PixelsPerDp = 2.625f;
    private const float SideDp = 12 / PixelsPerDp;
    private const float KeyGapDp = 14 / PixelsPerDp;
    private const float RowGapDp = 22.5f / PixelsPerDp;
    private const float NumberRowDp = 117 / PixelsPerDp;
    private const float LetterRowDp = 124 / PixelsPerDp;
    private const float TopDp = 11 / PixelsPerDp;
    private const float BottomDp = 32 / PixelsPerDp;
    private const float CornerDp = 15.5f / PixelsPerDp;

    /// <summary>
    /// Estándar XR's keys, Gboard's on the Galaxy XR as measured on its capture (docs/tODO/photos/gboard/xr-letters.png):
    /// a key 78 px tall, its corners 20 px round, 26 px between keys across and down; every row as tall, the digits' too.
    /// </summary>
    private const float XrCornerShare = 20f / 78;
    private const float XrGapShare = 26f / 78;

    /// <summary>Roboto's cap height, of its size: what a glyph's measured height is turned into a size by.</summary>
    private const float CapHeight = 0.711f;

    /// <summary>A letter's size: its digits' cap height (52 px) over Roboto's, of the row's 124 px.</summary>
    private const float LabelShare = 52 / CapHeight / 124;

    /// <summary>A corner's symbol: on the Fold, q's % 16 px tall on an 82 px key.</summary>
    private const float HintShare = 16 / CapHeight / 82;

    /// <summary>Where a corner's symbol stands: on the Fold, 9 px down and 8 px in, of an 82 px key.</summary>
    private const float HintTopShare = 9f / 82;
    private const float HintRightShare = 8f / 82;

    /// <summary>?123, ABC and =\&lt;: their ink 34 px tall over Roboto's cap height.</summary>
    private const float WordShare = 34 / CapHeight / 124;

    /// <summary>The language on the space bar: "EN • ES" 24 px tall.</summary>
    private const float SpaceLabelShare = 24 / CapHeight / 124;

    /// <summary>1234's two lines.</summary>
    private const float PadLabelShare = SpaceLabelShare;

    /// <summary>Each sign's box, its drawn part as tall or wide as Gboard's (px over the part of its 960 box it fills).</summary>
    private const float ShiftShare = 55f / (760f / 960) / 124;
    private const float DeleteShare = 65f / (720f / 960) / 124;
    private const float LanguageShare = 52f / (800f / 960) / 124;
    private const float ReturnShare = 48f / (720f / 960) / 124;

    /// <summary>How much of a function key's width its word may take.</summary>
    private const float LabelRoomShare = 0.94f;

    /// <summary>The steps a word goes down by, of its largest size, until it fits its key.</summary>
    private static readonly float[] WordSteps = [1f, 0.9f, 0.8f, 0.7f, 0.6f, 0.5f, 0.4f, 0.3f];

    /// <summary>A PC's words in a medium weight, as the owner asked: the regular one too thin when small.</summary>
    private static readonly Typeface MediumWords = Typeface.Create("sans-serif-medium", TypefaceStyle.Normal)!;

    /// <summary>The comma's emoji: on the Fold, 20 px wide on an 86 px key, its centre at 0.30 of it, the comma's at 0.62.</summary>
    private const float EmojiShare = 20f / (800f / 960) / 86;
    private const float EmojiCentreShare = 0.30f;
    private const float CommaCentreShare = 0.62f;

    /// <summary>The trail Gboard draws: 30 px wide at the finger, 14 px at its end, 93 % then 40 % opaque, the whole swipe in sight.</summary>
    private const float TrailDp = 30 / PixelsPerDp;
    private const float TrailTailDp = 14 / PixelsPerDp;
    private const long TrailLife = 1500;
    private const float TrailHeadAlpha = 0.93f;
    private const float TrailTailAlpha = 0.40f;


    private readonly StandardColors colors;
    private readonly Paint fill = new(PaintFlags.AntiAlias);
    private readonly Paint text = new(PaintFlags.AntiAlias) { TextAlign = Paint.Align.Center };
    private readonly Handler handler = new(Looper.MainLooper!);
    private readonly Dictionary<int, Drawable> icons = [];
    private readonly float density;

    /// <summary>A dead key's accent and the combining mark it puts on the next letter.</summary>
    private static readonly Dictionary<string, string> DeadMarks = new()
    {
        ["´"] = "\u0301",
        ["`"] = "\u0300",
        ["^"] = "\u0302",
        ["¨"] = "\u0308",
    };

    private StandardPage page = StandardPage.Letters;
    private ShiftState shift = ShiftState.Off;
    private bool pc;
    private bool ctrl;
    private bool alt;
    private bool meta;
    private bool altGr;
    private bool capsLock;

    /// <summary>A dead key's accent typed, waiting for the letter it goes on.</summary>
    private string? deadKey;
    private long shiftTappedAt;
    private List<(StandardKey Key, RectF Box)> laid = [];

    /// <summary>Each function key's word size, one for its whole row.</summary>
    private readonly Dictionary<StandardKey, float> wordSizes = [];

    /// <summary>The finger on a key: which, where it went down, and what it has done so far.</summary>
    private Finger? finger;
    private SwipeTrail? trail;
    private Java.Lang.Runnable? held;

    private enum ShiftState
    {
        Off,
        Once,
        Locked,
    }

    private sealed class Finger(int pointer, StandardKey key, RectF box, float x, float y, long start)
    {
        public int Pointer { get; } = pointer;
        public StandardKey Key { get; } = key;
        public RectF Box { get; } = box;
        public float X { get; } = x;
        public float Y { get; } = y;
        public long Start { get; } = start;
        public List<(float X, float Y, long Time)> Points { get; } = [];
        public bool Swiping { get; set; }
        public bool LongPressed { get; set; }

        /// <summary>A PC's key held: it went on by itself, so the finger lifting types nothing more.</summary>
        public bool Repeated { get; set; }
        public bool Done { get; set; }
    }

    public StandardKeyboard(Context context, StandardColors colors) : base(context)
    {
        this.colors = colors;
        density = context.Resources!.DisplayMetrics!.Density;
        HapticFeedbackEnabled = true;
    }

    /// <summary>Text typed: a key's, a long press's symbol, a space.</summary>
    public Action<string>? Typed { get; set; }

    /// <summary>⌫, once on the touch and again while held.</summary>
    public Action? Deleted { get; set; }

    public Action? Entered { get; set; }

    /// <summary>A PC's key that is no character, or a character with Ctrl, Alt or ⊞: its key code and the modifiers on.</summary>
    public Action<Keycode, MetaKeyStates>? KeyPressed { get; set; }

    /// <summary>Whether the keys are a PC's (Estándar PC) rather than Gboard's.</summary>
    public bool Pc
    {
        get => pc;
        set
        {
            pc = value;
            page = BasePage;
            RequestLayout();
            Lay();
        }
    }

    /// <summary>Whether the keys are Gboard's on the Galaxy XR (Estándar XR): a tablet's letters and symbols.</summary>
    public bool Xr
    {
        get => xr;
        set
        {
            xr = value;
            page = BasePage;
            RequestLayout();
            Lay();
        }
    }

    private bool xr;

    /// <summary>The keyboard's own page, the one a design is of and its height made for: a PC's, Estándar XR's letters, or Gboard's.</summary>
    public StandardPage BasePage => pc ? StandardPage.Pc : xr ? StandardPage.XrLetters : StandardPage.Letters;

    /// <summary>🌐, or a swipe along the space bar: the next language.</summary>
    public Action? NextLanguage { get; set; }

    /// <summary>The comma held: the emoji, as Gboard opens them.</summary>
    public Action? EmojisAsked { get; set; }

    /// <summary>A key whose part is not made yet: what it is, to say so.</summary>
    public Action<string>? NotMade { get; set; }

    /// <summary>A swipe over the letters, to be read as a word, and whether Shift or the capitals' lock was on.</summary>
    public Action<KeyPanel.SwipePath, bool, bool>? Swiped { get; set; }

    /// <summary>The language the keys are laid out for, the space bar reads and a swipe is read in.</summary>
    public StandardLanguage Language
    {
        get => language;
        set
        {
            language = value;
            Lay();
        }
    }

    private StandardLanguage language = StandardKeys.Spanish;

    /// <summary>How long a key is held for its corner's symbol, and Shift's two taps' room (Preferencias › Duración de la pulsación).</summary>
    public int LongPressMs { get; set; } = KeyPlusPreferences.LongPressDefault;

    /// <summary>Whether a key's touch is felt (Preferencias › Vibración al pulsar las teclas).</summary>
    public bool Vibrates { get; set; } = true;

    /// <summary>Whether a swipe draws its trail (Escritura deslizando el dedo › Recorrido del deslizamiento).</summary>
    public bool ShowsTrail { get; set; } = true;

    /// <summary>Whether a touch types: off, the keys are only to be seen (Tema's preview), designed all the same.</summary>
    public bool Types { get; set; } = true;

    /// <summary>The keys' size, 1 for Gboard's own.</summary>
    public float Scale
    {
        get => scale;
        set
        {
            scale = value;
            RequestLayout();
        }
    }

    private float scale = 1f;

    private float Dp(float value) => value * density * scale;

    private float LetterRow => Dp(LetterRowDp);

    /// <summary>
    /// Gboard's letter key, as wide as it is tall: (1080 − 2·12 − 9·14) / 10 =
    /// 93 px across a 124 px row on the Pixel 7.
    /// </summary>
    private const float GboardKeyAspect = 93f / 124;

    /// <summary>
    /// The letters', signs' and corners' size, of Gboard's: 1 on Gboard's
    /// keys, as much smaller as a narrower key (a PC's fifteen across) is.
    /// </summary>
    private float glyphScale = 1f;

    /// <summary>
    /// The keys' width at Gboard's size for so many keys across, in dp: each
    /// key as wide as Gboard's is for its row's height (93 px on a 124 px
    /// row), Gboard's gaps and sides; ten across are Gboard's own 1080 px.
    /// The width a keyboard has of its own, whatever room it stands in.
    /// </summary>
    public static float WidthDp(float across) => (2 * SideDp) + (across * LetterRowDp * GboardKeyAspect) + ((across - 1) * KeyGapDp);

    /// <summary>A page's keys across, as made: its widest row's widths, in keys.</summary>
    public static float Across(StandardPage page, StandardLanguage language) =>
        Math.Max(1, StandardKeys.Page(page, language).Max(row => row.Sum(key => key.Width)));

    /// <summary>The keys' height for a number of rows at Gboard's size, in dp: the keys at a size are as many times as tall.</summary>
    public static float HeightDp(int rows, bool xr = false) => xr
        ? TopDp + (rows * LetterRowDp) + ((rows - 1) * LetterRowDp * XrGapShare) + BottomDp
        : TopDp + NumberRowDp + ((rows - 1) * (LetterRowDp + RowGapDp)) + BottomDp;

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        var width = MeasureSpec.GetSize(widthMeasureSpec);
        var rows = Math.Max(1, Rows(BasePage).Count);
        SetMeasuredDimension(width, (int)Math.Ceiling(Dp(HeightDp(rows, xr))));
    }

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        Lay();
    }

    /// <summary>
    /// The page's keys in place: its widest row's keys across (Gboard's ten, a
    /// PC's fifteen) with Gboard's gaps, a shorter row in the middle; the
    /// letters page's top row lower than its letters', the symbols' pages'
    /// rows sharing the same height alike; a shaped key's top sized after its
    /// lower part (<see cref="KeyWidths"/>).
    /// </summary>
    private void Lay()
    {
        laid = [];
        places = [];
        rowBands = [];
        joints = [];
        wordSizes.Clear();
        var rows = Rows(page);
        var side = Dp(SideDp);
        var gap = xr ? LetterRow * XrGapShare : Dp(KeyGapDp);
        // As wide as the keyboard as made is, a design or not: a row made wider than that squeezes its own keys
        // alone, so a key widened takes its room from its row, the others keep theirs.
        var across = Across(page, language);
        var unit = (Width - (2 * side) - ((across - 1) * gap)) / across;
        keyUnit = unit;
        glyphScale = Math.Min(1f, unit / (LetterRow * GboardKeyAspect));
        var rowGap = xr ? gap : Dp(RowGapDp);
        float[] heights = page is StandardPage.XrLetters ? [.. Enumerable.Repeat(LetterRow, rows.Count)]
            : page is StandardPage.Letters or StandardPage.Pc
            ? [Dp(NumberRowDp), .. Enumerable.Repeat(LetterRow, rows.Count - 1)]
            : Enumerable.Repeat((Height - Dp(TopDp) - Dp(BottomDp) - ((rows.Count - 1) * rowGap)) / rows.Count, rows.Count).ToArray();
        var top = Dp(TopDp);
        List<int> shapedAbove = [];
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var widths = KeyWidths(rows, r, across, unit);
            var rowWidth = widths.Sum() + ((widths.Length - 1) * gap);
            var x = (Width - rowWidth) / 2;
            // A row with a shaped key's lower part stands so the part is right-aligned under the key.
            if (row.Select((key, i) => key.Lower ? i : -1).FirstOrDefault(i => i >= 0, -1) is var lowerAt and >= 0 && shapedAbove.Count > 0)
            {
                var lowerRight = x + widths.Take(lowerAt + 1).Sum() + (lowerAt * gap);
                x += laid[shapedAbove[0]].Box.Right - lowerRight;
            }

            List<int> shaped = [];
            var lowers = 0;
            for (var i = 0; i < row.Count; i++)
            {
                var key = row[i];
                var keyWidth = widths[i];
                laid.Add((key, new RectF(x, top, x + keyWidth, top + heights[r])));
                places.Add((r, i));
                if (key.Below > 0)
                {
                    shaped.Add(laid.Count - 1);
                }

                if (key.Lower && lowers < shapedAbove.Count)
                {
                    joints.Add((shapedAbove[lowers++], laid.Count - 1));
                }

                x += keyWidth + gap;
            }

            shapedAbove = shaped;
            rowBands.Add((top, top + heights[r]));
            top += heights[r] + rowGap;
        }

        SizeWords(laid);
        Invalidate();
    }

    /// <summary>
    /// A row's keys' widths: each key's width in keys at the row's key size,
    /// the keyboard as made's widest row across, a row longer than that
    /// squeezing its own keys alone; a shaped key's top as its lower part
    /// makes it, its width in keys at the size of the row below, where that
    /// part is, so the two grow and shrink alike and the shape is kept, the
    /// other keys of its row sharing what is left.
    /// </summary>
    private float[] KeyWidths(IReadOnlyList<IReadOnlyList<StandardKey>> rows, int r, float across, float unit)
    {
        var row = rows[r];
        var gap = Dp(KeyGapDp);
        Dictionary<int, float> following = [];
        if (r + 1 < rows.Count && rows[r + 1].Where(key => key.Lower).ToList() is { Count: > 0 } lowers)
        {
            var belowUnit = RowUnit(rows[r + 1], across, unit);
            var tops = row.Select((key, i) => key.Below > 0 ? i : -1).Where(i => i >= 0).ToList();
            for (var k = 0; k < tops.Count && k < lowers.Count; k++)
            {
                following[tops[k]] = (row[tops[k]].Width * belowUnit) + ((lowers[k].Width - 1) * gap);
            }
        }

        var others = row.Where((_, i) => !following.ContainsKey(i)).ToList();
        var keys = others.Sum(key => key.Width);
        var room = Width - (2 * Dp(SideDp)) - following.Values.Sum() - ((keys - others.Count + row.Count - 1) * gap);
        var rowUnit = keys > 0 && keys * unit > room ? room / keys : unit;
        return [.. row.Select((key, i) => following.TryGetValue(i, out var made) ? made : (key.Width * rowUnit) + ((key.Width - 1) * gap))];
    }

    /// <summary>A row's key size: the keyboard's, or smaller where the row is longer than the keyboard as made's widest.</summary>
    private float RowUnit(IReadOnlyList<StandardKey> row, float across, float unit)
    {
        var keys = row.Sum(key => key.Width);
        return keys > across ? (Width - (2 * Dp(SideDp)) - ((keys - 1) * Dp(KeyGapDp))) / keys : unit;
    }

    /// <summary>A page's rows: the letters' as designed where the keyboard has a design, else as made.</summary>
    private IReadOnlyList<IReadOnlyList<StandardKey>> Rows(StandardPage shown) =>
        design is not null && shown is StandardPage.Letters or StandardPage.Pc or StandardPage.XrLetters
            ? design.Page(StandardKeys.Page(shown, language), language)
            : StandardKeys.Page(shown, language);

    private void ShowPage(StandardPage shown)
    {
        page = shown;
        shift = ShiftState.Off;
        Lay();
    }

    protected override void OnDraw(Canvas canvas)
    {
        canvas.DrawColor(colors.Ground);
        foreach (var (upper, lower) in joints)
        {
            if (!(designing && dragged is { } moving && (moving.Place == places[upper] || moving.Place == places[lower])))
            {
                DrawJoint(canvas, upper, lower);
            }
        }

        // Shaped keys' lower parts first, so the label their top draws across both stays on top.
        foreach (var i in Enumerable.Range(0, laid.Count).OrderBy(i => laid[i].Key.Lower ? 0 : 1))
        {
            if (designing && dragged is { } held && held.Place == places[i])
            {
                continue;
            }

            DrawKey(canvas, laid[i].Key, laid[i].Box, Marked(i), LabelAt(i));
        }

        if (designing)
        {
            DrawDesigning(canvas);
        }
    }

    /// <summary>A key's ground: marked blue while designing, a modifier on, a function key's tint or a letter's, darker while pressed.</summary>
    /// <summary>A key's corners: Gboard's, Estándar XR's a quarter of its height.</summary>
    private float Corner(RectF box) => xr ? box.Height() * XrCornerShare : Dp(CornerDp);

    /// <summary>Whether a key is Estándar XR's Enter, in the primary colour as Gboard's there, its sign on it.</summary>
    private bool PrimaryEnter(StandardKey key) => xr && key.Action == StandardAction.Enter;

    private Color KeyGround(StandardKey key, bool marked)
    {
        var pressed = !designing && finger is { Swiping: false } now && now.Key == key;
        return marked ? colors.Primary
            : PrimaryEnter(key) ? (pressed ? colors.Accent : colors.Primary)
            : IsOn(key) ? colors.GridCircle
            : key.Look != StandardLook.Letter ? (pressed ? colors.FunctionPressed : colors.Function)
            : (pressed ? colors.Function : colors.Key);
    }

    /// <summary>A shaped key's two parts drawn as one: the room between them filled where both are across, into their corners, so the edge they share runs straight.</summary>
    private void DrawJoint(Canvas canvas, int upper, int lower)
    {
        var top = laid[upper].Box;
        var bottom = laid[lower].Box;
        var left = Math.Max(top.Left, bottom.Left);
        var right = Math.Min(top.Right, bottom.Right);
        if (right <= left)
        {
            return;
        }

        var corner = Corner(top);
        fill.Color = KeyGround(laid[upper].Key, Marked(upper));
        canvas.DrawRect(left, top.Bottom - corner, right, bottom.Top + corner, fill);
    }

    /// <summary>Whether a laid key is marked while designing: itself, or the other part of its shaped key, the whole key marked as one.</summary>
    private bool Marked(int i) =>
        designing && (selected == places[i] || joints.Any(joint => (joint.Top == i || joint.Lower == i) && (selected == places[joint.Top] || selected == places[joint.Lower])));

    /// <summary>Where a shaped key's label goes: across, the middle of where its two parts both are; down, halfway from its top to its bottom, inside the shape whichever it is. None for any other key, labelled in its box's middle.</summary>
    private PointF? LabelAt(int i)
    {
        var at = joints.FindIndex(joint => joint.Top == i);
        if (at < 0)
        {
            return null;
        }

        var top = laid[joints[at].Top].Box;
        var bottom = laid[joints[at].Lower].Box;
        return new PointF((Math.Max(top.Left, bottom.Left) + Math.Min(top.Right, bottom.Right)) / 2, (top.Top + bottom.Bottom) / 2);
    }

    private void DrawKey(Canvas canvas, StandardKey key, RectF box, bool marked = false, PointF? labelAt = null)
    {
        if (key.Look == StandardLook.Gap)
        {
            if (designing)
            {
                fill.Color = marked ? colors.Primary : colors.FaintOutline;
                fill.SetStyle(Paint.Style.Stroke);
                fill.StrokeWidth = Dp(2);
                canvas.DrawRoundRect(box, Corner(box), Corner(box), fill);
                fill.SetStyle(Paint.Style.Fill);
            }

            return;
        }

        var function = key.Look != StandardLook.Letter;
        fill.Color = KeyGround(key, marked);
        var corner = key.Look == StandardLook.Pill ? box.Height() / 2 : Corner(box);
        canvas.DrawRoundRect(box, corner, corner, fill);
        if (key.Lower)
        {
            return;
        }

        // A shaped key's label in the middle of the whole key, not of its top.
        if (labelAt is { } middle)
        {
            box = new RectF(middle.X - (box.Width() / 2), middle.Y - (box.Height() / 2), middle.X + (box.Width() / 2), middle.Y + (box.Height() / 2));
        }

        var glyph = marked || PrimaryEnter(key) ? colors.OnPrimary : function ? colors.FunctionGlyph : colors.Glyph;
        var h = LetterRow * glyphScale;

        if (key.Action == StandardAction.Shift)
        {
            DrawIcon(canvas, shift switch
            {
                ShiftState.Once => Resource.Drawable.ic_key_shift_on,
                ShiftState.Locked => Resource.Drawable.ic_key_shift_lock,
                _ => Resource.Drawable.ic_key_shift,
            }, box.CenterX(), box.CenterY(), h * ShiftShare, glyph);
            return;
        }

        if (key.Icon != 0)
        {
            var share = key.Action switch
            {
                StandardAction.Delete => DeleteShare,
                StandardAction.Language => LanguageShare,
                _ => ReturnShare,
            };
            DrawIcon(canvas, key.Icon, box.CenterX(), box.CenterY(), h * share, glyph);
            return;
        }

        if (key.HintIcon != 0)
        {
            DrawIcon(canvas, key.HintIcon, box.CenterX(), box.Top + (box.Height() * EmojiCentreShare), h * EmojiShare, glyph);
            DrawText(canvas, key.Label, box.CenterX(), box.Top + (box.Height() * CommaCentreShare), h * LabelShare, glyph);
            return;
        }

        switch (key.Action)
        {
            case StandardAction.Space:
                DrawText(canvas, language.Name, box.CenterX(), box.CenterY(), h * SpaceLabelShare, glyph);
                return;
            case StandardAction.Numbers:
                DrawText(canvas, "12", box.CenterX(), box.CenterY() - (h * PadLabelShare * 0.45f), h * PadLabelShare, glyph);
                DrawText(canvas, "34", box.CenterX(), box.CenterY() + (h * PadLabelShare * 0.45f), h * PadLabelShare, glyph);
                return;
            case StandardAction.Type:
                break;
            default:
                text.SetTypeface(pc ? MediumWords : Typeface.Default);
                DrawText(canvas, key.Label, box.CenterX(), box.CenterY(), wordSizes.TryGetValue(key, out var size) ? size : h * LabelShare, glyph);
                text.SetTypeface(Typeface.Default);
                return;
        }

        // The corner's symbol in the key's place while held, or for every key while Alt Gr is on, as Shift shows its capitals.
        var showsHint = key.Hint is not null && (altGr || (finger is { LongPressed: true } longPress && longPress.Key == key));

        // Alt Gr on, a PC's keys show only its symbols: a key with none, blank.
        if (pc && altGr && key.Hint is null)
        {
            return;
        }

        DrawText(canvas, showsHint ? key.Hint! : LabelOf(key), box.CenterX(), box.CenterY(), h * LabelShare, glyph);

        // A PC's key, as a PC's keycap: its Shift's at the top corner, its Alt Gr's at the bottom one, and with Shift or
        // Alt Gr on only what it types then; Gboard's, its held symbol at the top but while it stands in the key's place.
        var corners = !showsHint && !(pc && (shift != ShiftState.Off || altGr));
        if (corners && pc && key.ShiftCorner is { } shifted)
        {
            DrawCorner(canvas, shifted, box, h, glyph, top: true);
        }

        if (corners && key.Hint is not null)
        {
            DrawCorner(canvas, key.Hint, box, h, glyph, top: !pc);
        }
    }

    /// <summary>A symbol in a key's right corner, Gboard's size and inset (on the Fold 9 px from the edge and 8 px in, of an 82 px key), at its top or bottom.</summary>
    private void DrawCorner(Canvas canvas, string symbol, RectF box, float h, Color glyph, bool top)
    {
        text.TextAlign = Paint.Align.Right;
        text.TextSize = h * HintShare;
        text.Color = glyph;
        var inset = box.Height() * glyphScale * HintTopShare;
        var baseline = top ? box.Top + inset + (text.TextSize * CapHeight) : box.Bottom - inset;
        canvas.DrawText(symbol, box.Right - (box.Height() * glyphScale * HintRightShare), baseline, text);
        text.TextAlign = Paint.Align.Center;
    }

    /// <summary>A key's label as Shift has it: a letter in capitals, a digit as its symbol.</summary>
    private string LabelOf(StandardKey key) => ShiftedFor(key) && key.Shifted is not null ? key.Shifted : key.Label;

    /// <summary>Whether a key types its Shift's: a letter with Shift or Bloq Mayús, not both; any other with Shift.</summary>
    private bool ShiftedFor(StandardKey key) =>
        key.Text is { Length: 1 } one && char.IsLetter(one[0]) ? (shift != ShiftState.Off) != capsLock : shift != ShiftState.Off;

    /// <summary>Whether a modifier key is on, drawn so.</summary>
    private bool IsOn(StandardKey key) => key.Action switch
    {
        StandardAction.Ctrl => ctrl,
        StandardAction.Alt => alt,
        StandardAction.Meta => meta,
        StandardAction.AltGr => altGr,
        StandardAction.CapsLock => capsLock,
        _ => false,
    };

    /// <summary>
    /// The page's function keys' words as large as most of them hold (three
    /// in four), the same size, those that do not fit a step smaller at a
    /// time (ImpPnt, Bloq Mayús): the owner's, room not to be wasted, the
    /// keys even. Gboard's size at most on Gboard's keys, a letter's on a PC's. A
    /// sign alone (an arrow, ⊞) is a letter's size.
    /// </summary>
    private void SizeWords(List<(StandardKey Key, RectF Box)> page)
    {
        var largest = LetterRow * (pc ? LabelShare : WordShare);
        var own = page.Where(laidKey => Worded(laidKey.Key))
            .Select(laidKey => (laidKey.Key, Size: largest * WordSteps.FirstOrDefault(share => Fits(laidKey.Key.Label, largest * share, laidKey.Box.Width() * LabelRoomShare), WordSteps[^1])))
            .ToList();
        if (own.Count == 0)
        {
            return;
        }

        var even = own.Select(word => word.Size).OrderDescending().ElementAt((int)Math.Ceiling(own.Count * MostWordsShare) - 1);
        foreach (var (key, size) in own)
        {
            wordSizes[key] = Math.Min(size, even);
        }
    }

    /// <summary>How many of a page's words its even size must fit.</summary>
    private const float MostWordsShare = 0.75f;

    /// <summary>Whether a key is drawn as a word: a function key with no sign and more than one character.</summary>
    private static bool Worded(StandardKey key) =>
        key.Icon == 0 && key.HintIcon == 0 && key.Label.Length > 1
        && key.Action is not (StandardAction.Type or StandardAction.Space or StandardAction.Numbers or StandardAction.Shift);

    private bool Fits(string label, float size, float room)
    {
        text.SetTypeface(pc ? MediumWords : Typeface.Default);
        text.TextSize = size;
        var fits = text.MeasureText(label) <= room;
        text.SetTypeface(Typeface.Default);
        return fits;
    }

    private void DrawText(Canvas canvas, string label, float centreX, float centreY, float size, Color color)
    {
        text.TextSize = size;
        text.Color = color;
        canvas.DrawText(label, centreX, centreY - ((text.Ascent() + text.Descent()) / 2), text);
    }

    private void DrawIcon(Canvas canvas, int id, float centreX, float centreY, float size, Color color)
    {
        if (!icons.TryGetValue(id, out var icon))
        {
            icons[id] = icon = Context!.GetDrawable(id)!.Mutate()!;
        }

        icon.SetTint(color);
        var half = size / 2;
        icon.SetBounds((int)(centreX - half), (int)(centreY - half), (int)(centreX + half), (int)(centreY + half));
        icon.Draw(canvas);
    }

    /// <summary>The key under a point: the one in its row nearest across, the row being the one whose band, gaps shared, holds it.</summary>
    private (StandardKey Key, RectF Box)? KeyAt(float x, float y)
    {
        var rowGap = Dp(RowGapDp);
        var inRow = laid.Where(laidKey => y >= laidKey.Box.Top - (rowGap / 2) && y < laidKey.Box.Bottom + (rowGap / 2)).ToList();
        if (inRow.Count == 0)
        {
            inRow = laid.Where(laidKey => Math.Abs(laidKey.Box.CenterY() - y) == laid.Min(other => Math.Abs(other.Box.CenterY() - y))).ToList();
        }

        return inRow.Count == 0 ? null : inRow.MinBy(laidKey => x < laidKey.Box.Left ? laidKey.Box.Left - x : x > laidKey.Box.Right ? x - laidKey.Box.Right : 0);
    }

    public override bool OnTouchEvent(MotionEvent? motion)
    {
        if (motion is null)
        {
            return false;
        }

        if (designing)
        {
            DesignTouch(motion);
            return true;
        }

        if (!Types)
        {
            return true;
        }

        switch (motion.ActionMasked)
        {
            case MotionEventActions.Down:
            case MotionEventActions.PointerDown:
                // A second finger down finishes the first's key, as fast typing has it.
                if (finger is { } first)
                {
                    Finish(first);
                }

                var index = motion.ActionIndex;
                Begin(motion.GetPointerId(index), motion.GetX(index), motion.GetY(index), motion.EventTime);
                break;
            case MotionEventActions.Move when finger is { Done: false } now:
                var at = motion.FindPointerIndex(now.Pointer);
                if (at >= 0)
                {
                    for (var i = 0; i < motion.HistorySize; i++)
                    {
                        Track(now, motion.GetHistoricalX(at, i), motion.GetHistoricalY(at, i), motion.GetHistoricalEventTime(i));
                    }

                    Track(now, motion.GetX(at), motion.GetY(at), motion.EventTime);
                }

                break;
            case MotionEventActions.Up:
            case MotionEventActions.PointerUp:
                if (finger is { } lifted && motion.GetPointerId(motion.ActionIndex) == lifted.Pointer)
                {
                    Track(lifted, motion.GetX(motion.ActionIndex), motion.GetY(motion.ActionIndex), motion.EventTime);
                    Finish(lifted);
                }

                break;
            case MotionEventActions.Cancel:
                Forget();
                break;
        }

        return true;
    }

    private void Begin(int pointer, float x, float y, long time)
    {
        if (KeyAt(x, y) is not { } hit)
        {
            return;
        }

        var now = new Finger(pointer, hit.Key, hit.Box, x, y, time);
        now.Points.Add((x, y, time));
        finger = now;
        if (Vibrates)
        {
            PerformHapticFeedback(FeedbackConstants.KeyboardTap);
        }

        if (hit.Key.Action == StandardAction.Delete)
        {
            DeleteOnce();
            Hold(DeleteOnce, KeyPanel.RepeatIntervalMs, KeyPanel.RepeatDelayMs);
        }
        else if (hit.Key.Repeats)
        {
            Press(hit.Key.Code);
            Hold(() => Press(hit.Key.Code), KeyPanel.RepeatIntervalMs, KeyPanel.RepeatDelayMs);
        }
        else if (pc && hit.Key.Action is StandardAction.Type or StandardAction.Space or StandardAction.Enter)
        {
            // A PC's key held goes on as a PC's does, the owner's: once after the delay, then again and again;
            // the corner's symbol is Alt Gr's. Moved off, it is a swipe and nothing repeats.
            Hold(() =>
            {
                now.Repeated = true;
                Act(now.Key);
            }, KeyPanel.RepeatIntervalMs, KeyPanel.RepeatDelayMs);
        }
        else if (hit.Key.Hint is not null)
        {
            Hold(() =>
            {
                now.LongPressed = true;
                if (Vibrates)
                {
                    PerformHapticFeedback(FeedbackConstants.LongPress);
                }

                Invalidate();
            }, 0, LongPressMs);
        }
        else if (hit.Key.HintIcon == Resource.Drawable.ic_key_emoji)
        {
            // The comma with ☺ in its corner, held: the emoji at once, the finger still down, as Gboard's.
            Hold(() =>
            {
                now.LongPressed = true;
                if (Vibrates)
                {
                    PerformHapticFeedback(FeedbackConstants.LongPress);
                }

                EmojisAsked?.Invoke();
            }, 0, LongPressMs);
        }

        Invalidate();
    }

    /// <summary>Does something once the key has been held a while, and again every so often where it repeats.</summary>
    private void Hold(Action action, int every, int after)
    {
        StopHolding();
        held = new Java.Lang.Runnable(() =>
        {
            action();
            if (every > 0)
            {
                handler.PostDelayed(held!, every);
            }
        });
        handler.PostDelayed(held, after);
    }

    private void StopHolding()
    {
        if (held is not null)
        {
            handler.RemoveCallbacks(held);
            held = null;
        }
    }

    /// <summary>A point of the finger's path: past a key's touch on a letter, a swipe, its trail drawn.</summary>
    private void Track(Finger now, float x, float y, long time)
    {
        now.Points.Add((x, y, time));
        trail?.Add(x, y, time);
        if (now.Swiping || now.LongPressed)
        {
            return;
        }

        var far = Math.Max(Math.Abs(x - now.X), Math.Abs(y - now.Y)) > Math.Min(now.Box.Width(), now.Box.Height()) * 0.6f;
        if (far && page is StandardPage.Letters or StandardPage.Pc or StandardPage.XrLetters && Swiped is not null && now.Key.Text is { Length: 1 } letter && language.SwipeLetters.Contains(letter))
        {
            now.Swiping = true;
            StopHolding();
            ShowTrail(now);
            Invalidate();
        }
        else if (far && now.Key.Action != StandardAction.Delete && !now.Key.Repeats)
        {
            StopHolding();
        }
    }

    private void ShowTrail(Finger now)
    {
        if (!ShowsTrail)
        {
            return;
        }

        trail = new SwipeTrail(Dp(TrailDp), colors.Trail, 0, 0, Dp(TrailTailDp), TrailLife, TrailHeadAlpha, TrailTailAlpha);
        trail.SetBounds(0, 0, Width, Height);
        foreach (var (x, y, time) in now.Points)
        {
            trail.Add(x, y, time);
        }

        Overlay!.Add(trail);
    }

    private void Forget()
    {
        StopHolding();
        if (trail is not null)
        {
            Overlay!.Remove(trail);
            trail = null;
        }

        finger = null;
        Invalidate();
    }

    /// <summary>The finger lifted, or another came down: what the key does, or the swipe read.</summary>
    private void Finish(Finger now)
    {
        now.Done = true;
        var swiping = now.Swiping;
        var longPressed = now.LongPressed;
        var repeated = now.Repeated;
        Forget();
        if (swiping)
        {
            ReadSwipe(now);
        }
        else if (repeated)
        {
            // Typed while held.
        }
        else if (longPressed)
        {
            if (now.Key.Hint is { } hint)
            {
                Type(hint);
                ModifiersUsed();
            }
        }
        else if (SwipedAlong(now))
        {
            NextLanguage?.Invoke();
        }
        else
        {
            Act(now.Key);
        }
    }

    /// <summary>A swipe along the space bar, further than a key's width: the next language, as Gboard's.</summary>
    private bool SwipedAlong(Finger now) =>
        now.Key.Action == StandardAction.Space && now.Points.Count > 0 && Math.Abs(now.Points[^1].X - now.X) > LetterRow && now.Points[^1].X != now.X;

    private void Act(StandardKey key)
    {
        switch (key.Action)
        {
            case StandardAction.Type:
                TypeKey(key);
                break;
            case StandardAction.Space when Combining:
                Press(Keycode.Space);
                ModifiersUsed();
                break;
            case StandardAction.Space:
                Type(" ");
                break;
            case StandardAction.Key when key.Code == Keycode.Unknown:
                break;
            case StandardAction.Key when !key.Repeats:
                FlushDead();
                Press(key.Code);
                ModifiersUsed();
                break;
            case StandardAction.Key:
            case StandardAction.Delete when Combining:
                ModifiersUsed();
                break;
            case StandardAction.Ctrl:
                ctrl = !ctrl;
                Invalidate();
                break;
            case StandardAction.Alt:
                alt = !alt;
                Invalidate();
                break;
            case StandardAction.Meta:
                meta = !meta;
                Invalidate();
                break;
            case StandardAction.AltGr:
                altGr = !altGr;
                Invalidate();
                break;
            case StandardAction.CapsLock:
                capsLock = !capsLock;
                Invalidate();
                break;
            case StandardAction.Shift:
                ToggleShift();
                break;
            case StandardAction.Symbols:
                ShowPage(xr ? StandardPage.XrSymbols : StandardPage.Symbols);
                break;
            case StandardAction.MoreSymbols:
                ShowPage(StandardPage.MoreSymbols);
                break;
            case StandardAction.Letters:
                ShowPage(BasePage);
                break;
            case StandardAction.Numbers:
                ShowPage(xr ? StandardPage.XrNumbers : StandardPage.Numbers);
                break;
            case StandardAction.Language:
                NextLanguage?.Invoke();
                break;
            case StandardAction.Enter when Combining:
                Press(Keycode.Enter);
                ModifiersUsed();
                break;
            case StandardAction.Enter:
                FlushDead();
                Entered?.Invoke();
                break;
        }
    }

    /// <summary>
    /// A character's key: with Ctrl, Alt or ⊞ on, its key code with them (a
    /// letter's, a digit's, a sign's that has one); with Alt Gr, its corner's
    /// symbol; a dead key's accent kept for the next letter; else its text.
    /// </summary>
    private void TypeKey(StandardKey key)
    {
        if (Combining && CodeOf(key.Text) is { } code)
        {
            Press(code);
        }
        else if (altGr && key.Hint is not null)
        {
            Type(key.Hint);
        }
        else
        {
            var typed = ShiftedFor(key) && key.Shifted is not null ? key.Shifted : key.Text!;
            if (key.Dead && DeadMarks.ContainsKey(typed))
            {
                FlushDead();
                deadKey = typed;
            }
            else
            {
                Type(typed);
            }
        }

        ModifiersUsed();
    }

    /// <summary>Text typed, a dead key's accent on it where it has one with it (´ then a: á), or before it; a space types the accent alone.</summary>
    private void Type(string typed)
    {
        if (deadKey is { } accent)
        {
            deadKey = null;
            var composed = (typed + DeadMarks[accent]).Normalize(NormalizationForm.FormC);
            typed = typed == " " ? accent : composed.Length == 1 ? composed : accent + typed;
        }

        Typed?.Invoke(typed);
    }

    /// <summary>A dead key's accent typed alone, for a key that takes none.</summary>
    private void FlushDead()
    {
        if (deadKey is { } accent)
        {
            deadKey = null;
            Typed?.Invoke(accent);
        }
    }

    /// <summary>⌫: with Ctrl, Alt or ⊞ on, the key with them; a dead key's accent waiting, only that, dropped.</summary>
    private void DeleteOnce()
    {
        if (Combining)
        {
            Press(Keycode.Del);
        }
        else if (deadKey is not null)
        {
            deadKey = null;
        }
        else
        {
            Deleted?.Invoke();
        }
    }

    private void Press(Keycode code) => KeyPressed?.Invoke(code, Held());

    /// <summary>Whether a key goes with Ctrl, Alt or ⊞ as a key code rather than as text.</summary>
    private bool Combining => ctrl || alt || meta;

    /// <summary>The modifiers on, Shift with them, as a key event's meta state.</summary>
    private MetaKeyStates Held()
    {
        var held = MetaKeyStates.None;
        if (ctrl)
        {
            held |= MetaKeyStates.CtrlOn | MetaKeyStates.CtrlLeftOn;
        }

        if (alt)
        {
            held |= MetaKeyStates.AltOn | MetaKeyStates.AltLeftOn;
        }

        if (meta)
        {
            held |= MetaKeyStates.MetaOn | MetaKeyStates.MetaLeftOn;
        }

        if (shift != ShiftState.Off)
        {
            held |= MetaKeyStates.ShiftOn | MetaKeyStates.ShiftLeftOn;
        }

        return held;
    }

    /// <summary>A key gone with the modifiers: Ctrl, Alt, ⊞, Alt Gr and a once-only Shift off again.</summary>
    private void ModifiersUsed()
    {
        if (ctrl || alt || meta || altGr)
        {
            ctrl = alt = meta = altGr = false;
            Invalidate();
        }

        ShiftUsed();
    }

    /// <summary>The key code of a character a shortcut is made with: a letter's, a digit's, a few signs'.</summary>
    private static Keycode? CodeOf(string? text) => text is { Length: 1 } one
        ? one[0] switch
        {
            >= 'a' and <= 'z' => Keycode.A + (one[0] - 'a'),
            >= '0' and <= '9' => Keycode.Num0 + (one[0] - '0'),
            ',' => Keycode.Comma,
            '.' => Keycode.Period,
            '-' => Keycode.Minus,
            '+' => Keycode.Plus,
            '=' => Keycode.Equals,
            '/' => Keycode.Slash,
            _ => null,
        }
        : null;

    /// <summary>Shift: on for one key; tapped again within a long press's time, the capitals locked; tapped once more, off.</summary>
    private void ToggleShift()
    {
        var now = SystemClock.UptimeMillis();
        shift = shift switch
        {
            ShiftState.Off => ShiftState.Once,
            ShiftState.Once when now - shiftTappedAt <= LongPressMs => ShiftState.Locked,
            _ => ShiftState.Off,
        };
        shiftTappedAt = now;
        Invalidate();
    }

    /// <summary>A key typed with Shift on for one key: Shift off again.</summary>
    private void ShiftUsed()
    {
        if (shift == ShiftState.Once)
        {
            shift = ShiftState.Off;
            Invalidate();
        }
    }

    /// <summary>The swipe made FUTO Swipe's: the points and the letters' centres from 0 to 1 over the room the letters take.</summary>
    private void ReadSwipe(Finger now)
    {
        var letters = laid.Where(laidKey => laidKey.Key.Text is { Length: 1 } letter && language.SwipeLetters.Contains(letter)).ToList();
        var centres = language.SwipeLetters.Select(letter => letters.First(laidKey => laidKey.Key.Text == letter.ToString()).Box).ToList();
        var left = letters.Min(laidKey => laidKey.Box.Left);
        var top = letters.Min(laidKey => laidKey.Box.Top);
        var width = letters.Max(laidKey => laidKey.Box.Right) - left;
        var height = letters.Max(laidKey => laidKey.Box.Bottom) - top;
        float X(float x) => (x - left) / width;
        float Y(float y) => (y - top) / height;
        var path = new KeyPanel.SwipePath(
            [.. now.Points.Select(point => X(point.X))],
            [.. now.Points.Select(point => Y(point.Y))],
            [.. now.Points.Select(point => (float)(point.Time - now.Start))],
            [.. centres.Select(box => X(box.CenterX()))],
            [.. centres.Select(box => Y(box.CenterY()))],
            language);
        var capitals = shift == ShiftState.Locked;
        var capital = shift == ShiftState.Once;
        ShiftUsed();
        Swiped?.Invoke(path, capital, capitals);
    }

    /// <summary>The keys' places as laid out, each its row and its place in the row, alongside <see cref="laid"/>.</summary>
    private List<(int Row, int Index)> places = [];

    /// <summary>Each row's top and bottom as laid out, an empty one's too.</summary>
    private List<(float Top, float Bottom)> rowBands = [];

    /// <summary>Each shaped key's two parts as laid out, by their places in <see cref="laid"/>, drawn joined.</summary>
    private List<(int Top, int Lower)> joints = [];

    private StandardDesign? design;
    private bool designing;

    /// <summary>A key one wide as laid out, without the gaps: the size a key added is drawn at before it is in a row.</summary>
    private float keyUnit;

    /// <summary>While designing: the key marked (its place), or a row (its place -1); the key dragged and where the finger is.</summary>
    private (int Row, int Index)? selected;
    private ((int Row, int Index) Place, float X, float Y)? dragged;
    private (int Row, int Index, float X, float Y)? designDown;

    /// <summary>A key chosen in + Tecla, not in the design yet: drawn loose where it is, marked, until it is dragged and let go into a row; whether the finger holds it.</summary>
    private readonly record struct LooseKey(string Id, StandardKey Key, float X, float Y, bool Held);

    private LooseKey? added;

    /// <summary>The letters' page as designed (⊞ › Diseñar); none, as made.</summary>
    public StandardDesign? Design
    {
        get => design;
        set
        {
            design = value;
            selected = null;
            added = null;
            RequestLayout();
            Lay();
        }
    }

    /// <summary>Whether the keys are being designed: a tap marks a key, a drag moves it; nothing is typed.</summary>
    public bool Designing
    {
        get => designing;
        set
        {
            designing = value;
            selected = null;
            dragged = null;
            added = null;
            if (value)
            {
                ShowPage(BasePage);
            }

            Invalidate();
        }
    }

    /// <summary>The design changed while designing.</summary>
    public Action? DesignChanged { get; set; }

    /// <summary>−, +: the marked key a quarter of a key narrower or wider, a shaped Enter's two parts alike.</summary>
    public void ResizeMarked(bool wider) => ChangeMarked((row, index) =>
        design!.Resize(row, index, wider ? WidthStep : -WidthStep, MinWidth, MaxWidth));

    /// <summary>Quitar: the marked key out; a marked empty row out.</summary>
    public void RemoveMarked()
    {
        if (design is null || selected is not { } at)
        {
            return;
        }

        if (at.Index < 0)
        {
            design.Rows.RemoveAt(at.Row);
            selected = null;
        }
        else
        {
            design.Remove(at.Row, at.Index);
            selected = design.Rows[at.Row].Count == 0 ? (at.Row, -1) : (at.Row, Math.Min(at.Index, design.Rows[at.Row].Count - 1));
        }

        Redesigned();
    }

    /// <summary>+ Tecla: a key from the list loose in the keys' middle, marked, to be dragged to its place; in the design only once let go there.</summary>
    public void AddKey(string id)
    {
        if (design is null || StandardKeys.Extra(id, language) is not { } key)
        {
            return;
        }

        selected = null;
        added = new LooseKey(id, key, Width / 2f, Height / 2f, Held: false);
        Invalidate();
    }

    /// <summary>Whether the keyboard as designed has a key of + Tecla's list already, which the list then leaves out.</summary>
    public bool HasKey(string id) => StandardKeys.HasExtra(Rows(BasePage), id, language);

    /// <summary>A loose key's box: as wide as it is in keys, a letter row tall, centred where it is.</summary>
    private RectF LooseBox(LooseKey loose)
    {
        var width = (loose.Key.Width * keyUnit) + ((loose.Key.Width - 1) * Dp(KeyGapDp));
        return new RectF(loose.X - (width / 2), loose.Y - (LetterRow / 2), loose.X + (width / 2), loose.Y + (LetterRow / 2));
    }

    /// <summary>The key added let go, as a key moved.</summary>
    private void PlaceAdded(LooseKey loose, float x, float y) => LetGo(new DesignedKey("x:" + loose.Id, loose.Key.Width), null, x, y);

    /// <summary>A marked Enter tapped again: its next shape, a shaped one's top going up right-aligned over the Enter's own part; false for any other key.</summary>
    private bool ReshapeEnter((int Row, int Index) at)
    {
        if (design is null)
        {
            return false;
        }

        var made = StandardKeys.Page(BasePage, language);
        var own = design.LowerOf(at.Row, at.Index) ?? at;
        if (places.IndexOf(own) is not (var laidAt and >= 0))
        {
            return false;
        }

        // The top goes in over the Enter's right edge: placed by its right half, whatever its width.
        var topMiddle = laid[laidAt].Box.Right - (keyUnit / 2);
        var upperIndex = laid.Where((laidKey, i) => places[i].Row == own.Row - 1 && laidKey.Box.CenterX() < topMiddle).Count();
        if (design.Reshape(at.Row, at.Index, made, language, upperIndex) is not { } now)
        {
            return false;
        }

        selected = now;
        Redesigned();
        return true;
    }

    /// <summary>+ Columna: a new column at the right, as + Fila adds a row at the top: a hole a key wide at the end of every row with keys, to be filled; the top one marked.</summary>
    public void AddColumn()
    {
        if (design is null)
        {
            return;
        }

        selected = null;
        for (var r = 0; r < design.Rows.Count; r++)
        {
            if (design.Rows[r].Count == 0)
            {
                continue;
            }

            design.Rows[r].Add(new DesignedKey(StandardKeys.GapId, 1f));
            selected ??= (r, design.Rows[r].Count - 1);
        }

        Redesigned();
    }

    /// <summary>
    /// A key let go, moved (from its place) or added: on a hole, in the
    /// hole's place; else into the row under it, before the first key whose
    /// middle is right of it; a shaped key starting there, its lower part in
    /// the row below, ending under its right edge; marked. False where
    /// refused: a shaped key in the last row.
    /// </summary>
    private bool LetGo(DesignedKey key, (int Row, int Index)? from, float x, float y)
    {
        if (design is null)
        {
            return false;
        }

        if (design.Rows.Count == 0)
        {
            design.Rows.Add([]);
        }

        var gap = laid.FindIndex(laidKey => laidKey.Key.Look == StandardLook.Gap && laidKey.Box.Contains(x, y));
        var fills = gap >= 0 && places[gap] != from;
        var row = fills ? places[gap].Row : Math.Max(0, RowAt(y));
        // Counted with the key moved still in its place: the design puts it first and takes it out after.
        var index = fills ? places[gap].Index : laid.Where((laidKey, i) => places[i].Row == row && laidKey.Box.CenterX() < x).Count();
        var lowerIndex = 0;
        if (StandardDesign.Shaped(key.Id) && StandardKeys.Extra(key.Id[2..], language) is { } shape)
        {
            var right = x + (key.Width * keyUnit) + ((key.Width - 1) * Dp(KeyGapDp));
            var lowerMiddle = right - (((shape.Below * keyUnit) + ((shape.Below - 1) * Dp(KeyGapDp))) / 2);
            lowerIndex = laid.Where((laidKey, i) => places[i].Row == row + 1 && laidKey.Box.CenterX() < lowerMiddle).Count();
        }

        if (design.Put(key, row, index, fills, lowerIndex, from) is not { } now)
        {
            return false;
        }

        selected = now;
        Redesigned();
        return true;
    }

    /// <summary>+ Fila: an empty row over the marked key's row, or at the top, as the owner asked; marked.</summary>
    public void AddRow()
    {
        if (design is null)
        {
            return;
        }

        var row = selected is { } at ? at.Row : 0;
        design.Rows.Insert(row, []);
        selected = (row, -1);
        Redesigned();
    }

    /// <summary>Whether something is marked, for −, + and Quitar.</summary>
    public bool HasMarked => selected is not null;

    private const float WidthStep = 0.25f;
    private const float MinWidth = 0.5f;
    private const float MaxWidth = 10f;

    private void ChangeMarked(Action<int, int> change)
    {
        if (design is null || selected is not { Index: >= 0 } at)
        {
            return;
        }

        change(at.Row, at.Index);
        Redesigned();
    }

    private void Redesigned()
    {
        RequestLayout();
        Lay();
        DesignChanged?.Invoke();
    }

    /// <summary>
    /// A touch while designing: a tap marks the key under it, or the empty row;
    /// a key dragged follows the finger and, let go, stands where it was let
    /// go, in that row before the first key whose middle is right of it.
    /// </summary>
    private void DesignTouch(MotionEvent motion)
    {
        var x = motion.GetX();
        var y = motion.GetY();
        switch (motion.ActionMasked)
        {
            case MotionEventActions.Down when added is { } loose && LooseBox(loose).Contains(x, y):
                added = loose with { Held = true };
                Parent?.RequestDisallowInterceptTouchEvent(true);
                break;
            case MotionEventActions.Move when added is { Held: true } moving:
                added = moving with { X = x, Y = y };
                Invalidate();
                break;
            case MotionEventActions.Up when added is { Held: true } held:
                added = null;
                PlaceAdded(held, x, y);
                break;
            case MotionEventActions.Down:
                var hit = laid.FindIndex(laidKey => laidKey.Box.Contains(x, y));
                var row = RowAt(y);
                designDown = hit >= 0 ? (places[hit].Row, places[hit].Index, x, y) : row >= 0 && design?.Rows[row].Count == 0 ? (row, -1, x, y) : null;
                break;
            case MotionEventActions.Move when dragged is { } moving:
                dragged = (moving.Place, x, y);
                Invalidate();
                break;
            case MotionEventActions.Move when designDown is { Index: >= 0 } down
                && Math.Max(Math.Abs(x - down.X), Math.Abs(y - down.Y)) > ViewConfiguration.Get(Context!)!.ScaledTouchSlop:
                selected = (down.Row, down.Index);
                dragged = ((down.Row, down.Index), x, y);
                Parent?.RequestDisallowInterceptTouchEvent(true);
                Invalidate();
                break;
            case MotionEventActions.Up when dragged is { } held:
                MoveKey(held.Place, x, y);
                dragged = null;
                designDown = null;
                break;
            case MotionEventActions.Up when designDown is { } down:
                // A marked Enter tapped again takes its next shape; anything else tapped is marked.
                if (!(down.Index >= 0 && selected == (down.Row, down.Index) && ReshapeEnter((down.Row, down.Index))))
                {
                    selected = (down.Row, down.Index);
                }

                designDown = null;
                Invalidate();
                DesignChanged?.Invoke();
                break;
            case MotionEventActions.Cancel:
                dragged = null;
                designDown = null;
                added = added is { } dropped ? dropped with { Held = false } : null;
                Invalidate();
                break;
        }
    }

    /// <summary>The row whose band, gaps shared, holds a height; the nearest above or below the keys; -1 with none.</summary>
    private int RowAt(float y)
    {
        if (rowBands.Count == 0)
        {
            return -1;
        }

        var half = Dp(RowGapDp) / 2;
        for (var r = 0; r < rowBands.Count; r++)
        {
            if (y >= rowBands[r].Top - half && y < rowBands[r].Bottom + half)
            {
                return r;
            }
        }

        return y < rowBands[0].Top ? 0 : rowBands.Count - 1;
    }

    private void MoveKey((int Row, int Index) from, float x, float y)
    {
        if (design is null)
        {
            return;
        }

        // A shaped key's lower part moves the whole key.
        var top = design.UpperOf(from.Row, from.Index) ?? from;
        if (!LetGo(design.Rows[top.Row][top.Index], top, x, y))
        {
            Invalidate();
        }
    }

    /// <summary>While designing: each empty row outlined, to be marked and filled; the key dragged, or the one added, under the finger.</summary>
    private void DrawDesigning(Canvas canvas)
    {
        if (design is not null)
        {
            for (var r = 0; r < rowBands.Count && r < design.Rows.Count; r++)
            {
                if (design.Rows[r].Count > 0)
                {
                    continue;
                }

                fill.Color = selected == (r, -1) ? colors.Primary : colors.FaintOutline;
                fill.SetStyle(Paint.Style.Stroke);
                fill.StrokeWidth = Dp(2);
                var band = new RectF(Dp(SideDp), rowBands[r].Top, Width - Dp(SideDp), rowBands[r].Bottom);
                canvas.DrawRoundRect(band, Dp(CornerDp), Dp(CornerDp), fill);
                fill.SetStyle(Paint.Style.Fill);
            }
        }

        if (dragged is { } held && places.IndexOf(held.Place) is var i and >= 0)
        {
            var box = laid[i].Box;
            var moved = new RectF(box);
            moved.OffsetTo(held.X - (box.Width() / 2), held.Y - (box.Height() / 2));
            DrawKey(canvas, laid[i].Key, moved, marked: true);
        }

        if (added is { } loose)
        {
            DrawKey(canvas, loose.Key, LooseBox(loose), marked: true);
        }
    }

    protected override void OnDetachedFromWindow()
    {
        Forget();
        base.OnDetachedFromWindow();
    }
}
