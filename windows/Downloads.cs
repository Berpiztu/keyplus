using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace Berpiztu.KeyPlus;

/// <summary>
/// What Spelling fetches once instead of the keyboard carrying it (the accent
/// model, the dictionary): each file from where it is published at a fixed
/// revision, written beside its place and moved there only once its SHA-256
/// is the one expected, so a file broken or cut short never takes the place
/// of one that works. Fetched again whole, it replaces what was there.
/// </summary>
public static class Downloads
{
    /// <summary>A file to fetch: where from, its name in the folder, its SHA-256.</summary>
    public sealed record Item(string Url, string Name, string Sha256);

    /// <summary>Whether all the files are in the folder.</summary>
    public static bool Present(string folder, IEnumerable<Item> files) => files.All(file => File.Exists(Path.Combine(folder, file.Name)));

    /// <summary>When the files were last written, the newest: a keyboard holding them loaded reads them again once it changes.</summary>
    public static DateTime Stamp(string folder, IEnumerable<Item> files) =>
        files.Select(file => File.GetLastWriteTimeUtc(Path.Combine(folder, file.Name))).Max();

    /// <summary>The files fetched into the folder in turn; how much of all is done, told as it goes.</summary>
    public static async Task FilesAsync(string folder, IReadOnlyList<Item> files, IProgress<(long Done, long Total)> progress, CancellationToken cancel = default)
    {
        Directory.CreateDirectory(folder);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BerpiztuKeyboard/1");
        long done = 0, total = 0;
        foreach (var file in files)
        {
            var place = Path.Combine(folder, file.Name);
            var part = place + ".part";
            using var answer = await client.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancel);
            answer.EnsureSuccessStatusCode();
            total += answer.Content.Headers.ContentLength ?? 0;
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await using (var source = await answer.Content.ReadAsStreamAsync(cancel))
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

            File.Move(part, place, overwrite: true);
        }
    }
}
