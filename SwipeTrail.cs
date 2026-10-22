using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;

namespace Berpiztu.KeyPlus;

/// <summary>
/// A swipe's trail over the keys, as Gboard's: the line the finger draws,
/// <paramref name="width"/> wide and <paramref name="headAlpha"/> opaque where
/// the finger is, thinning to <paramref name="tailWidth"/> and fading to
/// <paramref name="tailAlpha"/> behind it as its points age, gone past
/// <paramref name="life"/> milliseconds. It stands on the keys' overlay, over
/// them, and is given points on the screen, the keys' place there taken off
/// (left, top). While it has points in sight it draws itself again each
/// frame, to thin them.
/// </summary>
public sealed class SwipeTrail(
    float width,
    Color color,
    int left,
    int top,
    float tailWidth = 0,
    long life = SwipeTrail.WholeKeyboardLife,
    float headAlpha = 1,
    float tailAlpha = 1) : Drawable
{
    /// <summary>How long a point of the whole keyboard's trail stays in sight, in milliseconds.</summary>
    public const long WholeKeyboardLife = 300;

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
        points.RemoveAll(point => now - point.Time > life);
        for (var i = 1; i < points.Count; i++)
        {
            var (fromX, fromY, from) = points[i - 1];
            var (toX, toY, _) = points[i];
            var fresh = 1 - ((now - from) / (float)life);
            paint.StrokeWidth = tailWidth + ((width - tailWidth) * fresh);
            paint.Alpha = (int)(255 * (tailAlpha + ((headAlpha - tailAlpha) * fresh)));
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
