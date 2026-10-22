using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Android.Views;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The standard keyboard on Windows, the Android one's (StandardKeyboard.cs)
/// drawn and typed the same way: the layouts of <see cref="StandardKeys"/>,
/// Gboard's sizes (one Android dp a WPF unit), each key acting as the finger
/// or the mouse lets go; held, a letter of Estándar types its corner's
/// symbol, a key of Estándar PC goes on, ⌫ and the arrows repeat; Shift once
/// or locked, Bloq Mayús, Ctrl, Alt, ⊞ and Alt Gr on until the next key, the
/// dead keys' accents on the next letter; 🌐 the next language.
/// </summary>
public sealed class KeyboardSurface : FrameworkElement
{
    private const double PixelsPerDp = 2.625;
    private const double SideDp = 12 / PixelsPerDp;
    private const double KeyGapDp = 14 / PixelsPerDp;
    private const double RowGapDp = 22.5 / PixelsPerDp;
    private const double NumberRowDp = 117 / PixelsPerDp;
    private const double LetterRowDp = 124 / PixelsPerDp;
    private const double TopDp = 11 / PixelsPerDp;
    private const double BottomDp = 32 / PixelsPerDp;
    private const double CornerDp = 15.5 / PixelsPerDp;
    private const double CapHeight = 0.711;
    private const double LabelShare = 52 / CapHeight / 124;
    private const double HintShare = 16 / CapHeight / 82;
    private const double HintTopShare = 9.0 / 82;
    private const double HintRightShare = 8.0 / 82;
    private const double WordShare = 34 / CapHeight / 124;
    private const double SpaceLabelShare = 24 / CapHeight / 124;
    private const double ShiftShare = 55.0 / (760.0 / 960) / 124;
    private const double DeleteShare = 65.0 / (720.0 / 960) / 124;
    private const double LanguageShare = 52.0 / (800.0 / 960) / 124;
    private const double ReturnShare = 48.0 / (720.0 / 960) / 124;
    private const double EmojiShare = 20.0 / (800.0 / 960) / 86;
    private const double EmojiCentreShare = 0.30;
    private const double CommaCentreShare = 0.62;
    private const double GboardKeyAspect = 93.0 / 124;
    private const double LabelRoomShare = 0.94;
    private const double MostWordsShare = 0.75;
    private static readonly double[] WordSteps = [1, 0.9, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3];

    /// <summary>A key held: how long before it acts (Estándar's symbol) or goes on, and how often then.</summary>
    private static readonly TimeSpan LongPress = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(50);

    private static readonly Dictionary<string, string> DeadMarks = new()
    {
        ["´"] = "́",
        ["`"] = "̀",
        ["^"] = "̂",
        ["¨"] = "̈",
    };

    private static readonly Typeface Regular = new("Segoe UI");
    private static readonly Typeface Medium = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

    private readonly Palette colors;
    private readonly DispatcherTimer holdTimer;
    private readonly Dictionary<StandardKey, double> wordSizes = [];
    private List<(StandardKey Key, Rect Box)> laid = [];
    private StandardPage page = StandardPage.Letters;
    private StandardLanguage language = StandardKeys.Spanish;
    private bool pc;
    private double glyphScale = 1;
    private Shift shift = Shift.Off;
    private DateTime shiftTappedAt;
    private bool ctrl;
    private bool alt;
    private bool meta;
    private bool altGr;
    private bool capsLock;
    private string? deadKey;
    private Pressed? pressed;
    private Action? heldAction;

    private enum Shift
    {
        Off,
        Once,
        Locked,
    }

    /// <summary>The key under the finger or the mouse, and what holding it has done.</summary>
    private sealed class Pressed(StandardKey key, Rect box, int? touch)
    {
        public StandardKey Key { get; } = key;
        public Rect Box { get; } = box;
        public int? Touch { get; } = touch;
        public bool LongPressed { get; set; }
        public bool Repeated { get; set; }
    }

    public KeyboardSurface(Palette colors)
    {
        this.colors = colors;
        holdTimer = new DispatcherTimer();
        holdTimer.Tick += (_, _) =>
        {
            holdTimer.Interval = RepeatInterval;
            heldAction?.Invoke();
        };
        Focusable = false;
    }

