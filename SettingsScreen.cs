using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;
using Orientation = Android.Widget.Orientation;

namespace Berpiztu.KeyPlus;

/// <summary>
/// A setting's row: its title, the line under it, its sign, and what a tap
/// does; with a switch, whether it is on and what turning it does; one of a
/// choice's, shown with all the others, whether it is the one chosen (a tap
/// chooses it); whether a tap opens a page of its own.
/// </summary>
public sealed record SettingsRow(
    string Title,
    string? Summary = null,
    int Icon = 0,
    Action? Tapped = null,
    bool? On = null,
    Action<bool>? Switched = null,
    bool? Chosen = null,
    bool Opens = false);

/// <summary>A page of settings: its title and its rows in groups, each group a card; or, in their place, a view of its own that takes the page's room (Tema).</summary>
public sealed record SettingsPage(string Title, IReadOnlyList<IReadOnlyList<SettingsRow>> Groups, View? Body = null);

/// <summary>
/// The keyboard's app as Gboard's settings (docs/tODO/photos/gboard/fold-settings.png):
/// a big title, then the rows in groups, each group a card whose ends are
/// rounded and whose rows stand a little apart; a row opens its page, or
/// switches. Where the screen is wide (the Fold open, 600 dp and more) the
/// list stands at the left, a third, and the page at the right. Its sizes
/// are Gboard's on the Fold, pixels over 2.25; its colours the device's
/// palette, light or dark as the device is: the list's ground
/// system_surface_dim, a page's system_surface_container, the cards
/// system_surface_bright, the words system_on_surface and the signs those at
/// 35 % (Gboard's grey, 164 on a card of 250).
/// </summary>
public sealed class SettingsScreen
{
    private const float PixelsPerDp = 2.25f;
    private const float RowDp = 162 / PixelsPerDp;
    private const float RowGapDp = 5 / PixelsPerDp;

    /// <summary>A choice's rows, and the title over them, in dp: one line each, small, as the owner wants them; the mark at four fifths.</summary>
    private const float CompactRowDp = 36;
    private const float CompactMarkScale = 0.8f;
    private const float GroupGapDp = 72 / PixelsPerDp;
    private const float SideDp = 36 / PixelsPerDp;
    private const float OuterCornerDp = 40 / PixelsPerDp;
    private const float InnerCornerDp = 10 / PixelsPerDp;
    private const float SignDp = 24;
    private const float SignLeftDp = 20;
    private const float TextLeftDp = 157 / PixelsPerDp;
    private const float TitleSp = 34 / 0.711f / PixelsPerDp;
    private const float RowTitleSp = 16;
    private const float SummarySp = 14;
    private const float SignAlpha = (250 - 164) / (250f - 1);

    /// <summary>Where the list and its page stand side by side.</summary>
    private const int WideDp = 600;

    /// <summary>The list's share of a wide screen, the owner's: a third, the page the rest, its settings the room they need.</summary>
    private const float ListShare = 1f / 3;

    private readonly Context context;
    private readonly float density;
    private readonly Color ground;
    private readonly Color paneGround;
    private readonly Color card;
    private readonly Color words;
    private readonly FrameLayout root;
    private readonly Stack<SettingsPage> opened = new();
    private SettingsPage? main;
    private View? top;

    /// <summary>Whether the page opened has the whole screen to its view, its title and the list out of sight (Tema's designer).</summary>
    private bool alone;

    public SettingsScreen(Context context)
    {
        this.context = context;
        density = context.Resources!.DisplayMetrics!.Density;
        var dark = (context.Resources.Configuration!.UiMode & UiMode.NightMask) == UiMode.NightYes;
        var surfaces = OperatingSystem.IsAndroidVersionAtLeast(34);
        ground = Read(surfaces ? (dark ? Android.Resource.Color.SystemSurfaceDimDark : Android.Resource.Color.SystemSurfaceDimLight) : (dark ? Android.Resource.Color.SystemNeutral1900 : Android.Resource.Color.SystemNeutral1100));
        paneGround = Read(surfaces ? (dark ? Android.Resource.Color.SystemSurfaceContainerDark : Android.Resource.Color.SystemSurfaceContainerLight) : (dark ? Android.Resource.Color.SystemNeutral1800 : Android.Resource.Color.SystemNeutral150));
        card = Read(surfaces ? (dark ? Android.Resource.Color.SystemSurfaceBrightDark : Android.Resource.Color.SystemSurfaceBrightLight) : (dark ? Android.Resource.Color.SystemNeutral1700 : Android.Resource.Color.SystemNeutral110));
        words = Read(surfaces ? (dark ? Android.Resource.Color.SystemOnSurfaceDark : Android.Resource.Color.SystemOnSurfaceLight) : (dark ? Android.Resource.Color.SystemNeutral150 : Android.Resource.Color.SystemNeutral1900));
        root = new FrameLayout(context);
        root.SetBackgroundColor(ground);
    }

