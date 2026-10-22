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
/// dead keys' accents on the next letter; 🌐 the next language. A drag over
/// the letters is a swipe, its trail drawn, read as a word (<see cref="Swiped"/>)
/// and typed with a space before it and none after, as on Android.
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
    private sealed class Pressed(StandardKey key, Rect box, int? touch, Point start)
    {
        public StandardKey Key { get; } = key;
        public Rect Box { get; } = box;
        public int? Touch { get; } = touch;
        public Point Start { get; } = start;
        public DateTime StartedAt { get; } = DateTime.UtcNow;

        /// <summary>The finger's or the mouse's path, each point with its milliseconds from the first.</summary>
        public List<(Point Point, float Time)> Points { get; } = [(start, 0)];
        public bool LongPressed { get; set; }
        public bool Repeated { get; set; }
        public bool Swiping { get; set; }
    }

    /// <summary>The trail Gboard draws: 30 px wide at the finger, 14 px at its end, 93 % then 40 % opaque.</summary>
    private const double TrailDp = 30 / PixelsPerDp;
    private const double TrailTailDp = 14 / PixelsPerDp;
    private const double TrailHeadAlpha = 0.93;
    private const double TrailTailAlpha = 0.40;

    /// <summary>The swiped word typed last, with the space before it, while nothing else has been typed; the last character typed, for the space a swiped word needs.</summary>
    private string? swipedText;
    private char? lastTyped;

    /// <summary>A dictation's words typed so far, with the space before them, while it goes on; none between dictations.</summary>
    private string? spokenText;
    private string spokenLead = "";

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

    /// <summary>A key whose part is not made on Windows yet: what it is, to say so.</summary>
    public event Action<string>? NotMade;

    /// <summary>The comma held, Estándar's ☺: the emoji, as Gboard opens them.</summary>
    public event Action? EmojisAsked;

    /// <summary>Text typed from elsewhere (an emoji), as a key's: a swipe's words gone, and known as typed last.</summary>
    public void TypeText(string text)
    {
        EndSwipe(letter: false);
        ForgetTyping();
        KeySender.Type(text);
        lastTyped = text.Length > 0 ? text[^1] : lastTyped;
    }

    /// <summary>Words spoken (🎤), typed as a swiped word is: a space before them unless after a space or nothing.</summary>
    public void TypeSpoken(string text)
    {
        TypeSpokenSoFar(text);
        spokenText = null;
    }

    /// <summary>
    /// A dictation's words so far, typed as they are heard, as Gboard and
    /// Windows' own voice typing write them: in place of those typed before,
    /// only what changed deleted and typed again, with a space before them
    /// unless after a space or nothing.
    /// </summary>
    public void TypeSpokenSoFar(string text)
    {
        if (spokenText is null)
        {
            if (text.Length == 0)
            {
                return;
            }

            EndSwipe(letter: false);
            spokenLead = lastTyped is null || char.IsWhiteSpace(lastTyped.Value) ? "" : " ";
            spokenText = "";
        }

        var wanted = text.Length == 0 ? "" : spokenLead + text;
        var same = 0;
        while (same < spokenText.Length && same < wanted.Length && spokenText[same] == wanted[same])
        {
            same++;
        }

        // A surrogate pair is deleted and typed whole.
        if (same > 0 && char.IsHighSurrogate(wanted[same - 1]))
        {
            same--;
        }

        for (var gone = new StringInfo(spokenText[same..]).LengthInTextElements; gone > 0; gone--)
        {
            KeySender.Press(Keycode.Del, KeySender.Held.None);
        }

        KeySender.Type(wanted[same..]);
        spokenText = wanted;
        if (wanted.Length > 0)
        {
            lastTyped = wanted[^1];
        }
    }

    /// <summary>⌫ from elsewhere (the emoji's), as the key's.</summary>
    public void DeleteBack() => DeleteOnce();

    /// <summary>A swipe over the letters, to be read as a word, and whether Shift or the capitals were on.</summary>
    public event Action<SwipeEngine.Swipe, bool, bool>? Swiped;

    /// <summary>The words a swipe read as, the one typed among them, for the toolbar; none, the toolbar as it was.</summary>
    public event Action<IReadOnlyList<string>, string?>? Suggestions;

    /// <summary>The words to offer for a word being typed in a language, the likeliest first (<see cref="WordSuggestions"/>); none, nothing is offered as letters are typed.</summary>
    public Func<StandardLanguage, string, IReadOnlyList<string>>? Suggesting { get; set; }

    /// <summary>The words offered for the word being typed, for the toolbar, each to be taken with a tap (<see cref="TakeSuggestion"/>); none, the toolbar as it was.</summary>
    public event Action<IReadOnlyList<string>>? Offered;

    /// <summary>
    /// The word being typed letter by letter, as the keys typed it: Windows
    /// tells a keyboard nothing of the field, so it is the keys' own count,
    /// begun again by anything but a letter or ⌫.
    /// </summary>
    private string typingWord = "";

    /// <summary>The word being typed changed: the words starting so offered, as Gboard's while a word is typed.</summary>
    private void OfferWords()
    {
        IReadOnlyList<string> words = typingWord.Length > 0 && swipedText is null && Suggesting is { } suggesting ? suggesting(language, typingWord) : [];
        Offered?.Invoke(words);
    }

    /// <summary>The word being typed begun again, the words offered for it gone.</summary>
    private void ForgetTyping()
    {
        if (typingWord.Length > 0)
        {
            typingWord = "";
            Offered?.Invoke([]);
        }
    }

    /// <summary>A word offered taken in place of the one being typed, with a space after it, as Gboard's.</summary>
    public void TakeSuggestion(string word)
    {
        foreach (var _ in typingWord)
        {
            KeySender.Press(Keycode.Del, KeySender.Held.None);
        }

        KeySender.Type(word + " ");
        lastTyped = ' ';
        typingWord = "";
        Offered?.Invoke([]);
    }

    /// <summary>
    /// A swipe's words: the likeliest typed, in capitals or with a capital as
    /// Shift had it, with a space before it unless after a space or nothing,
    /// none after; the words offered in the toolbar.
    /// </summary>
    public void TypeSwiped(IReadOnlyList<string> words, bool capital, bool capitals)
    {
        if (words.Count == 0)
        {
            return;
        }

        ForgetTyping();
        var word = Cased(words[0], capital, capitals);
        var text = lastTyped is null || char.IsWhiteSpace(lastTyped.Value) ? word : " " + word;
        KeySender.Type(text);
        lastTyped = text[^1];
        swipedText = text;
        Suggestions?.Invoke(words, words[0]);
    }

    /// <summary>A word of the toolbar taken in the swiped word's place, as Gboard's: the suggestions end; the next word still starts with its space.</summary>
    public void ReplaceSwiped(string word)
    {
        if (swipedText is not { } typed)
        {
            return;
        }

        var old = typed.Trim();
        var capital = old.Length > 0 && char.IsUpper(old[0]);
        var text = (typed.StartsWith(' ') ? " " : "") + Cased(word, capital, capitals: false);
        foreach (var _ in typed)
        {
            KeySender.Press(Keycode.Del, KeySender.Held.None);
        }

        KeySender.Type(text);
        lastTyped = text[^1];
        swipedText = text;
        Suggestions?.Invoke([], null);
    }

    private static string Cased(string word, bool capital, bool capitals) =>
        capitals ? word.ToUpper(CultureInfo.CurrentCulture)
        : capital ? char.ToUpper(word[0], CultureInfo.CurrentCulture) + word[1..]
        : word;

    /// <summary>Something else typed: the swipe's words gone; whether a space is owed first, a letter right after a swiped word starting the next word.</summary>
    private bool EndSwipe(bool letter)
    {
        var owed = swipedText is not null && letter;
        if (swipedText is not null)
        {
            swipedText = null;
            Suggestions?.Invoke([], null);
        }

        return owed;
    }

    /// <summary>The least share of Gboard's row height the rows are made: half, keys a mouse still reads and hits.</summary>
    public const double MinRowScale = 0.5;

    /// <summary>The rows' height as a share of Gboard's, from the height the window gives the keys: 1 at Gboard's own; the gaps stay Gboard's.</summary>
    public double RowScale { get; private set; } = 1;

    /// <summary>The keys' height with the rows at a share of Gboard's, for the keyboard in use.</summary>
    public double HeightAt(double scale) => TopDp + (NumberRowDp * scale) + ((BaseRows - 1) * ((LetterRowDp * scale) + RowGapDp)) + BottomDp;

    /// <summary>The rows of the keyboard's first page, the one its height is made for.</summary>
    private int BaseRows => StandardKeys.Page(pc ? StandardPage.Pc : StandardPage.Letters, language).Count;

    private double LetterRow => LetterRowDp * RowScale;

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 400 : available.Width;
        return new Size(width, double.IsInfinity(available.Height) ? HeightAt(RowScale) : available.Height);
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

        var gaps = TopDp + BottomDp + ((BaseRows - 1) * RowGapDp);
        RowScale = ActualHeight > 0 ? Math.Max(MinRowScale, (ActualHeight - gaps) / (NumberRowDp + ((BaseRows - 1) * LetterRowDp))) : RowScale;
        var across = Math.Max(1, rows.Max(row => row.Sum(key => (double)key.Width)));
        var unit = (width - (2 * SideDp) - ((across - 1) * KeyGapDp)) / across;
        glyphScale = Math.Min(1, unit / (LetterRow * GboardKeyAspect));
        double[] heights = page is StandardPage.Letters or StandardPage.Pc
            ? [NumberRowDp * RowScale, .. Enumerable.Repeat(LetterRow, rows.Count - 1)]
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

        if (pressed is { Swiping: true } now)
        {
            DrawTrail(context, now);
        }
    }

    private void DrawKey(DrawingContext context, StandardKey key, Rect box)
    {
        var down = pressed is { Swiping: false } now && now.Key == key;
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

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.StylusDevice is null && e.LeftButton == MouseButtonState.Pressed)
        {
            Track(e.GetPosition(this), null);
        }
    }

    protected override void OnTouchMove(TouchEventArgs e)
    {
        Track(e.GetTouchPoint(this).Position, e.TouchDevice.Id);
        e.Handled = true;
    }

    /// <summary>A point of the path: past a key's touch on a letter, a swipe, its trail drawn; on any other key, nothing held goes on.</summary>
    private void Track(Point point, int? touch)
    {
        if (pressed is not { } now || now.Touch != touch)
        {
            return;
        }

        now.Points.Add((point, (float)(DateTime.UtcNow - now.StartedAt).TotalMilliseconds));
        if (now.Swiping)
        {
            InvalidateVisual();
            return;
        }

        if (now.LongPressed || now.Repeated)
        {
            return;
        }

        var far = Math.Max(Math.Abs(point.X - now.Start.X), Math.Abs(point.Y - now.Start.Y)) > Math.Min(now.Box.Width, now.Box.Height) * 0.6;
        if (far && page is StandardPage.Letters or StandardPage.Pc && now.Key.Text is { Length: 1 } letter && language.SwipeLetters.Contains(letter))
        {
            now.Swiping = true;
            StopHolding();
            InvalidateVisual();
        }
        else if (far && now.Key.Action != StandardAction.Delete && !now.Key.Repeats)
        {
            StopHolding();
        }
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

        var now = new Pressed(hit.Key, hit.Box, touch, point);
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
                pressed = null;
                InvalidateVisual();
                EmojisAsked?.Invoke();
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
        if (now.Swiping)
        {
            ReadSwipe(now);
        }
        else if (now.Repeated)
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
                EndSwipe(letter: false);
                Type(" ");
                break;
            case StandardAction.Key when key.Code == Keycode.Unknown:
                break;
            case StandardAction.Key when !key.Repeats:
                FlushDead();
                ForgetTyping();
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
                EndSwipe(letter: false);
                ForgetTyping();
                FlushDead();
                Press(Keycode.Enter);
                lastTyped = '\n';
                break;
        }

        InvalidateVisual();
    }

    /// <summary>The swipe made FUTO Swipe's: the points and the letters' centres from 0 to 1 over the room the letters take.</summary>
    private void ReadSwipe(Pressed now)
    {
        var letters = laid.Where(laidKey => laidKey.Key.Text is { Length: 1 } letter && language.SwipeLetters.Contains(letter)).ToList();
        if (letters.Count == 0)
        {
            return;
        }

        var centres = language.SwipeLetters.Select(letter => letters.First(laidKey => laidKey.Key.Text == letter.ToString()).Box).ToList();
        var left = letters.Min(laidKey => laidKey.Box.Left);
        var top = letters.Min(laidKey => laidKey.Box.Top);
        var width = letters.Max(laidKey => laidKey.Box.Right) - left;
        var height = letters.Max(laidKey => laidKey.Box.Bottom) - top;
        float X(double x) => (float)((x - left) / width);
        float Y(double y) => (float)((y - top) / height);
        var path = new SwipeEngine.Swipe(
            [.. now.Points.Select(point => X(point.Point.X))],
            [.. now.Points.Select(point => Y(point.Point.Y))],
            [.. now.Points.Select(point => point.Time)],
            [.. centres.Select(box => X(box.X + (box.Width / 2)))],
            [.. centres.Select(box => Y(box.Y + (box.Height / 2)))],
            language);
        var capitals = shift == Shift.Locked || capsLock;
        var capital = shift == Shift.Once;
        if (shift == Shift.Once)
        {
            shift = Shift.Off;
        }

        EndSwipe(letter: false);
        Swiped?.Invoke(path, capital, capitals);
    }

    /// <summary>The swipe's trail as Gboard draws it, from its end, thin and faint, to the finger, wide and strong.</summary>
    private void DrawTrail(DrawingContext context, Pressed now)
    {
        var points = now.Points;
        for (var i = 1; i < points.Count; i++)
        {
            var share = (double)i / (points.Count - 1);
            var alpha = TrailTailAlpha + ((TrailHeadAlpha - TrailTailAlpha) * share);
            var color = Color.FromArgb((byte)(alpha * 255), colors.Primary.R, colors.Primary.G, colors.Primary.B);
            var pen = new Pen(new SolidColorBrush(color), TrailTailDp + ((TrailDp - TrailTailDp) * share)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            context.DrawLine(pen, points[i - 1].Point, points[i].Point);
        }
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
            if (EndSwipe(typed.Length == 1 && char.IsLetter(typed[0])))
            {
                Type(" ");
            }

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
        lastTyped = typed.Length > 0 ? typed[^1] : lastTyped;
        if (typed.Length > 0 && typed.All(char.IsLetter))
        {
            typingWord += typed;
            OfferWords();
        }
        else
        {
            ForgetTyping();
        }
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
        EndSwipe(letter: false);
        lastTyped = null;
        if (Combining)
        {
            Press(Keycode.Del);
            ForgetTyping();
        }
        else if (deadKey is not null)
        {
            deadKey = null;
        }
        else
        {
            KeySender.Press(Keycode.Del, KeySender.Held.None);
            if (typingWord.Length > 0)
            {
                typingWord = typingWord[..^1];
                OfferWords();
            }
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
