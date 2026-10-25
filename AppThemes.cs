using System.Text.Json;
using Android.Content;
using Android.Content.PM;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Personalizado's Por app on Android, as the Windows app's apps.json: the
/// applications typed in, by their package, each with its colours
/// (<see cref="AppColor"/>), kept for every screen; the keyboard adds one the
/// first time it types in it, with the background its theme declares, for a
/// keyboard alone cannot see another application's window, made light or
/// dark as the device is; with the keyboard's accessibility service on, the
/// colour seen on the screen beside the field (MenuWatchService) instead.
/// What De la app takes.
/// </summary>
public static class AppThemes
{
    private const string Key = "appColors";

    /// <summary>Whether a preference changed is the applications' colours.</summary>
    public static bool IsList(string? key) => key == Key;

    /// <summary>Every application typed in, by package, with its colours.</summary>
    public static SortedDictionary<string, AppColor> All(Context context)
    {
        var kept = KeyPlusPreferences.Global(context).GetString(Key, null);
        if (string.IsNullOrEmpty(kept))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<SortedDictionary<string, AppColor>>(kept) ?? [];
        }
        catch (JsonException unreadable)
        {
            Log.Write($"The applications' colours could not be read: {unreadable.Message}");
            return [];
        }
    }

    /// <summary>An application's colours; one not typed in yet, its theme's.</summary>
    public static AppColor For(Context context, string app) => All(context).GetValueOrDefault(app) ?? new AppColor();

    /// <summary>An application typed in: added the first time, taking its theme's background; noted again where it changed (the device dark now), unless its colour was seen on the screen.</summary>
    public static void Seen(Context context, string app)
    {
        var all = All(context);
        var background = Background(context, app)?.Text;
        if (!all.TryGetValue(app, out var kept))
        {
            all[app] = new AppColor(WindowColor: background);
        }
        else if (background is not null && !kept.OnScreen && kept.WindowColor != background)
        {
            all[app] = kept with { WindowColor = background };
        }
        else
        {
            return;
        }

        Keep(context, all);
    }

    /// <summary>An application's colour as seen on the screen beside its field, kept where it changed.</summary>
    public static void SeenOnScreen(Context context, string app, Rgb color)
    {
        var all = All(context);
        if (all.TryGetValue(app, out var kept) && (kept.WindowColor != color.Text || !kept.OnScreen))
        {
            all[app] = kept with { WindowColor = color.Text, OnScreen = true };
            Keep(context, all);
        }
    }

    public static void Set(Context context, string app, AppColor color)
    {
        var all = All(context);
        all[app] = color;
        Keep(context, all);
    }

    /// <summary>An application's name as the launcher shows it; its package where it has none.</summary>
    public static string Name(Context context, string app)
    {
        try
        {
            return context.PackageManager!.GetApplicationLabel(context.PackageManager.GetApplicationInfo(app, 0)) ?? app;
        }
        catch (PackageManager.NameNotFoundException)
        {
            return app;
        }
    }

    /// <summary>
    /// The background an application's theme declares (android:colorBackground), light or dark as the device is now: an app
    /// that turns dark by itself (Chrome) declares a light one, mirrored then (<see cref="Rgb.Mirrored"/>); none where it
    /// declares none or cannot be read.
    /// </summary>
    private static Rgb? Background(Context context, string app)
    {
        try
        {
            var info = context.PackageManager!.GetApplicationInfo(app, 0);
            var its = context.CreatePackageContext(app, 0)!;
            its.SetTheme(info.Theme);
            using var read = its.Theme!.ObtainStyledAttributes([Android.Resource.Attribute.ColorBackground]);
            if (!read.HasValue(0))
            {
                return null;
            }

            var color = new Android.Graphics.Color(read.GetColor(0, 0));
            var declared = new Rgb(color.R, color.G, color.B);
            var deviceDark = KeyPlusPreferences.DeviceDark(context);
            var made = (declared.Readable == Rgb.Black) == deviceDark ? declared.Mirrored : declared;
            Log.Detail($"Colours: {app}'s theme declares {declared.Text}, the device {(deviceDark ? "dark" : "light")}: {made.Text}");
            return made;
        }
        catch (Exception unread) when (unread is PackageManager.NameNotFoundException or Java.Lang.SecurityException or Android.Content.Res.Resources.NotFoundException)
        {
            Log.Write($"The theme of {app} could not be read: {unread.Message}");
            return null;
        }
    }

    private static void Keep(Context context, SortedDictionary<string, AppColor> all) =>
        KeyPlusPreferences.Global(context).Edit()!.PutString(Key, JsonSerializer.Serialize(all))!.Apply();
}
