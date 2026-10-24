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
/// swiping over the letters and its trail; whether the keyboard is docked at
/// the screen's bottom rather than floating, as it starts; its colours
/// (Tema: as Windows' mode, light, dark, or Personalizado's keyboard and
/// accent colours, #RRGGBB, none chosen Windows' own) and those two as they
/// were before the last one was chosen, for Deshacer; whether each
/// application has colours of its own (Por aplicación, <see cref="AppColors"/>). Kept in
/// the app's folder, where the settings, a program of their own, write them
/// and the keyboard sees each change at once.
/// </summary>
/// <summary>Personalizado's keyboard and accent colours.</summary>
public sealed record KeyboardColors(string? KeyColor, string? AccentColor);

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
    Dictionary<string, string>? Chosen = null,
    bool Docked = false,
    [property: JsonConverter(typeof(JsonStringEnumConverter<KeyboardLook>))] KeyboardLook Look = KeyboardLook.System,
    string? KeyColor = null,
    string? AccentColor = null,
    KeyboardColors? Before = null,
    bool PerApp = false)
{
    /// <summary>
    /// The colours the keyboard takes typing in an application: Tema's, or,
    /// with Personalizado's Por app, the application's own: its colours, or
    /// its window's colour (<paramref name="own"/>, as the keyboard saw it)
    /// as the keyboard's colour with Windows' accent; a keyboard colour it
    /// has not chosen, Tema's Personalizado's, an accent, Windows' own.
    /// </summary>
    public KeyboardPreferences ColorsIn(string? app, System.Windows.Media.Color? own)
    {
        if (Look != KeyboardLook.Custom || !PerApp || app is null)
        {
            return this;
        }

        var color = AppColors.For(app);
        return color.Look switch
        {
            AppLook.Custom => this with { KeyColor = color.KeyColor ?? KeyColor, AccentColor = color.AccentColor },
            AppLook.Own when own is { } seen => this with { Look = KeyboardLook.Custom, KeyColor = Palette.Text(seen), AccentColor = null },
            _ => this,
        };
    }

    /// <summary>Personalizado's colours as they are, kept as <see cref="Before"/> when one is chosen.</summary>
    [JsonIgnore]
    public KeyboardColors Colors => new(KeyColor, AccentColor);

    /// <summary>The preferences with these Personalizado colours.</summary>
    public KeyboardPreferences With(KeyboardColors colors) => this with { KeyColor = colors.KeyColor, AccentColor = colors.AccentColor };

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
    public StandardLanguage TypingLanguage => StandardKeys.LanguageOf(Language);

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
