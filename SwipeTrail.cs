using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;

namespace Berpiztu.KeyPlus;

/// <summary>
/// A swipe's trail over the whole keyboard, as Gboard's: the line the finger
/// draws, full width where the finger is and thinning to nothing behind it as
/// its points age past <see cref="Life"/>, so a finger at rest leaves none.
/// It stands on the keyboard's overlay, over the keys, and is given points
/// on the screen, the keyboard's place there taken off (left, top). While it
/// has points in sight it draws itself again each frame, to thin them.
/// </summary>
public sealed class SwipeTrail(float width, Color color, int left, int top) : Drawable
{
    /// <summary>How long a point of the trail stays in sight, in milliseconds.</summary>
    private const long Life = 300;

    private readonly Paint paint = new(PaintFlags.AntiAlias) { Color = color, StrokeCap = Paint.Cap.Round };
    private readonly List<(float X, float Y, long Time)> points = [];

    /// <summary>A point the finger reached, on the screen, at its motion event's time (uptime, in milliseconds).</summary>
    public void Add(float x, float y, long time)
    {
        points.Add((x - left, y - top, time));
        InvalidateSelf();
    }

    public override void Draw(Canvas canvas)
    {
        var now = SystemClock.UptimeMillis();
        points.RemoveAll(point => now - point.Time > Life);
        for (var i = 1; i < points.Count; i++)
        {
            var (fromX, fromY, from) = points[i - 1];
            var (toX, toY, _) = points[i];
            paint.StrokeWidth = width * (1 - ((now - from) / (float)Life));
            canvas.DrawLine(fromX, fromY, toX, toY, paint);
        }

        if (points.Count > 0)
        {
            InvalidateSelf();
        }
    }

    public override void SetAlpha(int alpha) => paint.Alpha = alpha;

    public override void SetColorFilter(ColorFilter? colorFilter) => paint.SetColorFilter(colorFilter);

    public override int Opacity => (int)Format.Translucent;
}
