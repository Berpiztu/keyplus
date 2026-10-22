using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Gboard's colours as Gboard takes them, from the device's own palette
/// (Material You), never written here: each part of the keyboard is the
/// system colour Gboard's matches on the owner's Fold (the ground
/// system_surface_container_light, a key system_surface_container_lowest_light,
/// its letters system_on_surface_light, a function key system_accent2_100 and
/// its sign system_accent2_700); before Android 14, which has no surface
/// colours, their neutral tones. The swipe's trail is system_accent1_600,
/// the toolbar's ⊞ circled system_accent1_200 while words are suggested and
/// the lines between the words system_neutral2_200, the grid's dots and ✏'s
/// outline system_outline_light (the dots not in sight at half), as on
/// Gboard on the emulator and the Fold.
/// </summary>
public sealed class StandardColors(Context context)
{
    public Color Ground { get; } = Read(context, OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemSurfaceContainerLight : Android.Resource.Color.SystemNeutral1100);

    public Color Key { get; } = Read(context, OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemSurfaceContainerLowestLight : Android.Resource.Color.SystemNeutral110);

    public Color Glyph { get; } = Read(context, OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemOnSurfaceLight : Android.Resource.Color.SystemNeutral1900);

    public Color Function { get; } = Read(context, Android.Resource.Color.SystemAccent2100);

    /// <summary>A function key while pressed: the next tone down of its own.</summary>
    public Color FunctionPressed { get; } = Read(context, Android.Resource.Color.SystemAccent2200);

    public Color FunctionGlyph { get; } = Read(context, Android.Resource.Color.SystemAccent2700);

    public Color Trail { get; } = Read(context, Android.Resource.Color.SystemAccent1600);

    public Color GridCircle { get; } = Read(context, Android.Resource.Color.SystemAccent1200);

    public Color Divider { get; } = Read(context, Android.Resource.Color.SystemNeutral2200);

    /// <summary>The floating keyboard's outline while resized, its corners and, at 80 %, its edge.</summary>
    public Color Accent { get; } = Read(context, Android.Resource.Color.SystemAccent1600);

    /// <summary>The floating keyboard's Reset while resized: Gboard's 223 grey, the palette's nearest tone.</summary>
    public Color ResetGround { get; } = Read(context, Android.Resource.Color.SystemNeutral1100);

    /// <summary>The floating keyboard's handle: Gboard's 168 grey, the palette's nearest tone.</summary>
    public Color Handle { get; } = Read(context, Android.Resource.Color.SystemNeutral1300);

    public Color Outline { get; } = Read(context, OperatingSystem.IsAndroidVersionAtLeast(34) ? Android.Resource.Color.SystemOutlineLight : Android.Resource.Color.SystemNeutral1500);

    /// <summary>The outline at half: on the Fold, a dot not in sight 180 where the ground is 228 and the outline 132.</summary>
    public Color FaintOutline => Color.Argb(128, Outline.R, Outline.G, Outline.B);

    private static Color Read(Context context, int color) => new(context.GetColor(color));
}

/// <summary>
/// The standard keyboard, Gboard's copied (docs/tODO/settings-languages.md):
/// its pages (<see cref="StandardKeys"/>) drawn on one view as Gboard draws
/// them, in the device's colours (<see cref="StandardColors"/>). A key acts as
/// the finger lifts; held, a letter types its corner's symbol and ⌫ repeats;
/// Shift twice quickly locks the capitals; a swipe over the letters is read
/// as a word (<see cref="Swiped"/>) and a swipe along the space bar is the
/// next language. Its sizes are Gboard's on a Pixel 7 (420 dpi): pixels
/// measured there over 2.625; its letters' size, signs and corners' symbols
/// are shares of a letter row's height, so they grow with the keys.
/// </summary>
public sealed class StandardKeyboard : View
{
    private const float PixelsPerDp = 2.625f;
    private const float SideDp = 12 / PixelsPerDp;
    private const float KeyGapDp = 14 / PixelsPerDp;
    private const float RowGapDp = 22.5f / PixelsPerDp;
    private const float NumberRowDp = 117 / PixelsPerDp;
    private const float LetterRowDp = 124 / PixelsPerDp;
    private const float TopDp = 11 / PixelsPerDp;
    private const float BottomDp = 32 / PixelsPerDp;
    private const float CornerDp = 15.5f / PixelsPerDp;

