using System.ComponentModel;
using System.Diagnostics;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The keyboard as administrator (Troubleshooting's Start as administrator): Windows lets a program into a window
/// only when it runs as that window does, so in one run as administrator (Task Manager, a terminal opened so) the
/// keyboard neither sees the focus nor types unless it runs so too. Started again through Windows' own question
/// (runas), which the user answers each time.
/// </summary>
public static class Elevation
{
    /// <summary>The argument of a keyboard started in place of one still closing: it waits for it to go.</summary>
    public const string ReplacingArgument = "--replacing";

    /// <summary>How long a keyboard started in place of another waits for it to close.</summary>
    public static readonly TimeSpan ReplacingWait = TimeSpan.FromSeconds(10);

    /// <summary>Whether the keyboard runs as administrator.</summary>
    public static bool Running => Environment.IsPrivilegedProcess;

    /// <summary>
    /// The keyboard started again as administrator, with these arguments: whether it was, the user saying yes to
    /// Windows' question; a no, or a failure, written to the log.
    /// </summary>
    public static bool StartAgain(IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
        foreach (var argument in arguments.Where(argument => argument != ReplacingArgument).Append(ReplacingArgument))
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var started = Process.Start(start);
            Log.Write("Keyboard: started again as administrator");
            return true;
        }
        catch (Win32Exception refused)
        {
            // 1223: the user said no to Windows' question.
            Log.Write(refused.NativeErrorCode == 1223
                ? "Warning: the keyboard was not started as administrator: Windows' question was answered no"
                : $"Warning: the keyboard could not be started as administrator: {refused.Message}");
            return false;
        }
    }
}
