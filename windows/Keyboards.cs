using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The keyboards of the list on Windows, as Android's (Keyboards.cs):
/// Estándar and Estándar PC, which the keyboard comes with and which never
/// change, and those saved with Diseñar, each with its design, kept in the
/// app's folder (keyboards.json); the one shown is the preferences'
/// (<see cref="KeyboardPreferences.KeyboardId"/>), so the settings and the
/// keyboard, two processes, share it through the preferences' watch.
/// </summary>
public static class Keyboards
{
    public const string StandardId = "standard";
    public const string StandardPcId = "standardPc";
    public const string StandardBase = "standard";
    public const string StandardPcBase = "standardPc";

    private static readonly string Kept = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Berpiztu", "Keyboard", "keyboards.json");

    /// <summary>Every keyboard: the two the keyboard comes with, then those saved, in the order they were.</summary>
    public static IReadOnlyList<KeyboardEntry> All() =>
    [
        new(StandardId, Texts.Get(Resource.String.keyboard_standard), StandardBase),
        new(StandardPcId, Texts.Get(Resource.String.keyboard_standard_pc), StandardPcBase),
        .. Made(),
    ];

    /// <summary>The keyboard the preferences show; one deleted or unknown, Estándar PC.</summary>
    public static KeyboardEntry Selected(KeyboardPreferences preferences)
    {
        var all = All();
        return all.FirstOrDefault(keyboard => keyboard.Id == preferences.KeyboardId) ?? all.First(keyboard => keyboard.Id == StandardPcId);
    }

    /// <summary>A keyboard chosen: the keyboard shows it at once.</summary>
    public static void Select(string id) => (KeyboardPreferences.Read() with { Keyboard = id, Pc = null }).Keep();

    /// <summary>A keyboard saved, with its base and its design, under a name.</summary>
    public static KeyboardEntry SaveAs(string name, string from, string? design)
    {
        var made = new KeyboardEntry(Guid.NewGuid().ToString("N")[..8], name, from, design);
        Keep([.. Made(), made]);
        return made;
    }

    /// <summary>A name for a keyboard saved as it is designed, to be renamed in Tema: its own and the time.</summary>
    public static string NewName(KeyboardEntry from) =>
        Texts.Get(Resource.String.saved_as, from.Name, DateTime.Now.ToString("dd-MM HH:mm", CultureInfo.InvariantCulture));

    public static void Rename(string id, string name) =>
        Keep([.. Made().Select(keyboard => keyboard.Id == id ? keyboard with { Name = name } : keyboard)]);

    /// <summary>A saved keyboard deleted: shown, the keyboard shows Estándar PC again.</summary>
    public static void Delete(string id)
    {
        Keep([.. Made().Where(keyboard => keyboard.Id != id)]);
        if (KeyboardPreferences.Read().KeyboardId == id)
        {
            Select(StandardPcId);
        }
    }

    /// <summary>A saved keyboard's design, kept with it.</summary>
    public static void SetDesign(string id, string? design) =>
        Keep([.. Made().Select(keyboard => keyboard.Id == id ? keyboard with { Design = design } : keyboard)]);

    private static List<KeyboardEntry> Made()
    {
        try
        {
            return File.Exists(Kept) ? JsonSerializer.Deserialize<List<KeyboardEntry>>(File.ReadAllText(Kept)) ?? [] : [];
        }
        catch (JsonException unreadable)
        {
            Log.Write($"The keyboards saved could not be read: {unreadable.Message}");
            return [];
        }
    }

    private static void Keep(List<KeyboardEntry> made)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Kept)!);
        File.WriteAllText(Kept, JsonSerializer.Serialize(made));
    }
}
