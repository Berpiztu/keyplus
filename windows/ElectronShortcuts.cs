using System.IO;

namespace Berpiztu.KeyPlus;

/// <summary>
/// VS Code's shortcuts made to start it with its accessibility on
/// (--force-renderer-accessibility): Electron tells its menus only then, and
/// the keyboard moves out of the way of menus it is told of. The user's own
/// shortcuts, in the Start menu, the taskbar and on the desktop, each that
/// starts Code.exe given the switch where it lacks it.
/// </summary>
public static class ElectronShortcuts
{
    public const string Switch = "--force-renderer-accessibility";

    /// <summary>What was found: shortcuts given the switch, and those that had it already.</summary>
    public sealed record Result(int Changed, int Already);

    public static Result AddSwitch()
    {
        var changed = 0;
        var already = 0;
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        foreach (var path in Shortcuts())
        {
            dynamic shortcut = shell.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, [path])!;
            string target = shortcut.TargetPath ?? "";
            if (!Path.GetFileName(target).Equals("Code.exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string arguments = shortcut.Arguments ?? "";
            if (arguments.Contains(Switch, StringComparison.Ordinal))
            {
                already++;
                continue;
            }

            shortcut.Arguments = $"{arguments} {Switch}".Trim();
            shortcut.Save();
            changed++;
            Log.Write($"VS Code's shortcut {path} now starts it with {Switch}");
        }

        return new Result(changed, already);
    }

    /// <summary>The user's shortcuts: the Start menu's, the taskbar's pinned ones and the desktop's.</summary>
    private static IEnumerable<string> Shortcuts()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] folders =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Path.Combine(roaming, "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        ];
        return folders.Where(Directory.Exists).SelectMany(folder => Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories));
    }
}
