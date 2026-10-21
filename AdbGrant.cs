using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Icon = Android.Graphics.Drawables.Icon;

[assembly: UsesPermission(Android.Manifest.Permission.ForegroundService)]
[assembly: UsesPermission(Android.Manifest.Permission.PostNotifications)]
[assembly: UsesPermission(Android.Manifest.Permission.Internet)]

namespace Berpiztu.KeyPlus;

/// <summary>
/// The ADB commands that let the app turn itself on (<see cref="Commands"/>),
/// run by the app itself over the device's own Wireless debugging, as an ADB
/// app on the device would run them (AdbShell.java, libadb-android): it waits
/// for Settings' "Pair device with pairing code", takes the code in its
/// notification, since leaving Settings would close that dialog and end the
/// pairing, pairs, runs the commands as the shell user and turns the app on.
/// A short foreground service: Android gives it three minutes.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeShortService)]
public sealed class AdbGrant : Service
{
    private const string StartAction = "ai.berpiztu.keyplus.adb.START";
    private const string CodeAction = "ai.berpiztu.keyplus.adb.CODE";
    private const string CodeKey = "code";
    private const string Channel = "wireless-debugging";
    private const int NotificationId = 1;

    /// <summary>How long Settings' pairing dialog, then its code, are waited for: within the three minutes of a short service.</summary>
    private static readonly TimeSpan PairingWait = TimeSpan.FromMinutes(2);

    /// <summary>The Java side (AdbShell.java), reached through JNI: it is not bound to C#.</summary>
    private static readonly IntPtr Shell = JNIEnv.FindClass("ai/berpiztu/keyplus/AdbShell");

    private TaskCompletionSource<string>? code;
    private bool running;

    /// <summary>The pairing started, from the app's screen, which then opens Wireless debugging.</summary>
    public static void Start(Context context) =>
        context.StartForegroundService(new Intent(context, typeof(AdbGrant)).SetAction(StartAction));

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        switch (intent?.Action)
        {
            case StartAction:
                // Every start asks for the foreground again, or Android stops the app.
                StartForeground(NotificationId, Searching());
                if (!running)
                {
                    running = true;
                    Task.Run(Grant);
                }

                break;
            case CodeAction:
                var typed = RemoteInput.GetResultsFromIntent(intent)?.GetCharSequence(CodeKey)?.ToString();
                if (!string.IsNullOrWhiteSpace(typed))
                {
                    code?.TrySetResult(typed.Trim());
                }

                break;
        }