    /// <summary>Whether the keys are a PC's (Estándar PC) rather than Gboard's.</summary>
    public bool Pc
    {
        get => pc;
        set
        {
            pc = value;
            ShowPage(value ? StandardPage.Pc : StandardPage.Letters);
            InvalidateMeasure();
        }
    }

    public StandardLanguage KeyLanguage
    {
        get => language;
        set
        {
            language = value;
            InvalidateMeasure();
            Lay();
        }
    }

    /// <summary>🌐, or the language changed: the language to show next.</summary>
    public event Action? NextLanguage;

    /// <summary>The comma held, Estándar's ☺: the emoji (not made on Windows yet).</summary>
    public event Action<string>? NotMade;

    private double LetterRow => LetterRowDp;

    protected override Size MeasureOverride(Size available)
    {
        var rows = StandardKeys.Page(pc ? StandardPage.Pc : StandardPage.Letters, language).Count;
        var width = double.IsInfinity(available.Width) ? 400 : available.Width;
        return new Size(width, TopDp + NumberRowDp + ((rows - 1) * (LetterRow + RowGapDp)) + BottomDp);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        Lay();
    }

    /// <summary>The page's keys in place, as the Android keyboard lays them: its widest row across, Gboard's gaps, the top row lower.</summary>
    private void Lay()
    {
        laid = [];
        wordSizes.Clear();
        var rows = StandardKeys.Page(page, language);
        var width = ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var across = Math.Max(1, rows.Max(row => row.Sum(key => (double)key.Width)));
        var unit = (width - (2 * SideDp) - ((across - 1) * KeyGapDp)) / across;
        glyphScale = Math.Min(1, unit / (LetterRow * GboardKeyAspect));
        double[] heights = page is StandardPage.Letters or StandardPage.Pc
            ? [NumberRowDp, .. Enumerable.Repeat(LetterRow, rows.Count - 1)]
            : [.. Enumerable.Repeat((ActualHeight - TopDp - BottomDp - ((rows.Count - 1) * RowGapDp)) / rows.Count, rows.Count)];
        var top = TopDp;
        for (var r = 0; r < rows.Count; r++)
        {
            var keys = rows[r].Sum(key => (double)key.Width);
            var rowWidth = (keys * unit) + ((keys - 1) * KeyGapDp);
            var x = (width - rowWidth) / 2;
            foreach (var key in rows[r])
            {
                var keyWidth = (key.Width * unit) + ((key.Width - 1) * KeyGapDp);
                laid.Add((key, new Rect(x, top, Math.Max(1, keyWidth), Math.Max(1, heights[r]))));
                x += keyWidth + KeyGapDp;
            }

            top += heights[r] + RowGapDp;
        }

        SizeWords();
        InvalidateVisual();
    }

    /// <summary>The words (Esc, Ctrl, Bloq Mayús…) as large as three in four of them hold, the same size, the rest a step smaller.</summary>
    private void SizeWords()
    {
        var largest = LetterRow * (pc ? LabelShare : WordShare);
        var own = laid.Where(laidKey => Worded(laidKey.Key))
            .Select(laidKey => (laidKey.Key, Size: largest * WordSteps.FirstOrDefault(share => TextWidth(laidKey.Key.Label, largest * share, pc) <= laidKey.Box.Width * LabelRoomShare, WordSteps[^1])))
            .ToList();
        if (own.Count == 0)
        {
            return;
        }

        var even = own.Select(word => word.Size).OrderDescending().ElementAt((int)Math.Ceiling(own.Count * MostWordsShare) - 1);
        foreach (var (key, size) in own)
        {
            wordSizes[key] = Math.Min(size, even);
        }
    }

    private static bool Worded(StandardKey key) =>
        key.Icon == 0 && key.HintIcon == 0 && key.Label.Length > 1
        && key.Action is not (StandardAction.Type or StandardAction.Space or StandardAction.Numbers or StandardAction.Shift);

    private void ShowPage(StandardPage shown)
    {
        page = shown;
        shift = Shift.Off;
        Lay();
    }

