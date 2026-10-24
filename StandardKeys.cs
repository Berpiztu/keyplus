using Android.Views;

namespace Berpiztu.KeyPlus;

/// <summary>What a key of the standard keyboard does.</summary>
public enum StandardAction
{
    /// <summary>Types its text, its shifted text with Shift; held, its hint.</summary>
    Type,
    Shift,
    Delete,
    /// <summary>?123: the symbols' page.</summary>
    Symbols,
    /// <summary>=\&lt;: the second symbols' page.</summary>
    MoreSymbols,
    /// <summary>1234: the number pad.</summary>
    Numbers,
    /// <summary>ABC: the letters again.</summary>
    Letters,
    /// <summary>🌐: the next language.</summary>
    Language,
    Space,
    Enter,
    /// <summary>A key of a PC's that is no character (Esc, Tab, F1–F12, the arrows, Supr): its key code sent, with the modifiers on.</summary>
    Key,
    /// <summary>Ctrl, Alt and ⊞: on until the next key, which goes with them.</summary>
    Ctrl,
    Alt,
    Meta,
    /// <summary>Alt Gr: on until the next key, which types its corner's symbol.</summary>
    AltGr,
    /// <summary>Bloq Mayús: the letters in capitals until pressed again.</summary>
    CapsLock,
}

/// <summary>How a key is drawn, as Gboard draws it: a letter's key, a function key's tinted one, or a tinted pill.</summary>
public enum StandardLook
{
    Letter,
    Function,
    Pill,
    /// <summary>A hole in a designed row (Diseñar › + Columna): nothing drawn, nothing typed; outlined while designing.</summary>
    Gap,
}

/// <summary>
/// A key of the standard keyboard: its label, what it does, the text it
/// types (and with Shift), the symbol drawn in its corner that a long press
/// types, its width in keys, how it is drawn and its icon, if a sign is drawn
/// in place of its label; on a PC's, the key code it sends and whether it
/// is a dead key, the accent it types going on the next letter. A shaped
/// key (an Enter of + Tecla) goes on into the row below: its lower part,
/// as wide as Below, right-aligned under it, laid there as a key of that
/// row marked Lower, which does what the key does and shows no label.
/// </summary>
public sealed record StandardKey(
    string Label,
    StandardAction Action = StandardAction.Type,
    string? Text = null,
    string? Shifted = null,
    string? Hint = null,
    float Width = 1f,
    StandardLook Look = StandardLook.Letter,
    int Icon = 0,
    int HintIcon = 0,
    Keycode Code = Keycode.Unknown,
    bool Dead = false,
    float Below = 0f,
    bool Lower = false)
{
    /// <summary>Whether held it goes on and on, as a PC's arrows do.</summary>
    public bool Repeats => Code is Keycode.DpadLeft or Keycode.DpadRight or Keycode.DpadUp or Keycode.DpadDown;
}

/// <summary>The standard keyboard's pages.</summary>
public enum StandardPage
{
    Letters,
    Symbols,
    MoreSymbols,
    /// <summary>Estándar PC's only page: a PC's keys, every symbol with Shift or Alt Gr.</summary>
    Pc,
}

/// <summary>
/// A language of the standard keyboard, as Gboard has it: its code, its
/// name on the space bar, its letters' rows and the symbols in their corners,
/// the second row of its ?123 and =\&lt; pages, the word list a swipe is
/// read with (Assets/dictionaries, AOSP LatinIME's), and its locale, as the
/// speech service (ia-voice) asks for a language.
/// </summary>
public sealed record StandardLanguage(string Code, string Name, string[] LetterRows, string[] Hints, string Symbols, string MoreSymbols, string WordList, string Locale)
{
    /// <summary>The letters a swipe is read over, in the order their centres are handed with it: the language's own, in Unicode's order.</summary>
    public string SwipeLetters { get; } = string.Concat(LetterRows.SelectMany(row => row).Order());
}

/// <summary>
/// The standard keyboard's pages as Gboard lays them out with the number row
/// on, as the owner's Fold has it (docs/tODO/photos/gboard/fold-*.png) and,
/// for English, the emulator: Shift turns the number row into
/// ! @ # $ % ^ &amp; * ( ); each letter's corner shows the symbol a long press
/// types, the ?123 page's at that place. Spanish (Spain, QWERTY (Ñ)) and
/// English (US, QWERTY), in the order 🌐 goes through them.
/// </summary>
public static class StandardKeys
{
    /// <summary>The space bar's width, in keys, and Shift's, ⌫'s, ?123's and Enter's.</summary>
    private const float SpaceWidth = 4f;
    private const float WideWidth = 1.5f;

