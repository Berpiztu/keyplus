using System.Globalization;
using System.Runtime.InteropServices;
using Android.Views;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Keys and text sent to the window in the foreground, as Windows' own
/// (SendInput): text as Unicode characters, whatever the keyboard layout;
/// a key by its virtual key code, with Ctrl, Alt, ⊞ and Shift held around it;
/// a character in a shortcut by the key of the keyboard language's layout.
/// </summary>
public static class KeySender
{
    [Flags]
    public enum Held
    {
        None = 0,
        Ctrl = 1,
        Alt = 2,
        Meta = 4,
        Shift = 8,
    }

    private const uint KeyboardInput = 1;
    private const uint ExtendedKey = 0x0001;
    private const uint KeyUp = 0x0002;
    private const uint Unicode = 0x0004;

    private const ushort ControlKey = 0x11;
    private const ushort MenuKey = 0x12;
    private const ushort LeftWindowsKey = 0x5B;
    private const ushort ShiftKey = 0x10;

    /// <summary>VkKeyScanEx's high byte: Shift, and Ctrl and Alt together (Alt Gr).</summary>
    private const short ShiftBit = 0x100;
    private const short AltGrBits = 0x600;

    /// <summary>Text typed, each of its characters.</summary>
    public static void Type(string text)
    {
        List<Input> inputs = [];
        foreach (var character in text)
        {
            inputs.Add(Keyboard(0, character, Unicode));
            inputs.Add(Keyboard(0, character, Unicode | KeyUp));
        }

        Send(inputs);
    }

    /// <summary>A key pressed and let go, the modifiers held around it.</summary>
    public static void Press(Keycode code, Held held)
    {
        if (VirtualKey(code) is { } key)
        {
            Press(key, held, IsExtended(code) ? ExtendedKey : 0);
        }
    }

    /// <summary>
    /// A character pressed as the key that types it in the Windows layout of
    /// the keyboard's language, the modifiers held around it, as a physical
    /// keyboard of that language sends it (Spanish's ç is VK_OEM_2, so Ctrl+ç
    /// is VS Code's Ctrl+/); false when that layout has no key for it, or only
    /// with Alt Gr.
    /// </summary>
    public static bool PressCharacter(char character, string locale, Held held)
    {
        var scan = VkKeyScanEx(character, LayoutOf(locale));
        if (scan == -1 || (scan & AltGrBits) != 0)
        {
            return false;
        }

        Press((ushort)(scan & 0xFF), (scan & ShiftBit) != 0 ? held | Held.Shift : held, 0);
        return true;
    }

    /// <summary>
    /// The language's layout among those Windows has installed, never one
    /// loaded here (Windows may add it to the user's list); without it, the
    /// foreground window's.
    /// </summary>
    private static IntPtr LayoutOf(string locale)
    {
        var language = CultureInfo.GetCultureInfo(locale).LCID;
        var layouts = new IntPtr[GetKeyboardLayoutList(0, null)];
        GetKeyboardLayoutList(layouts.Length, layouts);
        return layouts.FirstOrDefault(layout => (layout.ToInt64() & 0xFFFF) == language) is var found && found != IntPtr.Zero
            ? found
            : GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero));
    }

    private static void Press(ushort key, Held held, uint extended)
    {
        List<Input> inputs = [];
        foreach (var modifier in Modifiers(held))
        {
            inputs.Add(Keyboard(modifier, 0, 0));
        }

        inputs.Add(Keyboard(key, 0, extended));
        inputs.Add(Keyboard(key, 0, extended | KeyUp));
        foreach (var modifier in Modifiers(held).Reverse())
        {
            inputs.Add(Keyboard(modifier, 0, KeyUp));
        }

        Send(inputs);
    }

    private static IEnumerable<ushort> Modifiers(Held held)
    {
        if (held.HasFlag(Held.Ctrl))
        {
            yield return ControlKey;
        }

        if (held.HasFlag(Held.Alt))
        {
            yield return MenuKey;
        }

        if (held.HasFlag(Held.Meta))
        {
            yield return LeftWindowsKey;
        }

        if (held.HasFlag(Held.Shift))
        {
            yield return ShiftKey;
        }
    }

    /// <summary>Android's key code as Windows' virtual key.</summary>
    private static ushort? VirtualKey(Keycode code) => code switch
    {
        >= Keycode.A and <= Keycode.A + 25 => (ushort)(0x41 + (code - Keycode.A)),
        >= Keycode.Num0 and <= Keycode.Num0 + 9 => (ushort)(0x30 + (code - Keycode.Num0)),
        >= Keycode.F1 and <= Keycode.F1 + 11 => (ushort)(0x70 + (code - Keycode.F1)),
        Keycode.Escape => 0x1B,
        Keycode.Tab => 0x09,
        Keycode.Del => 0x08,
        Keycode.ForwardDel => 0x2E,
        Keycode.Enter => 0x0D,
        Keycode.Space => 0x20,
        Keycode.DpadLeft => 0x25,
        Keycode.DpadUp => 0x26,
        Keycode.DpadRight => 0x27,
        Keycode.DpadDown => 0x28,
        Keycode.MoveHome => 0x24,
        Keycode.MoveEnd => 0x23,
        Keycode.PageUp => 0x21,
        Keycode.PageDown => 0x22,
        Keycode.Insert => 0x2D,
        Keycode.Sysrq => 0x2C,
        Keycode.Comma => 0xBC,
        Keycode.Period => 0xBE,
        Keycode.Minus => 0xBD,
        Keycode.Plus or Keycode.Equals => 0xBB,
        Keycode.Slash => 0xBF,
        _ => null,
    };

    /// <summary>The keys Windows calls extended: the arrows and the six over them, so a numeric pad's are not taken for them.</summary>
    private static bool IsExtended(Keycode code) => code is Keycode.DpadLeft or Keycode.DpadUp or Keycode.DpadRight or Keycode.DpadDown
        or Keycode.MoveHome or Keycode.MoveEnd or Keycode.PageUp or Keycode.PageDown or Keycode.Insert or Keycode.ForwardDel;

    private static Input Keyboard(ushort key, ushort character, uint flags) => new()
    {
        Type = KeyboardInput,
        Data = new InputData { Keyboard = new KeyboardInputData { VirtualKey = key, ScanCode = character, Flags = flags } },
    };

    private static void Send(List<Input> inputs)
    {
        if (inputs.Count > 0)
        {
            SendInput((uint)inputs.Count, [.. inputs], Marshal.SizeOf<Input>());
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern short VkKeyScanEx(char character, IntPtr layout);

    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int count, IntPtr[]? layouts);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint thread);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputData Data;
    }

    /// <summary>INPUT's union, as large as its largest member (the mouse's), so SendInput takes its size.</summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)]
        public KeyboardInputData Keyboard;

        [FieldOffset(0)]
        public MouseInputData Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
