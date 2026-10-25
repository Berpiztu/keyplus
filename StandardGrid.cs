using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;
using Orientation = Android.Widget.Orientation;

namespace Berpiztu.KeyPlus;

/// <summary>A button of the standard keyboard's grid or toolbar: its id, its name, its sign, what it does, whether it opens more (›), and whether what it turns on is on (lit, in the accent's colour).</summary>
public sealed record GridTile(string Id, string Label, int Icon, Action Chosen, bool More = false, bool On = false);

/// <summary>
/// The standard keyboard's grid, Gboard's (⊞) as the owner's Fold has it
/// (docs/tODO/photos/gboard/fold-grid-*.png), in the keys' place: pages of
/// buttons two by two, as wide as the keyboard leaves them (the owner's
/// wish: names that fit rather than scroll), each a pill with its sign and its name, › where it
/// opens more; under them a dot for each page, the one in sight darker, and
/// ✏ at the right. A swipe across turns the page. Customised (✏), as
/// Gboard's: "Mantén pulsado y arrastra para personalizar" and every button
/// in one list that scrolls, in a blue outline, two across or, on a wide
/// keyboard, three; a button held is lifted and dragged (<see cref="HoldToDrag"/>),
/// a gap where it would go. Its sizes are Gboard's on the Fold, pixels
/// measured there over 2.25; its colours the device's (<see cref="StandardColors"/>).
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

    /// <summary>
    /// Customising, on the Fold: the hint in a band 130 px tall under the
    /// toolbar; the list's outline 4 px, its corners 67 px, the buttons 36 px
    /// in from it and the outline 62 px in from the keyboard's sides.
    /// </summary>
    private const float HintBandDp = 130 / PixelsPerDp;
    public const float OutlineDp = 4 / PixelsPerDp;
    private const float ListCornerDp = 67 / PixelsPerDp;
    private const float ListPaddingDp = 36 / PixelsPerDp;
    private const float ListSideDp = 62 / PixelsPerDp;

    /// <summary>The grid's buttons on a page: two rows of two, as the Fold's.</summary>
    private const int Columns = 2;
    private const int Rows = 2;

    /// <summary>Customising, the most buttons across: Gboard's three on a wide keyboard.</summary>
    private const int MostColumns = 3;

    private readonly StandardColors colors;
    private readonly float density;
    private readonly int touchSlop;
    private readonly LinearLayout page;
    private readonly FrameLayout footer;
    private readonly LinearLayout dots;
    private IReadOnlyList<GridTile> tiles = [];
    private int shown;
    private float downX;
    private bool turning;

    /// <summary>While customising: the hint and the list, the list's cells in order (a gap's too), and its scroll kept as it is made again.</summary>
    private LinearLayout? editing;
    private ScrollView? list;
    private List<View> cells = [];

    public StandardGrid(Context context, StandardColors colors, Action edit) : base(context)
    {
        this.colors = colors;
        density = context.Resources!.DisplayMetrics!.Density;
        touchSlop = ViewConfiguration.Get(context)!.ScaledTouchSlop;
        SetBackgroundColor(colors.Ground);

        // The pills as wide as the keyboard leaves them, the customising list's sides apart: a name scrolls only where it does not fit.
        page = new LinearLayout(context) { Orientation = Orientation.Vertical };
        AddView(page, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top)
        {
            TopMargin = Dp(TopDp),
            LeftMargin = Dp(ListSideDp),
            RightMargin = Dp(ListSideDp),
        });

        footer = new FrameLayout(context);
        dots = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        dots.SetGravity(GravityFlags.CenterVertical);
        footer.AddView(dots, new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent, GravityFlags.Center));
        footer.AddView(EditButton(edit), new LayoutParams(Dp(EditWidthDp), Dp(EditHeightDp), GravityFlags.Right | GravityFlags.CenterVertical));
        AddView(footer, new LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(EditHeightDp), GravityFlags.Bottom)
        {
            BottomMargin = Dp(BottomDp),
            LeftMargin = Dp(ListSideDp - EditLineDp),
            RightMargin = Dp(ListSideDp - EditLineDp),
        });
    }

    /// <summary>The grid's buttons, in their order; shown from the first page, customising over.</summary>
    public IReadOnlyList<GridTile> Tiles
    {
        set
        {
            StopEditing();
            tiles = value;
            Show(0);
        }
    }

    /// <summary>
    /// Customising: every button in one list, a gap at <paramref name="gap"/>
    /// (none, -1) where the one dragged would go; each held lifts it,
    /// <paramref name="lift"/> told where on the screen.
    /// </summary>
    public void ShowEditing(IReadOnlyList<GridTile> buttons, int gap, Action<GridTile, float, float> lift)
    {
        var scrolled = list?.ScrollY ?? 0;
        page.Visibility = ViewStates.Gone;
        footer.Visibility = ViewStates.Gone;
        if (editing is not null)
        {
            RemoveView(editing);
        }

        editing = new LinearLayout(Context) { Orientation = Orientation.Vertical };
        var hint = new TextView(Context) { Text = Context!.GetString(Resource.String.customize_hint), Gravity = GravityFlags.Center };
        hint.SetTextColor(colors.Glyph);
        hint.SetTextSize(ComplexUnitType.Sp, LabelSp);
        hint.SetSingleLine(true);
        editing.AddView(hint, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(HintBandDp)));

        // The outline goes with the list as it scrolls, as Gboard's: its top out of sight once scrolled down.
        var box = new GradientDrawable();
        box.SetColor(colors.Dim);
        box.SetCornerRadius(Dp(ListCornerDp));
        box.SetStroke(Dp(OutlineDp), colors.Primary);
        list = new ScrollView(Context) { VerticalScrollBarEnabled = false };
        var rows = new LinearLayout(Context) { Orientation = Orientation.Vertical, Background = box };
        var padding = Dp(ListPaddingDp);
        rows.SetPadding(padding, padding, padding, padding);
        list.AddView(rows);
        editing.AddView(list, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f) { LeftMargin = Dp(ListSideDp), RightMargin = Dp(ListSideDp) });
        AddView(editing, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        var inner = Width - (2 * Dp(ListSideDp)) - (2 * padding);
        var across = Math.Clamp((inner + Dp(ColumnGapDp)) / Dp(PillWidthDp + ColumnGapDp), Columns, MostColumns);
        cells = [];
        var shownTiles = buttons.Select(tile => (GridTile?)tile).ToList();
        if (gap >= 0)
        {
            shownTiles.Insert(Math.Min(gap, shownTiles.Count), null);
        }

        for (var first = 0; first < shownTiles.Count; first += across)
        {
            var line = new LinearLayout(Context) { Orientation = Orientation.Horizontal };
            for (var column = 0; column < across; column++)
            {
                var place = new LinearLayout.LayoutParams(0, Dp(PillHeightDp), 1f) { LeftMargin = column > 0 ? Dp(ColumnGapDp) : 0 };
                if (first + column >= shownTiles.Count)
                {
                    line.AddView(new View(Context), place);
                    continue;
                }

                var cell = shownTiles[first + column] is { } tile ? Pill(tile, held: (x, y) => lift(tile, x, y)) : new View(Context);
                cells.Add(cell);
                line.AddView(cell, place);
            }

            rows.AddView(line, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = first > 0 ? Dp(RowGapDp) : 0 });
        }

        list.Post(() => list.ScrollTo(0, scrolled));
    }

    /// <summary>Customising over: the pages again.</summary>
    private void StopEditing()
    {
        if (editing is not null)
        {
            RemoveView(editing);
            editing = null;
            list = null;
            cells = [];
        }

        page.Visibility = ViewStates.Visible;
        footer.Visibility = ViewStates.Visible;
    }

    /// <summary>Whether a point on the screen is over the customising list.</summary>
    public bool OverList(float x, float y) => list is not null && Contains(list, x, y);

    /// <summary>The list's cells as shown, in order, a gap's among them.</summary>
    public IReadOnlyList<View> Cells => cells;

    /// <summary>The nearest of some views to a point on the screen, by its index; -1 with none.</summary>
    public static int Nearest(IReadOnlyList<View> views, float x, float y)
    {
        var at = new int[2];
        var best = -1;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < views.Count; i++)
        {
            views[i].GetLocationOnScreen(at);
            var dx = x - (at[0] + (views[i].Width / 2f));
            var dy = y - (at[1] + (views[i].Height / 2f));
            var distance = (dx * dx) + (dy * dy);
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Whether a point on the screen is over a view.</summary>
    public static bool Contains(View view, float x, float y)
    {
        var at = new int[2];
        view.GetLocationOnScreen(at);
        return x >= at[0] && x < at[0] + view.Width && y >= at[1] && y < at[1] + view.Height;
    }

    /// <summary>
    /// A view held still for a long press lifts what it shows: haptic, then
    /// <paramref name="lift"/> with where on the screen. Moved first, it is a
    /// scroll or a swipe, not a lift.
    /// </summary>
    public static void HoldToDrag(View view, Action<float, float> lift)
    {
        var slop = ViewConfiguration.Get(view.Context!)!.ScaledTouchSlop;
        float downX = 0;
        float downY = 0;
        Java.Lang.Runnable? hold = null;
        view.Touch += (_, touch) =>
        {
            var motion = touch.Event!;
            switch (motion.ActionMasked)
            {
                case MotionEventActions.Down:
                    downX = motion.RawX;
                    downY = motion.RawY;
                    hold = new Java.Lang.Runnable(() =>
                    {
                        hold = null;
                        view.PerformHapticFeedback(FeedbackConstants.LongPress);
                        lift(downX, downY);
                    });
                    view.PostDelayed(hold, ViewConfiguration.LongPressTimeout);
                    break;
                case MotionEventActions.Move when Math.Abs(motion.RawX - downX) > slop || Math.Abs(motion.RawY - downY) > slop:
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    if (hold is not null)
                    {
                        view.RemoveCallbacks(hold);
                        hold = null;
                    }

                    break;
            }

            touch.Handled = true;
        };
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
                var place = new LinearLayout.LayoutParams(0, Dp(PillHeightDp), 1f) { LeftMargin = column > 0 ? Dp(ColumnGapDp) : 0 };
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

    /// <summary>
    /// A button: a pill in the keys' colour, its sign and name, › where it
    /// opens more; tapped, what it does; while customising, held, lifted.
    /// </summary>
    public FrameLayout Pill(GridTile tile, Action<float, float>? held = null)
    {
        var shape = new GradientDrawable();
        shape.SetColor(tile.On ? colors.Primary : colors.Key);
        shape.SetCornerRadius(Dp(PillHeightDp) / 2f);
        var pill = new FrameLayout(Context!)
        {
            ContentDescription = tile.Label,
            Background = new RippleDrawable(ColorStateList.ValueOf(colors.Function), shape, null),
            Clickable = true,
        };
        if (held is null)
        {
            pill.Click += (_, _) => tile.Chosen();
        }
        else
        {
            HoldToDrag(pill, held);
        }

        var glyph = tile.On ? colors.OnPrimary : colors.Glyph;
        pill.AddView(Sign(tile.Icon, glyph), new LayoutParams(Dp(SignDp), Dp(SignDp), GravityFlags.Left | GravityFlags.CenterVertical) { LeftMargin = Dp(SignLeftDp) });

        var name = new TextView(Context) { Text = tile.Label, Ellipsize = Android.Text.TextUtils.TruncateAt.Marquee, Gravity = GravityFlags.CenterVertical };
        name.SetTextColor(glyph);
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

    /// <summary>A pill's size, for a button dragged over the list.</summary>
    public (int Width, int Height) PillSize => (cells.FirstOrDefault(cell => cell.Width > 0)?.Width ?? Dp(PillWidthDp), Dp(PillHeightDp));

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

    /// <summary>A swipe across, past the touch's slop, is the grid's: it turns the page as the finger lifts; never while customising, whose list scrolls.</summary>
    public override bool OnInterceptTouchEvent(MotionEvent? motion)
    {
        if (motion is null || editing is not null)
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
        if (editing is not null)
        {
            return true;
        }

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
