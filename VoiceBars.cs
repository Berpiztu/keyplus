namespace Berpiztu.KeyPlus;

/// <summary>
/// 🎤 while it listens, on Android and Windows, as Gboard's: bars in the
/// microphone's place that rise and fall with the voice heard, each its own
/// height and moving on its own beat, so the sound is seen coming in; low and
/// still in silence. Sizes are shares of the sign's.
/// </summary>
public static class VoiceBars
{
    public const int Count = 4;

    /// <summary>A bar's width, and the room between two, as shares of the sign.</summary>
    public const double WidthShare = 0.13;
    public const double GapShare = 0.1;

    /// <summary>A bar's height in silence.</summary>
    private const double Least = 0.2;

    /// <summary>Each bar's height at the loudest: the middle ones the tallest.</summary>
    private static readonly double[] Tallest = [0.6, 1, 0.85, 0.5];

    /// <summary>A bar's height at a level heard (0 to 1) and a moment, each bar wavering on a beat of its own.</summary>
    public static double Height(int bar, double level, double seconds)
    {
        var waver = 0.7 + (0.3 * Math.Sin((seconds * 11) + (bar * 1.9)));
        return Least + ((1 - Least) * Math.Clamp(level, 0, 1) * Tallest[bar] * waver);
    }

    /// <summary>A bar's left edge, from the left of a sign of the size given, the bars together in its middle.</summary>
    public static double Left(int bar, double size)
    {
        var all = ((Count * WidthShare) + ((Count - 1) * GapShare)) * size;
        return ((size - all) / 2) + (bar * (WidthShare + GapShare) * size);
    }
}
