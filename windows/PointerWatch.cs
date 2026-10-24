using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Berpiztu.KeyPlus;

/// <summary>
/// When the mouse's left button, a finger or a pen (which Windows also sends
/// as the left button) last pressed, anywhere: a low-level mouse hook on a
/// thread of its own that only notes the moment, so no press ever waits on
/// the keyboard. Kept only while it is needed: after ✕ in a field, to tell
/// the field tapped again from the caret moved by typing.
/// </summary>
public sealed class PointerWatch : IDisposable
{
    private const int LowLevelMouse = 14;
    private const int LeftButtonDown = 0x0201;

    private readonly HookProc noted;
    private readonly Dispatcher watching;

    /// <summary>Environment.TickCount64 at the last press; 0 before one.</summary>
    private long pressedAt;

    public PointerWatch()
    {
        noted = Noted;
        Dispatcher? made = null;
        using var started = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            made = Dispatcher.CurrentDispatcher;
            var hook = SetWindowsHookEx(LowLevelMouse, noted, GetModuleHandle(null), 0);
            started.Set();
            Dispatcher.Run();
            UnhookWindowsHookEx(hook);
        })
        { IsBackground = true, Name = "Pointer watch" };
        thread.Start();
        started.Wait();
        watching = made!;
    }

    /// <summary>Whether a press came no longer ago than <paramref name="span"/>.</summary>
    public bool PressedWithin(TimeSpan span) =>
        Interlocked.Read(ref pressedAt) is > 0 and var at && Environment.TickCount64 - at <= span.TotalMilliseconds;

    /// <summary>Never waiting: the watch's thread lets go of its hook once it stops.</summary>
    public void Dispose() => watching.BeginInvokeShutdown(DispatcherPriority.Send);

    /// <summary>Windows' call for every mouse message: the moment noted, and the message passed on at once.</summary>
    private IntPtr Noted(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && (int)message == LeftButtonDown)
        {
            Interlocked.Exchange(ref pressedAt, Environment.TickCount64);
        }

        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int kind, HookProc proc, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
