using System.Runtime.InteropServices;
using System.Windows;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Berpiztu AI Keyboard for Windows: the keyboard, or, started with
/// --settings, its settings, each a program of its own as on Android, one of
/// each at a time; started again while running, the keyboard shows itself
/// and the settings come to the front. Started with --hidden (as Windows
/// starts it at sign-in), the keyboard runs out of sight, by the clock, until
/// a text field takes the focus.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] arguments)
    {
        var settings = arguments.Contains(SettingsWindow.Argument);
        var hidden = arguments.Contains(HiddenArgument);
        using var running = new Mutex(true, settings ? @"Local\Berpiztu.KeyPlus.Windows.Settings" : @"Local\Berpiztu.KeyPlus.Windows", out var first);
        if (!first)
        {
            var open = FindWindow(null, settings ? Texts.Get(Resource.String.settings_title) : KeyboardWindow.WindowTitle);
            if (open != IntPtr.Zero && settings)
            {
                ShowWindow(open, Restore);
                SetForegroundWindow(open);
            }
            else if (open != IntPtr.Zero && !hidden)
            {
                PostMessage(open, KeyboardWindow.ShowMessage, IntPtr.Zero, IntPtr.Zero);
            }

            return;
        }

        // The detailed log only where the user turned it on: it names what is on the screen.
        Log.Detailed = KeyboardPreferences.Read().DetailedLog;

        // An error that ends the program written to the log first, for Windows' own report keeps no trace of it.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"Ended by an error: {e.ExceptionObject}");
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        if (settings)
        {
            app.Run(new SettingsWindow());
            return;
        }

        UpdateCheck.Start();
        if (hidden)
        {
            // Its window made but not shown: it watches the focus and shows itself for a text field.
            new System.Windows.Interop.WindowInteropHelper(new KeyboardWindow()).EnsureHandle();
            app.Run();
        }
        else
        {
            app.Run(new KeyboardWindow());
        }
    }

    /// <summary>The argument that starts the keyboard out of sight.</summary>
    public const string HiddenArgument = "--hidden";

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
