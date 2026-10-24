using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace Berpiztu.KeyPlus;

/// <summary>Información › Buscar actualizaciones automáticamente, and when GitHub was last asked; in %APPDATA%\Berpiztu\Keyboard\updates.json.</summary>
public sealed record UpdateSettings(bool Automatic = true, DateTime? Checked = null)
{
    private static readonly string Kept = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Berpiztu", "Keyboard", "updates.json");

    public static UpdateSettings Read()
    {
        try
        {
            return File.Exists(Kept) ? JsonSerializer.Deserialize<UpdateSettings>(File.ReadAllText(Kept)) ?? new() : new();
        }
        catch (Exception unreadable) when (unreadable is JsonException or IOException)
        {
            Log.Write($"Updates: updates.json could not be read: {unreadable.Message}");
            return new();
        }
    }

    public void Keep()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Kept)!);
        File.WriteAllText(Kept, JsonSerializer.Serialize(this));
    }
}

/// <summary>
/// The Windows side of <see cref="Updates"/>: the keyboard asks once a day
/// while Buscar actualizaciones automáticamente is on, and the settings' Buscar
/// ahora when tapped; a newer release is told in a window that asks whether to
/// update, and a yes downloads its installer and opens it, which stops the
/// keyboard and starts the new one.
/// </summary>
public static class UpdateCheck
{
    private static readonly TimeSpan Look = TimeSpan.FromHours(1);
    private static bool checking;

    /// <summary>The version running, as Información shows it.</summary>
    public static string Running => System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "";

    /// <summary>The keyboard's daily check: looked at every hour, and once a minute after the start, so signing in is not slowed.</summary>
    public static void Start()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        timer.Tick += async (_, _) =>
        {
            timer.Interval = Look;
            var settings = UpdateSettings.Read();
            if (settings.Automatic && Updates.Due(settings.Checked))
            {
                await CheckAsync(asked: false);
            }
        };
        timer.Start();
    }

    /// <summary>GitHub asked for the latest release; asked by hand, an answer whatever it is; by itself, a failure only in the log.</summary>
    public static async Task CheckAsync(bool asked)
    {
        if (checking)
        {
            return;
        }

        checking = true;
        var answered = false;
        try
        {
            var release = await Updates.LatestAsync(Updates.WindowsInstaller);
            (UpdateSettings.Read() with { Checked = DateTime.UtcNow }).Keep();
            if (!Updates.IsNewer(release, Running))
            {
                if (asked)
                {
                    MessageBox.Show(Texts.Get(Resource.String.update_current, Running), Texts.Get(Resource.String.updates_check));
                }

                return;
            }

            Log.Write($"Updates: {release.Version} is out, {Running} running");
            if (MessageBox.Show(Texts.Get(Resource.String.update_ask, release.Version, Running), Texts.Get(Resource.String.update_title),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            answered = true;
            var installer = await Updates.DownloadAsync(release, Path.GetTempPath());
            Log.Write($"Updates: {installer} downloaded, opened");
            Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{installer}\"") { UseShellExecute = true });
        }
        catch (Exception failed) when (failed is HttpRequestException or TaskCanceledException or IOException or JsonException
            or KeyNotFoundException or InvalidOperationException or FormatException or System.ComponentModel.Win32Exception)
        {
            // A check by itself that fails (no network) is only written down; one asked for, or a yes, is told.
            Log.Write($"Updates: {failed.Message}");
            if (!asked && !answered)
            {
                return;
            }

            MessageBox.Show(Texts.Get(Resource.String.update_failed, failed.Message), Texts.Get(Resource.String.update_title),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            checking = false;
        }
    }
}
