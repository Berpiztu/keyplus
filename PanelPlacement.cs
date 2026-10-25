using Android.Content;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Where the panel stands on one screen (<see cref="KeyPlusPreferences.ForScreen"/>),
/// kept across restarts: docked right above the keyboard and in sight only
/// while one is open, or floating where the user left it, in sight all the
/// time, one for the screen whatever the mode. Keys, its two rows and ABC
/// (<see cref="Mode"/>) each keep their own place, width and keys' size, so
/// each opens as it was left; the standard keyboard one for each way it is
/// shown (<see cref="Way"/>). The icon keeps one place for the screen,
/// whichever of them it stands for.
/// </summary>
public sealed class PanelPlacement(ISharedPreferences preferences)
{
    /// <summary>
    /// The keys' size is the user's to choose, as small or as large as a
    /// corner takes it: these only keep a window there is something left of
    /// to take hold of, and one that stays a window.
    /// </summary>
    public const float MinScale = 0.1f;
    public const float MaxScale = 10f;

    /// <summary>The least width a corner leaves the window, in dp: room for the bottom corners' two grips, to take it larger again.</summary>
    public const int MinWidthDp = 48;

    /// <summary>The two modes the window is in, the strip's keys and the whole keyboard.</summary>
    public const string KeysMode = "keys";
    public const string WholeKeyboardMode = "abc";

    /// <summary>Keys made small: two rows and no title bar (doc/compact-keys.md).</summary>
    public const string CompactMode = "compact";

    /// <summary>The standard keyboard, Gboard's (Berpiztu AI Keyboard on a phone).</summary>
    public const string StandardMode = "standard";

    private const string FloatingKey = "floating";

    /// <summary>Whether the window floats, one for the screen whatever its mode: docked in three rows, it stays docked in two.</summary>
    private const string WindowFloatingKey = "windowFloating";
    private const string XKey = "x";
    private const string YKey = "y";
    private const string WidthKey = "width";
    private const string ScaleKey = "scale";
    private const string LiftKey = "lift";
    private const string DockedXKey = "dockedX";
    private const string DockedWidthKey = "dockedWidth";
    private const string IconXKey = "iconX";
    private const string IconYKey = "iconY";

    /// <summary>Each way's layout (<see cref="Way"/>), read the first time it is wanted.</summary>
    private readonly Dictionary<string, Layout> layouts = [];

    /// <summary>The mode whose layout the placement stands for now.</summary>
    public string Mode { get; set; } = KeysMode;

    /// <summary>The standard keyboard shown is Estándar PC, not Estándar.</summary>
    public bool Pc { get; set; }

    /// <summary>The screen lies wider than tall.</summary>
    public bool Landscape { get; set; }

    /// <summary>
    /// The layout the placement stands for now: the mode's; the standard
    /// keyboard's, as the Windows keyboard's, one for each way it is shown,
    /// as the user may want Estándar PC's more keys larger than Estándar's:
    /// Estándar or Estándar PC, floating or docked, the screen lying or
    /// standing ("standard.pc.docked.portrait"); one not kept yet, as the
    /// standard keyboard was left before.
    /// </summary>
    private string Way => Mode == StandardMode
        ? $"{Mode}.{(Pc ? "pc" : "standard")}.{(Floating ? "floating" : "docked")}.{(Landscape ? "landscape" : "portrait")}"
        : Mode;

    /// <summary>
    /// Whether the window floats, in sight all the time, or is docked and
    /// shows only while a keyboard is open: one for the screen, kept whether
    /// it is the full Keys, two rows or ABC. Where none is kept yet, the
    /// full Keys' own, as it was kept before.
    /// </summary>
    public bool Floating { get; set; } = preferences.GetBoolean(WindowFloatingKey,
        preferences.GetBoolean(Keyed(KeysMode, FloatingKey), preferences.GetBoolean(FloatingKey, false)));

    /// <summary>The floating panel's left edge, in pixels from the display's.</summary>
    public int X
    {
        get => Current.X;
        set => Current.X = value;
    }

    /// <summary>The floating panel's top edge, in pixels from the display's.</summary>
    public int Y
    {
        get => Current.Y;
        set => Current.Y = value;
    }

    /// <summary>The floating panel's width in pixels; 0 until the user gives it one.</summary>
    public int Width
    {
        get => Current.Width;
        set => Current.Width = value;
    }

    /// <summary>The keys' size, 1 for the usual one.</summary>
    public float Scale
    {
        get => Current.Scale;
        set => Current.Scale = value;
    }

