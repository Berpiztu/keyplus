using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Berpiztu.KeyPlus;

/// <summary>
/// How 🎤 listens, as IA-BotMsgs' chat lets it be set: the microphone (by
/// its name, none for the system's default), whether it stops by itself once
/// the user stops speaking, after how long a silence, or only when 🎤 is
/// tapped again (the default), how much the recorded volume is
/// raised, and which ia-voice reads it: the local one, this computer's
/// container, or one at an address, with the user and password its proxy
/// asks for. Kept in the app's folder, the password sealed for this Windows
/// user (DPAPI), never in the repository.
/// </summary>
public sealed record VoiceSettings(string Microphone, bool AutoStop, double SilenceSeconds, double Gain, bool Local, string Address, string User, string Password)
{
    public const double LeastSilence = 0.5;
    public const double MostSilence = 30;
    public const double MostGain = 4;

    private static readonly string Kept = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Berpiztu", "Keyboard", "voice.json");

    public static VoiceSettings Default { get; } = new("", false, 2, 1, true, "", "", "");

    /// <summary>Where the audio is sent: this computer's container, or the address written.</summary>
    public string Service => Local ? VoiceTyping.DefaultService : Address;

    public static VoiceSettings Read()
    {
        try
        {
            if (!File.Exists(Kept) || JsonSerializer.Deserialize<Stored>(File.ReadAllText(Kept)) is not { } stored)
            {
                return Default;
            }

            return new VoiceSettings(stored.Microphone ?? "", stored.AutoStop, stored.SilenceSeconds, stored.Gain, stored.Local, stored.Address ?? "", stored.User ?? "", Unsealed(stored.Password)).Bounded();
        }
        catch (Exception unreadable) when (unreadable is JsonException or CryptographicException or FormatException)
        {
            Log.Write($"The voice settings could not be read: {unreadable.Message}");
            return Default;
        }
    }

    public void Keep()
    {
        var kept = Bounded();
        Directory.CreateDirectory(Path.GetDirectoryName(Kept)!);
        File.WriteAllText(Kept, JsonSerializer.Serialize(new Stored(kept.Microphone, kept.AutoStop, kept.SilenceSeconds, kept.Gain, kept.Local, kept.Address, kept.User, Sealed(kept.Password))));
    }

    /// <summary>The values within their limits: silence 0.5 to 30 s, gain ×1 to ×4; with no address, the local service.</summary>
    public VoiceSettings Bounded() => this with
    {
        Microphone = Microphone ?? "",
        SilenceSeconds = Math.Clamp(SilenceSeconds, LeastSilence, MostSilence),
        Gain = Math.Clamp(Gain, 1, MostGain),
        Local = Local || string.IsNullOrWhiteSpace(Address),
        Address = (Address ?? "").Trim().TrimEnd('/'),
        User = (User ?? "").Trim(),
        Password = Password ?? "",
    };

    private static string Sealed(string password) =>
        password.Length == 0 ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser));

    private static string Unsealed(string? sealedPassword) =>
        string.IsNullOrEmpty(sealedPassword) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(sealedPassword), null, DataProtectionScope.CurrentUser));

    /// <summary>The settings as the file holds them, the password sealed.</summary>
    private sealed record Stored(string? Microphone, bool AutoStop, double SilenceSeconds, double Gain, bool Local, string? Address, string? User, string? Password);
}
