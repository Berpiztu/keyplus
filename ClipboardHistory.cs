using System.Text.Json;
using Android.Content;

namespace Berpiztu.KeyPlus;

/// <summary>
/// What was copied lately, which the title bar's 📋 or the row above the
/// keys lists to paste again: the pinned ones first, never dropped, then the
/// last <see cref="Most"/> others, the newest first, each once, kept across
/// restarts. A text, or an image, which is kept as a file of its own, for the
/// app that copied it may not give it again. Berpiztu AI Keyboard keeps what
/// is copied in any app, for the keyboard in use may read the clipboard; the
/// popup what its Ctrl+C and Ctrl+X copy, texts alone, for Android keeps the
/// clipboard from an accessibility service.
/// </summary>
public sealed class ClipboardHistory(Context context)
{
    /// <summary>How many are kept besides the pinned ones: the oldest goes when one more comes.</summary>
    public const int Most = 20;

    private const string EntriesKey = "clipboardEntries";

    /// <summary>A text copied, or an image: its file in <see cref="ImagesDirectory"/> and its type (image/png); pinned, never dropped.</summary>
    public sealed record Entry(string? Text = null, string? Image = null, string? MimeType = null, bool Pinned = false)
    {
        public bool IsImage => Image is not null;
    }

    private readonly ISharedPreferences preferences = KeyPlusPreferences.Global(context);

    /// <summary>Where the images copied are kept.</summary>
    public static string ImagesDirectory(Context context) => Path.Combine(context.FilesDir!.AbsolutePath, "clipboard");

    /// <summary>Something came, went or changed.</summary>
    public event Action? Changed;

    /// <summary>What was copied, the pinned ones first, then the newest first.</summary>
    public IReadOnlyList<Entry> Entries
    {
        get
        {
            var saved = preferences.GetString(EntriesKey, null);
            return string.IsNullOrEmpty(saved) ? [] : JsonSerializer.Deserialize<List<Entry>>(saved) ?? [];
        }
    }

    /// <summary>The path of an image's file.</summary>
    public string PathOf(Entry image) => Path.Combine(ImagesDirectory(context), image.Image!);

    /// <summary>A text copied: first in the list, taken from where it was if it was already there, pinned as it was.</summary>
    public void Add(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var pinned = Entries.Any(kept => kept.Text == text && kept.Pinned);
        Save([new Entry(text, Pinned: pinned), .. Entries.Where(kept => kept.Text != text)]);
    }

    /// <summary>An image copied, read from its stream into a file of its own: first in the list.</summary>
    public void AddImage(Stream image, string mimeType)
    {
        Directory.CreateDirectory(ImagesDirectory(context));
        var extension = mimeType.Split('/').Last().Split('+').First();
        var name = $"{Guid.NewGuid():N}.{extension}";
        using (var file = File.Create(Path.Combine(ImagesDirectory(context), name)))
        {
            image.CopyTo(file);
        }

        Save([new Entry(Image: name, MimeType: mimeType), .. Entries]);
    }

    /// <summary>Pinned, never dropped and first in the list, or not.</summary>
    public void SetPinned(Entry entry, bool pinned) => Save([.. Entries.Select(kept => kept == entry ? kept with { Pinned = pinned } : kept)]);

    /// <summary>A text edited, where it stands; emptied, taken out of the list.</summary>
    public void Replace(Entry entry, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Remove(entry);
            return;
        }

        Save([.. Entries.Select(kept => kept == entry ? kept with { Text = text } : kept)]);
    }

    /// <summary>
    /// An image's file written again with another (an edit an editor saved as a copy), its name kept, so it stays
    /// where it stands, pinned as it was, the same thing to the keyboard; its type the new one's.
    /// </summary>
    public void ReplaceImage(Entry entry, Stream image, string mimeType)
    {
        using (var file = File.Create(PathOf(entry)))
        {
            image.CopyTo(file);
        }

        Save([.. Entries.Select(kept => kept.Image == entry.Image ? kept with { MimeType = mimeType } : kept)]);
    }

    /// <summary>Something taken out of the list, an image's file with it.</summary>
    public void Remove(Entry entry) => Save([.. Entries.Where(kept => kept != entry)]);

    /// <summary>
    /// Keeps the pinned ones first and the newest <see cref="Most"/> others,
    /// and deletes the files of images no longer kept.
    /// </summary>
    private void Save(List<Entry> entries)
    {
        List<Entry> kept = [.. entries.Where(entry => entry.Pinned), .. entries.Where(entry => !entry.Pinned).Take(Most)];
        preferences.Edit()!.PutString(EntriesKey, JsonSerializer.Serialize(kept))!.Apply();
        var images = kept.Where(entry => entry.IsImage).Select(entry => entry.Image).ToHashSet();
        if (Directory.Exists(ImagesDirectory(context)))
        {
            foreach (var file in Directory.GetFiles(ImagesDirectory(context)).Where(file => !images.Contains(Path.GetFileName(file))))
            {
                File.Delete(file);
            }
        }

        Changed?.Invoke();
    }
}
