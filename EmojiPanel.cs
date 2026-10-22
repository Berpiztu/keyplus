using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Widget;
using Orientation = Android.Widget.Orientation;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The standard keyboard's emoji, Gboard's as the owner's Fold has it
/// (a long press on the comma): over them ← in a grey circle, Buscar and a
/// tab for each group, the one in sight in a blue circle, all scrolling
/// across; the emoji under a grey name for each group, those typed lately
/// first, in rows that scroll, a tap typing one; under them ABC, the emoji
/// (in use), GIF, stickers, :-) and ⌫. Its sizes are Gboard's on the Fold,
/// pixels measured there over 2.25; its colours the device's
/// (<see cref="StandardColors"/>).
/// </summary>
public sealed class EmojiPanel : LinearLayout
{
    private const float PixelsPerDp = 2.25f;
    private const float BarDp = 48;
    private const float BackDp = 70 / PixelsPerDp;
    private const float SearchWidthDp = 158 / PixelsPerDp;
    private const float SearchHeightDp = 74 / PixelsPerDp;
    private const float TabDp = 72 / PixelsPerDp;

    /// <summary>A tab's sign: 35 px drawn, Material's 800 of its 960.</summary>
    private const float TabIconDp = 35f / (800f / 960) / PixelsPerDp;

    /// <summary>The bottom bar: 114 px tall, its pills 122 × 82 px, its signs 43 px drawn, ABC's capitals 24 px.</summary>
    private const float BottomDp = 114 / PixelsPerDp;
    private const float PillWidthDp = 122 / PixelsPerDp;
    private const float PillHeightDp = 82 / PixelsPerDp;
    private const float PillIconDp = 43f / (800f / 960) / PixelsPerDp;
    private const float AbcSp = 24 / 0.711f / PixelsPerDp;
    private const float SmallSp = 20 / 0.711f / PixelsPerDp;

    private readonly StandardColors colors;
    private readonly float density;
    private readonly EmojiGrid grid;
    private readonly ScrollView scroller;
    private readonly LinearLayout tabs;
    private readonly Handler handler = new(Looper.MainLooper!);
    private Java.Lang.Runnable? repeating;
    private int shownTab = -1;

