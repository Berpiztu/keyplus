using System.Text.Json.Serialization;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The keyboard's colours, Tema's choice, on Android and Windows alike: as
/// the system has them (Android's dark theme, Windows' mode), light or dark
/// whatever it has, or Personalizado, the keyboard's colour and the accent's
/// chosen. Only the colours: the keys' places never change with them.
/// </summary>
public enum KeyboardLook
{
    System,
    Light,
    Dark,
    Custom,
}

public static class KeyboardLooks
{
    /// <summary>Tema's choices, in their order.</summary>
    public static readonly KeyboardLook[] All = [KeyboardLook.System, KeyboardLook.Light, KeyboardLook.Dark, KeyboardLook.Custom];

    /// <summary>A choice's words.</summary>
    public static int Name(KeyboardLook look) => look switch
    {
        KeyboardLook.Light => Resource.String.look_light,
        KeyboardLook.Dark => Resource.String.look_dark,
        KeyboardLook.Custom => Resource.String.look_custom,
        _ => Resource.String.look_system,
    };

    /// <summary>Whether the keyboard is dark, the system being dark or not: Personalizado with no keyboard colour chosen goes as the system.</summary>
    public static bool IsDark(KeyboardLook look, bool systemDark) => look == KeyboardLook.Dark || (look is KeyboardLook.System or KeyboardLook.Custom && systemDark);

    /// <summary>A choice kept as its name; none, or one unknown, the system's.</summary>
    public static KeyboardLook Parse(string? kept) => Enum.TryParse<KeyboardLook>(kept, out var look) ? look : KeyboardLook.System;
}

/// <summary>An application's colours, with Personalizado's Por app on: its window's (De la app), as an application starts, or its own; General, Tema's Personalizado, is how those kept before read.</summary>
public enum AppLook
{
    General,
    Own,
    Custom,
}

/// <summary>
/// An application's choice and, for Personalizado, its keyboard and accent colours (#RRGGBB; one not chosen, Tema's
/// Personalizado's); and its window's colour as the keyboard last saw it there (Windows) or as
/// the application's theme declares it (Android), for De la app.
/// </summary>
public sealed record AppColor(
    [property: JsonConverter(typeof(JsonStringEnumConverter<AppLook>))] AppLook Look = AppLook.Own,
    string? KeyColor = null,
    string? AccentColor = null,
    string? WindowColor = null);

/// <summary>
/// A colour as Tema keeps it, #RRGGBB, and the arithmetic both keyboards make
/// their colours with, so Personalizado reads alike on Windows and Android:
/// one colour with a share of another in it, and white or black, whichever
/// stands out from a colour more (WCAG's contrast ratio).
/// </summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb White = new(255, 255, 255);
    public static readonly Rgb Black = new(0, 0, 0);

    /// <summary>A colour kept as #RRGGBB; none, or one unreadable, none.</summary>
    public static Rgb? Parse(string? kept) =>
        kept is ['#', .. var hex] && hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value)
            ? new Rgb((byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : null;

    /// <summary>The colour as it is kept, #RRGGBB.</summary>
    public string Text => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>This colour with a share of another in it.</summary>
    public Rgb Mix(Rgb over, double share) => new(
        (byte)Math.Round(R + ((over.R - R) * share)),
        (byte)Math.Round(G + ((over.G - G) * share)),
        (byte)Math.Round(B + ((over.B - B) * share)));

    /// <summary>White or black on this colour, whichever stands out from it more.</summary>
    public Rgb Readable
    {
        get
        {
            static double Linear(byte channel) => channel / 255.0 is var c && c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            var luminance = (0.2126 * Linear(R)) + (0.7152 * Linear(G)) + (0.0722 * Linear(B));
            return 1.05 / (luminance + 0.05) >= (luminance + 0.05) / 0.05 ? White : Black;
        }
    }

    /// <summary>An accent's tone, as Windows makes its own: each step lighter (+) or darker (−) a quarter of the way to white or black.</summary>
    public Rgb Tone(int step) => Mix(step > 0 ? White : Black, Math.Abs(step) / 4.0);
}
