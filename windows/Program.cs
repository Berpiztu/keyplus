using System.Windows;

namespace Berpiztu.KeyPlus;

/// <summary>Berpiztu AI Keyboard for Windows: its window, the app ending when it closes; started again while it runs, nothing more.</summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        using var running = new Mutex(true, @"Local\Berpiztu.KeyPlus.Windows", out var first);
        if (!first)
        {
            return;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(new KeyboardWindow());
    }
}