    /// <summary>The screen's view, for the activity to show.</summary>
    public View View => root;

    /// <summary>The colour the screen's bars stand on.</summary>
    public Color Ground => ground;

    private bool Wide => context.Resources!.Configuration!.ScreenWidthDp >= WideDp;

    private int Dp(float value) => (int)Math.Round(value * density);

    private Color Read(int color) => new(context.GetColor(color));

    /// <summary>
    /// The main page shown, with a view over its list (the box to try the
    /// keys in); in a wide screen, where asked, the page of its first row that
    /// opens one beside it. A row that only does something (Elegir el
    /// teclado) is never tapped for it: the list would not be laid out.
    /// </summary>
    public void Show(SettingsPage page, View? above, bool openFirst)
    {
        main = page;
        top = above;
        opened.Clear();
        Lay();
        if (openFirst && Wide && page.Groups.SelectMany(group => group).FirstOrDefault(row => row.Opens) is { } first)
        {
            first.Tapped!();
        }
    }

    /// <summary>A page opened from the main one: in its place, or beside the list on a wide screen.</summary>
    public void Open(SettingsPage page)
    {
        alone = false;
        if (Wide)
        {
            opened.Clear();
        }

        opened.Push(page);
        Lay();
    }

    /// <summary>The page shown made again, its rows as they are now (a switch turned, a value chosen).</summary>
    public void Refresh(SettingsPage page)
    {
        if (opened.Count > 0)
        {
            opened.Pop();
            opened.Push(page);
        }
        else
        {
            main = page;
        }

        Lay();
    }

    /// <summary>Back: the page closed, on a narrow screen; false where there is none to close, the activity's to end.</summary>
    public bool Back()
    {
        if (Wide || opened.Count == 0)
        {
            return false;
        }

        alone = false;
        opened.Pop();
        Lay();
        return true;
    }

    /// <summary>The page opened with the whole screen to its own view, or laid as the others again.</summary>
    public void Alone(bool on)
    {
        alone = on;
        Lay();
    }

    private void Lay()
    {
        root.RemoveAllViews();
        if (main is null)
        {
            return;
        }

        if (alone && opened.TryPeek(out var only) && only.Body is { } body)
        {
            (body.Parent as ViewGroup)?.RemoveView(body);
            body.SetBackgroundColor(paneGround);
            // The screen's whole width to it, the keyboard one to one where it fits.
            body.SetPadding(0, Dp(24), 0, 0);
            root.AddView(body, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            return;
        }

        if (Wide)
        {
            var panes = new LinearLayout(context) { Orientation = Orientation.Horizontal };
            panes.AddView(Pane(main, top, ground, back: false), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, ListShare));
            var right = opened.TryPeek(out var shown) ? Pane(shown, null, paneGround, back: false) : new View(context);
            right.SetBackgroundColor(paneGround);
            panes.AddView(right, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1 - ListShare));
            root.AddView(panes);
            return;
        }

