using Android.Graphics;
using Android.Text.Style;
using Java.Lang;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Draws a small glyph (a comma, a quote, an accent) larger, centred on its
/// line, without making the line any taller: the line keeps the metrics of
/// the text around it, so the legend does not shrink to make room.
/// </summary>
public sealed class BigGlyphSpan(float factor) : ReplacementSpan
{
    public override int GetSize(Paint paint, ICharSequence? text, int start, int end, Paint.FontMetricsInt? fm)
    {
        if (fm is not null)
        {
            var metrics = paint.GetFontMetricsInt()!;
            (fm.Ascent, fm.Descent, fm.Top, fm.Bottom) = (metrics.Ascent, metrics.Descent, metrics.Top, metrics.Bottom);
        }

        using var big = Larger(paint);
        return (int)System.Math.Ceiling(big.MeasureText(text, start, end));
    }

    public override void Draw(Canvas canvas, ICharSequence? text, int start, int end, float x, int top, int y, int bottom, Paint paint)
    {
        using var big = Larger(paint);
        var glyph = text!.ToString()[start..end];
        var bounds = new Rect();
        big.GetTextBounds(glyph, 0, glyph.Length, bounds);
        canvas.DrawText(glyph, x, ((top + bottom) / 2f) - bounds.ExactCenterY(), big);
    }

    private Paint Larger(Paint paint) => new(paint) { TextSize = paint.TextSize * factor };
}
