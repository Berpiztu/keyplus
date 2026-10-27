using System.Windows.Media;
using Windows.UI.ViewManagement;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The keyboard's colours from Windows' own, as the Android keyboard takes
/// the device's (Material You): the user's accent and its lighter and darker
/// tones (UISettings), and the theme's background and foreground. Gboard's
/// parts on Windows' tones: a key the background, its letters the
/// foreground, the ground the background with a little of the foreground in
/// it, a function key the accent's lightest tone (darkest in a dark theme)
/// and its sign a dark one, a modifier on the next tone, ✓'s blue the accent,
/// and what is written on it white or black, whichever reads. Tema's choice
/// (<see cref="KeyboardLook"/>) makes it light or dark whatever Windows'
/// mode: its background and foreground, black and white, change places;
/// Personalizado puts the keyboard's colour chosen in the background's
/// place, its letters white or black, whichever reads, and the accent chosen
/// in the accent's, its tones made as Windows' are, each a quarter of the
/// way further to white or to black; one not chosen, Windows' own.
/// </summary>
public sealed class Palette
{
    private Palette(UISettings ui, KeyboardPreferences chosen)
    {
        Color Of(UIColorType type)
        {
            var color = ui.GetColorValue(type);
            return Color.FromArgb(color.A, color.R, color.G, color.B);
        }

        var background = Of(UIColorType.Background);
        var foreground = Of(UIColorType.Foreground);
        var windowsDark = background.R + background.G + background.B < 3 * 128;
        Look = chosen.Look;
        (KeyColor, AccentColor) = Look == KeyboardLook.Custom ? (Parse(chosen.KeyColor), Parse(chosen.AccentColor)) : (null, null);
        if (KeyColor is { } keyboard)
        {
            (background, foreground) = (keyboard, Readable(keyboard));
            Dark = foreground == Colors.White;
        }
        else
        {
            Dark = KeyboardLooks.IsDark(Look, windowsDark);
            if (Dark != windowsDark)
            {
                (background, foreground) = (foreground, background);
            }
        }

        // The accent's tones, lighter (+) or darker (−): Windows' own, or the colour chosen's.
        Color Tone(int step) => AccentColor is { } accent
            ? ToColor(ToRgb(accent).Tone(step))
            : Of(step switch
            {
                3 => UIColorType.AccentLight3,
                2 => UIColorType.AccentLight2,
                1 => UIColorType.AccentLight1,
                -1 => UIColorType.AccentDark1,
                -2 => UIColorType.AccentDark2,
                -3 => UIColorType.AccentDark3,
                _ => UIColorType.Accent,
            });

        Glyph = foreground;
        Key = Dark ? Mix(background, foreground, 0.16) : background;
        Ground = Mix(background, foreground, Dark ? 0.06 : 0.08);
        Function = Tone(Dark ? -3 : 3);
        FunctionPressed = Tone(Dark ? -2 : 2);
        FunctionGlyph = Tone(Dark ? 3 : -2);
        On = Tone(Dark ? -1 : 2);
        Primary = Tone(0);
        OnPrimary = Readable(Primary);
        Outline = Mix(background, foreground, 0.45);
    }

    /// <summary>Tema's choice it was made for, and Personalizado's colours.</summary>
    public KeyboardLook Look { get; }

    public Color? KeyColor { get; }

    public Color? AccentColor { get; }

    /// <summary>Whether the preferences ask for these very colours: the same choice, and Personalizado's the same colours.</summary>
    public bool Matches(KeyboardPreferences chosen) =>
        chosen.Look == Look && (Look != KeyboardLook.Custom || (Parse(chosen.KeyColor) == KeyColor && Parse(chosen.AccentColor) == AccentColor));

    /// <summary>A colour kept as #RRGGBB; none, or one unreadable, none.</summary>
    public static Color? Parse(string? kept) => Rgb.Parse(kept) is { } color ? ToColor(color) : null;

    /// <summary>A colour as it is kept, #RRGGBB.</summary>
    public static string Text(Color color) => ToRgb(color).Text;

    public bool Dark { get; }

    public Color Ground { get; }

    public Color Key { get; }

    public Color Glyph { get; }

    public Color Function { get; }

    public Color FunctionPressed { get; }

    public Color FunctionGlyph { get; }

    /// <summary>A modifier on (Ctrl, Alt, ⊞, Alt Gr, Bloq Mayús), as Gboard's ⊞ circled.</summary>
    public Color On { get; }

    public Color Primary { get; }

    /// <summary>What is written on <see cref="Primary"/>: Hecho's word, a key marked while designing.</summary>
    public Color OnPrimary { get; }

    public Color Outline { get; }

    /// <summary>An empty row's outline while designing, Android's: the outline half seen.</summary>
    public Color FaintOutline => Color.FromArgb(128, Outline.R, Outline.G, Outline.B);


    /// <summary>The colours as Tema's choice has them.</summary>
    public static Palette Read(KeyboardPreferences chosen) => new(new UISettings(), chosen);

    /// <summary>White or black on a colour, whichever stands out from it more (<see cref="Rgb.Readable"/>, shared with Android).</summary>
    private static Color Readable(Color ground) => ToColor(ToRgb(ground).Readable);

    /// <summary>One colour with a share of another in it (<see cref="Rgb.Mix"/>).</summary>
    private static Color Mix(Color under, Color over, double share) => ToColor(ToRgb(under).Mix(ToRgb(over), share));

    private static Rgb ToRgb(Color color) => new(color.R, color.G, color.B);

    private static Color ToColor(Rgb color) => Color.FromRgb(color.R, color.G, color.B);
}
