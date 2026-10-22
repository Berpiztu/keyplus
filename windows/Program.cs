using System.Runtime.InteropServices;
using System.Windows;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Berpiztu AI Keyboard for Windows: the keyboard, or, started with
/// --settings, its settings, each a program of its own as on Android, one of
/// each at a time; started again while running, the keyboard shows itself
/// and the settings come to the front.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] arguments)
    {
        var settings = arguments.Contains(SettingsWindow.Argument);
        using var running = new Mutex(true, settings ? @"Local\Berpiztu.KeyPlus.Windows.Settings" : @"Local\Berpiztu.KeyPlus.Windows", out var first);
        if (!first)
        {
            var open = FindWindow(null, settings ? Texts.Get(Resource.String.settings_title) : KeyboardWindow.WindowTitle);
            if (open != IntPtr.Zero && settings)
            {
                ShowWindow(open, Restore);
                SetForegroundWindow(open);
            }
            else if (open != IntPtr.Zero)
            {
                PostMessage(open, KeyboardWindow.ShowMessage, IntPtr.Zero, IntPtr.Zero);
            }

            return;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(settings ? new SettingsWindow() : new KeyboardWindow());
    }

    private const int Restore = 9;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int how);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
}
