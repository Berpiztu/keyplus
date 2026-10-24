using System.IO;

namespace Berpiztu.KeyPlus;

/// <summary>
/// A file of the app's folder watched: each change told on the caller's
/// thread until the watch is disposed, so the keyboard and the settings, two
/// processes, see each other's writes at once.
/// </summary>
public static class FileWatch
{
    public static IDisposable Changed(string file, Action changed)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var caller = SynchronizationContext.Current;
        var watch = new FileSystemWatcher(Path.GetDirectoryName(file)!, Path.GetFileName(file)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
        void Tell()
        {
            try
            {
                changed();
            }
            catch (IOException busy)
            {
                // Still being written: its next change tells it again.
                Log.Write($"{Path.GetFileName(file)} was busy: {busy.Message}");
            }
        }

        void Seen(object sender, FileSystemEventArgs e)
        {
            if (caller is null)
            {
                Tell();
            }
            else
            {
                caller.Post(_ => Tell(), null);
            }
        }

        watch.Changed += Seen;
        watch.Created += Seen;
        watch.EnableRaisingEvents = true;
        return watch;
    }
}
