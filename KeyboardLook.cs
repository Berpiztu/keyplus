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
