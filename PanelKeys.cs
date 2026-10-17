using Android.Views;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The modifiers as the panel holds them: Ctrl, Alt and AltGr for the next
/// key, Shift locked until pressed again for the strip's keys and let go after
/// a character typed, Caps Lock and Fn until pressed again.
/// </summary>
[Flags]
public enum Held
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
    AltGr = 8,
    CapsLock = 16,
    Fn = 32,
}

/// <summary>
/// A key on the panel: what it reads, the key it sends or the modifier it
/// holds, whether holding it repeats it, the modifiers it always sends with
/// (With: Ctrl for a shortcut key such as Ctrl+C), and its share of its row's
/// width. A character key also has the text it types, plain, with Shift and
/// with AltGr; it types it as text, so it types what the layout says whatever
/// the editor's own keyboard layout, and sends its key (Code) only with Ctrl
/// or Alt held. A key may be another with Fn held (FnLabel, FnCode): F1 for
/// the 1, Home for the left arrow.
/// </summary>
public sealed record PanelKey(
    string Label,
    Keycode Code,
    Held Modifier = Held.None,
    bool Repeats = false,
    Held With = Held.None,
    float Weight = 1f,
    string? Text = null,
    string? ShiftText = null,
    string? AltGrText = null,
    string? FnLabel = null,
    Keycode? FnCode = null)
{
    public bool IsModifier => Modifier != Held.None;

    public bool IsCharacter => Text is not null;

    public bool IsLetter => Text is { Length: 1 } && char.IsLetter(Text[0]);

    /// <summary>The text the key types with the modifiers held; none where the layout has nothing.</summary>
    public string? TextFor(Held held)
    {
        if (held.HasFlag(Held.AltGr))
        {
            return AltGrText;
        }

        var shifted = held.HasFlag(Held.Shift) ^ (IsLetter && held.HasFlag(Held.CapsLock));
        return shifted ? ShiftText : Text;
    }
}

/// <summary>
/// Rows of keys laid out together, and a key standing as tall as all of them
/// at their right (Tall), as Enter stands beside two rows; the rows take
/// RowsWeight of the block's width, the tall key its own weight.
/// </summary>
public sealed record KeyBlock(IReadOnlyList<IReadOnlyList<PanelKey>> Rows, PanelKey? Tall = null, float RowsWeight = 1f);

/// <summary>The panel's layouts, the accents its dead keys put on letters, and how a held modifier reaches a key event.</summary>
public static class PanelKeys
{
    /// <summary>
    /// The editing shortcuts, as keys of their own: Ctrl then a "c" typed on
    /// the phone's keyboard cannot copy, because the "c" replaces the selection
    /// before it is taken back. Under them a terminal's keys as Termux lays
    /// them out: two rows, the arrows as a cross with Home and End beside the
    /// up arrow and the pages at the right, then Backspace and Enter where a
    /// keyboard has them; Shift where Termux has the minus, which the phone's
    /// keyboard has at hand, so Shift and the arrows select.
    /// </summary>
    public static readonly IReadOnlyList<KeyBlock> Terminal =
    [
        new([
        [
            new("Ctrl+A", Keycode.A, With: Held.Ctrl),
            new("Ctrl+C", Keycode.C, With: Held.Ctrl),
            new("Ctrl+V", Keycode.V, With: Held.Ctrl),
            new("Ctrl+Z", Keycode.Z, Repeats: true, With: Held.Ctrl),
            new("Ctrl+Y", Keycode.Y, Repeats: true, With: Held.Ctrl),
            new("Del", Keycode.ForwardDel, Repeats: true),
        ],
        [
            new("Esc", Keycode.Escape),
            new("/", Keycode.Slash),
            new("Shift", Keycode.ShiftLeft, Held.Shift),
            new("Home", Keycode.MoveHome),
            new("↑", Keycode.DpadUp, Repeats: true),
            new("End", Keycode.MoveEnd),
            new("PgUp", Keycode.PageUp, Repeats: true),
            new("⌫", Keycode.Del, Repeats: true),
        ],
        [
            new("Tab", Keycode.Tab),
            new("Ctrl", Keycode.CtrlLeft, Held.Ctrl),
            new("Alt", Keycode.AltLeft, Held.Alt),
            new("←", Keycode.DpadLeft, Repeats: true),
            new("↓", Keycode.DpadDown, Repeats: true),
            new("→", Keycode.DpadRight, Repeats: true),
            new("PgDn", Keycode.PageDown, Repeats: true),
            new("⏎", Keycode.Enter),
        ],
        ]),
    ];