    protected override void OnRender(DrawingContext context)
    {
        context.DrawRectangle(new SolidColorBrush(colors.Ground), null, new Rect(RenderSize));
        foreach (var (key, box) in laid)
        {
            DrawKey(context, key, box);
        }
    }

    private void DrawKey(DrawingContext context, StandardKey key, Rect box)
    {
        var down = pressed is { } now && now.Key == key;
        var function = key.Look != StandardLook.Letter;
        var ground = IsOn(key) ? colors.On
            : function ? (down ? colors.FunctionPressed : colors.Function)
            : (down ? colors.Function : colors.Key);
        var corner = key.Look == StandardLook.Pill ? box.Height / 2 : CornerDp;
        context.DrawRoundedRectangle(new SolidColorBrush(ground), null, box, corner, corner);
        var glyph = function ? colors.FunctionGlyph : colors.Glyph;
        var h = LetterRow * glyphScale;
        var centre = new Point(box.X + (box.Width / 2), box.Y + (box.Height / 2));

        if (key.Action == StandardAction.Shift)
        {
            var sign = shift switch
            {
                Shift.Once => Resource.Drawable.ic_key_shift_on,
                Shift.Locked => Resource.Drawable.ic_key_shift_lock,
                _ => Resource.Drawable.ic_key_shift,
            };
            Icons.Draw(context, sign, centre, h * ShiftShare, glyph);
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
            Icons.Draw(context, key.Icon, centre, h * share, glyph);
            return;
        }

        if (key.HintIcon != 0)
        {
            Icons.Draw(context, key.HintIcon, new Point(centre.X, box.Y + (box.Height * EmojiCentreShare)), h * EmojiShare, glyph);
            DrawText(context, key.Label, new Point(centre.X, box.Y + (box.Height * CommaCentreShare)), h * LabelShare, glyph, Regular);
            return;
        }

        switch (key.Action)
        {
            case StandardAction.Space:
                DrawText(context, language.Name, centre, h * SpaceLabelShare, glyph, Regular);
                return;
            case StandardAction.Numbers:
                DrawText(context, "12", new Point(centre.X, centre.Y - (h * SpaceLabelShare * 0.45)), h * SpaceLabelShare, glyph, Regular);
                DrawText(context, "34", new Point(centre.X, centre.Y + (h * SpaceLabelShare * 0.45)), h * SpaceLabelShare, glyph, Regular);
                return;
            case StandardAction.Type:
                break;
            default:
                DrawText(context, key.Label, centre, wordSizes.TryGetValue(key, out var size) ? size : h * LabelShare, glyph, pc ? Medium : Regular);
                return;
        }

        var showsHint = key.Hint is not null && (altGr || pressed is { LongPressed: true } held && held.Key == key);
        DrawText(context, showsHint ? key.Hint! : LabelOf(key), centre, h * LabelShare, glyph, Regular);
        if (key.Hint is not null && !showsHint)
        {
            var hint = Text(key.Hint, h * HintShare, glyph, Regular);
            var baseline = box.Y + (box.Height * glyphScale * HintTopShare) + (h * HintShare * CapHeight);
            context.DrawText(hint, new Point(box.Right - (box.Height * glyphScale * HintRightShare) - hint.Width, baseline - hint.Baseline));
        }
    }

    /// <summary>A line of text centred on a point as Android centres it: its baseline half the ascent and descent from the middle.</summary>
    private void DrawText(DrawingContext context, string label, Point centre, double size, Color color, Typeface typeface)
    {
        var text = Text(label, size, color, typeface);
        var descent = text.Height - text.Baseline;
        var baseline = centre.Y + ((text.Baseline - descent) / 2);
        context.DrawText(text, new Point(centre.X - (text.WidthIncludingTrailingWhitespace / 2), baseline - text.Baseline));
    }