    public static readonly StandardLanguage Spanish = new(
        "es",
        "Español",
        ["qwertyuiop", "asdfghjklñ", "zxcvbnm"],
        ["%\\|=[]<>{}", "@#€_&-+()/", "*\"':;!?"],
        "@#€_&-+()/",
        "£¥$¢^°={}\\",
        "dictionaries/es_wordlist.combined.gz",
        "es-ES");

    public static readonly StandardLanguage English = new(
        "en",
        "English",
        ["qwertyuiop", "asdfghjkl", "zxcvbnm"],
        ["%\\|=[]<>{}", "@#$_&-+()", "*\"':;!?"],
        "@#$_&-+()/",
        "£¢€¥^°={}\\",
        "dictionaries/en_US_wordlist.combined.gz",
        "en-US");

    public static readonly IReadOnlyList<StandardLanguage> Languages = [Spanish, English];

    /// <summary>A language by its code; none, or one unknown, Spanish.</summary>
    public static StandardLanguage LanguageOf(string? code) => Languages.FirstOrDefault(language => language.Code == code) ?? Spanish;

    /// <summary>Each language's pages, made once.</summary>
    private static readonly Dictionary<(StandardPage, StandardLanguage), IReadOnlyList<IReadOnlyList<StandardKey>>> Made = [];

    /// <summary>A page's rows, top to bottom, in a language.</summary>
    public static IReadOnlyList<IReadOnlyList<StandardKey>> Page(StandardPage page, StandardLanguage language)
    {
        if (!Made.TryGetValue((page, language), out var rows))
        {
            Made[(page, language)] = rows = page switch
            {
                StandardPage.Symbols => SymbolsPage(language),
                StandardPage.MoreSymbols => MoreSymbolsPage(language),
                StandardPage.Pc => language == English ? UsPcPage() : SpanishPcPage(),
                _ => LettersPage(language),
            };
        }

        return rows;
    }

    /// <summary>Estándar PC's design in a language, the one the keyboard comes with: Spanish's the owner's; none, the keys as made.</summary>
    public static string? PcDesign(string language) => language == Spanish.Code ? SpanishPcDesign : null;

    /// <summary>
    /// Estándar PC in Spanish as the owner designed it (2026-10-23,
    /// docs/tODO/designs/2026-10-23_174700_Windows-IAMAX_keyboards.json):
    /// Insert before Supr, ISO's Enter, and Inicio, Fin, AvPág and RePág
    /// around the arrows. Each key "id" or "id=width", a key wide by default.
    /// </summary>
    private static readonly string SpanishPcDesign = Designed(
        "b:0:0 b:0:1 b:0:2 b:0:3 b:0:4 b:0:5 b:0:6 b:0:7 b:0:8 b:0:9 b:0:10 b:0:11 b:0:12 b:0:13 x:ins b:0:14",
        "b:1:0 b:1:1 b:1:2 b:1:3 b:1:4 b:1:5 b:1:6 b:1:7 b:1:8 b:1:9 b:1:10 b:1:11 b:1:12 b:1:13=2",
        "b:2:0=1.5 b:2:1 b:2:2 b:2:3 b:2:4 b:2:5 b:2:6 b:2:7 b:2:8 b:2:9 b:2:10 b:2:11 b:2:12 x:enterIso=1.75",
        "b:3:0=1.75 b:3:1 b:3:2 b:3:3 b:3:4 b:3:5 b:3:6 b:3:7 b:3:8 b:3:9 b:3:10 b:3:11 b:2:13=1.5 below=1.5",
        "b:4:0=1.25 b:4:1 b:4:2 b:4:3 b:4:4 b:4:5 b:4:6 b:4:7 b:4:8 b:4:9 b:4:10 b:4:11 b:4:13=1.25 x:home b:4:12 x:end=1.25 x:pgdn",
        "b:5:0=1.25 b:5:1=1.25 b:5:2=1.25 b:5:3 b:5:4=6 b:5:5=1.25 b:5:6 b:5:7 b:5:8 x:pgup");

    private static string Designed(params string[] rows) => new StandardDesign
    {
        Rows = [.. rows.Select(row => row.Split(' ').Select(key => key.Split('=') is [var id, var width]
            ? new DesignedKey(id, float.Parse(width, System.Globalization.CultureInfo.InvariantCulture))
            : new DesignedKey(key, 1)).ToList())],
    }.Text;