    /// <summary>
    /// A whole keyboard as Windows' touch keyboard lays out Spain's (ISO,
    /// es-ES): AltGr's characters included, ´ ` ^ and ¨ as dead keys, Enter
    /// as tall as the two rows beside it, and the arrows as an inverted T at
    /// the bottom right, the up arrow between the minus and the right Shift.
    /// Fn, held until pressed again, makes the digits' row F1 to F12 and the
    /// arrows Home, End, Page up and Page down. Every row weighs 15.5, so the
    /// up arrow stands right above the down one.
    /// </summary>
    public static readonly IReadOnlyList<KeyBlock> Spanish =
    [
        new([
        [
            new("Esc", Keycode.Escape), Character("º", "ª", "\\"),
            Function(Character("1", "!", "|", Keycode.Num1), 1), Function(Character("2", "\"", "@", Keycode.Num2), 2),
            Function(Character("3", "·", "#", Keycode.Num3), 3), Function(Character("4", "$", "~", Keycode.Num4), 4),
            Function(Character("5", "%", "€", Keycode.Num5), 5), Function(Character("6", "&", "¬", Keycode.Num6), 6),
            Function(Character("7", "/", null, Keycode.Num7), 7), Function(Character("8", "(", null, Keycode.Num8), 8),
            Function(Character("9", ")", null, Keycode.Num9), 9), Function(Character("0", "=", null, Keycode.Num0), 10),
            Function(Character("'", "?", null, Keycode.Apostrophe), 11), Function(Character("¡", "¿", null), 12),
            new("⌫", Keycode.Del, Repeats: true, Weight: 1.5f),
        ],
        ]),
        new(
        [
            [
                new("Tab", Keycode.Tab, Weight: 1.75f), .. Letters("qwertyuiop"),
                Character("`", "^", "[", Keycode.Grave), Character("+", "*", "]", Keycode.Plus),
            ],
            [
                new("Bloq May", Keycode.CapsLock, Held.CapsLock, Weight: 1.75f), .. Letters("asdfghjkl"), Character("ñ", "Ñ", null),
                Character("´", "¨", "{"), Character("ç", "Ç", "}"),
            ],
        ],
        Tall: new("Entrar", Keycode.Enter, Weight: 1.75f),
        RowsWeight: 13.75f),
        new([
        [
            new("Mayús", Keycode.ShiftLeft, Held.Shift, Weight: 1.5f), Character("<", ">", null), .. Letters("zxcvbnm"),
            Character(",", ";", null, Keycode.Comma), Character(".", ":", null, Keycode.Period), Character("-", "_", null, Keycode.Minus),
            new("↑", Keycode.DpadUp, Repeats: true, FnLabel: "RePág", FnCode: Keycode.PageUp),
            new("Mayús", Keycode.ShiftRight, Held.Shift), new("Supr", Keycode.ForwardDel, Repeats: true),
        ],
        [
            new("Ctrl", Keycode.CtrlLeft, Held.Ctrl), new("Fn", Keycode.Function, Held.Fn), new("Alt", Keycode.AltLeft, Held.Alt),
            Character(" ", " ", null, Keycode.Space) with { Label = "", Weight = 6.5f },
            new("Alt Gr", Keycode.AltRight, Held.AltGr), new("Ctrl", Keycode.CtrlRight, Held.Ctrl),
            new("←", Keycode.DpadLeft, Repeats: true, FnLabel: "Inicio", FnCode: Keycode.MoveHome),
            new("↓", Keycode.DpadDown, Repeats: true, FnLabel: "AvPág", FnCode: Keycode.PageDown),
            new("→", Keycode.DpadRight, Repeats: true, FnLabel: "Fin", FnCode: Keycode.MoveEnd),
            new("Insert", Keycode.Insert),
        ],
        ]),
    ];

    /// <summary>The letters each dead key puts its accent on, and the accented letter.</summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<char, char>> Accents = new Dictionary<string, IReadOnlyDictionary<char, char>>
    {
        ["´"] = Accented("aeiouAEIOU", "áéíóúÁÉÍÓÚ"),
        ["`"] = Accented("aeiouAEIOU", "àèìòùÀÈÌÒÙ"),
        ["^"] = Accented("aeiouAEIOU", "âêîôûÂÊÎÔÛ"),
        ["¨"] = Accented("aeiouAEIOU", "äëïöüÄËÏÖÜ"),
    };

    /// <summary>Whether a key's text is a dead key's accent, which waits for the next character.</summary>
    public static bool IsDead(string text) => Accents.ContainsKey(text);

    /// <summary>
    /// The text a dead key's accent and the next character make: the accented
    /// letter, the accent alone before a space, or both as they are.
    /// </summary>
    public static string Compose(string accent, string next) =>
        next == " " ? accent
        : next.Length == 1 && Accents[accent].TryGetValue(next[0], out var accented) ? accented.ToString()
        : accent + next;

    /// <summary>The meta state a key event carries for the modifiers held.</summary>
    public static MetaKeyStates MetaOf(Held held)
    {
        var meta = MetaKeyStates.None;
        if (held.HasFlag(Held.Shift))
        {
            meta |= MetaKeyStates.ShiftOn | MetaKeyStates.ShiftLeftOn;
        }

        if (held.HasFlag(Held.Ctrl))
        {
            meta |= MetaKeyStates.CtrlOn | MetaKeyStates.CtrlLeftOn;
        }

        if (held.HasFlag(Held.Alt))
        {
            meta |= MetaKeyStates.AltOn | MetaKeyStates.AltLeftOn;
        }

        return meta;
    }

    private static PanelKey Character(string text, string? shifted, string? altGr, Keycode code = Keycode.Unknown) =>
        new(text, code, Text: text, ShiftText: shifted, AltGrText: altGr);

    /// <summary>A key that is a function key, F1 to F12, while Fn is held.</summary>
    private static PanelKey Function(PanelKey key, int number) =>
        key with { FnLabel = $"F{number}", FnCode = Keycode.F1 + (number - 1) };

    private static IEnumerable<PanelKey> Letters(string letters) =>
        letters.Select(letter => Character(letter.ToString(), char.ToUpperInvariant(letter).ToString(), letter == 'e' ? "€" : null, Keycode.A + (letter - 'a')));

    private static Dictionary<char, char> Accented(string plain, string accented) =>
        plain.Zip(accented).ToDictionary(pair => pair.First, pair => pair.Second);
}
