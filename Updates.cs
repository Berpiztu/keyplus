using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Whether a newer Berpiztu AI Keyboard is published: GitHub's latest release
/// of the repository, its tag (v1.0.48) against the version running, and the
/// file of it each product installs from (the release workflow's names).
/// Asked by itself at most once a day (Información › Buscar actualizaciones
/// automáticamente), or when Buscar ahora asks; nothing is installed without
/// the user's yes. Shared by Android and Windows.
/// </summary>
public static class Updates
{
    public const string WindowsInstaller = "Berpiztu-AI-Keyboard-Windows.msi";
    public const string KeyboardApk = "Berpiztu-AI-Keyboard.apk";
    public const string PopupApk = "KeyPlus-Popup.apk";

    private const string LatestRelease = "https://api.github.com/repos/Berpiztu/keyplus/releases/latest";
    private static readonly TimeSpan Every = TimeSpan.FromDays(1);

    /// <summary>A release: its version, its page, and the address of the file asked for (null when it has none).</summary>
    public sealed record Release(Version Version, string Page, string? Download);

    /// <summary>Whether a day has gone since the last check, or there was none.</summary>
    public static bool Due(DateTime? checkedAt) => checkedAt is not { } at || DateTime.UtcNow - at >= Every;

    /// <summary>
    /// Whether the release is above the version running: a build made here
    /// counts a fourth part (1.0.48.3), above its own release and under the
    /// next one.
    /// </summary>
    public static bool IsNewer(Release latest, string running) =>
        Version.TryParse(running, out var current) && latest.Version > current;

    /// <summary>GitHub's latest release, with the address of <paramref name="file"/> in it.</summary>
    public static async Task<Release> LatestAsync(string file, CancellationToken cancel = default)
    {
        using var client = Client();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var answer = JsonDocument.Parse(await client.GetStringAsync(LatestRelease, cancel));
        var release = answer.RootElement;
        var download = release.GetProperty("assets").EnumerateArray()
            .Where(asset => asset.GetProperty("name").GetString() == file)
            .Select(asset => asset.GetProperty("browser_download_url").GetString())
            .FirstOrDefault();
        return new Release(Version.Parse(release.GetProperty("tag_name").GetString()!.TrimStart('v')), release.GetProperty("html_url").GetString()!, download);
    }

    /// <summary>The release's file downloaded into <paramref name="folder"/>, named with its version (1.0.49-Berpiztu-AI-Keyboard.apk); one there before is replaced.</summary>
    public static async Task<string> DownloadAsync(Release release, string folder, CancellationToken cancel = default)
    {
        var address = release.Download ?? throw new InvalidOperationException($"The release {release.Version} has no file to install.");
        var path = Path.Combine(folder, $"{release.Version}-{Path.GetFileName(new Uri(address).LocalPath)}");
        using var client = Client();
        await using (var source = await client.GetStreamAsync(address, cancel))
        await using (var target = File.Create(path))
        {
            await source.CopyToAsync(target, cancel);
        }

        return path;
    }

    /// <summary>GitHub's API answers only a request that says who asks.</summary>
    private static HttpClient Client()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BerpiztuKeyboard", "1"));
        return client;
    }
}
