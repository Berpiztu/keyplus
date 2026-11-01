using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The context models Berpiztu publishes on Hugging Face (berpiztu/keyboard-correctors, mT5's under Apache 2.0, and
/// berpiztu/keyboard-correctors-gemma, T5Gemma 2's under the Gemma Terms of Use; made by llm-create's huggingface/),
/// read live from each repository's correctors.json, the owner's choice (2026-11-01), so a corrector published there
/// is offered without a new keyboard: each corrector's name, licence, size and files with their SHA-256, its licence's
/// files with them, fetched into a folder of its name as <see cref="ContextCorrector"/> reads them.
/// </summary>
public static class CorrectorCatalog
{
    private const string Hub = "https://huggingface.co/berpiztu/";
    private static readonly string[] Repositories = ["keyboard-correctors-gemma", "keyboard-correctors"];

    /// <summary>A corrector offered: its name, its weights' bits (8, 32), its licence (gemma, apache-2.0), its own files' size and its files, its licence's (a few KB) among them.</summary>
    public sealed record Offered(string Name, int Bits, string License, long Bytes, IReadOnlyList<Downloads.Item> Files)
    {
        /// <summary>Under the Gemma Terms of Use: its notice and use restrictions said before it is downloaded.</summary>
        public bool Gemma => License == "gemma";
    }

    /// <summary>The correctors offered, as the repositories list them now; none where they cannot be read, said in the log.</summary>
    public static async Task<IReadOnlyList<Offered>> ReadAsync(CancellationToken cancel = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BerpiztuKeyboard/1");
            var offered = new List<Offered>();
            foreach (var repository in Repositories)
            {
                var root = $"{Hub}{repository}/resolve/main/";
                using var listed = JsonDocument.Parse(await client.GetStringAsync(root + "correctors.json", cancel));
                Downloads.Item[] licence = listed.RootElement.TryGetProperty("licence_files", out var notices) ? [.. Items(root, notices)] : [];
                foreach (var corrector in listed.RootElement.GetProperty("correctors").EnumerateArray())
                {
                    var files = Items(root, corrector.GetProperty("files")).ToList();
                    offered.Add(new(corrector.GetProperty("name").GetString()!, corrector.GetProperty("bits").GetInt32(), corrector.GetProperty("license").GetString()!,
                        corrector.GetProperty("bytes").GetInt64(), [.. files, .. licence]));
                }
            }

            return offered;
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Log.Write($"Corrector: the correctors on Hugging Face could not be read: {failure.Message}");
            return [];
        }
    }

    /// <summary>Each file listed (its path in the repository, its SHA-256), to be fetched by its own name into a corrector's folder.</summary>
    private static IEnumerable<Downloads.Item> Items(string root, JsonElement files) =>
        files.EnumerateArray().Select(file => file.GetProperty("path").GetString()!)
            .Zip(files.EnumerateArray().Select(file => file.GetProperty("sha256").GetString()!))
            .Select(pair => new Downloads.Item(root + pair.First, Path.GetFileName(pair.First), pair.Second));
}
