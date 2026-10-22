using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;
using Orientation = Android.Widget.Orientation;

namespace Berpiztu.KeyPlus;

/// <summary>A button of the standard keyboard's grid: its name, its sign, what it does, and whether it opens more (›).</summary>
public sealed record GridTile(string Label, int Icon, Action Chosen, bool More = false);

/// <summary>
/// The standard keyboard's grid, Gboard's (⊞) as the owner's Fold has it
/// (docs/tODO/photos/gboard/fold-grid-*.png), in the keys' place: pages of
/// buttons two by two, each a pill with its sign and its name, › where it
/// opens more; under them a dot for each page, the one in sight darker, and
/// ✏ at the right. A swipe across turns the page. Its sizes are Gboard's on
/// the Fold, pixels measured there over 2.25; its colours the device's
/// (<see cref="StandardColors"/>).
/// </summary>
public sealed class StandardGrid : FrameLayout
{
    private const float PixelsPerDp = 2.25f;
    private const float PillWidthDp = 291 / PixelsPerDp;
    private const float PillHeightDp = 126 / PixelsPerDp;
    private const float ColumnGapDp = 32 / PixelsPerDp;
    private const float RowGapDp = 18 / PixelsPerDp;

    /// <summary>The first row's top, under the keys' top.</summary>
    private const float TopDp = 45 / PixelsPerDp;

    /// <summary>A sign's box: its drawn part 50 px (the keyboard's 800 of its 960), 35 px into the pill.</summary>
    private const float SignDp = 50f / (800f / 960) / PixelsPerDp;
    private const float SignLeftDp = (35 - ((50f / (800f / 960)) - 50) / 2) / PixelsPerDp;

    /// <summary>The name: a capital 24 px tall over Roboto's 0.711, 122 px into the pill.</summary>
    private const float LabelSp = 24 / 0.711f / PixelsPerDp;
    private const float LabelLeftDp = 122 / PixelsPerDp;

    /// <summary>›: drawn 10 px wide (240 of its 960), its right 49 px from the pill's.</summary>
    private const float MoreDp = 10f / (240f / 960) / PixelsPerDp;
    private const float MoreRightDp = (49 - ((10f / (240f / 960)) - 10) / 2) / PixelsPerDp;

    /// <summary>The dots: 13 px each, 70 px the three; ✏: 161 × 90 px, its outline 3 px, its bottom 29 px over the keys' bottom.</summary>
    private const float DotDp = 13 / PixelsPerDp;
    private const float DotGapDp = (70 - (3 * 13)) / 2f / PixelsPerDp;
    private const float EditWidthDp = 161 / PixelsPerDp;
    private const float EditHeightDp = 90 / PixelsPerDp;
    private const float EditLineDp = 3 / PixelsPerDp;
    private const float BottomDp = 29 / PixelsPerDp;

    /// <summary>The grid's buttons on a page: two rows of two, as the Fold's.</summary>
    private const int Columns = 2;
    private const int Rows = 2;

    private readonly StandardColors colors;
    private readonly float density;
    private readonly int touchSlop;
    private readonly LinearLayout page;
    private readonly LinearLayout dots;
    private IReadOnlyList<GridTile> tiles = [];
    private int shown;
    private float downX;
    private bool turning;