    private static IReadOnlyList<IReadOnlyList<StandardKey>> LettersPage(StandardLanguage language) =>
    [
        [.. "1234567890".Zip("!@#$%^&*()", (digit, shifted) => new StandardKey(digit.ToString(), Text: digit.ToString(), Shifted: shifted.ToString()))],
        [.. Letters(language.LetterRows[0], language.Hints[0])],
        [.. Letters(language.LetterRows[1], language.Hints[1])],
        [
            Shift(WideWidth),
            .. Letters(language.LetterRows[2], language.Hints[2]),
            Delete(),
        ],
        [
            new("?123", StandardAction.Symbols, Width: WideWidth, Look: StandardLook.Pill),
            new(",", Text: ",", Look: StandardLook.Function, HintIcon: Resource.Drawable.ic_key_emoji),
            new("🌐", StandardAction.Language, Icon: Resource.Drawable.ic_key_language),
            Space(),
            new(".", Text: ".", Look: StandardLook.Function),
            Enter(),
        ],
    ];

    private static IReadOnlyList<IReadOnlyList<StandardKey>> SymbolsPage(StandardLanguage language) =>
    [
        [.. Characters("1234567890")],
        [.. Characters(language.Symbols)],
        [
            new("=\\<", StandardAction.MoreSymbols, Width: WideWidth, Look: StandardLook.Function),
            .. Characters("*\"':;!?"),
            Delete(),
        ],
        [
            new("ABC", StandardAction.Letters, Width: WideWidth, Look: StandardLook.Pill),
            new(",", Text: ",", Look: StandardLook.Function),
            new("1234", StandardAction.Numbers),
            Space(),
            new(".", Text: ".", Look: StandardLook.Function),
            Enter(),
        ],
    ];

    private static IReadOnlyList<IReadOnlyList<StandardKey>> MoreSymbolsPage(StandardLanguage language) =>
    [
        [.. Characters("~`|•√π÷×§Δ")],
        [.. Characters(language.MoreSymbols)],
        [
            new("?123", StandardAction.Symbols, Width: WideWidth, Look: StandardLook.Function),
            .. Characters("%©®™✓[]"),
            Delete(),
        ],
        [
            new("ABC", StandardAction.Letters, Width: WideWidth, Look: StandardLook.Pill),
            new("<", Text: "<", Look: StandardLook.Function),
            new("1234", StandardAction.Numbers),
            Space(),
            new(">", Text: ">", Look: StandardLook.Function),
            Enter(),
        ],
    ];

    /// <summary>
    /// Estándar PC in Spanish: a Spanish PC's keys (Spain, ISO), as wide as
    /// it has them, 15 keys across: F1–F12 over the digits; Ç at the end of
    /// the Q row, where a Spanish laptop of the narrow Enter has it; ` and ´
    /// dead keys; Alt Gr's symbol in each key's corner.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<StandardKey>> SpanishPcPage() =>
    [
        FunctionRow("ImpPnt", "Supr"),
        [.. PcKeys("º1234567890'¡", "ª!\"·$%&/()=?¿", "\\|@#~€¬"), Delete() with { Width = 2f }],
        [PcKey("Tab", Keycode.Tab, WideWidth), .. PcKeys("qwertyuiop`+", "QWERTYUIOP^*", "  €       []"), PcKeys("ç", "Ç", "}").Single() with { Width = WideWidth }],
        [CapsLock("Bloq Mayús"), .. PcKeys("asdfghjklñ´", "ASDFGHJKLÑ¨", "          {"), Enter() with { Width = 2.25f }],
        [Shift(1.25f), .. PcKeys("<zxcvbnm,.-", ">ZXCVBNM;:_", ""), PcKey("↑", Keycode.DpadUp), Shift(1.75f)],
        BottomPcRow(new("Alt Gr", StandardAction.AltGr, Width: 1.25f, Look: StandardLook.Function)),
    ];

