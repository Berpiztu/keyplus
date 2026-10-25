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
/// The standard keyboard's emoji (a long press on the comma, or ☺), in the
/// keys' place, the toolbar over it, as the Windows keyboard's: a bar with ←
/// in a grey circle, Buscar and a tab for each group, the one in sight in a
/// blue circle, scrolling across, and ⌫ at its right; the emoji under a grey
/// name for each group, those typed lately first, in rows that scroll, a tap
/// typing one. No bar under them, as Gboard's ABC, ☺, GIF, stickers, :-) and
/// ⌫: ← is ABC, the toolbar has ☺, and that bar's height kept a keyboard from
/// showing rows of emoji. The bar's sizes are Gboard's on the Fold, pixels
/// measured there over 2.25; its colours the device's (<see cref="StandardColors"/>).
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
    }

    /// <summary>An emoji tapped: to be typed.</summary>
    public Action<string>? Typed { get; set; }

    public Action? Deleted { get; set; }

    /// <summary>←: the keys again.</summary>
    public Action? Closed { get; set; }

    /// <summary>A part not made yet (Buscar): what it is, to say so.</summary>
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
        bar.AddView(DeleteButton(context), new LayoutParams(Dp(BackDp), Dp(BackDp)) { LeftMargin = Dp(6), RightMargin = Dp(8) });
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

    /// <summary>⌫ as ←, a circle of its size, once on the touch and again while held, as a key's.</summary>
    private FrameLayout DeleteButton(Context context)
    {
        var button = new FrameLayout(context) { ContentDescription = "⌫" };
        var sign = new ImageView(context) { ImageTintList = ColorStateList.ValueOf(colors.Glyph) };
        sign.SetImageResource(Resource.Drawable.ic_key_backspace);
        button.AddView(sign, new FrameLayout.LayoutParams(Dp(TabIconDp), Dp(TabIconDp), GravityFlags.Center));
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
        shape.SetCornerRadius(Dp(SearchHeightDp) / 2f);
        return shape;
    }
}

/// <summary>
/// The emoji panel's list, drawn on one view, only the rows in sight: each
/// section's grey name, then its emoji across, 4 apart across and down, as
/// the Windows keyboard's; a tap types one.
/// </summary>
public sealed class EmojiGrid : View
{
    private const float PixelsPerDp = 2.25f;
    private const float HeaderSp = 20 / 0.711f / PixelsPerDp;
    private const float SideDp = 12 / PixelsPerDp;

    /// <summary>An emoji's size and the room between two, across and down, the owner's (2026-10-25), as on Windows: Gboard's 37 dp emoji 9 apart left two rows in a keyboard of a window's height.</summary>
    private const float EmojiDp = 30;
    private const float EmojiGapDp = 4;

    /// <summary>A row's height, and a column's least width.</summary>
    private const float RowDp = EmojiDp + EmojiGapDp;
    private const float CellDp = EmojiDp + EmojiGapDp;

    /// <summary>
    /// A section's name nearer its own emoji than the row above it, so each name is read with its emoji: 6 from a row's
    /// emoji to the next name's capitals and 3 from the name's lowest letters to its emoji, as drawn (the owner's,
    /// 2026-10-25, as on Windows).
    /// </summary>
    private const float AboveNameDp = 6;
    private const float UnderNameDp = 3;

    /// <summary>The room a row leaves above and under its emoji.</summary>
    private const float RowGapDp = (RowDp - EmojiDp) / 2;

    private readonly StandardColors colors;
    private readonly float density;
    private readonly float scaledDensity;
    private readonly int touchSlop;
    private readonly Paint emoji = new(PaintFlags.AntiAlias);
    private readonly Paint header = new(PaintFlags.AntiAlias);

    /// <summary>A name's ink, in pixels: its capitals' height over the baseline and its lowest letters' depth under it.</summary>
    private readonly float nameCaps;
    private readonly float nameDescent;

    /// <summary>"😀"'s ink, in pixels from where it is drawn: its middle, across and down, to draw each emoji in the middle of its place.</summary>
    private readonly float emojiMiddleX;
    private readonly float emojiMiddleY;
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
        var ink = new Rect();
        header.GetTextBounds("H", 0, 1, ink);
        nameCaps = -ink.Top;
        header.GetTextBounds("gjpqy", 0, 5, ink);
        nameDescent = ink.Bottom;

        // Sized by what "😀" draws, its larger side, not by its advance, and drawn by its middle: the font centres its
        // line, not its drawing.
        const string probe = "😀";
        emoji.TextSize = 100;
        emoji.GetTextBounds(probe, 0, probe.Length, ink);
        emoji.TextSize = 100 * Dp(EmojiDp) / Math.Max(1, Math.Max(ink.Width(), ink.Height()));
        emoji.GetTextBounds(probe, 0, probe.Length, ink);
        emojiMiddleX = ink.ExactCenterX();
        emojiMiddleY = ink.ExactCenterY();
    }

    /// <summary>A section's name's box, in pixels: its room above, its ink and its room under.</summary>
    private float HeaderHeight => Dp(AboveNameDp - RowGapDp) + nameCaps + nameDescent + Dp(UnderNameDp - RowGapDp);

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
        tops = [];
        var height = 0f;
        foreach (var (_, items) in sections)
        {
            tops.Add((int)height);
            if (items.Count > 0)
            {
                height += HeaderHeight + (((items.Count + columns - 1) / columns) * Dp(RowDp));
            }
        }

        SetMeasuredDimension(width, (int)Math.Ceiling(height));
    }

    protected override void OnDraw(Canvas canvas)
    {
        var shown = canvas.ClipBounds;
        for (var s = 0; s < sections.Count; s++)
        {
            var (title, items) = sections[s];
            if (items.Count == 0)
            {
                continue;
            }

            var top = tops[s];
            canvas.DrawText(title, Dp(SideDp) + Dp(8), top + Dp(AboveNameDp - RowGapDp) + nameCaps, header);
            var rowsTop = top + HeaderHeight;
            for (var i = 0; i < items.Count; i++)
            {
                var y = rowsTop + ((i / columns) * Dp(RowDp));
                if (y + Dp(RowDp) < shown.Top || y > shown.Bottom)
                {
                    continue;
                }

                canvas.DrawText(items[i], Dp(SideDp) + (((i % columns) + 0.5f) * cell) - emojiMiddleX, y + (Dp(RowDp) / 2) - emojiMiddleY, emoji);
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
            var rowsTop = tops[s] + HeaderHeight;
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
