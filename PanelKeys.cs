using Android.Views;

namespace Berpiztu.KeyPlus;

/// <summary>Ctrl, Alt and Shift as the panel holds them: Ctrl and Alt for the next key, Shift until it is pressed again.</summary>
[Flags]
public enum Held
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
}

/// <summary>
/// A key on the panel: what it reads, the key it sends or the modifier it
/// holds, whether holding it repeats it, and the modifiers it always sends
/// with (With: Ctrl for a shortcut key such as Ctrl+C).
/// </summary>
public sealed record PanelKey(string Label, Keycode Code, Held Modifier = Held.None, bool Repeats = false, Held With = Held.None)
{
    public bool IsModifier => Modifier != Held.None;
}

/// <summary>The panel's layouts and how a held modifier reaches a key event.</summary>
public static class PanelKeys
{
    /// <summary>
    /// A terminal's keys as Termux lays them out: two rows of seven, the arrows
    /// as a cross with Home and End beside the up arrow and the pages at the
    /// right; Shift where Termux has the minus, which the phone's keyboard has
    /// at hand, so Shift and the arrows select. Above them, the editing
    /// shortcuts as keys of their own: Ctrl then a "c" typed on the phone's
    /// keyboard cannot copy, because the "c" replaces the selection before it
    /// is taken back.
    /// </summary>
    public static readonly IReadOnlyList<IReadOnlyList<PanelKey>> Terminal =
    [
        [
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
        ],
        [
            new("Tab", Keycode.Tab),
            new("Ctrl", Keycode.CtrlLeft, Held.Ctrl),
            new("Alt", Keycode.AltLeft, Held.Alt),
            new("←", Keycode.DpadLeft, Repeats: true),
            new("↓", Keycode.DpadDown, Repeats: true),
            new("→", Keycode.DpadRight, Repeats: true),
            new("PgDn", Keycode.PageDown, Repeats: true),
        ],
    ];

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
}
