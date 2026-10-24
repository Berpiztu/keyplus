using Android.App;
using Android.Content;
using Android.Content.PM;
using System.Text.Json;

[assembly: UsesPermission(Android.Manifest.Permission.RequestInstallPackages)]

namespace Berpiztu.KeyPlus;

/// <summary>
/// The Android side of <see cref="Updates"/>, both products: the keyboard (or
/// the popup's service) asks once a day while Buscar actualizaciones
/// automáticamente is on, and a newer release is a notification that opens
/// the app; the app asks whether to update (also when it opens, and from
/// Buscar ahora), and a yes downloads the product's APK and hands it to
/// Android's installer, which asks the user too (and, the first time, to
/// allow installing from this app).
/// </summary>
public static class AndroidUpdates
{
    /// <summary>The app opened from the notification: it asks whether to update.</summary>
    public const string AskExtra = "askUpdate";

    /// <summary>The app opened by Android's installer with how the install went (PackageInstaller.ExtraStatus).</summary>
    public const string InstallExtra = "installStatus";

    private const string Channel = "updates";
    private const int NotificationId = 7;

#if KEYBOARD
    private const string File = Updates.KeyboardApk;
#else
    private const string File = Updates.PopupApk;
#endif

    private static bool checking;

    /// <summary>The version installed, as Información shows it.</summary>
    public static string Running(Context context) => context.PackageManager!.GetPackageInfo(context.PackageName!, 0)!.VersionName ?? "";

    /// <summary>Buscar actualizaciones automáticamente on, and a day gone since GitHub was last asked.</summary>
    public static bool Due(Context context) =>
        KeyPlusPreferences.Global(context).GetBoolean(KeyPlusPreferences.UpdatesAutomaticKey, true) && Updates.Due(Checked(context));

    /// <summary>From the keyboard or the service: GitHub asked when it is due, a newer release told in a notification; a failure only in the log.</summary>
    public static async void CheckInBackground(Context context)
    {
        if (checking || !Due(context))
        {
            return;
        }

        checking = true;
        try
        {
            var release = await Updates.LatestAsync(File);
            KeepChecked(context);
            if (Updates.IsNewer(release, Running(context)))
            {
                Android.Util.Log.Info("KeyPlus", $"Updates: {release.Version} is out, {Running(context)} installed");
                Notify(context, release);
            }
        }
        catch (Exception failed) when (Failed(failed))
        {
            Android.Util.Log.Warn("KeyPlus", $"Updates: {failed.Message}");
        }
        finally
        {
            checking = false;
        }
    }

    /// <summary>From the app: GitHub asked, and a newer release offered in a dialog; asked by hand, an answer whatever it is.</summary>
    public static async Task CheckAsync(Activity activity, bool asked)
    {
        if (checking)
        {
            return;
        }

        checking = true;
        try
        {
            var release = await Updates.LatestAsync(File);
            KeepChecked(activity);
            if (!Updates.IsNewer(release, Running(activity)))
            {
                if (asked)
                {
                    Tell(activity, Say(activity, Resource.String.update_current, Running(activity)));
                }

                return;
            }

            new AlertDialog.Builder(activity)
                .SetTitle(Resource.String.update_title)!
                .SetMessage(Say(activity, Resource.String.update_ask, release.Version.ToString(), Running(activity)))!
                .SetPositiveButton(Resource.String.update_yes, async (_, _) => await InstallAsync(activity, release))!
                .SetNegativeButton(Resource.String.update_no, (_, _) => { })!
                .Show();
        }
        catch (Exception failed) when (Failed(failed))
        {
            Android.Util.Log.Warn("KeyPlus", $"Updates: {failed.Message}");
            if (asked)
            {
                Tell(activity, Say(activity, Resource.String.update_failed, failed.Message));
            }
        }
        finally
        {
            checking = false;
        }
    }

