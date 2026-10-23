using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The keyboard's preferences on Windows, the Android ones that apply here,
/// with Android's names and defaults: showing the keyboard when a text field
/// takes the focus, what it does when the focus leaves (hides, becomes its ⌨
/// icon or stays), the icon where it never shows by itself, moving out of
/// the way of the text and of open menus; the language typed in and the
/// keyboard chosen for each language (Estándar, Estándar PC or one saved in
/// it, by its id: <see cref="Keyboards"/>), as Idiomas and Tema choose them
/// and the keyboard's own buttons change them;
/// swiping over the letters and its trail. Kept in
/// the app's folder, where the settings, a program of their own, write them
/// and the keyboard sees each change at once.
/// </summary>
public sealed record KeyboardPreferences(
    bool ShowKeyboard = true,
    string FocusLost = KeyboardPreferences.FocusLostHide,
    bool KeyboardIcon = true,
    bool AvoidCaret = true,
    bool AvoidMenus = true,
    bool? Pc = null,
    string? Language = null,
    bool Glide = true,
    bool GlideTrail = true,
    string? Keyboard = null,
    Dictionary<string, string>? Chosen = null)
{
    public const string FocusLostHide = "hide";
    public const string FocusLostIcon = "icon";
    public const string FocusLostStay = "stay";
    public static readonly string[] FocusLostChoices = [FocusLostHide, FocusLostIcon, FocusLostStay];

    private static readonly string Kept = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Berpiztu", "Keyboard", "preferences.json");

    public static KeyboardPreferences Default { get; } = new();

    /// <summary>
    /// The keyboard a language shows, by its id: the one chosen for it; else,
    /// in Spanish, the one chosen before there were languages (Keyboard); else
    /// Estándar PC, unless the choice kept before there were ids (Pc) said
    /// Estándar.
    /// </summary>
    public string KeyboardIdFor(string language) =>
        Chosen?.GetValueOrDefault(language)
        ?? (language == StandardKeys.Spanish.Code ? Keyboard : null)
        ?? (Pc == false ? Keyboards.StandardId : Keyboards.StandardPcId);

    /// <summary>The language typed in: the one chosen, Spanish at first.</summary>
    [JsonIgnore]
    public StandardLanguage TypingLanguage => StandardKeys.Languages.FirstOrDefault(known => known.Code == Language) ?? StandardKeys.Spanish;

    public static KeyboardPreferences Read()
    {
        try
        {
            return File.Exists(Kept) ? JsonSerializer.Deserialize<KeyboardPreferences>(File.ReadAllText(Kept)) ?? Default : Default;
        }
        catch (JsonException unreadable)
        {
            Log.Write($"The keyboard's preferences could not be read: {unreadable.Message}");
            return Default;
        }
    }

    public void Keep()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Kept)!);
        File.WriteAllText(Kept, JsonSerializer.Serialize(this));
    }

    /// <summary>Each change of the preferences, as they then are, told on the caller's thread until the watch is disposed.</summary>
    public static IDisposable Watch(Action<KeyboardPreferences> changed) => FileWatch.Changed(Kept, () => changed(Read()));
}
