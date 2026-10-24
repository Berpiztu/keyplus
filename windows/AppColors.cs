using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Berpiztu.KeyPlus;

/// <summary>An application's colours, with Personalizado's Por app on: its window's (De la app), as an application starts, or its own; General, Tema's Personalizado, is how those kept before read.</summary>
public enum AppLook
{
    General,
    Own,
    Custom,
}

/// <summary>
/// An application's choice and, for Personalizado, its keyboard and accent colours (#RRGGBB; one not chosen, Tema's
/// Personalizado's); and its window's colour as the keyboard last saw it there, for De la app in Tema's preview.
/// </summary>
public sealed record AppColor(
    [property: JsonConverter(typeof(JsonStringEnumConverter<AppLook>))] AppLook Look = AppLook.Own,
    string? KeyColor = null,
    string? AccentColor = null,
    string? WindowColor = null);

/// <summary>
/// The applications typed in, by their program's name (Code, chrome), each
/// with its colours, in the app's folder (apps.json): the keyboard adds one
/// the first time it types in it, Tema chooses their colours; the keyboard
/// and the settings, two processes, see each other's writes through the watch.
/// </summary>
public static class AppColors
{
    private static readonly string Kept = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Berpiztu", "Keyboard", "apps.json");

    /// <summary>Every application typed in, by name, with its colours.</summary>
    public static SortedDictionary<string, AppColor> All()
    {
        try
        {
            return File.Exists(Kept) ? JsonSerializer.Deserialize<SortedDictionary<string, AppColor>>(File.ReadAllText(Kept)) ?? [] : [];
        }
        catch (Exception unreadable) when (unreadable is JsonException or IOException)
        {
            Log.Write($"The applications' colours could not be read: {unreadable.Message}");
            return [];
        }
    }

    /// <summary>An application's colours; one not typed in yet, Tema's.</summary>
    public static AppColor For(string app) => All().GetValueOrDefault(app) ?? new AppColor();

    /// <summary>An application typed in: added the first time, taking its window's colours.</summary>
    public static void Seen(string app)
    {
        var all = All();
        if (all.TryAdd(app, new AppColor()))
        {
            Keep(all);
        }
    }

    /// <summary>An application's colours chosen.</summary>
    public static void Set(string app, AppColor color)
    {
        var all = All();
        all[app] = color;
        Keep(all);
    }

    /// <summary>An application's window's colour as the keyboard saw it, kept where it changed.</summary>
    public static void SeenColor(string app, string color)
    {
        var all = All();
        if (all.TryGetValue(app, out var kept) && kept.WindowColor != color)
        {
            all[app] = kept with { WindowColor = color };
            Keep(all);
        }
    }

    /// <summary>Each change, told on the caller's thread until the watch is disposed.</summary>
    public static IDisposable Watch(Action changed) => FileWatch.Changed(Kept, changed);

    private static void Keep(SortedDictionary<string, AppColor> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Kept)!);
        File.WriteAllText(Kept, JsonSerializer.Serialize(all));
    }
}
