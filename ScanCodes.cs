using Android.Views;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The scan code a hardware keyboard sends with each key: Linux's input code
/// for the key at that place, which is what Android reports for a real
/// keyboard. Remote desktop apps read it, not the key code, to press the same
/// key on the remote machine, whose own layout then says what it types; a key
/// event without it (0) they ignore. The key codes are the places of a US
/// keyboard, as Android's are: Spain's ç is where the US backslash is.
/// </summary>
public static class ScanCodes
{
    private static readonly IReadOnlyDictionary<Keycode, int> ByKey = new Dictionary<Keycode, int>
    {
        [Keycode.Escape] = 1,
        [Keycode.Num1] = 2, [Keycode.Num2] = 3, [Keycode.Num3] = 4, [Keycode.Num4] = 5, [Keycode.Num5] = 6,
        [Keycode.Num6] = 7, [Keycode.Num7] = 8, [Keycode.Num8] = 9, [Keycode.Num9] = 10, [Keycode.Num0] = 11,
        [Keycode.Minus] = 12, [Keycode.Equals] = 13, [Keycode.Del] = 14, [Keycode.Tab] = 15,
        [Keycode.Q] = 16, [Keycode.W] = 17, [Keycode.E] = 18, [Keycode.R] = 19, [Keycode.T] = 20,
        [Keycode.Y] = 21, [Keycode.U] = 22, [Keycode.I] = 23, [Keycode.O] = 24, [Keycode.P] = 25,
        [Keycode.LeftBracket] = 26, [Keycode.RightBracket] = 27, [Keycode.Enter] = 28, [Keycode.CtrlLeft] = 29,
        [Keycode.A] = 30, [Keycode.S] = 31, [Keycode.D] = 32, [Keycode.F] = 33, [Keycode.G] = 34,
        [Keycode.H] = 35, [Keycode.J] = 36, [Keycode.K] = 37, [Keycode.L] = 38,
        [Keycode.Semicolon] = 39, [Keycode.Apostrophe] = 40, [Keycode.Grave] = 41, [Keycode.ShiftLeft] = 42, [Keycode.Backslash] = 43,
        [Keycode.Z] = 44, [Keycode.X] = 45, [Keycode.C] = 46, [Keycode.V] = 47, [Keycode.B] = 48, [Keycode.N] = 49, [Keycode.M] = 50,
        [Keycode.Comma] = 51, [Keycode.Period] = 52, [Keycode.Slash] = 53, [Keycode.ShiftRight] = 54,
        [Keycode.AltLeft] = 56, [Keycode.Space] = 57, [Keycode.CapsLock] = 58,
        [Keycode.F1] = 59, [Keycode.F2] = 60, [Keycode.F3] = 61, [Keycode.F4] = 62, [Keycode.F5] = 63,
        [Keycode.F6] = 64, [Keycode.F7] = 65, [Keycode.F8] = 66, [Keycode.F9] = 67, [Keycode.F10] = 68,
        [Keycode.F11] = 87, [Keycode.F12] = 88,
        [Keycode.CtrlRight] = 97, [Keycode.AltRight] = 100,
        [Keycode.MoveHome] = 102, [Keycode.DpadUp] = 103, [Keycode.PageUp] = 104, [Keycode.DpadLeft] = 105,
        [Keycode.DpadRight] = 106, [Keycode.MoveEnd] = 107, [Keycode.DpadDown] = 108, [Keycode.PageDown] = 109,
        [Keycode.Insert] = 110, [Keycode.ForwardDel] = 111,
    };

    /// <summary>The scan code of a key; 0 for one a PC keyboard does not have.</summary>
    public static int Of(Keycode code) => ByKey.GetValueOrDefault(code);
}
