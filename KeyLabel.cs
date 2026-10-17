using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Widget;

namespace Berpiztu.KeyPlus;

/// <summary>
/// A key's label: a TextView that can draw its text closer to its left edge
/// than the middle would, by a share of the room left of it, measured on the
/// text as it is laid out, whatever size it fitted to. The key's ground and
/// what is drawn over it (an Alt Gr character in the corner) stay put.
/// </summary>
public sealed class KeyLabel : TextView
{
    public KeyLabel(Context context)
        : base(context)
    {
    }

    private KeyLabel(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    /// <summary>The share of the room left of the text it gives up, 0 for none.</summary>
    public float LeftRoomGivenUp { get; set; }

    protected override void OnDraw(Canvas? canvas)
    {
        var shift = LeftRoomGivenUp > 0 ? LeftRoom() * LeftRoomGivenUp : 0f;
        canvas!.Save();
        canvas.Translate(-shift, 0);
        base.OnDraw(canvas);
        canvas.Restore();
    }

    /// <summary>The room between the key's left edge and its text's widest line.</summary>
    private float LeftRoom()
    {
        if (Layout is not { LineCount: > 0 } layout)
        {
            return 0f;
        }

        var left = float.MaxValue;
        for (var line = 0; line < layout.LineCount; line++)
        {
            left = Math.Min(left, layout.GetLineLeft(line));
        }

        return CompoundPaddingLeft + left;
    }
}
