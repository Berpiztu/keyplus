using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Berpiztu.KeyPlus;

/// <summary>
/// What the user does with anything but the keyboard's own keys, anywhere:
/// when the mouse's left button, a finger or a pen (which Windows sends as
/// the left button) last pressed outside the keyboard's windows, and when a
/// key of the computer's own keyboard last went down (one sent by a program,
/// the keyboard's own included, is marked injected and left out). Low-level
/// hooks on a thread of its own that only note the moment, so no press ever
/// waits on the keyboard. For a tap on the text after ✕ (the caret moving
/// just after a press), and for Spelling on Windows, which mends a sentence
/// only while nothing but the keyboard has touched it.
/// </summary>
public sealed class InputWatch : IDisposable
{
    private const int LowLevelKeyboard = 13;
    private const int LowLevelMouse = 14;
    private const int LeftButtonDown = 0x0201;
    private const int KeyDown = 0x0100;
    private const int SystemKeyDown = 0x0104;
    private const uint Injected = 0x10;

    private static readonly uint Own = (uint)Environment.ProcessId;

    private readonly HookProc mouseNoted;
    private readonly HookProc keysNoted;
    private readonly Dispatcher watching;

    /// <summary>Environment.TickCount64 at the last press outside the keyboard's windows, and at the last of the computer's keys; 0 before one.</summary>
    private long pressedAt;
    private long keyedAt;

    public InputWatch()
    {
        mouseNoted = MouseNoted;
        keysNoted = KeysNoted;
        Dispatcher? made = null;
        using var started = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            made = Dispatcher.CurrentDispatcher;
            var module = GetModuleHandle(null);
            var mouse = SetWindowsHookEx(LowLevelMouse, mouseNoted, module, 0);
            var keys = SetWindowsHookEx(LowLevelKeyboard, keysNoted, module, 0);
            started.Set();
            Dispatcher.Run();
            UnhookWindowsHookEx(mouse);
            UnhookWindowsHookEx(keys);
        })
        { IsBackground = true, Name = "Input watch" };
        thread.Start();
        started.Wait();
        watching = made!;
    }

    /// <summary>Whether a press outside the keyboard came no longer ago than <paramref name="span"/>.</summary>
    public bool PressedWithin(TimeSpan span) =>
        Interlocked.Read(ref pressedAt) is > 0 and var at && Environment.TickCount64 - at <= span.TotalMilliseconds;

    /// <summary>Environment.TickCount64 at the last press outside the keyboard's windows: the caret may have gone anywhere.</summary>
    public long PressedAt => Interlocked.Read(ref pressedAt);

    /// <summary>Environment.TickCount64 at the last key of the computer's own keyboard: the text may hold what the keyboard did not type.</summary>
    public long KeyedAt => Interlocked.Read(ref keyedAt);

    /// <summary>Never waiting: the watch's thread lets go of its hooks once it stops.</summary>
    public void Dispose() => watching.BeginInvokeShutdown(DispatcherPriority.Send);

    /// <summary>Windows' call for every mouse message: a press outside the keyboard's windows noted, and the message passed on at once.</summary>
    private IntPtr MouseNoted(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && (int)message == LeftButtonDown)
        {
            var point = Marshal.PtrToStructure<MouseData>(data).Point;
            GetWindowThreadProcessId(WindowFromPoint(point), out var process);
            if (process != Own)
            {
                Interlocked.Exchange(ref pressedAt, Environment.TickCount64);
            }
        }

        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    /// <summary>Windows' call for every key: one of the computer's going down noted, those a program sends left out.</summary>
    private IntPtr KeysNoted(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && (int)message is KeyDown or SystemKeyDown && (Marshal.PtrToStructure<KeyData>(data).Flags & Injected) == 0)
        {
            Interlocked.Exchange(ref keyedAt, Environment.TickCount64);
        }

        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint
    {
        public int X;
        public int Y;
    }

    /// <summary>MSLLHOOKSTRUCT: the point first.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseData
    {
        public ScreenPoint Point;
        public uint MouseData_;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    /// <summary>KBDLLHOOKSTRUCT.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int kind, HookProc proc, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(ScreenPoint point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