    /// <summary>Estándar PC in English: a US PC's keys (ANSI), 15 keys across, F1–F12 over the digits; no Alt Gr, two Alts.</summary>
    private static IReadOnlyList<IReadOnlyList<StandardKey>> UsPcPage() =>
    [
        FunctionRow("PrtSc", "Del"),
        [.. PcKeys("`1234567890-=", "~!@#$%^&*()_+", ""), Delete() with { Width = 2f }],
        [PcKey("Tab", Keycode.Tab, WideWidth), .. PcKeys("qwertyuiop[]", "QWERTYUIOP{}", ""), PcKeys("\\", "|", "").Single() with { Width = WideWidth }],
        [CapsLock("Caps Lock"), .. PcKeys("asdfghjkl;'", "ASDFGHJKL:\"", ""), Enter() with { Width = 2.25f }],
        [Shift(2.25f), .. PcKeys("zxcvbnm,./", "ZXCVBNM<>?", ""), PcKey("↑", Keycode.DpadUp), Shift(1.75f)],
        BottomPcRow(new("Alt", StandardAction.Alt, Width: 1.25f, Look: StandardLook.Function)),
    ];

    /// <summary>Esc, F1–F12, then print screen and the forward delete, as a laptop's top row.</summary>
    private static IReadOnlyList<StandardKey> FunctionRow(string printScreen, string forwardDelete) =>
    [
        PcKey("Esc", Keycode.Escape),
        .. Enumerable.Range(1, 12).Select(number => PcKey($"F{number}", Keycode.F1 + (number - 1))),
        PcKey(printScreen, Keycode.Sysrq),
        PcKey(forwardDelete, Keycode.ForwardDel),
    ];

    /// <summary>Ctrl, ⊞, Alt, 🌐, the space bar, the right Alt (Spanish's Alt Gr) and the arrows.</summary>
    private static IReadOnlyList<StandardKey> BottomPcRow(StandardKey rightAlt) =>
    [
        new("Ctrl", StandardAction.Ctrl, Width: 1.25f, Look: StandardLook.Function),
        new("⊞", StandardAction.Meta, Width: 1.25f, Look: StandardLook.Function),
        new("Alt", StandardAction.Alt, Width: 1.25f, Look: StandardLook.Function),
        new("🌐", StandardAction.Language, Look: StandardLook.Function, Icon: Resource.Drawable.ic_key_language),
        Space() with { Width = 6f },
        rightAlt,
        PcKey("←", Keycode.DpadLeft),
        PcKey("↓", Keycode.DpadDown),
        PcKey("→", Keycode.DpadRight),
    ];

    /// <summary>A PC's characters: each with its Shift's and its Alt Gr's, a space none; ` and ´ dead keys.</summary>
    private static IEnumerable<StandardKey> PcKeys(string texts, string shifted, string altGr) =>
        texts.Select((text, at) => new StandardKey(
            text.ToString(),
            Text: text.ToString(),
            Shifted: shifted[at].ToString(),
            Hint: at < altGr.Length && altGr[at] != ' ' ? altGr[at].ToString() : null,
            Dead: text is '`' or '´'));

    private static StandardKey PcKey(string label, Keycode code, float width = 1f) =>
        new(label, StandardAction.Key, Width: width, Look: StandardLook.Function, Code: code);

    private static StandardKey CapsLock(string label) =>
        new(label, StandardAction.CapsLock, Width: 1.75f, Look: StandardLook.Function);

    private static StandardKey Shift(float width) =>
        new("Shift", StandardAction.Shift, Width: width, Look: StandardLook.Function, Icon: Resource.Drawable.ic_key_shift);

    /// <summary>A place a language has no key at, in a design: an empty key that does nothing.</summary>
    public static readonly StandardKey Blank = new("", StandardAction.Key, Look: StandardLook.Function, Code: Keycode.Unknown);

    /// <summary>A hole a design leaves in a row, by its id there: room a key wide, empty.</summary>
    public const string GapId = "gap";

    public static readonly StandardKey Gap = new("", StandardAction.Key, Look: StandardLook.Gap, Code: Keycode.Unknown);

    /// <summary>A shaped key's lower part, by its id in the row below it in a design.</summary>
    public const string BelowId = "below";

    /// <summary>
    /// The keys a design can add (Diseñar › + Tecla), by group as the list
    /// shows them, each by its id: a PC's that the standard keyboards may
    /// lack.
    /// </summary>
    public static readonly (int Name, string[] Ids)[] Extras =
    [
        (Resource.String.design_editing, ["esc", "tab", "del", "ins", "home", "end", "pgup", "pgdn"]),
        (Resource.String.design_modifiers, ["ctrl", "alt", "meta", "altgr", "caps"]),
        (Resource.String.design_functions, ["f1", "f2", "f3", "f4", "f5", "f6", "f7", "f8", "f9", "f10", "f11", "f12", "prtsc"]),
        (Resource.String.design_arrows, ["left", "up", "down", "right"]),
    ];

