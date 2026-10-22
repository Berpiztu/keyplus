using System.IO;
using System.Text.Json;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The keyboard's preferences on Windows, the Android ones that apply here,
/// with Android's names and defaults: showing the keyboard when a text field
/// takes the focus, what it does when the focus leaves (hides, becomes its ⌨
/// icon or stays), the icon where it never shows by itself, moving out of
/// the way of the text and of open menus; the keyboard (Estándar or
/// Estándar PC) and its language, as Tema and Idiomas choose them and the
/// keyboard's own buttons change them; swiping over the letters and its
/// trail. Kept in
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
    bool GlideTrail = true)
{
    public const string FocusLostHide = "hide";
    public const string FocusLostIcon = "icon";
    public const string FocusLostStay = "stay";
    public static readonly string[] FocusLostChoices = [FocusLostHide, FocusLostIcon, FocusLostStay];

    private static readonly string Kept = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Berpiztu", "Keyboard", "preferences.json");

    public static KeyboardPreferences Default { get; } = new();

    /// <summary>The preferences with Estándar PC chosen, or Estándar.</summary>
    public KeyboardPreferences With(bool pc) => this with { Pc = pc };

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
    public static IDisposable Watch(Action<KeyboardPreferences> changed)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Kept)!);
        var caller = SynchronizationContext.Current;
        var watch = new FileSystemWatcher(Path.GetDirectoryName(Kept)!, Path.GetFileName(Kept)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
        void Seen(object sender, FileSystemEventArgs e)
        {
            try
            {
                var kept = Read();
                if (caller is null)
                {
                    changed(kept);
                }
                else
                {
                    caller.Post(_ => changed(kept), null);
                }
            }
            catch (IOException busy)
            {
                // Still being written: its next change tells it again.
                Log.Write($"The keyboard's preferences were busy: {busy.Message}");
            }
        }

        watch.Changed += Seen;
        watch.Created += Seen;
        watch.EnableRaisingEvents = true;
        return watch;
    }
}