    /// <summary>
    /// The installer's word on the install, brought back to the app: it needs
    /// the user's yes (Android's own dialog, started here), or it failed.
    /// </summary>
    public static void Installed(Activity activity, Intent intent)
    {
        var status = (PackageInstallStatus)intent.GetIntExtra(PackageInstaller.ExtraStatus, (int)PackageInstallStatus.Failure);
        if (status == PackageInstallStatus.PendingUserAction && intent.GetParcelableExtra(Intent.ExtraIntent, Java.Lang.Class.FromType(typeof(Intent))) is Intent confirm)
        {
            activity.StartActivity(confirm.AddFlags(ActivityFlags.NewTask));
        }
        else if (status != PackageInstallStatus.Success)
        {
            var message = intent.GetStringExtra(PackageInstaller.ExtraStatusMessage) ?? status.ToString();
            Android.Util.Log.Warn("KeyPlus", $"Updates: the install failed: {message}");
            Tell(activity, Say(activity, Resource.String.update_failed, message));
        }
    }

    /// <summary>The release's APK downloaded into the app's cache and written into an install session, which Android then asks the user to confirm.</summary>
    private static async Task InstallAsync(Activity activity, Updates.Release release)
    {
        try
        {
            var apk = await Updates.DownloadAsync(release, activity.CacheDir!.AbsolutePath);
            var installer = activity.PackageManager!.PackageInstaller;
            var id = installer.CreateSession(new PackageInstaller.SessionParams(PackageInstallMode.FullInstall));
            using var session = installer.OpenSession(id);
            await using (var source = System.IO.File.OpenRead(apk))
            using (var target = session.OpenWrite(File, 0, source.Length))
            {
                await source.CopyToAsync(target);
                session.Fsync(target);
            }

            System.IO.File.Delete(apk);
            var told = new Intent(activity, typeof(MainActivity)).PutExtra(InstallExtra, true).AddFlags(ActivityFlags.SingleTop);
            // Mutable: the installer adds its status to it.
            var status = PendingIntent.GetActivity(activity, id, told, PendingIntentFlags.Mutable | PendingIntentFlags.UpdateCurrent)!;
            session.Commit(status.IntentSender!);
            Android.Util.Log.Info("KeyPlus", $"Updates: {release.Version} handed to Android's installer");
        }
        catch (Exception failed) when (Failed(failed) || failed is Java.Lang.Exception)
        {
            Android.Util.Log.Warn("KeyPlus", $"Updates: {failed.Message}");
            Tell(activity, Say(activity, Resource.String.update_failed, failed.Message));
        }
    }

    private static void Notify(Context context, Updates.Release release)
    {
        var notifications = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        notifications.CreateNotificationChannel(new NotificationChannel(Channel, context.GetString(Resource.String.update_title), NotificationImportance.Default));
        var open = new Intent(context, typeof(MainActivity)).PutExtra(AskExtra, true).AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
        var text = Say(context, Resource.String.update_ask, release.Version.ToString(), Running(context));
        notifications.Notify(NotificationId, new Notification.Builder(context, Channel)
            .SetSmallIcon(Resource.Drawable.ic_keyplus)!
            .SetContentTitle(context.GetString(Resource.String.update_title))!
            .SetContentText(text)!
            .SetStyle(new Notification.BigTextStyle().BigText(text))!
            .SetContentIntent(PendingIntent.GetActivity(context, NotificationId, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent))!
            .SetAutoCancel(true)!
            .Build()!);
    }

    /// <summary>A text with its %1$s and on filled in.</summary>
    private static string Say(Context context, int text, params string[] values) =>
        context.GetString(text, [.. values.Select(value => new Java.Lang.String(value))]);

    private static void Tell(Activity activity, string message) =>
        new AlertDialog.Builder(activity).SetTitle(Resource.String.update_title)!.SetMessage(message)!.SetPositiveButton(Android.Resource.String.Ok, (_, _) => { })!.Show();

    private static DateTime? Checked(Context context) =>
        KeyPlusPreferences.Global(context).GetLong(KeyPlusPreferences.UpdatesCheckedKey, 0) is > 0 and var at ? DateTimeOffset.FromUnixTimeMilliseconds(at).UtcDateTime : null;

    private static void KeepChecked(Context context) =>
        KeyPlusPreferences.Global(context).Edit()!.PutLong(KeyPlusPreferences.UpdatesCheckedKey, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())!.Apply();

    private static bool Failed(Exception failed) => failed is HttpRequestException or TaskCanceledException or IOException or JsonException
        or KeyNotFoundException or InvalidOperationException or FormatException;
}