        root.AddView(opened.TryPeek(out var page) ? Pane(page, null, paneGround, back: true) : Pane(main, top, ground, back: false));
    }

    /// <summary>A page: its title (with ← where it closes), a view over its rows, and its groups as cards, scrolling.</summary>
    private View Pane(SettingsPage page, View? above, Color pageGround, bool back)
    {
        var column = new LinearLayout(context) { Orientation = Orientation.Vertical };
        column.SetBackgroundColor(pageGround);
        var header = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);
        header.SetPadding(Dp(SideDp), Dp(24), Dp(SideDp), Dp(16));
        if (back)
        {
            var arrow = new ImageView(context) { ContentDescription = "Back", ImageTintList = ColorStateList.ValueOf(words) };
            arrow.SetImageResource(Resource.Drawable.ic_tool_back);
            arrow.Click += (_, _) => Back();
            header.AddView(arrow, new LinearLayout.LayoutParams(Dp(SignDp), Dp(SignDp)) { RightMargin = Dp(16) });
        }

        var title = new TextView(context) { Text = page.Title };
        title.SetTextColor(words);
        title.SetTextSize(ComplexUnitType.Sp, TitleSp);
        header.AddView(title);
        column.AddView(header);
        if (page.Body is { } body)
        {
            (body.Parent as ViewGroup)?.RemoveView(body);
            body.SetPadding(Dp(SideDp), 0, Dp(SideDp), Dp(SideDp));
            column.AddView(body, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
            return column;
        }

        var list = new LinearLayout(context) { Orientation = Orientation.Vertical };
        list.SetPadding(Dp(SideDp), 0, Dp(SideDp), Dp(GroupGapDp));
        if (above is not null)
        {
            (above.Parent as ViewGroup)?.RemoveView(above);
            list.AddView(above, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { BottomMargin = Dp(16) });
        }

        foreach (var group in page.Groups)
        {
            for (var i = 0; i < group.Count; i++)
            {
                // A choice's rows, and the title over them, close together: one setting, not several.
                var choice = group[i].Chosen is not null;
                var choiceNext = i + 1 < group.Count && group[i + 1].Chosen is not null;
                var place = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
                {
                    TopMargin = i == 0 || choice ? 0 : Dp(RowGapDp),
                    BottomMargin = i == group.Count - 1 ? Dp(GroupGapDp) / 2 : 0,
                };
                list.AddView(Row(group[i], first: i == 0, last: i == group.Count - 1, compact: choice || choiceNext), place);
            }
        }

        var scroll = new WheelScroll(context, Dp(RowDp) / 2) { FillViewport = true };
        scroll.AddView(list);
        column.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
        return column;
    }

    /// <summary>
    /// A list the mouse wheel moves half a row a step: Android moves it 64 dp
    /// times what the wheel reports, and a computer's wheel (the emulator's,
    /// a pointer on DeX or a headset) reports so much that a step leapt pages.
    /// </summary>
    private sealed class WheelScroll(Context context, int step) : ScrollView(context)
    {
        public override bool OnGenericMotionEvent(MotionEvent? motion)
        {
            if (motion is { Action: MotionEventActions.Scroll } && motion.IsFromSource(InputSourceType.ClassPointer)
                && motion.GetAxisValue(Axis.Vscroll) is var wheel and not 0)
            {
                ScrollBy(0, (int)(-Math.Clamp(wheel, -1f, 1f) * step));
                return true;
            }

            return base.OnGenericMotionEvent(motion);
        }
    }

    /// <summary>A row as a card: rounded large at its group's ends, a little elsewhere; its sign, title, line and switch.</summary>
    private View Row(SettingsRow row, bool first, bool last, bool compact)
    {
        var shape = new GradientDrawable();
        shape.SetColor(card);
        float outer = Dp(OuterCornerDp), inner = Dp(InnerCornerDp);
        var topCorner = first ? outer : inner;
        var bottomCorner = last ? outer : inner;
        shape.SetCornerRadii([topCorner, topCorner, topCorner, topCorner, bottomCorner, bottomCorner, bottomCorner, bottomCorner]);
        var frame = new FrameLayout(context) { Background = new RippleDrawable(ColorStateList.ValueOf(paneGround), shape, null) };
        frame.SetMinimumHeight(Dp(compact ? CompactRowDp : RowDp));

        if (row.Icon != 0)
        {
            var sign = new ImageView(context) { ImageTintList = ColorStateList.ValueOf(Color.Argb((int)(255 * SignAlpha), words.R, words.G, words.B)) };
            sign.SetImageResource(row.Icon);
            frame.AddView(sign, new FrameLayout.LayoutParams(Dp(SignDp), Dp(SignDp), GravityFlags.Left | GravityFlags.CenterVertical) { LeftMargin = Dp(SignLeftDp) });
        }

        var texts = new LinearLayout(context) { Orientation = Orientation.Vertical };
        texts.SetGravity(GravityFlags.CenterVertical);
        texts.SetPadding(0, Dp(compact ? 0 : 12), 0, Dp(compact ? 0 : 12));
        var title = new TextView(context) { Text = row.Title };
        title.SetTextColor(words);
        title.SetTextSize(ComplexUnitType.Sp, RowTitleSp);
        texts.AddView(title);
        if (row.Summary is { } summary)
        {
            var line = new TextView(context) { Text = summary };
            line.SetTextColor(words);
            line.SetTextSize(ComplexUnitType.Sp, SummarySp);
            texts.AddView(line);
        }

        var right = row.On is not null ? Dp(72) : Dp(SignLeftDp);
        frame.AddView(texts, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.CenterVertical)
        {
            LeftMargin = row.Icon != 0 || row.Chosen is not null ? Dp(TextLeftDp) : Dp(SignLeftDp),
            RightMargin = right,
        });

        if (row.Chosen is { } chosen)
        {
            var mark = new RadioButton(context) { Checked = chosen, Clickable = false, ScaleX = CompactMarkScale, ScaleY = CompactMarkScale };
            frame.AddView(mark, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Left | GravityFlags.CenterVertical) { LeftMargin = Dp(SignLeftDp) - Dp(8) });
        }

        if (row.On is { } on)
        {
            var toggle = new Switch(context) { Checked = on, Clickable = false };
            frame.AddView(toggle, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Right | GravityFlags.CenterVertical) { RightMargin = Dp(SignLeftDp) });
            frame.Click += (_, _) =>
            {
                toggle.Checked = !toggle.Checked;
                row.Switched?.Invoke(toggle.Checked);
            };
        }
        else if (row.Tapped is { } tapped)
        {
            frame.Click += (_, _) => tapped();
        }

        return frame;
    }
}
