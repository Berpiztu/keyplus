using System.IO;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The keyboard's log, Windows' and Android's alike, a line each with its
/// time: on Windows in %LOCALAPPDATA%\Berpiztu\Keyboard\keyboard.log, on
/// Android in the app's own files (keyboard.log), each line also to logcat
/// (tag KeyPlus); past a megabyte the log goes to keyboard.old.log and a new
/// one starts. Always the main events (<see cref="Write"/>), every warning
/// (<see cref="Warning(string)"/>, the line marked WARN after the process) and every
/// error (<see cref="Error(string, Exception)"/>, marked ERROR, with the exception's type, message
/// and stack where there is one), which the viewers show in yellow and red
/// (<see cref="LevelOf"/>), never what is on the screen; what the keyboard
/// follows (the names of the windows, fields and menus focused, showing and
/// hiding, moving out of the way, why Spelling did or did not correct) only
/// with Troubleshooting's Detailed log on (<see cref="Detail"/>), for it
/// names what the user looks at.
/// </summary>
public static class Log
{
    private const long MostBytes = 1024 * 1024;

#if ANDROID
    public static readonly string File = Path.Combine(Android.App.Application.Context.FilesDir!.AbsolutePath, "keyboard.log");

    /// <summary>Troubleshooting › Detailed log (<see cref="KeyPlusPreferences.DetailedLogKey"/>), off until turned on: read as it is now, for the settings and the keyboard share the process.</summary>
    public static bool Detailed => KeyPlusPreferences.Global(Android.App.Application.Context).GetBoolean(KeyPlusPreferences.DetailedLogKey, false);
#else
    public static readonly string File = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Berpiztu", "Keyboard", "keyboard.log");

    /// <summary>Troubleshooting › Detailed log (<c>preferences.json</c>'s <c>DetailedLog</c>), off until turned on.</summary>
    public static bool Detailed { get; set; }
#endif

    private static readonly object Guard = new();

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

    /// <summary>The mark after the process of a warning's line.</summary>
    public const string WarningMark = "WARN";

    /// <summary>The mark after the process of an error's line.</summary>
    public const string ErrorMark = "ERROR";

    /// <summary>How much a line of the log weighs, for its colour in the viewers.</summary>
    public enum Level
    {
        Event,
        Warning,
        Error,
    }

    /// <summary>A main event, always written; it never names what is on the screen.</summary>
    public static void Write(string line) => Put(Level.Event, line);

    /// <summary>Something that went wrong and the keyboard goes on without (a file not read, a service not answering), always written.</summary>
    public static void Warning(string line) => Put(Level.Warning, line);

    /// <summary>Something that went wrong and should not have, always written.</summary>
    public static void Error(string line) => Put(Level.Error, line);

    /// <summary>A warning with the exception that caused it, its type and message (an expected failure: a file, the network).</summary>
    public static void Warning(string line, Exception failure) => Put(Level.Warning, $"{line}: {failure.GetType().Name}: {failure.Message}");

    /// <summary>An error with the exception that caused it whole, its stack included, to find where it went wrong.</summary>
    public static void Error(string line, Exception failure) => Put(Level.Error, $"{line}: {failure}");

    /// <summary>
    /// A line's weight, as the viewers colour it: by its mark after the
    /// process; a line with no time of its own (an exception's stack) weighs
    /// what the line before it weighs (<paramref name="before"/>).
    /// </summary>
    public static Level LevelOf(string line, Level before)
    {
        if (line.Length < 24 || !char.IsDigit(line[0]) || line[4] != '-')
        {
            return line.Length == 0 ? Level.Event : before;
        }

        var process = line.IndexOf("] ", 24, StringComparison.Ordinal);
        return process < 0 ? Level.Event
            : string.CompareOrdinal(line, process + 2, ErrorMark + " ", 0, ErrorMark.Length + 1) == 0 ? Level.Error
            : string.CompareOrdinal(line, process + 2, WarningMark + " ", 0, WarningMark.Length + 1) == 0 ? Level.Warning
            : Level.Event;
    }

    /// <summary>
    /// A log's text in pieces of one weight each, for a viewer to colour: the
    /// lines of one weight that follow each other in one piece, each piece
    /// ending with its line's end.
    /// </summary>
    public static IEnumerable<(string Text, Level Level)> Pieces(string text)
    {
        var level = Level.Event;
        var start = 0;
        var pieceLevel = Level.Event;
        for (var at = 0; at < text.Length;)
        {
            var end = text.IndexOf('\n', at);
            end = end < 0 ? text.Length : end + 1;
            level = LevelOf(text[at..end].TrimEnd('\r', '\n'), level);
            if (level != pieceLevel && at > start)
            {
                yield return (text[start..at], pieceLevel);
                start = at;
            }

            pieceLevel = level;
            at = end;
        }

        if (start < text.Length)
        {
            yield return (text[start..], pieceLevel);
        }
    }

    private static void Put(Level level, string line)
    {
        var marked = level switch
        {
            Level.Error => $"{ErrorMark} {line}",
            Level.Warning => $"{WarningMark} {line}",
            _ => line,
        };
#if ANDROID
        switch (level)
        {
            case Level.Error:
                Android.Util.Log.Error("KeyPlus", line);
                break;
            case Level.Warning:
                Android.Util.Log.Warn("KeyPlus", line);
                break;
            default:
                Android.Util.Log.Info("KeyPlus", line);
                break;
        }
#endif
        lock (Guard)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File)!);
                if (System.IO.File.Exists(File) && new FileInfo(File).Length > MostBytes)
                {
                    System.IO.File.Move(File, Path.ChangeExtension(File, ".old.log"), overwrite: true);
                }

                System.IO.File.AppendAllText(File, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {marked}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // The log is not worth stopping the keyboard for.
            }
        }
    }
}
