using System.IO;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The keyboard's log, a line each with its time, in
/// %LOCALAPPDATA%\Berpiztu\Keyboard\keyboard.log; past a megabyte the log
/// goes to keyboard.old.log and a new one starts. Always every warning and
/// error and the main events (<see cref="Write"/>), never what is on the
/// screen; what the keyboard follows (the names of the windows, fields and
/// menus focused, showing and hiding, moving out of the way, why Spelling
/// did or did not correct) only with Troubleshooting's Detailed log on
/// (<see cref="Detail"/>), for it names what the user looks at.
/// </summary>
public static class Log
{
    private const long MostBytes = 1024 * 1024;

    public static readonly string File = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Berpiztu", "Keyboard", "keyboard.log");

    private static readonly object Guard = new();

    /// <summary>Troubleshooting › Detailed log (<c>preferences.json</c>'s <c>DetailedLog</c>), off until turned on.</summary>
    public static bool Detailed { get; set; }

    /// <summary>A line of what the keyboard follows, written only with the detailed log on.</summary>
    public static void Detail(string line)
    {
        if (Detailed)
        {
            Write(line);
        }
    }

    /// <summary>The log as it is now, for Troubleshooting's viewer: read while the keyboard may be writing it; empty where there is none.</summary>
    public static string Read()
    {
        lock (Guard)
        {
            try
            {
                using var stream = new FileStream(File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException)
            {
                return "";
            }
        }
    }

    /// <summary>The log and its older part emptied, as Troubleshooting's Clear the log asks.</summary>
    public static void Clear()
    {
        lock (Guard)
        {
            try
            {
                System.IO.File.Delete(File);
                System.IO.File.Delete(Path.ChangeExtension(File, ".old.log"));
            }
            catch (IOException)
            {
                // Being written that moment: the next Clear empties it.
            }
        }
    }

    /// <summary>A warning, an error or a main event, always written; it never names what is on the screen.</summary>
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