    /// <summary>Roboto's cap height, of its size: what a glyph's measured height is turned into a size by.</summary>
    private const float CapHeight = 0.711f;

    /// <summary>A letter's size: its digits' cap height (52 px) over Roboto's, of the row's 124 px.</summary>
    private const float LabelShare = 52 / CapHeight / 124;

    /// <summary>A corner's symbol: on the Fold, q's % 16 px tall on an 82 px key.</summary>
    private const float HintShare = 16 / CapHeight / 82;

    /// <summary>Where a corner's symbol stands: on the Fold, 9 px down and 8 px in, of an 82 px key.</summary>
    private const float HintTopShare = 9f / 82;
    private const float HintRightShare = 8f / 82;

    /// <summary>?123, ABC and =\&lt;: their ink 34 px tall over Roboto's cap height.</summary>
    private const float WordShare = 34 / CapHeight / 124;

    /// <summary>The language on the space bar: "EN • ES" 24 px tall.</summary>
    private const float SpaceLabelShare = 24 / CapHeight / 124;

    /// <summary>1234's two lines.</summary>
    private const float PadLabelShare = SpaceLabelShare;

    /// <summary>Each sign's box, its drawn part as tall or wide as Gboard's (px over the part of its 960 box it fills).</summary>
    private const float ShiftShare = 55f / (760f / 960) / 124;
    private const float DeleteShare = 65f / (720f / 960) / 124;
    private const float LanguageShare = 52f / (800f / 960) / 124;
    private const float ReturnShare = 48f / (720f / 960) / 124;

    /// <summary>The comma's emoji: on the Fold, 20 px wide on an 86 px key, its centre at 0.30 of it, the comma's at 0.62.</summary>
    private const float EmojiShare = 20f / (800f / 960) / 86;
    private const float EmojiCentreShare = 0.30f;
    private const float CommaCentreShare = 0.62f;

    /// <summary>The trail Gboard draws: 30 px wide at the finger, 14 px at its end, 93 % then 40 % opaque, the whole swipe in sight.</summary>
    private const float TrailDp = 30 / PixelsPerDp;
    private const float TrailTailDp = 14 / PixelsPerDp;
    private const long TrailLife = 1500;
    private const float TrailHeadAlpha = 0.93f;
    private const float TrailTailAlpha = 0.40f;


    private readonly StandardColors colors;
    private readonly Paint fill = new(PaintFlags.AntiAlias);
    private readonly Paint text = new(PaintFlags.AntiAlias) { TextAlign = Paint.Align.Center };
    private readonly Handler handler = new(Looper.MainLooper!);
    private readonly Dictionary<int, Drawable> icons = [];
    private readonly float density;

    private StandardPage page = StandardPage.Letters;
    private ShiftState shift = ShiftState.Off;
    private long shiftTappedAt;
    private List<(StandardKey Key, RectF Box)> laid = [];

    /// <summary>The finger on a key: which, where it went down, and what it has done so far.</summary>
    private Finger? finger;
    private SwipeTrail? trail;
    private Java.Lang.Runnable? held;

    private enum ShiftState
    {
        Off,
        Once,
        Locked,
    }

    private sealed class Finger(int pointer, StandardKey key, RectF box, float x, float y, long start)
    {
        public int Pointer { get; } = pointer;
        public StandardKey Key { get; } = key;
        public RectF Box { get; } = box;
        public float X { get; } = x;
        public float Y { get; } = y;
        public long Start { get; } = start;
        public List<(float X, float Y, long Time)> Points { get; } = [];
        public bool Swiping { get; set; }
        public bool LongPressed { get; set; }
        public bool Done { get; set; }
    }

    public StandardKeyboard(Context context, StandardColors colors) : base(context)
    {
        this.colors = colors;
        density = context.Resources!.DisplayMetrics!.Density;
        HapticFeedbackEnabled = true;
    }

    /// <summary>Text typed: a key's, a long press's symbol, a space.</summary>
    public Action<string>? Typed { get; set; }

    /// <summary>⌫, once on the touch and again while held.</summary>
    public Action? Deleted { get; set; }

    public Action? Entered { get; set; }

    /// <summary>🌐, or a swipe along the space bar: the next language.</summary>
    public Action? NextLanguage { get; set; }

