using Android.Content;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Where the panel stands, kept across restarts: docked right above the
/// keyboard and in sight only while one is open, or floating where the user
/// left it, in sight all the time, for the places that lack only the arrows.
/// The size of its keys holds in both.
/// </summary>
public sealed class PanelPlacement(ISharedPreferences preferences)
{
    public const float MinScale = 0.6f;
    public const float MaxScale = 2.5f;

    private const string FloatingKey = "floating";
    private const string XKey = "x";
    private const string YKey = "y";
    private const string WidthKey = "width";
    private const string ScaleKey = "scale";

    public bool Floating { get; set; } = preferences.GetBoolean(FloatingKey, false);

    /// <summary>The floating panel's left edge, in pixels from the display's.</summary>
    public int X { get; set; } = preferences.GetInt(XKey, 0);

    /// <summary>The floating panel's top edge, in pixels from the display's.</summary>
    public int Y { get; set; } = preferences.GetInt(YKey, 0);

    /// <summary>The floating panel's width in pixels; 0 until the user gives it one.</summary>
    public int Width { get; set; } = preferences.GetInt(WidthKey, 0);

    /// <summary>The keys' size, 1 for the usual one.</summary>
    public float Scale { get; set; } = preferences.GetFloat(ScaleKey, 1f);

    public void Save() => preferences.Edit()!
        .PutBoolean(FloatingKey, Floating)!
        .PutInt(XKey, X)!
        .PutInt(YKey, Y)!
        .PutInt(WidthKey, Width)!
        .PutFloat(ScaleKey, Scale)!
        .Apply();
}
