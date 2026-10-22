using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Berpiztu.KeyPlus;

/// <summary>A window a click never activates (WS_EX_NOACTIVATE), so the focus stays in the window typed in; with a taskbar button where asked (WS_EX_APPWINDOW).</summary>
public static class NeverActivated
{
    private const int ExtendedStyle = -20;
    private const long NoActivate = 0x08000000;
    private const long AppWindow = 0x00040000;
    private const long ToolWindow = 0x00000080;

    /// <summary>Set once the window has its handle (OnSourceInitialized).</summary>
    public static void Apply(Window window, bool taskbar)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var style = GetWindowLongPtr(handle, ExtendedStyle).ToInt64() | NoActivate;
        style = taskbar ? style | AppWindow : (style & ~AppWindow) | ToolWindow;
        SetWindowLongPtr(handle, ExtendedStyle, new IntPtr(style));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