    private FormattedText Text(string label, double size, Color color, Typeface typeface) =>
        new(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, Math.Max(1, size), new SolidColorBrush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private double TextWidth(string label, double size, bool medium) => Text(label, size, Colors.Black, medium ? Medium : Regular).WidthIncludingTrailingWhitespace;

    private string LabelOf(StandardKey key) => ShiftedFor(key) && key.Shifted is not null ? key.Shifted : key.Label;

    private bool ShiftedFor(StandardKey key) =>
        key.Text is { Length: 1 } one && char.IsLetter(one[0]) ? (shift != Shift.Off) != capsLock : shift != Shift.Off;

    private bool IsOn(StandardKey key) => key.Action switch
    {
        StandardAction.Ctrl => ctrl,
        StandardAction.Alt => alt,
        StandardAction.Meta => meta,
        StandardAction.AltGr => altGr,
        StandardAction.CapsLock => capsLock,
        _ => false,
    };

    private (StandardKey Key, Rect Box)? KeyAt(Point point)
    {
        var half = RowGapDp / 2;
        var inRow = laid.Where(laidKey => point.Y >= laidKey.Box.Top - half && point.Y < laidKey.Box.Bottom + half).ToList();
        if (inRow.Count == 0)
        {
            return null;
        }

        return inRow.MinBy(laidKey => point.X < laidKey.Box.Left ? laidKey.Box.Left - point.X : point.X > laidKey.Box.Right ? point.X - laidKey.Box.Right : 0);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.StylusDevice is null)
        {
            CaptureMouse();
            Begin(e.GetPosition(this), null);
        }

        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (e.StylusDevice is null)
        {
            ReleaseMouseCapture();
            Finish(null);
        }

        e.Handled = true;
    }

    protected override void OnTouchDown(TouchEventArgs e)
    {
        CaptureTouch(e.TouchDevice);
        Begin(e.GetTouchPoint(this).Position, e.TouchDevice.Id);
        e.Handled = true;
    }

    protected override void OnTouchUp(TouchEventArgs e)
    {
        ReleaseTouchCapture(e.TouchDevice);
        Finish(e.TouchDevice.Id);
        e.Handled = true;
    }

    /// <summary>A key touched: a second finger down finishes the first's, as fast typing has it; held, what the key does held.</summary>
    private void Begin(Point point, int? touch)
    {
        if (pressed is not null)
        {
            Finish(pressed.Touch);
        }

        if (KeyAt(point) is not { } hit)
        {
            return;
        }

        var now = new Pressed(hit.Key, hit.Box, touch);
        pressed = now;
        if (hit.Key.Action == StandardAction.Delete)
        {
            DeleteOnce();
            Hold(DeleteOnce, RepeatDelay);
        }
        else if (hit.Key.Repeats)
        {
            Press(hit.Key.Code);
            Hold(() => Press(hit.Key.Code), RepeatDelay);
        }
        else if (pc && hit.Key.Action is StandardAction.Type or StandardAction.Space or StandardAction.Enter)
        {
            Hold(() =>
            {
                now.Repeated = true;
                Act(now.Key);
            }, RepeatDelay);
        }
        else if (hit.Key.Hint is not null)
        {
            Hold(() =>
            {
                now.LongPressed = true;
                StopHolding();
                InvalidateVisual();
            }, LongPress);
        }
        else if (hit.Key.HintIcon != 0)
        {
            Hold(() =>
            {
                now.LongPressed = true;
                StopHolding();
                NotMade?.Invoke("Emojis");
            }, LongPress);
        }

        InvalidateVisual();
    }

    private void Hold(Action action, TimeSpan after)
    {
        StopHolding();
        heldAction = action;
        holdTimer.Interval = after;
        holdTimer.Start();
    }

    private void StopHolding()
    {
        holdTimer.Stop();
        heldAction = null;
    }

    private void Finish(int? touch)
    {
        if (pressed is not { } now || now.Touch != touch)
        {
            return;
        }

        StopHolding();
        pressed = null;
        if (now.Repeated)
        {
            // Typed while held.
        }
        else if (now.LongPressed)
        {
            if (now.Key.Hint is { } hint)
            {
                Type(hint);
                ModifiersUsed();
            }
        }
        else
        {
            Act(now.Key);
        }

        InvalidateVisual();
    }

