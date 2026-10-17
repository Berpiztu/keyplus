using Android.Views;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The modifiers as the panel holds them: Ctrl, Alt and AltGr for the next
/// key, Shift locked until pressed again for the strip's keys and let go after
/// a character typed, Caps Lock until pressed again.
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
}

/// <summary>
/// A key on the panel: what it reads, the key it sends or the modifier it
/// holds, whether holding it repeats it, the modifiers it always sends with
/// (With: Ctrl for a shortcut key such as Ctrl+C), and its share of its row's
/// width. A character key also has the text it types, plain, with Shift and
/// with AltGr; it types it as text, so it types what the layout says whatever
/// the editor's own keyboard layout, and sends its key (Code) only with Ctrl
/// or Alt held.
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
    string? AltGrText = null)
{
    public bool IsModifier => Modifier != Held.None;

    public bool IsCharacter => Text is not null;

    /// <summary>The text the key types with the modifiers held; none where the layout has nothing.</summary>
    public string? TextFor(Held held)
    {
        if (held.HasFlag(Held.AltGr))
        {
            return AltGrText;
        }

        var isLetter = Text is { Length: 1 } && char.IsLetter(Text[0]);
        var shifted = held.HasFlag(Held.Shift) ^ (isLetter && held.HasFlag(Held.CapsLock));
        return shifted ? ShiftText : Text;
    }
}

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
    public static readonly IReadOnlyList<IReadOnlyList<PanelKey>> Terminal =
    [
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
    ];

    /// <summary>
    /// A whole keyboard as Spain's (ISO, es-ES) lays it out, AltGr's characters
    /// included, with ´ ` ^ and ¨ as dead keys: Esc, the function keys and Del
    /// along the top, as a PC's; the arrows as an inverted T at the bottom
    /// right, Home and End beside the up arrow. The last two rows weigh the
    /// same, so the up arrow stands right above the down one.
    /// </summary>
    public static readonly IReadOnlyList<IReadOnlyList<PanelKey>> Spanish =
    [
        [
            new("Esc", Keycode.Escape), .. Enumerable.Range(1, 12).Select(n => new PanelKey($"F{n}", Keycode.F1 + (n - 1))),
            new("Del", Keycode.ForwardDel, Repeats: true),
        ],
        [
            Character("º", "ª", "\\"), Character("1", "!", "|", Keycode.Num1), Character("2", "\"", "@", Keycode.Num2),
            Character("3", "·", "#", Keycode.Num3), Character("4", "$", "~", Keycode.Num4), Character("5", "%", "€", Keycode.Num5),
            Character("6", "&", "¬", Keycode.Num6), Character("7", "/", null, Keycode.Num7), Character("8", "(", null, Keycode.Num8),
            Character("9", ")", null, Keycode.Num9), Character("0", "=", null, Keycode.Num0), Character("'", "?", null, Keycode.Apostrophe),
            Character("¡", "¿", null), new("⌫", Keycode.Del, Repeats: true, Weight: 1.5f),
        ],
        [
            new("Tab", Keycode.Tab, Weight: 1.5f), .. Letters("qwertyuiop"),
            Character("`", "^", "[", Keycode.Grave), Character("+", "*", "]", Keycode.Plus),
        ],
        [
            new("⇪", Keycode.CapsLock, Held.CapsLock, Weight: 1.5f), .. Letters("asdfghjkl"), Character("ñ", "Ñ", null),
            Character("´", "¨", "{"), Character("ç", "Ç", "}"), new("⏎", Keycode.Enter),
        ],
        [
            new("⇧", Keycode.ShiftLeft, Held.Shift), Character("<", ">", null), .. Letters("zxcvbnm"),
            Character(",", ";", null, Keycode.Comma), Character(".", ":", null, Keycode.Period), Character("-", "_", null, Keycode.Minus),
            new("Home", Keycode.MoveHome), new("↑", Keycode.DpadUp, Repeats: true), new("End", Keycode.MoveEnd),
        ],
        [
            new("Ctrl", Keycode.CtrlLeft, Held.Ctrl, Weight: 1.5f), new("Alt", Keycode.AltLeft, Held.Alt, Weight: 1.5f),
            Character(" ", " ", null, Keycode.Space) with { Label = "Space", Weight = 7.5f },
            new("AltGr", Keycode.AltRight, Held.AltGr, Weight: 1.5f),
            new("←", Keycode.DpadLeft, Repeats: true), new("↓", Keycode.DpadDown, Repeats: true), new("→", Keycode.DpadRight, Repeats: true),
        ],
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

    private static IEnumerable<PanelKey> Letters(string letters) =>
        letters.Select(letter => Character(letter.ToString(), char.ToUpperInvariant(letter).ToString(), letter == 'e' ? "€" : null, Keycode.A + (letter - 'a')));

    private static Dictionary<char, char> Accented(string plain, string accented) =>
        plain.Zip(accented).ToDictionary(pair => pair.First, pair => pair.Second);
}