    public StandardGrid(Context context, StandardColors colors, Action edit) : base(context)
    {
        this.colors = colors;
        density = context.Resources!.DisplayMetrics!.Density;
        touchSlop = ViewConfiguration.Get(context)!.ScaledTouchSlop;
        SetBackgroundColor(colors.Ground);

        page = new LinearLayout(context) { Orientation = Orientation.Vertical };
        AddView(page, new LayoutParams(Dp(Columns * PillWidthDp + ((Columns - 1) * ColumnGapDp)), ViewGroup.LayoutParams.WrapContent, GravityFlags.Top | GravityFlags.CenterHorizontal)
        {
            TopMargin = Dp(TopDp),
        });

        var footer = new FrameLayout(context);
        dots = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        dots.SetGravity(GravityFlags.CenterVertical);
        footer.AddView(dots, new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent, GravityFlags.Center));
        footer.AddView(EditButton(edit), new LayoutParams(Dp(EditWidthDp), Dp(EditHeightDp), GravityFlags.Right | GravityFlags.CenterVertical));
        AddView(footer, new LayoutParams(Dp(Columns * PillWidthDp + ((Columns - 1) * ColumnGapDp) + EditLineDp * 2), Dp(EditHeightDp), GravityFlags.Bottom | GravityFlags.CenterHorizontal)
        {
            BottomMargin = Dp(BottomDp),
        });
    }

    /// <summary>The grid's buttons, in their order; shown from the first page.</summary>
    public IReadOnlyList<GridTile> Tiles
    {
        set
        {
            tiles = value;
            Show(0);
        }
    }

    private int Pages => Math.Max(1, (tiles.Count + (Columns * Rows) - 1) / (Columns * Rows));

    private int Dp(float value) => (int)Math.Round(value * density);

    private void Show(int index)
    {
        shown = Math.Clamp(index, 0, Pages - 1);
        page.RemoveAllViews();
        var first = shown * Columns * Rows;
        for (var row = 0; row < Rows; row++)
        {
            var line = new LinearLayout(Context) { Orientation = Orientation.Horizontal };
            for (var column = 0; column < Columns; column++)
            {
                var at = first + (row * Columns) + column;
                var place = new LinearLayout.LayoutParams(Dp(PillWidthDp), Dp(PillHeightDp)) { LeftMargin = column > 0 ? Dp(ColumnGapDp) : 0 };
                line.AddView(at < tiles.Count ? Pill(tiles[at]) : new View(Context), place);
            }

            page.AddView(line, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = row > 0 ? Dp(RowGapDp) : 0 });
        }

        dots.RemoveAllViews();
        for (var i = 0; i < Pages; i++)
        {
            var dot = new View(Context) { Background = Round(i == shown ? colors.Outline : colors.FaintOutline) };
            dots.AddView(dot, new LinearLayout.LayoutParams(Dp(DotDp), Dp(DotDp)) { LeftMargin = i > 0 ? Dp(DotGapDp) : 0 });
        }
    }

    /// <summary>A button: a pill in the keys' colour, its sign and name, › where it opens more.</summary>
    private FrameLayout Pill(GridTile tile)
    {
        var shape = new GradientDrawable();
        shape.SetColor(colors.Key);
        shape.SetCornerRadius(Dp(PillHeightDp) / 2f);
        var pill = new FrameLayout(Context!)
        {
            ContentDescription = tile.Label,
            Background = new RippleDrawable(ColorStateList.ValueOf(colors.Function), shape, null),
            Clickable = true,
        };
        pill.Click += (_, _) => tile.Chosen();
        pill.AddView(Sign(tile.Icon, colors.Glyph), new LayoutParams(Dp(SignDp), Dp(SignDp), GravityFlags.Left | GravityFlags.CenterVertical) { LeftMargin = Dp(SignLeftDp) });

        var name = new TextView(Context) { Text = tile.Label, Ellipsize = Android.Text.TextUtils.TruncateAt.Marquee, Gravity = GravityFlags.CenterVertical };
        name.SetTextColor(colors.Glyph);
        name.SetTextSize(ComplexUnitType.Sp, LabelSp);
        name.SetSingleLine(true);
        // A name longer than the room left scrolls along, as Gboard's do.
        name.Selected = true;
        var right = tile.More ? MoreRightDp + MoreDp : 0;
        pill.AddView(name, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) { LeftMargin = Dp(LabelLeftDp), RightMargin = Dp(right) });
        if (tile.More)
        {
            pill.AddView(Sign(Resource.Drawable.ic_grid_more, colors.Glyph), new LayoutParams(Dp(MoreDp), Dp(MoreDp), GravityFlags.Right | GravityFlags.CenterVertical) { RightMargin = Dp(MoreRightDp) });
        }

        return pill;
    }

    /// <summary>✏: an outlined pill with its sign, to make the grid one's own.</summary>
    private FrameLayout EditButton(Action edit)
    {
        var outline = new GradientDrawable();
        outline.SetCornerRadius(Dp(EditHeightDp) / 2f);
        outline.SetStroke(Dp(EditLineDp), colors.Outline);
        var button = new FrameLayout(Context!) { ContentDescription = "✏", Background = new RippleDrawable(ColorStateList.ValueOf(colors.Function), outline, null), Clickable = true };
        button.Click += (_, _) => edit();
        button.AddView(Sign(Resource.Drawable.ic_grid_edit, colors.Glyph), new LayoutParams(Dp(SignDp), Dp(SignDp), GravityFlags.Center));
        return button;
    }

    private ImageView Sign(int icon, Color color)
    {
        var sign = new ImageView(Context) { ImageTintList = ColorStateList.ValueOf(color) };
        sign.SetImageResource(icon);
        sign.SetScaleType(ImageView.ScaleType.FitCenter);
        return sign;
    }

    private static GradientDrawable Round(Color color)
    {
        var dot = new GradientDrawable();
        dot.SetShape(ShapeType.Oval);
        dot.SetColor(color);
        return dot;
    }

    /// <summary>A swipe across, past the touch's slop, is the grid's: it turns the page as the finger lifts.</summary>
    public override bool OnInterceptTouchEvent(MotionEvent? motion)
    {
        if (motion is null)
        {
            return false;
        }

        switch (motion.ActionMasked)
        {
            case MotionEventActions.Down:
                downX = motion.GetX();
                turning = false;
                break;
            case MotionEventActions.Move when Math.Abs(motion.GetX() - downX) > touchSlop:
                turning = true;
                break;
        }

        return turning;
    }

    public override bool OnTouchEvent(MotionEvent? motion)
    {
        if (motion?.ActionMasked == MotionEventActions.Down)
        {
            downX = motion.GetX();
        }
        else if (motion?.ActionMasked == MotionEventActions.Up)
        {
            var moved = motion.GetX() - downX;
            if (Math.Abs(moved) > Width / 6f)
            {
                Show(shown + (moved < 0 ? 1 : -1));
            }

            turning = false;
        }

        return true;
    }
}