    private void Act(StandardKey key)
    {
        switch (key.Action)
        {
            case StandardAction.Type:
                TypeKey(key);
                break;
            case StandardAction.Space when Combining:
                Press(Keycode.Space);
                ModifiersUsed();
                break;
            case StandardAction.Space:
                Type(" ");
                break;
            case StandardAction.Key when key.Code == Keycode.Unknown:
                break;
            case StandardAction.Key when !key.Repeats:
                FlushDead();
                Press(key.Code);
                ModifiersUsed();
                break;
            case StandardAction.Key:
            case StandardAction.Delete when Combining:
                ModifiersUsed();
                break;
            case StandardAction.Ctrl:
                ctrl = !ctrl;
                break;
            case StandardAction.Alt:
                alt = !alt;
                break;
            case StandardAction.Meta:
                meta = !meta;
                break;
            case StandardAction.AltGr:
                altGr = !altGr;
                break;
            case StandardAction.CapsLock:
                capsLock = !capsLock;
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
            case StandardAction.Enter when Combining:
                Press(Keycode.Enter);
                ModifiersUsed();
                break;
            case StandardAction.Enter:
                FlushDead();
                Press(Keycode.Enter);
                break;
        }

        InvalidateVisual();
    }

    private void TypeKey(StandardKey key)
    {
        if (Combining && CodeOf(key.Text) is { } code)
        {
            Press(code);
        }
        else if (altGr && key.Hint is not null)
        {
            Type(key.Hint);
        }
        else
        {
            var typed = ShiftedFor(key) && key.Shifted is not null ? key.Shifted : key.Text!;
            if (key.Dead && DeadMarks.ContainsKey(typed))
            {
                FlushDead();
                deadKey = typed;
            }
            else
            {
                Type(typed);
            }
        }

        ModifiersUsed();
    }

    /// <summary>Text typed, a dead key's accent on it where it has one with it (´ then a: á), or before it; a space types the accent alone.</summary>
    private void Type(string typed)
    {
        if (deadKey is { } accent)
        {
            deadKey = null;
            var composed = (typed + DeadMarks[accent]).Normalize(NormalizationForm.FormC);
            typed = typed == " " ? accent : composed.Length == 1 ? composed : accent + typed;
        }

        KeySender.Type(typed);
    }

    private void FlushDead()
    {
        if (deadKey is { } accent)
        {
            deadKey = null;
            KeySender.Type(accent);
        }
    }

    private void DeleteOnce()
    {
        if (Combining)
        {
            Press(Keycode.Del);
        }
        else if (deadKey is not null)
        {
            deadKey = null;
        }
        else
        {
            KeySender.Press(Keycode.Del, KeySender.Held.None);
        }
    }

    private void Press(Keycode code) => KeySender.Press(code, Held());

    private bool Combining => ctrl || alt || meta;

    private KeySender.Held Held() =>
        (ctrl ? KeySender.Held.Ctrl : 0) | (alt ? KeySender.Held.Alt : 0) | (meta ? KeySender.Held.Meta : 0) | (shift != Shift.Off ? KeySender.Held.Shift : 0);

    private void ModifiersUsed()
    {
        ctrl = alt = meta = altGr = false;
        if (shift == Shift.Once)
        {
            shift = Shift.Off;
        }

        InvalidateVisual();
    }

    private void ToggleShift()
    {
        var now = DateTime.UtcNow;
        shift = shift switch
        {
            Shift.Off => Shift.Once,
            Shift.Once when now - shiftTappedAt <= LongPress => Shift.Locked,
            _ => Shift.Off,
        };
        shiftTappedAt = now;
    }

    private static Keycode? CodeOf(string? text) => text is { Length: 1 } one
        ? char.ToLowerInvariant(one[0]) switch
        {
            >= 'a' and <= 'z' and var letter => Keycode.A + (letter - 'a'),
            >= '0' and <= '9' and var digit => Keycode.Num0 + (digit - '0'),
            ',' => Keycode.Comma,
            '.' => Keycode.Period,
            '-' => Keycode.Minus,
            '+' => Keycode.Plus,
            '=' => Keycode.Equals,
            '/' => Keycode.Slash,
            _ => null,
        }
        : null;
}