        return StartCommandResult.NotSticky;
    }

    /// <summary>Android's three minutes are over: the service ends at once, saying so.</summary>
    public override void OnTimeout(int startId) =>
        Finish("Wireless debugging took too long. Try again, and type the code as soon as Settings shows it.", done: false);

    /// <summary>The pairing dialog found, its code taken, the commands run and the app turned on; or what failed.</summary>
    private async Task Grant()
    {
        try
        {
            var port = FindPairingPort();
            if (port == 0)
            {
                Finish("No pairing code was shown. With Wi-Fi on, open Wireless debugging, tap Pair device with pairing code, and try again.", done: false);
                return;
            }

            code = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Notify(AskCode());
            var typed = await code.Task.WaitAsync(PairingWait);
            Notify(Working());

            var output = Run(port, typed, Commands(this));
            if (CheckSelfPermission(MainActivity.WriteSecureSettings) != Permission.Granted)
            {
                Finish($"Wireless debugging did not grant it. {output}".Trim(), done: false);
                return;
            }

#if !KEYBOARD
            MainActivity.TurnOnService(this);
#endif
            Finish($"{Label} is on.", done: true);
        }
        catch (TimeoutException)
        {
            Finish("The pairing code did not come in time. Try again.", done: false);
        }
        catch (Exception failure)
        {
            Finish($"Pairing failed: {failure.Message}", done: false);
        }
    }

    /// <summary>
    /// The shell commands that let the app turn itself on later, as a
    /// computer's adb or an ADB app on the device would run them.
    /// </summary>
    private static string[] Commands(Context context)
    {
        var package = context.PackageName!;
#if KEYBOARD
        // Turned on, made the keyboard in use, and the secure settings granted
        // so the app does it by itself later (MainActivity.UseKeyboard).
        var keyboard = MainActivity.KeyboardId(context);
        return [$"ime enable {keyboard}", $"ime set {keyboard}", $"pm grant {package} {MainActivity.WriteSecureSettings}"];
#else
        // Restricted settings allowed (Android 13 and later keep an app
        // installed outside a store from being an accessibility service until
        // they are), and the secure settings granted.
        return [$"appops set {package} ACCESS_RESTRICTED_SETTINGS allow", $"pm grant {package} {MainActivity.WriteSecureSettings}"];
#endif
    }

    /// <summary>The port Settings' pairing dialog listens on, or 0 when it was not opened in time.</summary>
    private int FindPairingPort()
    {
        var method = JNIEnv.GetStaticMethodID(Shell, "findPairingPort", "(Landroid/content/Context;J)I");
        return JNIEnv.CallStaticIntMethod(Shell, method, new JValue(this), new JValue((long)PairingWait.TotalMilliseconds));
    }

    /// <summary>Paired by the code typed, the commands run as the shell user: their output.</summary>
    private string Run(int port, string typed, string[] commands)
    {
        var (key, certificate) = NewKey();
        var method = JNIEnv.GetStaticMethodID(Shell, "run",
            "(Landroid/content/Context;ILjava/lang/String;[B[B[Ljava/lang/String;)Ljava/lang/String;");
        var javaCode = JNIEnv.NewString(typed);
        var javaKey = JNIEnv.NewArray(key);
        var javaCertificate = JNIEnv.NewArray(certificate);
        var javaCommands = JNIEnv.NewArray(commands);
        try
        {
            var output = JNIEnv.CallStaticObjectMethod(Shell, method, new JValue(this), new JValue(port),
                new JValue(javaCode), new JValue(javaKey), new JValue(javaCertificate), new JValue(javaCommands));
            return JNIEnv.GetString(output, JniHandleOwnership.TransferLocalRef) ?? "";
        }
        finally
        {
            JNIEnv.DeleteLocalRef(javaCode);
            JNIEnv.DeleteLocalRef(javaKey);
            JNIEnv.DeleteLocalRef(javaCertificate);
            JNIEnv.DeleteLocalRef(javaCommands);
        }
    }

    /// <summary>
    /// The key this pairing is made with, and its certificate, which the
    /// device keeps among its paired ones: made for this one time and never
    /// stored, so the app keeps no way back into the shell.
    /// </summary>
    private static (byte[] Key, byte[] Certificate) NewKey()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=KeyPlus", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return (rsa.ExportPkcs8PrivateKey(), certificate.RawData);
    }

    /// <summary>The last word, kept in the notification shade, and the service ended.</summary>
    private void Finish(string message, bool done)
    {
        running = false;
        StopForeground(StopForegroundFlags.Detach);
        Notify(Builder(done ? $"{Label} is on" : "Wireless debugging", message)
            .SetContentIntent(PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable))
            .SetAutoCancel(true)
            .Build()!);
        StopSelf();
    }

    private Notification Searching() =>
        Builder("Pair with Wireless debugging", "In Wireless debugging, tap Pair device with pairing code.")
            .SetOngoing(true)
            .Build()!;

    /// <summary>A field for the code in the notification, typed with Settings' dialog still open behind the shade.</summary>
    private Notification AskCode()
    {
        var reply = PendingIntent.GetService(this, 0, new Intent(this, typeof(AdbGrant)).SetAction(CodeAction),
            PendingIntentFlags.Mutable | PendingIntentFlags.UpdateCurrent)!;
        var field = new RemoteInput.Builder(CodeKey).SetLabel("Pairing code").Build()!;
        var action = new Notification.Action.Builder(Icon.CreateWithResource(this, Resource.Drawable.ic_keyplus), "Type the code", reply)
            .AddRemoteInput(field)
            .Build()!;
        return Builder("Type the pairing code", "Pull down the shade and type the six digits Settings shows, without leaving it.")
            .SetOngoing(true)
            .AddAction(action)
            .Build()!;
    }

    private Notification Working() =>
        Builder("Pairing", $"Turning {Label} on…")
            .SetOngoing(true)
            .Build()!;

    private Notification.Builder Builder(string title, string text)
    {
        var notifications = (NotificationManager)GetSystemService(NotificationService)!;
        notifications.CreateNotificationChannel(new NotificationChannel(Channel, "Wireless debugging", NotificationImportance.High));
        return new Notification.Builder(this, Channel)
            .SetSmallIcon(Resource.Drawable.ic_keyplus)!
            .SetContentTitle(title)!
            .SetContentText(text)!
            .SetStyle(new Notification.BigTextStyle().BigText(text))!;
    }

    private void Notify(Notification notification) =>
        ((NotificationManager)GetSystemService(NotificationService)!).Notify(NotificationId, notification);

    /// <summary>The app's name, as its launcher shows it.</summary>
    private string Label => ApplicationInfo!.LoadLabel(PackageManager!);
}
