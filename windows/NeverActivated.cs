using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Berpiztu.KeyPlus;

/// <summary>
/// A window a click never activates (WS_EX_NOACTIVATE, and MA_NOACTIVATE to WM_MOUSEACTIVATE), so the focus stays in the
/// window typed in; with a taskbar button where asked (WS_EX_APPWINDOW).
/// </summary>
public static class NeverActivated
{
    private const int ExtendedStyle = -20;
    private const long NoActivate = 0x08000000;
    private const long AppWindow = 0x00040000;
    private const long ToolWindow = 0x00000080;
    private const int MouseActivate = 0x0021;
    private const int MouseNoActivate = 3;

    /// <summary>Set once the window has its handle (OnSourceInitialized).</summary>
    public static void Apply(Window window, bool taskbar)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var style = GetWindowLongPtr(handle, ExtendedStyle).ToInt64() | NoActivate;
        style = taskbar ? style | AppWindow : (style & ~AppWindow) | ToolWindow;
        SetWindowLongPtr(handle, ExtendedStyle, new IntPtr(style));

        // The style alone is not always heeded: clicked with the taskbar in front, as when coming to a window from it, Windows
        // activated the keyboard, whose keys then went to itself. Told no on every click, it never is.
        HwndSource.FromHwnd(handle)?.AddHook((IntPtr _, int message, IntPtr _, IntPtr _, ref bool handled) =>
        {
            if (message != MouseActivate)
            {
                return IntPtr.Zero;
            }

            handled = true;
            return new IntPtr(MouseNoActivate);
        });
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
