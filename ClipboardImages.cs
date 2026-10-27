using Android.App;
using Android.Content;
using Android.Database;
using Android.OS;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Hands the images Berpiztu AI Keyboard keeps (<see cref="ClipboardHistory"/>)
/// to the app they are pasted into: content://ai.berpiztu.keyboard.clipboard/name,
/// readable only by an app the keyboard grants it to as it pastes, and
/// writable by the image editor it is opened in (Edit), which saves over it.
/// </summary>
[ContentProvider([Authority], Name = "ai.berpiztu.keyboard.ClipboardImages", Exported = false, GrantUriPermissions = true)]
public sealed class ClipboardImages : ContentProvider
{
    public const string Authority = "ai.berpiztu.keyboard.clipboard";

    /// <summary>The address of an image kept, by its file's name.</summary>
    public static Android.Net.Uri UriOf(string name) => Android.Net.Uri.Parse($"content://{Authority}/{name}")!;

    public override bool OnCreate() => true;

    public override ParcelFileDescriptor? OpenFile(Android.Net.Uri uri, string mode)
    {
        // Only the name: nothing outside the images' folder.
        var file = new Java.IO.File(ClipboardHistory.ImagesDirectory(Context!), Path.GetFileName(uri.LastPathSegment ?? ""));
        if (!file.Exists())
        {
            throw new Java.IO.FileNotFoundException(uri.ToString());
        }

        // As the app granted it asks: read to paste, write as an editor saves over it (granted only to the editor).
        return ParcelFileDescriptor.Open(file, ParcelFileDescriptor.ParseMode(mode));
    }

    public override string? GetType(Android.Net.Uri uri) =>
        Path.GetExtension(uri.LastPathSegment ?? "").TrimStart('.') is { Length: > 0 } extension ? $"image/{extension}" : null;

    public override ICursor? Query(Android.Net.Uri uri, string[]? projection, string? selection, string[]? selectionArgs, string? sortOrder) => null;

    public override Android.Net.Uri? Insert(Android.Net.Uri uri, ContentValues? values) => null;

    public override int Delete(Android.Net.Uri uri, string? selection, string[]? selectionArgs) => 0;

    public override int Update(Android.Net.Uri uri, ContentValues? values, string? selection, string[]? selectionArgs) => 0;
}
