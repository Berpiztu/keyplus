using System.IO;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The keyboard's log: what it saw and did (the focus, showing and hiding,
/// moving out of the way) and every warning and error, a line each with its
/// time, in %LOCALAPPDATA%\Berpiztu\Keyboard\keyboard.log; past a megabyte
/// the log goes to keyboard.old.log and a new one starts.
/// </summary>
public static class Log
{
    private const long MostBytes = 1024 * 1024;

    public static readonly string File = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Berpiztu", "Keyboard", "keyboard.log");

    private static readonly object Guard = new();

    public static void Write(string line)
    {
        lock (Guard)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File)!);
                if (System.IO.File.Exists(File) && new FileInfo(File).Length > MostBytes)
                {
                    System.IO.File.Move(File, Path.ChangeExtension(File, ".old.log"), overwrite: true);
                }

                System.IO.File.AppendAllText(File, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {line}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // The log is not worth stopping the keyboard for.
            }
        }
    }
}