    /// <summary>A key a design can add, by its id, labelled in a language as a PC there labels it.</summary>
    public static StandardKey? Extra(string id, StandardLanguage language)
    {
        var english = language == English;
        return id switch
        {
            "esc" => PcKey("Esc", Keycode.Escape),
            "tab" => PcKey("Tab", Keycode.Tab),
            "del" => PcKey(english ? "Del" : "Supr", Keycode.ForwardDel),
            "ins" => PcKey(english ? "Ins" : "Insert", Keycode.Insert),
            "home" => PcKey(english ? "Home" : "Inicio", Keycode.MoveHome),
            "end" => PcKey(english ? "End" : "Fin", Keycode.MoveEnd),
            "pgup" => PcKey(english ? "PgUp" : "RePág", Keycode.PageUp),
            "pgdn" => PcKey(english ? "PgDn" : "AvPág", Keycode.PageDown),
            "ctrl" => new("Ctrl", StandardAction.Ctrl, Look: StandardLook.Function),
            "alt" => new("Alt", StandardAction.Alt, Look: StandardLook.Function),
            "meta" => new("⊞", StandardAction.Meta, Look: StandardLook.Function),
            "altgr" => new("Alt Gr", StandardAction.AltGr, Look: StandardLook.Function),
            "caps" => CapsLock(english ? "Caps Lock" : "Bloq Mayús") with { Width = 1f },
            "prtsc" => PcKey(english ? "PrtSc" : "ImpPnt", Keycode.Sysrq),
            "left" => PcKey("←", Keycode.DpadLeft),
            "up" => PcKey("↑", Keycode.DpadUp),
            "down" => PcKey("↓", Keycode.DpadDown),
            "right" => PcKey("→", Keycode.DpadRight),
            "enter" => Enter(),
            "enterIso" => ShapedEnter(IsoEnterBelow),
            "enterBig" => ShapedEnter(BigEnterBelow),
            "enterSquare" => ShapedEnter(WideWidth),
            _ when id.StartsWith('f') && int.TryParse(id[1..], out var number) && number is >= 1 and <= 12 => PcKey($"F{number}", Keycode.F1 + (number - 1)),
            _ => null,
        };
    }

    /// <summary>
    /// The shaped Enters' lower parts, under a top a key and a half wide, as
    /// a PC's: ISO's narrower (1.25, Spain's), the old ANSI L's wider (2.25,
    /// as a PC's Enter row); the square one as wide as its top.
    /// </summary>
    private const float IsoEnterBelow = 1.25f;
    private const float BigEnterBelow = 2.25f;

    /// <summary>An Enter two rows tall, its top a key and a half wide and its lower part as wide as asked, drawn as a function key, not a pill.</summary>
    private static StandardKey ShapedEnter(float below) => Enter() with { Look = StandardLook.Function, Below = below };

    /// <summary>The shapes a marked Enter goes through, clicked again while designing (by their ids for <see cref="Extra"/>): one row, ISO's L, the old ANSI L, square, one row again.</summary>
    public static readonly string[] EnterShapes = ["enter", "enterIso", "enterBig", "enterSquare"];

    /// <summary>Whether a page has a key a design can add already: a PC's key by the code it sends, a modifier by what it does.</summary>
    public static bool HasExtra(IReadOnlyList<IReadOnlyList<StandardKey>> page, string id, StandardLanguage language) =>
        Extra(id, language) is { } extra && page.SelectMany(row => row).Any(key =>
            extra.Action == StandardAction.Key ? key.Action == StandardAction.Key && key.Code == extra.Code : key.Action == extra.Action);

    private static IEnumerable<StandardKey> Letters(string letters, string hints) =>
        letters.Zip(hints, (letter, hint) => new StandardKey(letter.ToString(), Text: letter.ToString(), Shifted: char.ToUpperInvariant(letter).ToString(), Hint: hint.ToString()));

    private static IEnumerable<StandardKey> Characters(string characters) =>
        characters.Select(character => new StandardKey(character.ToString(), Text: character.ToString()));

    private static StandardKey Delete() =>
        new("⌫", StandardAction.Delete, Width: WideWidth, Look: StandardLook.Function, Icon: Resource.Drawable.ic_key_backspace);

    private static StandardKey Space() => new("", StandardAction.Space, Text: " ", Width: SpaceWidth);

    private static StandardKey Enter() =>
        new("Enter", StandardAction.Enter, Width: WideWidth, Look: StandardLook.Pill, Icon: Resource.Drawable.ic_key_return);
}
