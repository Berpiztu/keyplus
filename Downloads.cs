using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace Berpiztu.KeyPlus;

/// <summary>
/// What the keyboard fetches once instead of carrying it (Spelling's accent
/// model and dictionaries, glide typing's model and word lists): each file
/// from where it is published at a fixed
/// revision, written beside its place and moved there only once its SHA-256
/// is the one expected, so a file broken or cut short never takes the place
/// of one that works. Fetched again whole, it replaces what was there.
/// </summary>
public static class Downloads
{
    /// <summary>A file to fetch: where from, its name in the folder, its SHA-256; sent as base64 (as AOSP's source browser sends a file), decoded as it comes.</summary>
    public sealed record Item(string Url, string Name, string Sha256, bool Base64 = false);

    /// <summary>Whether all the files are in the folder.</summary>
    public static bool Present(string folder, IEnumerable<Item> files) => files.All(file => File.Exists(Path.Combine(folder, file.Name)));

    /// <summary>When the files were last written, the newest: a keyboard holding them loaded reads them again once it changes.</summary>
    public static DateTime Stamp(string folder, IEnumerable<Item> files) =>
        files.Select(file => File.GetLastWriteTimeUtc(Path.Combine(folder, file.Name))).Max();

    /// <summary>The files fetched into the folder in turn; how much of all is done, told as it goes.</summary>
    public static Task FilesAsync(string folder, IReadOnlyList<Item> files, IProgress<(long Done, long Total)> progress, CancellationToken cancel = default) =>
        FilesAsync([.. files.Select(file => (folder, file))], progress, cancel);

    /// <summary>Files fetched each into its own folder, in turn, as one download (a dictionary and a word list); how much of all is done, told as it goes.</summary>
    public static async Task FilesAsync(IReadOnlyList<(string Folder, Item File)> files, IProgress<(long Done, long Total)> progress, CancellationToken cancel = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BerpiztuKeyboard/1");
        long done = 0, total = 0;
        foreach (var (folder, file) in files)
        {
            Directory.CreateDirectory(folder);
            var place = Path.Combine(folder, file.Name);
            var part = place + ".part";
            using var answer = await client.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancel);
            answer.EnsureSuccessStatusCode();
            total += (answer.Content.Headers.ContentLength ?? 0) * (file.Base64 ? 3 : 4) / 4;
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var sent = await answer.Content.ReadAsStreamAsync(cancel);
                await using (var source = file.Base64 ? new CryptoStream(sent, new FromBase64Transform(FromBase64TransformMode.IgnoreWhiteSpaces), CryptoStreamMode.Read) : sent)
                await using (var target = File.Create(part))
                {
                    var buffer = new byte[1 << 16];
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancel)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), cancel);
                        hash.AppendData(buffer, 0, read);
                        done += read;
                        progress.Report((done, total));
                    }
                }

                if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(part);
                    throw new InvalidDataException($"{file.Name} is not the file expected (its SHA-256 differs)");
                }
            }

            // Fetched again, the very file already here is left as it is: a keyboard holding it open (Glide typing's
            // model, loaded) cannot have it replaced, and Windows refused the move, which closed the settings.
            if (File.Exists(place) && Sha256Of(place).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(part);
                continue;
            }

            try
            {
                File.Move(part, place, overwrite: true);
            }
            catch (UnauthorizedAccessException inUse)
            {
                File.Delete(part);
                throw new IOException($"{file.Name} is in use by the keyboard and could not be replaced; it can be once the keyboard is closed", inUse);
            }
        }
    }

    private static string Sha256Of(string path)
    {
        // Read alongside whoever holds it open, the keyboard included.
        using var read = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(read));
    }
}
