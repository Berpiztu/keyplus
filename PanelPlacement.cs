using Android.Content;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Where the panel stands, kept across restarts: docked right above the
/// keyboard and in sight only while one is open, or floating where the user
/// left it, in sight all the time, for the places that lack only the arrows.
/// The size of its keys holds in both. Closed (Hidden), nothing of it is in
/// sight until the KeyPlus app is opened again.
/// </summary>
public sealed class PanelPlacement(ISharedPreferences preferences)
{
    public const float MinScale = 0.6f;
    public const float MaxScale = 2.5f;

    /// <summary>The preference behind <see cref="Hidden"/>, which the service watches.</summary>
    public const string HiddenKey = "hidden";

    private const string FloatingKey = "floating";
    private const string XKey = "x";
    private const string YKey = "y";
    private const string WidthKey = "width";
    private const string IconXKey = "iconX";
    private const string IconYKey = "iconY";
    private const string ScaleKey = "scale";

    public bool Floating { get; set; } = preferences.GetBoolean(FloatingKey, false);

    /// <summary>
    /// Whether KeyPlus was closed: written at once, for the app opening and the
    /// service showing the panel again are apart.
    /// </summary>
    public bool Hidden
    {
        get => preferences.GetBoolean(HiddenKey, false);
        set => preferences.Edit()!.PutBoolean(HiddenKey, value)!.Apply();
    }

    /// <summary>The floating panel's left edge, in pixels from the display's.</summary>
    public int X { get; set; } = preferences.GetInt(XKey, 0);

    /// <summary>The floating panel's top edge, in pixels from the display's.</summary>
    public int Y { get; set; } = preferences.GetInt(YKey, 0);

    /// <summary>The floating panel's width in pixels; 0 until the user gives it one.</summary>
    public int Width { get; set; } = preferences.GetInt(WidthKey, 0);

    /// <summary>
    /// Where the user left the icon, its left and top edges in pixels from the
    /// display's, apart from the window's; -1 until the icon is first dragged.
    /// </summary>
    public int IconX { get; set; } = preferences.GetInt(IconXKey, -1);

    public int IconY { get; set; } = preferences.GetInt(IconYKey, -1);

    public bool HasIconPlace => IconX >= 0 && IconY >= 0;

    /// <summary>The keys' size, 1 for the usual one.</summary>
    public float Scale { get; set; } = preferences.GetFloat(ScaleKey, 1f);

    public void Save() => preferences.Edit()!
        .PutBoolean(FloatingKey, Floating)!
        .PutInt(XKey, X)!
        .PutInt(YKey, Y)!
        .PutInt(WidthKey, Width)!
        .PutInt(IconXKey, IconX)!
        .PutInt(IconYKey, IconY)!
        .PutFloat(ScaleKey, Scale)!
        .Apply();
}
