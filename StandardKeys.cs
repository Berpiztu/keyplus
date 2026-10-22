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
}

/// <summary>How a key is drawn, as Gboard draws it: a letter's key, a function key's tinted one, or a tinted pill.</summary>
public enum StandardLook
{
    Letter,
    Function,
    Pill,
}

/// <summary>
/// A key of the standard keyboard: its label, what it does, the text it
/// types (and with Shift), the symbol drawn in its corner that a long press
/// types, its width in keys, how it is drawn and its icon, if a sign is drawn
/// in place of its label.
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
    int HintIcon = 0);

/// <summary>The standard keyboard's pages.</summary>
public enum StandardPage
{
    Letters,
    Symbols,
    MoreSymbols,
}

/// <summary>
/// A language of the standard keyboard, as Gboard has it: its code, its
/// name on the space bar, its letters' rows and the symbols in their corners,
/// the second row of its ?123 and =\&lt; pages, and the word list a swipe is
/// read with (Assets/dictionaries, AOSP LatinIME's).
/// </summary>
public sealed record StandardLanguage(string Code, string Name, string[] LetterRows, string[] Hints, string Symbols, string MoreSymbols, string WordList)
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
        "dictionaries/es_wordlist.combined.gz");

    public static readonly StandardLanguage English = new(
        "en",
        "English",
        ["qwertyuiop", "asdfghjkl", "zxcvbnm"],
        ["%\\|=[]<>{}", "@#$_&-+()", "*\"':;!?"],
        "@#$_&-+()/",
        "£¢€¥^°={}\\",
        "dictionaries/en_US_wordlist.combined.gz");

    public static readonly IReadOnlyList<StandardLanguage> Languages = [Spanish, English];

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
                _ => LettersPage(language),
            };
        }

        return rows;
    }

    private static IReadOnlyList<IReadOnlyList<StandardKey>> LettersPage(StandardLanguage language) =>
    [
        [.. "1234567890".Zip("!@#$%^&*()", (digit, shifted) => new StandardKey(digit.ToString(), Text: digit.ToString(), Shifted: shifted.ToString()))],
        [.. Letters(language.LetterRows[0], language.Hints[0])],
        [.. Letters(language.LetterRows[1], language.Hints[1])],
        [
            new("Shift", StandardAction.Shift, Width: WideWidth, Look: StandardLook.Function, Icon: Resource.Drawable.ic_key_shift),
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
