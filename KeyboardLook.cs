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
    /// <summary>The red of a ✕ that deletes (a word of the dictionary), on Android and Windows alike: Material's red 700.</summary>
    public const uint DeleteRed = 0xFFD32F2F;

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
/// Personalizado's); and its window's colour as the keyboard last saw it there, for De la app: on Windows always seen on the
/// screen, on Android seen there where the keyboard's accessibility service is on (<see cref="OnScreen"/>), else as the
/// application's theme declares it.
/// </summary>
public sealed record AppColor(
    [property: JsonConverter(typeof(JsonStringEnumConverter<AppLook>))] AppLook Look = AppLook.Own,
    string? KeyColor = null,
    string? AccentColor = null,
    string? WindowColor = null,
    bool OnScreen = false);

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

    /// <summary>The colour on the other side of light and dark, its hue and saturation kept, its lightness (HSL) mirrored: a light theme's background as its dark one would be.</summary>
    public Rgb Mirrored
    {
        get
        {
            double r = R / 255.0, g = G / 255.0, b = B / 255.0;
            double most = Math.Max(r, Math.Max(g, b)), least = Math.Min(r, Math.Min(g, b));
            var lightness = (most + least) / 2;
            var range = most - least;
            var saturation = range == 0 ? 0 : range / (1 - Math.Abs((2 * lightness) - 1));
            var hue = range == 0 ? 0 : most == r ? ((g - b) / range % 6 + 6) % 6 : most == g ? ((b - r) / range) + 2 : ((r - g) / range) + 4;
            lightness = 1 - lightness;
            var chroma = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
            var x = chroma * (1 - Math.Abs((hue % 2) - 1));
            var (r1, g1, b1) = hue switch
            {
                < 1 => (chroma, x, 0.0),
                < 2 => (x, chroma, 0.0),
                < 3 => (0.0, chroma, x),
                < 4 => (0.0, x, chroma),
                < 5 => (x, 0.0, chroma),
                _ => (chroma, 0.0, x),
            };
            var m = lightness - (chroma / 2);
            static byte Byte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
            return new Rgb(Byte(r1 + m), Byte(g1 + m), Byte(b1 + m));
        }
    }
}