    /// <summary>A key whose part is not made yet: what it is, to say so.</summary>
    public Action<string>? NotMade { get; set; }

    /// <summary>A swipe over the letters, to be read as a word, and whether Shift or the capitals' lock was on.</summary>
    public Action<KeyPanel.SwipePath, bool, bool>? Swiped { get; set; }

    /// <summary>The language the keys are laid out for, the space bar reads and a swipe is read in.</summary>
    public StandardLanguage Language
    {
        get => language;
        set
        {
            language = value;
            Lay();
        }
    }

    private StandardLanguage language = StandardKeys.Spanish;

    /// <summary>How long a key is held for its corner's symbol, and Shift's two taps' room (Preferencias › Duración de la pulsación).</summary>
    public int LongPressMs { get; set; } = KeyPlusPreferences.LongPressDefault;

    /// <summary>Whether a key's touch is felt (Preferencias › Vibración al pulsar las teclas).</summary>
    public bool Vibrates { get; set; } = true;

    /// <summary>Whether a swipe draws its trail (Escritura deslizando el dedo › Recorrido del deslizamiento).</summary>
    public bool ShowsTrail { get; set; } = true;

    /// <summary>The keys' size, 1 for Gboard's own.</summary>
    public float Scale
    {
        get => scale;
        set
        {
            scale = value;
            RequestLayout();
        }
    }

    private float scale = 1f;

    private float Dp(float value) => value * density * scale;

