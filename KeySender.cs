using Android.OS;
using Android.Views;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Sends the panel's keys to an editor's input connection: the popup's
/// accessibility service's, beside the user's keyboard (KeyPlusService), or
/// Berpiztu AI Keyboard's (KeyPlusKeyboard), two types with the same
/// sendKeyEvent; each product has one of them.
/// </summary>
public static class KeySender
{
    /// <summary>The device id of key events no hardware sent (KeyCharacterMap.VIRTUAL_KEYBOARD).</summary>
    private const int VirtualKeyboardId = -1;

    /// <summary>The modifiers a key may be sent with: the meta flag that says so, the key pressed for it, and the meta state it adds.</summary>
    private static readonly (MetaKeyStates On, Keycode Key, MetaKeyStates Meta)[] Modifiers =
    [
        (MetaKeyStates.CtrlOn, Keycode.CtrlLeft, MetaKeyStates.CtrlOn | MetaKeyStates.CtrlLeftOn),
        (MetaKeyStates.AltOn, Keycode.AltLeft, MetaKeyStates.AltOn | MetaKeyStates.AltLeftOn),
        (MetaKeyStates.ShiftOn, Keycode.ShiftLeft, MetaKeyStates.ShiftOn | MetaKeyStates.ShiftLeftOn),
        (MetaKeyStates.MetaOn, Keycode.MetaLeft, MetaKeyStates.MetaOn | MetaKeyStates.MetaLeftOn),
    ];

    /// <summary>
    /// Sends a key as a hardware keyboard would: with its scan code, which
    /// remote desktop apps go by (<see cref="ScanCodes"/>), and each modifier
    /// held pressed before it and let go after it. The meta state alone is not
    /// enough for every editor: Android's own text fields, for one, select with
    /// Shift and the arrows only when Shift itself went down.
    /// </summary>
    /// <param name="sendKeyEvent">The connection's sendKeyEvent.</param>
    public static void Send(Action<KeyEvent> sendKeyEvent, Keycode code, MetaKeyStates meta)
    {
        var down = SystemClock.UptimeMillis();
        void Press(KeyEventActions action, Keycode key, MetaKeyStates state) =>
            sendKeyEvent(new KeyEvent(
                down, SystemClock.UptimeMillis(), action, key, 0, state,
                VirtualKeyboardId, ScanCodes.Of(key), 0, InputSourceType.Keyboard));

        var pressed = Modifiers.Where(modifier => meta.HasFlag(modifier.On)).ToList();
        var state = MetaKeyStates.None;
        foreach (var modifier in pressed)
        {
            state |= modifier.Meta;
            Press(KeyEventActions.Down, modifier.Key, state);
        }

        Press(KeyEventActions.Down, code, meta);
        Press(KeyEventActions.Up, code, meta);
        foreach (var modifier in Enumerable.Reverse(pressed))
        {
            state &= ~modifier.Meta;
            Press(KeyEventActions.Up, modifier.Key, state);
        }
    }
}
