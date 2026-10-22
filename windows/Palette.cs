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
/// and its sign a dark one, a modifier on the next tone, ✓'s blue the accent.
/// </summary>
public sealed class Palette
{
    private Palette(UISettings ui)
    {
        Color Of(UIColorType type)
        {
            var color = ui.GetColorValue(type);
            return Color.FromArgb(color.A, color.R, color.G, color.B);
        }

        var background = Of(UIColorType.Background);
        var foreground = Of(UIColorType.Foreground);
        Dark = background.R + background.G + background.B < 3 * 128;
        Glyph = foreground;
        Key = Dark ? Mix(background, foreground, 0.16) : background;
        Ground = Mix(background, foreground, Dark ? 0.06 : 0.08);
        Function = Of(Dark ? UIColorType.AccentDark3 : UIColorType.AccentLight3);
        FunctionPressed = Of(Dark ? UIColorType.AccentDark2 : UIColorType.AccentLight2);
        FunctionGlyph = Of(Dark ? UIColorType.AccentLight3 : UIColorType.AccentDark2);
        On = Of(Dark ? UIColorType.AccentDark1 : UIColorType.AccentLight2);
        Primary = Of(UIColorType.Accent);
        Outline = Mix(background, foreground, 0.45);
        Handle = Mix(background, foreground, 0.35);
    }

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

    public Color Outline { get; }

    /// <summary>The floating card's handle.</summary>
    public Color Handle { get; }

    public static Palette Read() => new(new UISettings());

    /// <summary>One colour with a share of another in it.</summary>
    private static Color Mix(Color under, Color over, double share) => Color.FromRgb(
        (byte)Math.Round(under.R + ((over.R - under.R) * share)),
        (byte)Math.Round(under.G + ((over.G - under.G) * share)),
        (byte)Math.Round(under.B + ((over.B - under.B) * share)));
}