    /// <summary>How far Berpiztu AI Keyboard stands above the screen's bottom while docked, in pixels; 0, on it.</summary>
    public int Lift
    {
        get => Current.Lift;
        set => Current.Lift = value;
    }

    /// <summary>
    /// Berpiztu AI Keyboard's keys while docked, narrower than the screen so
    /// a finger reaches them: their left edge, in pixels from the screen's,
    /// and their width; 0, as wide as the screen. Apart from the window's.
    /// </summary>
    public int DockedX
    {
        get => Current.DockedX;
        set => Current.DockedX = value;
    }

    public int DockedWidth
    {
        get => Current.DockedWidth;
        set => Current.DockedWidth = value;
    }

    /// <summary>
    /// Where the user left the icon, its left and top edges in pixels from the
    /// display's, apart from the window's; -1 until the icon is first dragged.
    /// </summary>
    public int IconX { get; set; } = preferences.GetInt(IconXKey, -1);

    public int IconY { get; set; } = preferences.GetInt(IconYKey, -1);

    public bool HasIconPlace => IconX >= 0 && IconY >= 0;

    private Layout Current
    {
        get
        {
            var way = Way;
            if (!layouts.TryGetValue(way, out var layout))
            {
                layout = Layout.Read(preferences, way, Mode);
                layouts[way] = layout;
            }

            return layout;
        }
    }

    /// <summary>Whether the window floats on a screen, set from outside it (Berpiztu AI Keyboard's app).</summary>
    public static void SetFloating(ISharedPreferences preferences, bool floating) =>
        preferences.Edit()!.PutBoolean(WindowFloatingKey, floating)!.Apply();

    /// <summary>The preference <see cref="Floating"/> is kept in, which Berpiztu AI Keyboard watches.</summary>
    public const string FloatingPreference = WindowFloatingKey;

    public void Save()
    {
        var editor = preferences.Edit()!.PutInt(IconXKey, IconX)!.PutInt(IconYKey, IconY)!.PutBoolean(WindowFloatingKey, Floating)!;
        foreach (var (way, layout) in layouts)
        {
            editor = editor
                .PutInt(Keyed(way, XKey), layout.X)!
                .PutInt(Keyed(way, YKey), layout.Y)!
                .PutInt(Keyed(way, WidthKey), layout.Width)!
                .PutFloat(Keyed(way, ScaleKey), layout.Scale)!
                .PutInt(Keyed(way, LiftKey), layout.Lift)!
                .PutInt(Keyed(way, DockedXKey), layout.DockedX)!
                .PutInt(Keyed(way, DockedWidthKey), layout.DockedWidth)!;
        }

        editor.Apply();
    }

    /// <summary>A mode's or a way's own preference: "keys.x", "abc.scale", "standard.pc.docked.portrait.lift".</summary>
    public static string Keyed(string mode, string key) => $"{mode}.{key}";

    /// <summary>
    /// One way's layout. Read from the way's own preferences, or, where it
    /// has none yet, from its mode's (the standard keyboard's before each way
    /// had its own), or from the one layout kept before each mode had its own.
    /// </summary>
    private sealed class Layout
    {
        public int X { get; set; }

        public int Y { get; set; }

        public int Width { get; set; }

        public float Scale { get; set; }

        public int Lift { get; set; }

        public int DockedX { get; set; }

        public int DockedWidth { get; set; }

        public static Layout Read(ISharedPreferences preferences, string way, string mode) => new()
        {
            X = preferences.GetInt(Keyed(way, XKey), preferences.GetInt(Keyed(mode, XKey), preferences.GetInt(XKey, 0))),
            Y = preferences.GetInt(Keyed(way, YKey), preferences.GetInt(Keyed(mode, YKey), preferences.GetInt(YKey, 0))),
            Width = preferences.GetInt(Keyed(way, WidthKey), preferences.GetInt(Keyed(mode, WidthKey), preferences.GetInt(WidthKey, 0))),
            Scale = preferences.GetFloat(Keyed(way, ScaleKey), preferences.GetFloat(Keyed(mode, ScaleKey), preferences.GetFloat(ScaleKey, 1f))),
            Lift = preferences.GetInt(Keyed(way, LiftKey), preferences.GetInt(Keyed(mode, LiftKey), 0)),
            DockedX = preferences.GetInt(Keyed(way, DockedXKey), preferences.GetInt(Keyed(mode, DockedXKey), 0)),
            DockedWidth = preferences.GetInt(Keyed(way, DockedWidthKey), preferences.GetInt(Keyed(mode, DockedWidthKey), 0)),
        };
    }
}
