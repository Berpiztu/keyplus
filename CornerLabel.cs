using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Widget;

namespace Berpiztu.KeyPlus;

/// <summary>
/// A key's Alt Gr character, drawn small in its bottom right corner over the
/// key, as Windows' touch keyboard draws it: apart from the key's own label,
/// so it neither moves the label off the middle nor takes room the label
/// could grow into. Its size follows the label's, whatever that fitted to.
/// </summary>
public sealed class CornerLabel(TextView key, float share, int inset) : Drawable
{
    private readonly Paint paint = new(PaintFlags.AntiAlias) { TextAlign = Paint.Align.Right };

    /// <summary>The character drawn; none draws nothing.</summary>
    public string? Text { get; set; }

    public override void Draw(Canvas canvas)
    {
        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        paint.Color = new Color(key.CurrentTextColor);
        paint.TextSize = key.TextSize * share;
        var bounds = Bounds;
        canvas.DrawText(Text, bounds.Right - inset, bounds.Bottom - inset - paint.Descent(), paint);
    }

    public override void SetAlpha(int alpha) => paint.Alpha = alpha;

    public override void SetColorFilter(ColorFilter? colorFilter) => paint.SetColorFilter(colorFilter);

    // Drawn over a key, letting it show through.
    public override int Opacity => (int)Format.Translucent;
}