    private float LetterRow => Dp(LetterRowDp);

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        var width = MeasureSpec.GetSize(widthMeasureSpec);
        var height = Dp(TopDp) + Dp(NumberRowDp) + (4 * LetterRow) + (4 * Dp(RowGapDp)) + Dp(BottomDp);
        SetMeasuredDimension(width, (int)Math.Ceiling(height));
    }

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        Lay();
    }

    /// <summary>
    /// The page's keys in place: ten keys' widths across the row with Gboard's
    /// gaps, a shorter row in the middle; the letters page's number row lower
    /// than its letters', a page of four rows sharing the same height alike.
    /// </summary>
    private void Lay()
    {
        laid = [];
        var rows = StandardKeys.Page(page, language);
        var side = Dp(SideDp);
        var gap = Dp(KeyGapDp);
        var unit = (Width - (2 * side) - (9 * gap)) / 10;
        var rowGap = Dp(RowGapDp);
        float[] heights = rows.Count == 5
            ? [Dp(NumberRowDp), LetterRow, LetterRow, LetterRow, LetterRow]
            : Enumerable.Repeat((Height - Dp(TopDp) - Dp(BottomDp) - ((rows.Count - 1) * rowGap)) / rows.Count, rows.Count).ToArray();
        var top = Dp(TopDp);
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var keys = row.Sum(key => key.Width);
            var rowWidth = (keys * unit) + ((keys - 1) * gap);
            var x = (Width - rowWidth) / 2;
            foreach (var key in row)
            {
                var keyWidth = (key.Width * unit) + ((key.Width - 1) * gap);
                laid.Add((key, new RectF(x, top, x + keyWidth, top + heights[r])));
                x += keyWidth + gap;
            }

            top += heights[r] + rowGap;
        }

        Invalidate();
    }

    private void ShowPage(StandardPage shown)
    {
        page = shown;
        shift = ShiftState.Off;
        Lay();
    }

    protected override void OnDraw(Canvas canvas)
    {
        canvas.DrawColor(colors.Ground);
        foreach (var (key, box) in laid)
        {
            DrawKey(canvas, key, box);
        }
    }

    private void DrawKey(Canvas canvas, StandardKey key, RectF box)
    {
        var pressed = finger is { Swiping: false } now && now.Key == key;
        var function = key.Look != StandardLook.Letter;
        fill.Color = function
            ? (pressed ? colors.FunctionPressed : colors.Function)
            : (pressed ? colors.Function : colors.Key);
        var corner = key.Look == StandardLook.Pill ? box.Height() / 2 : Dp(CornerDp);
        canvas.DrawRoundRect(box, corner, corner, fill);
        var glyph = function ? colors.FunctionGlyph : colors.Glyph;
        var h = LetterRow;

        if (key.Action == StandardAction.Shift)
        {
            DrawIcon(canvas, shift switch
            {
                ShiftState.Once => Resource.Drawable.ic_key_shift_on,
                ShiftState.Locked => Resource.Drawable.ic_key_shift_lock,
                _ => Resource.Drawable.ic_key_shift,
            }, box.CenterX(), box.CenterY(), h * ShiftShare, glyph);
            return;
        }

        if (key.Icon != 0)
        {
            var share = key.Action switch
            {
                StandardAction.Delete => DeleteShare,
                StandardAction.Language => LanguageShare,
                _ => ReturnShare,
            };
            DrawIcon(canvas, key.Icon, box.CenterX(), box.CenterY(), h * share, glyph);
            return;
        }

        if (key.HintIcon != 0)
        {
            DrawIcon(canvas, key.HintIcon, box.CenterX(), box.Top + (box.Height() * EmojiCentreShare), h * EmojiShare, glyph);
            DrawText(canvas, key.Label, box.CenterX(), box.Top + (box.Height() * CommaCentreShare), h * LabelShare, glyph);
            return;
        }

        switch (key.Action)
        {
            case StandardAction.Space:
                DrawText(canvas, language.Name, box.CenterX(), box.CenterY(), h * SpaceLabelShare, glyph);
                return;
            case StandardAction.Numbers:
                DrawText(canvas, "12", box.CenterX(), box.CenterY() - (h * PadLabelShare * 0.45f), h * PadLabelShare, glyph);
                DrawText(canvas, "34", box.CenterX(), box.CenterY() + (h * PadLabelShare * 0.45f), h * PadLabelShare, glyph);
                return;
            case StandardAction.Type:
                break;
            default:
                DrawText(canvas, key.Label, box.CenterX(), box.CenterY(), h * WordShare, glyph);
                return;
        }

        var showsHint = finger is { LongPressed: true } longPress && longPress.Key == key && key.Hint is not null;
        DrawText(canvas, showsHint ? key.Hint! : LabelOf(key), box.CenterX(), box.CenterY(), h * LabelShare, glyph);
        if (key.Hint is not null && !showsHint)
        {
            text.TextAlign = Paint.Align.Right;
            text.TextSize = h * HintShare;
            text.Color = glyph;
            canvas.DrawText(key.Hint, box.Right - (box.Height() * HintRightShare), box.Top + (box.Height() * HintTopShare) + (text.TextSize * CapHeight), text);
            text.TextAlign = Paint.Align.Center;
        }
    }

    /// <summary>A key's label as Shift has it: a letter in capitals, a digit as its symbol.</summary>
    private string LabelOf(StandardKey key) => shift != ShiftState.Off && key.Shifted is not null ? key.Shifted : key.Label;

    private void DrawText(Canvas canvas, string label, float centreX, float centreY, float size, Color color)
    {
        text.TextSize = size;
        text.Color = color;
        canvas.DrawText(label, centreX, centreY - ((text.Ascent() + text.Descent()) / 2), text);
    }

    private void DrawIcon(Canvas canvas, int id, float centreX, float centreY, float size, Color color)
    {
        if (!icons.TryGetValue(id, out var icon))
        {
            icons[id] = icon = Context!.GetDrawable(id)!.Mutate()!;
        }

        icon.SetTint(color);
        var half = size / 2;
        icon.SetBounds((int)(centreX - half), (int)(centreY - half), (int)(centreX + half), (int)(centreY + half));
        icon.Draw(canvas);
    }

    /// <summary>The key under a point: the one in its row nearest across, the row being the one whose band, gaps shared, holds it.</summary>
    private (StandardKey Key, RectF Box)? KeyAt(float x, float y)
    {
        var rowGap = Dp(RowGapDp);
        var inRow = laid.Where(laidKey => y >= laidKey.Box.Top - (rowGap / 2) && y < laidKey.Box.Bottom + (rowGap / 2)).ToList();
        if (inRow.Count == 0)
        {
            inRow = laid.Where(laidKey => Math.Abs(laidKey.Box.CenterY() - y) == laid.Min(other => Math.Abs(other.Box.CenterY() - y))).ToList();
        }

        return inRow.Count == 0 ? null : inRow.MinBy(laidKey => x < laidKey.Box.Left ? laidKey.Box.Left - x : x > laidKey.Box.Right ? x - laidKey.Box.Right : 0);
    }

    public override bool OnTouchEvent(MotionEvent? motion)
    {
        if (motion is null)
        {
            return false;
        }

        switch (motion.ActionMasked)
        {
            case MotionEventActions.Down:
            case MotionEventActions.PointerDown:
                // A second finger down finishes the first's key, as fast typing has it.
                if (finger is { } first)
                {
                    Finish(first);
                }

                var index = motion.ActionIndex;
                Begin(motion.GetPointerId(index), motion.GetX(index), motion.GetY(index), motion.EventTime);
                break;
            case MotionEventActions.Move when finger is { Done: false } now:
                var at = motion.FindPointerIndex(now.Pointer);
                if (at >= 0)
                {
                    for (var i = 0; i < motion.HistorySize; i++)
                    {
                        Track(now, motion.GetHistoricalX(at, i), motion.GetHistoricalY(at, i), motion.GetHistoricalEventTime(i));
                    }

                    Track(now, motion.GetX(at), motion.GetY(at), motion.EventTime);
                }

                break;
            case MotionEventActions.Up:
            case MotionEventActions.PointerUp:
                if (finger is { } lifted && motion.GetPointerId(motion.ActionIndex) == lifted.Pointer)
                {
                    Track(lifted, motion.GetX(motion.ActionIndex), motion.GetY(motion.ActionIndex), motion.EventTime);
                    Finish(lifted);
                }

                break;
            case MotionEventActions.Cancel:
                Forget();
                break;
        }

        return true;
    }

    private void Begin(int pointer, float x, float y, long time)
    {
        if (KeyAt(x, y) is not { } hit)
        {
            return;
        }

        var now = new Finger(pointer, hit.Key, hit.Box, x, y, time);
        now.Points.Add((x, y, time));
        finger = now;
        if (Vibrates)
        {
            PerformHapticFeedback(FeedbackConstants.KeyboardTap);
        }

        if (hit.Key.Action == StandardAction.Delete)
        {
            Deleted?.Invoke();
            Hold(() => Deleted?.Invoke(), KeyPanel.RepeatIntervalMs, KeyPanel.RepeatDelayMs);
        }
        else if (hit.Key.Hint is not null)
        {
            Hold(() =>
            {
                now.LongPressed = true;
                if (Vibrates)
                {
                    PerformHapticFeedback(FeedbackConstants.LongPress);
                }

                Invalidate();
            }, 0, LongPressMs);
        }

        Invalidate();
    }

    /// <summary>Does something once the key has been held a while, and again every so often where it repeats.</summary>
    private void Hold(Action action, int every, int after)
    {
        StopHolding();
        held = new Java.Lang.Runnable(() =>
        {
            action();
            if (every > 0)
            {
                handler.PostDelayed(held!, every);
            }
        });
        handler.PostDelayed(held, after);
    }

    private void StopHolding()
    {
        if (held is not null)
        {
            handler.RemoveCallbacks(held);
            held = null;
        }
    }

    /// <summary>A point of the finger's path: past a key's touch on a letter, a swipe, its trail drawn.</summary>
    private void Track(Finger now, float x, float y, long time)
    {
        now.Points.Add((x, y, time));
        trail?.Add(x, y, time);
        if (now.Swiping || now.LongPressed)
        {
            return;
        }

        var far = Math.Max(Math.Abs(x - now.X), Math.Abs(y - now.Y)) > Math.Min(now.Box.Width(), now.Box.Height()) * 0.6f;
        if (far && page == StandardPage.Letters && Swiped is not null && now.Key.Text is { Length: 1 } letter && language.SwipeLetters.Contains(letter))
        {
            now.Swiping = true;
            StopHolding();
            ShowTrail(now);
            Invalidate();
        }
        else if (far && now.Key.Action != StandardAction.Delete)
        {
            StopHolding();
        }
    }

    private void ShowTrail(Finger now)
    {
        if (!ShowsTrail)
        {
            return;
        }

        trail = new SwipeTrail(Dp(TrailDp), colors.Trail, 0, 0, Dp(TrailTailDp), TrailLife, TrailHeadAlpha, TrailTailAlpha);
        trail.SetBounds(0, 0, Width, Height);
        foreach (var (x, y, time) in now.Points)
        {
            trail.Add(x, y, time);
        }

        Overlay!.Add(trail);
    }

    private void Forget()
    {
        StopHolding();
        if (trail is not null)
        {
            Overlay!.Remove(trail);
            trail = null;
        }

        finger = null;
        Invalidate();
    }

    /// <summary>The finger lifted, or another came down: what the key does, or the swipe read.</summary>
    private void Finish(Finger now)
    {
        now.Done = true;
        var swiping = now.Swiping;
        var longPressed = now.LongPressed;
        Forget();
        if (swiping)
        {
            ReadSwipe(now);
        }
        else if (longPressed)
        {
            Typed?.Invoke(now.Key.Hint!);
            ShiftUsed();
        }
        else if (SwipedAlong(now))
        {
            NextLanguage?.Invoke();
        }
        else
        {
            Act(now.Key);
        }
    }

    /// <summary>A swipe along the space bar, further than a key's width: the next language, as Gboard's.</summary>
    private bool SwipedAlong(Finger now) =>
        now.Key.Action == StandardAction.Space && now.Points.Count > 0 && Math.Abs(now.Points[^1].X - now.X) > LetterRow && now.Points[^1].X != now.X;

    private void Act(StandardKey key)
    {
        switch (key.Action)
        {
            case StandardAction.Type:
                Typed?.Invoke(shift != ShiftState.Off && key.Shifted is not null ? key.Shifted : key.Text!);
                ShiftUsed();
                break;
            case StandardAction.Space:
                Typed?.Invoke(" ");
                break;
            case StandardAction.Shift:
                ToggleShift();
                break;
            case StandardAction.Symbols:
                ShowPage(StandardPage.Symbols);
                break;
            case StandardAction.MoreSymbols:
                ShowPage(StandardPage.MoreSymbols);
                break;
            case StandardAction.Letters:
                ShowPage(StandardPage.Letters);
                break;
            case StandardAction.Numbers:
                NotMade?.Invoke("1234");
                break;
            case StandardAction.Language:
                NextLanguage?.Invoke();
                break;
            case StandardAction.Enter:
                Entered?.Invoke();
                break;
        }
    }

    /// <summary>Shift: on for one key; tapped again within a long press's time, the capitals locked; tapped once more, off.</summary>
    private void ToggleShift()
    {
        var now = SystemClock.UptimeMillis();
        shift = shift switch
        {
            ShiftState.Off => ShiftState.Once,
            ShiftState.Once when now - shiftTappedAt <= LongPressMs => ShiftState.Locked,
            _ => ShiftState.Off,
        };
        shiftTappedAt = now;
        Invalidate();
    }

    /// <summary>A key typed with Shift on for one key: Shift off again.</summary>
    private void ShiftUsed()
    {
        if (shift == ShiftState.Once)
        {
            shift = ShiftState.Off;
            Invalidate();
        }
    }

    /// <summary>The swipe made FUTO Swipe's: the points and the letters' centres from 0 to 1 over the room the letters take.</summary>
    private void ReadSwipe(Finger now)
    {
        var letters = laid.Where(laidKey => laidKey.Key.Text is { Length: 1 } letter && language.SwipeLetters.Contains(letter)).ToList();
        var centres = language.SwipeLetters.Select(letter => letters.First(laidKey => laidKey.Key.Text == letter.ToString()).Box).ToList();
        var left = letters.Min(laidKey => laidKey.Box.Left);
        var top = letters.Min(laidKey => laidKey.Box.Top);
        var width = letters.Max(laidKey => laidKey.Box.Right) - left;
        var height = letters.Max(laidKey => laidKey.Box.Bottom) - top;
        float X(float x) => (x - left) / width;
        float Y(float y) => (y - top) / height;
        var path = new KeyPanel.SwipePath(
            [.. now.Points.Select(point => X(point.X))],
            [.. now.Points.Select(point => Y(point.Y))],
            [.. now.Points.Select(point => (float)(point.Time - now.Start))],
            [.. centres.Select(box => X(box.CenterX()))],
            [.. centres.Select(box => Y(box.CenterY()))],
            language);
        var capitals = shift == ShiftState.Locked;
        var capital = shift == ShiftState.Once;
        ShiftUsed();
        Swiped?.Invoke(path, capital, capitals);
    }

    protected override void OnDetachedFromWindow()
    {
        Forget();
        base.OnDetachedFromWindow();
    }
}
