using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Brings each new screenshot into the copied list (<see cref="ClipboardHistory"/>),
/// as a keyboard does: Android puts no screenshot on the clipboard, it only
/// saves it among the photos, so the keyboard watches them. Only with the
/// right to read the photos (READ_MEDIA_IMAGES), which the app asks for; and
/// only screenshots taken after it first looked, so none of the old ones fill
/// the list.
/// </summary>
public sealed class ScreenshotWatcher(Context context, ClipboardHistory clipboard) : ContentObserver(new Handler(Looper.MainLooper!))
{
    /// <summary>The right to read the photos, the screenshots among them.</summary>
    public const string Permission = "android.permission.READ_MEDIA_IMAGES";

    /// <summary>The newest screenshot already seen, by its id among the photos.</summary>
    private const string LastSeenKey = "lastScreenshot";

    private readonly ISharedPreferences preferences = KeyPlusPreferences.Global(context);

    private bool watching;

    /// <summary>Whether the user let the keyboard read the photos.</summary>
    public static bool Allowed(Context context) => context.CheckSelfPermission(Permission) == Android.Content.PM.Permission.Granted;

    /// <summary>
    /// Starts watching the photos, once the user allows it, and takes the
    /// screenshots taken since it last looked; called again each time the
    /// keyboard opens, for the right may have come since.
    /// </summary>
    public void Start()
    {
        if (watching || !Allowed(context))
        {
            return;
        }

        context.ContentResolver!.RegisterContentObserver(MediaStore.Images.Media.ExternalContentUri!, true, this);
        watching = true;
        TakeNew();
    }

    public void Stop()
    {
        if (watching)
        {
            context.ContentResolver!.UnregisterContentObserver(this);
            watching = false;
        }
    }

    /// <summary>A photo came or changed: the new screenshots, if any.</summary>
    public override void OnChange(bool selfChange) => TakeNew();

    /// <summary>
    /// The screenshots newer than the last one seen, the oldest first, so the
    /// newest stands first in the list, each noted as seen once taken; the
    /// first time, none, only the newest's id noted.
    /// </summary>
    private void TakeNew()
    {
        var lastSeen = preferences.GetLong(LastSeenKey, -1);
        using var found = context.ContentResolver!.Query(
            MediaStore.Images.Media.ExternalContentUri!,
            [IBaseColumns.Id, MediaStore.IMediaColumns.MimeType],
            $"{MediaStore.IMediaColumns.RelativePath} LIKE ? AND {IBaseColumns.Id} > ?",
            ["%Screenshots%", $"{lastSeen}"],
            $"{IBaseColumns.Id} ASC");
        if (found is null)
        {
            return;
        }

        var first = lastSeen < 0;
        var seen = Math.Max(lastSeen, 0);
        while (found.MoveToNext())
        {
            var id = found.GetLong(0);
            if (!first)
            {
                try
                {
                    using var image = context.ContentResolver.OpenInputStream(ContentUris.WithAppendedId(MediaStore.Images.Media.ExternalContentUri!, id))!;
                    clipboard.AddImage(image, found.GetString(1) ?? "image/png");
                }
                catch (Exception unreadable) when (unreadable is Java.Lang.SecurityException or Java.IO.IOException or IOException)
                {
                    // A screenshot still being written is read again at its next change.
                    Log.Write($"Screenshot {id} could not be read yet: {unreadable.Message}");
                    break;
                }
            }

            seen = id;
        }

        preferences.Edit()!.PutLong(LastSeenKey, seen)!.Apply();
    }
}