    public EmojiPanel(Context context, StandardColors colors) : base(context)
    {
        this.colors = colors;
        density = context.Resources!.DisplayMetrics!.Density;
        Orientation = Orientation.Vertical;
        SetBackgroundColor(colors.Low);
        tabs = new LinearLayout(context) { Orientation = Orientation.Horizontal };

        grid = new EmojiGrid(context, colors) { Chosen = Choose };
        scroller = new ScrollView(context) { VerticalScrollBarEnabled = false };
        scroller.AddView(grid);
        scroller.ScrollChange += (_, _) => MarkTab(grid.SectionAt(scroller.ScrollY));

        AddView(TopBar(context), new LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(BarDp)));
        AddView(scroller, new LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
        AddView(BottomBar(context), new LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(BottomDp)));
    }

    /// <summary>An emoji tapped: to be typed.</summary>
    public Action<string>? Typed { get; set; }

    public Action? Deleted { get; set; }

    /// <summary>← or ABC: the keys again.</summary>
    public Action? Closed { get; set; }

    /// <summary>A part not made yet (Buscar, GIF, stickers, :-)): what it is, to say so.</summary>
    public Action<string>? NotMade { get; set; }

    /// <summary>The emoji as they are now, those typed lately first, from the top.</summary>
    public void Fill()
    {
        var recent = Emojis.Recent(Context!);
        var groups = Emojis.Groups(Context!);
        grid.Sections = [(Context!.GetString(Resource.String.emoji_recent), recent), .. groups.Select(group => (group.Name, group.Items))];
        FillTabs(groups);
        scroller.ScrollTo(0, 0);
        MarkTab(recent.Count > 0 ? 0 : 1);
    }

    private int Dp(float value) => (int)Math.Round(value * density);

    private void Choose(string emoji)
    {
        Emojis.Used(Context!, emoji);
        Typed?.Invoke(emoji);
    }

    private LinearLayout TopBar(Context context)
    {
        var bar = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        bar.SetGravity(GravityFlags.CenterVertical);
        var back = Round(Resource.Drawable.ic_tool_back, colors.High, colors.Glyph, Dp(BackDp), Dp(TabIconDp), () => Closed?.Invoke());
        back.ContentDescription = "ABC";
        bar.AddView(back, new LayoutParams(Dp(BackDp), Dp(BackDp)) { LeftMargin = Dp(8), RightMargin = Dp(6) });

        var across = new HorizontalScrollView(context) { HorizontalScrollBarEnabled = false };
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.AddView(Search(context), new LayoutParams(Dp(SearchWidthDp), Dp(SearchHeightDp)) { RightMargin = Dp(6) });
        row.AddView(tabs, new LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(TabDp)));
        across.AddView(row, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent));
        bar.AddView(across, new LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        return bar;
    }

    /// <summary>Buscar: a grey pill with its sign and its word, cut short where it does not fit, as Gboard's "Bu…".</summary>
    private LinearLayout Search(Context context)
    {
        var search = new LinearLayout(context) { Orientation = Orientation.Horizontal, Background = Pill(colors.High), Clickable = true };
        search.SetGravity(GravityFlags.CenterVertical);
        search.SetPadding(Dp(8), 0, Dp(8), 0);
        var word = context.GetString(Resource.String.search);
        search.Click += (_, _) => NotMade?.Invoke(word);
        var sign = new ImageView(context) { ImageTintList = ColorStateList.ValueOf(colors.Glyph) };
        sign.SetImageResource(Resource.Drawable.ic_emoji_search);
        search.AddView(sign, new LayoutParams(Dp(TabIconDp), Dp(TabIconDp)));
        var text = new TextView(context) { Text = word, Ellipsize = TextUtils.TruncateAt.End };
        text.SetSingleLine(true);
        text.SetTextColor(colors.Glyph);
        text.SetTextSize(ComplexUnitType.Sp, SmallSp);
        search.AddView(text, new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { LeftMargin = Dp(4) });
        return search;
    }

    /// <summary>A tab for those typed lately, then one for each group; tapped, the list at its group.</summary>
    private void FillTabs(IReadOnlyList<EmojiGroup> groups)
    {
        tabs.RemoveAllViews();
        int[] icons = [Resource.Drawable.ic_emoji_recent, .. groups.Select(group => group.Icon)];
        for (var i = 0; i < icons.Length; i++)
        {
            var section = i;
            tabs.AddView(Round(icons[i], Color.Transparent, colors.Outline, Dp(TabDp), Dp(TabIconDp), () => scroller.SmoothScrollTo(0, grid.SectionTop(section))),
                new LayoutParams(Dp(TabDp), Dp(TabDp)));
        }

        shownTab = -1;
    }

    /// <summary>The tab of the group in sight in a blue circle, its sign darker.</summary>
    private void MarkTab(int section)
    {
        if (section == shownTab || section >= tabs.ChildCount)
        {
            return;
        }

        for (var i = 0; i < tabs.ChildCount; i++)
        {
            var tab = (FrameLayout)tabs.GetChildAt(i)!;
            ((GradientDrawable)((RippleDrawable)tab.Background!).GetDrawable(0)!).SetColor(i == section ? colors.GridCircle : Color.Transparent);
            ((ImageView)tab.GetChildAt(0)!).ImageTintList = ColorStateList.ValueOf(i == section ? colors.Glyph : colors.Outline);
        }

        shownTab = section;
    }

    /// <summary>ABC, the emoji in use, GIF, stickers and :-) on grey pills, ⌫; spread across.</summary>
    private LinearLayout BottomBar(Context context)
    {
        var bar = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        bar.SetGravity(GravityFlags.CenterVertical);
        var abc = new TextView(context) { Text = "ABC", Gravity = GravityFlags.Center, Clickable = true };
        abc.SetTextColor(colors.Glyph);
        abc.SetTextSize(ComplexUnitType.Sp, AbcSp);
        abc.Click += (_, _) => Closed?.Invoke();
        View[] parts =
        [
            abc,
            PillButton(Resource.Drawable.ic_key_emoji, null, colors.GridCircle, () => { }),
            PillButton(Resource.Drawable.ic_grid_gif, "GIF", colors.High, () => NotMade?.Invoke("GIF")),
            PillButton(Resource.Drawable.ic_tool_stickers, context.GetString(Resource.String.stickers), colors.High, () => NotMade?.Invoke(context.GetString(Resource.String.stickers))),
            PillButton(0, ":-)", colors.High, () => NotMade?.Invoke(":-)")),
            DeleteButton(context),
        ];
        foreach (var part in parts)
        {
            var place = new FrameLayout(context);
            place.AddView(part, new FrameLayout.LayoutParams(part is TextView ? ViewGroup.LayoutParams.WrapContent : Dp(PillWidthDp), Dp(PillHeightDp), GravityFlags.Center));
            bar.AddView(place, new LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
        }

        return bar;
    }

    /// <summary>A pill with a sign, or with its words where it has no sign (:-)).</summary>
    private FrameLayout PillButton(int icon, string? words, Color ground, Action pressed)
    {
        var pill = new FrameLayout(Context!) { Background = new RippleDrawable(ColorStateList.ValueOf(colors.Function), Pill(ground), null), Clickable = true, ContentDescription = words };
        pill.Click += (_, _) => pressed();
        if (icon != 0)
        {
            var sign = new ImageView(Context) { ImageTintList = ColorStateList.ValueOf(colors.Glyph) };
            sign.SetImageResource(icon);
            pill.AddView(sign, new FrameLayout.LayoutParams(Dp(PillIconDp), Dp(PillIconDp), GravityFlags.Center));
        }
        else
        {
            var text = new TextView(Context) { Text = words, Gravity = GravityFlags.Center };
            text.SetTextColor(colors.Glyph);
            text.SetTextSize(ComplexUnitType.Sp, AbcSp);
            pill.AddView(text, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        }

        return pill;
    }

    /// <summary>⌫, once on the touch and again while held, as a key's.</summary>
    private FrameLayout DeleteButton(Context context)
    {
        var button = new FrameLayout(context) { ContentDescription = "⌫" };
        var sign = new ImageView(context) { ImageTintList = ColorStateList.ValueOf(colors.Glyph) };
        sign.SetImageResource(Resource.Drawable.ic_key_backspace);
        button.AddView(sign, new FrameLayout.LayoutParams(Dp(PillIconDp), Dp(PillIconDp), GravityFlags.Center));
        button.Touch += (_, touch) =>
        {
            switch (touch.Event!.ActionMasked)
            {
                case MotionEventActions.Down:
                    Deleted?.Invoke();
                    repeating = new Java.Lang.Runnable(() =>
                    {
                        Deleted?.Invoke();
                        handler.PostDelayed(repeating!, KeyPanel.RepeatIntervalMs);
                    });
                    handler.PostDelayed(repeating, KeyPanel.RepeatDelayMs);
                    break;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    if (repeating is not null)
                    {
                        handler.RemoveCallbacks(repeating);
                        repeating = null;
                    }

                    break;
            }

            touch.Handled = true;
        };
        return button;
    }

    private FrameLayout Round(int icon, Color ground, Color glyph, int size, int iconSize, Action pressed)
    {
        var shape = new GradientDrawable();
        shape.SetShape(ShapeType.Oval);
        shape.SetColor(ground);
        var round = new FrameLayout(Context!) { Background = new RippleDrawable(ColorStateList.ValueOf(colors.Function), shape, null), Clickable = true };
        round.Click += (_, _) => pressed();
        var sign = new ImageView(Context) { ImageTintList = ColorStateList.ValueOf(glyph) };
        sign.SetImageResource(icon);
        round.AddView(sign, new FrameLayout.LayoutParams(iconSize, iconSize, GravityFlags.Center));
        return round;
    }

    private GradientDrawable Pill(Color ground)
    {
        var shape = new GradientDrawable();
        shape.SetColor(ground);
        shape.SetCornerRadius(Dp(PillHeightDp) / 2f);
        return shape;
    }
}

/// <summary>
/// The emoji panel's list, drawn on one view, only the rows in sight: each
/// section's grey name, then its emoji across in cells of at least 100 px
/// of the Fold (eight on its keyboard held in one hand), rows 113 px apart,
/// each emoji 85 px wide; a tap types one.
/// </summary>
public sealed class EmojiGrid : View
{
    private const float PixelsPerDp = 2.25f;
    private const float CellDp = 100 / PixelsPerDp;
    private const float RowDp = 113 / PixelsPerDp;
    private const float HeaderDp = 87 / PixelsPerDp;
    private const float HeaderSp = 20 / 0.711f / PixelsPerDp;
    private const float SideDp = 12 / PixelsPerDp;

    /// <summary>An emoji's width, of its cell.</summary>
    private const float EmojiShare = 85f / 100;

    private readonly StandardColors colors;
    private readonly float density;
    private readonly float scaledDensity;
    private readonly int touchSlop;
    private readonly Paint emoji = new(PaintFlags.AntiAlias) { TextAlign = Paint.Align.Center };
    private readonly Paint header = new(PaintFlags.AntiAlias);
    private IReadOnlyList<(string Title, IReadOnlyList<string> Items)> sections = [];
    private List<int> tops = [];
    private int columns = 1;
    private float cell;
    private float downX;
    private float downY;

    public EmojiGrid(Context context, StandardColors colors) : base(context)
    {
        this.colors = colors;
        density = context.Resources!.DisplayMetrics!.Density;
        scaledDensity = TypedValue.ApplyDimension(ComplexUnitType.Sp, 1, context.Resources.DisplayMetrics);
        touchSlop = ViewConfiguration.Get(context)!.ScaledTouchSlop;
        header.Color = colors.Outline;
        header.TextSize = HeaderSp * scaledDensity;
    }

    public Action<string>? Chosen { get; set; }

    /// <summary>The sections, each its name and its emoji; one with none left out.</summary>
    public IReadOnlyList<(string Title, IReadOnlyList<string> Items)> Sections
    {
        set
        {
            sections = value;
            RequestLayout();
            Invalidate();
        }
    }

    /// <summary>Where a section starts, for its tab; one left out, where the next starts.</summary>
    public int SectionTop(int section) => section < tops.Count ? tops[section] : 0;

    /// <summary>The section at a height of the list, for its tab to be marked.</summary>
    public int SectionAt(int y)
    {
        var at = 0;
        for (var i = 0; i < tops.Count; i++)
        {
            if (sections[i].Items.Count > 0 && tops[i] <= y + 1)
            {
                at = i;
            }
        }

        return at;
    }

    private float Dp(float value) => value * density;

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        var width = MeasureSpec.GetSize(widthMeasureSpec);
        columns = Math.Max(1, (int)((width - (2 * Dp(SideDp))) / Dp(CellDp)));
        cell = (width - (2 * Dp(SideDp))) / columns;
        emoji.TextSize = 100;
        emoji.TextSize = 100 * cell * EmojiShare / Math.Max(1, emoji.MeasureText("😀"));
        tops = [];
        var height = 0f;
        foreach (var (_, items) in sections)
        {
            tops.Add((int)height);
            if (items.Count > 0)
            {
                height += Dp(HeaderDp) + (((items.Count + columns - 1) / columns) * Dp(RowDp));
            }
        }

        SetMeasuredDimension(width, (int)Math.Ceiling(height));
    }

    protected override void OnDraw(Canvas canvas)
    {
        var shown = canvas.ClipBounds;
        var middle = (emoji.Ascent() + emoji.Descent()) / 2;
        for (var s = 0; s < sections.Count; s++)
        {
            var (title, items) = sections[s];
            if (items.Count == 0)
            {
                continue;
            }

            var top = tops[s];
            canvas.DrawText(title, Dp(SideDp) + Dp(8), top + (Dp(HeaderDp) / 2) - ((header.Ascent() + header.Descent()) / 2), header);
            var rowsTop = top + Dp(HeaderDp);
            for (var i = 0; i < items.Count; i++)
            {
                var y = rowsTop + ((i / columns) * Dp(RowDp));
                if (y + Dp(RowDp) < shown.Top || y > shown.Bottom)
                {
                    continue;
                }

                canvas.DrawText(items[i], Dp(SideDp) + ((i % columns) + 0.5f) * cell, y + (Dp(RowDp) / 2) - middle, emoji);
            }
        }
    }

    /// <summary>A tap, the finger lifted where it went down: the emoji under it; a drag is the list's scroll.</summary>
    public override bool OnTouchEvent(MotionEvent? motion)
    {
        switch (motion?.ActionMasked)
        {
            case MotionEventActions.Down:
                downX = motion.GetX();
                downY = motion.GetY();
                return true;
            case MotionEventActions.Up when Math.Abs(motion.GetX() - downX) < touchSlop && Math.Abs(motion.GetY() - downY) < touchSlop:
                if (EmojiAt(motion.GetX(), motion.GetY()) is { } chosen)
                {
                    PerformHapticFeedback(FeedbackConstants.KeyboardTap);
                    Chosen?.Invoke(chosen);
                }

                return true;
        }

        return true;
    }

    private string? EmojiAt(float x, float y)
    {
        for (var s = sections.Count - 1; s >= 0; s--)
        {
            var rowsTop = tops[s] + Dp(HeaderDp);
            if (sections[s].Items.Count == 0 || y < tops[s])
            {
                continue;
            }

            if (y < rowsTop)
            {
                return null;
            }

            var column = (int)((x - Dp(SideDp)) / cell);
            var at = ((int)((y - rowsTop) / Dp(RowDp)) * columns) + column;
            return column >= 0 && column < columns && at < sections[s].Items.Count ? sections[s].Items[at] : null;
        }

        return null;
    }
}
