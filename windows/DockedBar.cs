using System.Runtime.InteropServices;
using System.Windows;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The keyboard docked as Windows' touch keyboard docks: an app bar along the
/// bottom of the screen it is on (SHAppBarMessage), as wide, above the
/// taskbar, the room it takes kept from the other windows, a maximized one
/// made shorter, as the taskbar's is; undocked, the room given back.
/// </summary>
public sealed class DockedBar(IntPtr window)
{
    private const uint New = 0;
    private const uint Remove = 1;
    private const uint QueryPosition = 2;
    private const uint SetPosition = 3;
    private const uint BottomEdge = 3;

    private bool registered;

    /// <summary>The bottom Windows gave the bar on the screen it is on, in pixels: placed again (taller or shorter rows), it keeps it, for asked again Windows counts the room the bar already holds as taken and puts it above.</summary>
    private (System.Drawing.Rectangle Screen, int Bottom)? given;

    /// <summary>The bar along a screen's bottom (its bounds in pixels), so many pixels tall: where Windows gives it, in pixels.</summary>
    public Int32Rect Dock(System.Drawing.Rectangle screen, int height)
    {
        var data = Data();
        if (!registered)
        {
            SHAppBarMessage(New, ref data);
            registered = true;
            given = null;
        }

        data.Edge = BottomEdge;
        if (given is { } kept && kept.Screen == screen)
        {
            data.Bounds = new Rect32 { Left = screen.Left, Right = screen.Right, Top = kept.Bottom - height, Bottom = kept.Bottom };
        }
        else
        {
            data.Bounds = new Rect32 { Left = screen.Left, Right = screen.Right, Top = screen.Bottom - height, Bottom = screen.Bottom };
            SHAppBarMessage(QueryPosition, ref data);
            data.Bounds.Top = data.Bounds.Bottom - height;
        }

        SHAppBarMessage(SetPosition, ref data);
        given = (screen, data.Bounds.Bottom);
        return new Int32Rect(data.Bounds.Left, data.Bounds.Top, data.Bounds.Right - data.Bounds.Left, data.Bounds.Bottom - data.Bounds.Top);
    }

    /// <summary>The room given back to the other windows.</summary>
    public void Undock()
    {
        if (!registered)
        {
            return;
        }

        var data = Data();
        SHAppBarMessage(Remove, ref data);
        registered = false;
        given = null;
    }

    private AppBarData Data() => new() { Size = Marshal.SizeOf<AppBarData>(), Window = window };

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size;
        public IntPtr Window;
        public uint CallbackMessage;
        public uint Edge;
        public Rect32 Bounds;
        public IntPtr Parameter;
    }

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
}
