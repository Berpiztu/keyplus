using Android.Graphics;
using Android.Graphics.Drawables;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The floating standard keyboard's outline while it is resized, as Gboard's
/// (on the emulator, pixels over 2.625): its card's edge in a thin line, the
/// accent at 80 %, and each corner in a thicker one, the accent itself, as
/// far as the corner's curve and <paramref name="reach"/> beyond it.
/// </summary>
public sealed class CardOutline(Color accent, float line, float mark, float corner, float reach) : Drawable
{
    /// <summary>The thin line's opacity: on the emulator, 105 where the card is 241 and the accent 71.</summary>
    private const float LineAlpha = (241f - 105) / (241 - 71);

    private readonly Paint thin = new(PaintFlags.AntiAlias) { Color = Color.Argb((int)(255 * LineAlpha), accent.R, accent.G, accent.B), StrokeWidth = line };
    private readonly Paint thick = new(PaintFlags.AntiAlias) { Color = accent, StrokeWidth = mark };

    public override void Draw(Canvas canvas)
    {
        thin.SetStyle(Paint.Style.Stroke);
        thick.SetStyle(Paint.Style.Stroke);
        var bounds = new RectF(Bounds);
        var edge = new RectF(bounds);
        edge.Inset(mark / 2, mark / 2);
        canvas.DrawRoundRect(edge, corner, corner, thin);

        var size = corner + reach;
        foreach (var (x, y) in new[] { (bounds.Left, bounds.Top), (bounds.Right - size, bounds.Top), (bounds.Left, bounds.Bottom - size), (bounds.Right - size, bounds.Bottom - size) })
        {
            canvas.Save();
            canvas.ClipRect(x, y, x + size, y + size);
            canvas.DrawRoundRect(edge, corner, corner, thick);
            canvas.Restore();
        }
    }

    public override void SetAlpha(int alpha)
    {
        thin.Alpha = alpha;
        thick.Alpha = alpha;
    }

    public override void SetColorFilter(ColorFilter? colorFilter)
    {
        thin.SetColorFilter(colorFilter);
        thick.SetColorFilter(colorFilter);
    }

    public override int Opacity => (int)Format.Translucent;
}
