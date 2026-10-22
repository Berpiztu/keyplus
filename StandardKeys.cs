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
/// The standard keyboard's pages as Gboard lays out Spanish (Spain,
/// QWERTY (Ñ)) with the number row on, as the owner's Fold has it
/// (docs/tODO/photos/gboard/fold-*.png): Shift turns the number row into
/// ! @ # $ % ^ &amp; * ( ); each letter's corner shows the symbol a long press
/// types, the ?123 page's at that place.
/// </summary>
public static class StandardKeys
{
    /// <summary>The space bar's width, in keys, and Shift's, ⌫'s, ?123's and Enter's.</summary>
    private const float SpaceWidth = 4f;
    private const float WideWidth = 1.5f;

    private static readonly IReadOnlyList<IReadOnlyList<StandardKey>> LettersPage =
    [
        [.. "1234567890".Zip("!@#$%^&*()", (digit, shifted) => new StandardKey(digit.ToString(), Text: digit.ToString(), Shifted: shifted.ToString()))],
        [.. Letters("qwertyuiop", "%\\|=[]<>{}")],
        [.. Letters("asdfghjklñ", "@#€_&-+()/")],
        [
            new("Shift", StandardAction.Shift, Width: WideWidth, Look: StandardLook.Function, Icon: Resource.Drawable.ic_key_shift),
            .. Letters("zxcvbnm", "*\"':;!?"),
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

    private static readonly IReadOnlyList<IReadOnlyList<StandardKey>> SymbolsPage =
    [
        [.. Characters("1234567890")],
        [.. Characters("@#€_&-+()/")],
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

    private static readonly IReadOnlyList<IReadOnlyList<StandardKey>> MoreSymbolsPage =
    [
        [.. Characters("~`|•√π÷×§Δ")],
        [.. Characters("£¥$¢^°={}\\")],
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

    /// <summary>A page's rows, top to bottom.</summary>
    public static IReadOnlyList<IReadOnlyList<StandardKey>> Page(StandardPage page) => page switch
    {
        StandardPage.Symbols => SymbolsPage,
        StandardPage.MoreSymbols => MoreSymbolsPage,
        _ => LettersPage,
    };

    /// <summary>The letters page's letters: what a swipe is read over.</summary>
    public static IEnumerable<StandardKey> SwipeKeys =>
        LettersPage.SelectMany(row => row).Where(key => key.Text is { Length: 1 } text && KeyPanel.SwipeLetters.Contains(text));

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
