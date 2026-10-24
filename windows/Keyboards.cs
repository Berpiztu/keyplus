using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The keyboards of the list on Windows, as Android's (Keyboards.cs), by
/// language, a PC's keys differing between them: Estándar and Estándar PC,
/// which the keyboard comes with in every language and which never change,
/// and those saved with Diseñar, each with its design and the language it
/// was designed in, kept in the app's folder (keyboards.json). Each language
/// shows the one chosen for it (<see cref="KeyboardPreferences.KeyboardIdFor"/>),
/// so the settings and the keyboard, two processes, share it through the
/// preferences' watch.
/// </summary>
public static class Keyboards
{
    public const string StandardId = "standard";
    public const string StandardPcId = "standardPc";
    public const string StandardBase = "standard";
    public const string StandardPcBase = "standardPc";

    /// <summary>Estándar XR, Gboard's on the Galaxy XR: the Android keyboard's on a headset, never listed here.</summary>
    public const string StandardXrId = "standardXr";
    public const string StandardXrBase = "standardXr";

    private static readonly string Kept = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Berpiztu", "Keyboard", "keyboards.json");

    /// <summary>A language's keyboards: the two the keyboard comes with, Estándar PC with that language's design, then those saved in it, in the order they were.</summary>
    public static IReadOnlyList<KeyboardEntry> All(string language) =>
    [
        new(StandardId, Texts.Get(Resource.String.keyboard_standard), StandardBase),
        new(StandardPcId, Texts.Get(Resource.String.keyboard_standard_pc), StandardPcBase, StandardKeys.PcDesign(language)),
        .. Made().Where(keyboard => keyboard.LanguageCode == language),
    ];

    /// <summary>The keyboard a language shows, as the preferences have it; one deleted or unknown, Estándar PC.</summary>
    public static KeyboardEntry Selected(KeyboardPreferences preferences, string language)
    {
        var all = All(language);
        var id = preferences.KeyboardIdFor(language);
        return all.FirstOrDefault(keyboard => keyboard.Id == id) ?? all.First(keyboard => keyboard.Id == StandardPcId);
    }

    /// <summary>
    /// A language come to keeps the kind of keyboard in sight (Estándar, Estándar PC): the language's keyboard where it is
    /// of that kind, a saved one too; else the one of that kind the keyboard comes with chosen for it.
    /// </summary>
    public static void Carry(string language, KeyboardEntry shown)
    {
        static string Kind(KeyboardEntry keyboard) => keyboard.Base == StandardBase ? StandardId : StandardPcId;
        if (Kind(Selected(KeyboardPreferences.Read(), language)) != Kind(shown))
        {
            Select(language, Kind(shown));
        }
    }

    /// <summary>A keyboard chosen for a language: the keyboard shows it at once while it types in it.</summary>
    public static void Select(string language, string id)
    {
        var preferences = KeyboardPreferences.Read();
        (preferences with { Chosen = new Dictionary<string, string>(preferences.Chosen ?? []) { [language] = id } }).Keep();
    }

    /// <summary>A keyboard saved in a language, with its base, its design and its docked one, under a name.</summary>
    public static KeyboardEntry SaveAs(string name, string from, string? design, string language, string? dockedDesign = null)
    {
        var made = new KeyboardEntry(Guid.NewGuid().ToString("N")[..8], name, from, design, language, dockedDesign);
        Keep([.. Made(), made]);
        return made;
    }

    /// <summary>A name for a keyboard saved as it is designed, to be renamed in Tema: its own and the time.</summary>
    public static string NewName(KeyboardEntry from) =>
        Texts.Get(Resource.String.saved_as, from.Name, DateTime.Now.ToString("dd-MM HH:mm", CultureInfo.InvariantCulture));

    /// <summary>A saved keyboard deleted: a language that showed it shows Estándar PC again.</summary>
    public static void Delete(string id)
    {
        Keep([.. Made().Where(keyboard => keyboard.Id != id)]);
        var preferences = KeyboardPreferences.Read();
        foreach (var language in StandardKeys.Languages.Where(language => preferences.KeyboardIdFor(language.Code) == id))
        {
            Select(language.Code, StandardPcId);
        }
    }

    /// <summary>Each change of the keyboards saved (one designed or deleted elsewhere), told on the caller's thread until the watch is disposed.</summary>
    public static IDisposable Watch(Action changed) => FileWatch.Changed(Kept, changed);

    /// <summary>A saved keyboard's design, kept with it: floating, its design; docked, its docked one.</summary>
    public static void SetDesign(string id, string? design, bool docked) =>
        Keep([.. Made().Select(keyboard => keyboard.Id != id ? keyboard : docked ? keyboard with { DockedDesign = design } : keyboard with { Design = design })]);

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
