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
/// and typed with a space before it and none after, as on Android. A
/// keyboard designed shows its letters' page as designed, and while it is
/// being designed nothing is typed: a click marks a key, a drag moves it.
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

        /// <summary>What its long press offers in a popup above it (<see cref="ChoicesFor"/>), and which is chosen now; none, nothing offered.</summary>
        public Choices? Offered { get; set; }
        public int Chosen { get; set; }
    }

    /// <summary>A long press's popup: what it offers, each one's cell, and the popup's ground around them.</summary>
    private sealed record Choices(IReadOnlyList<string> Texts, IReadOnlyList<Rect> Cells, Rect Ground);

    /// <summary>How many a popup's row holds, as Gboard's: more go in rows above it.</summary>
    private const int ChoicesAcross = 5;

    /// <summary>The room around a popup's cells, and its corners' roundness: Gboard's, measured on the emulator.</summary>
    private const double ChoicesPaddingDp = 6;
    private const double ChoicesCornerDp = 24;

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

    /// <summary>When the dictation last typed, for a key or a click outside the keyboard since.</summary>
    private long spokenAt;

    /// <summary>The dictation let go of the text (<see cref="LetSpokenGo"/>): nothing more of it is typed until the next begins.</summary>
    private bool spokenLetGo;

    /// <summary>A dictation's last words typed that are typed again when heard otherwise; those before stay as typed.</summary>
    private const int SpokenOpenWords = 3;

    public KeyboardSurface(Palette colors)
    {
        this.colors = colors;
        holdTimer = new DispatcherTimer();
        holdTimer.Tick += (_, _) =>
        {
            holdTimer.Interval = RepeatInterval;
            heldAction?.Invoke();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible && pressed is not null)
            {
                Forget();
            }
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
            ShowPage(BasePage);
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

    /// <summary>The comma held, Estándar's ☺: the emoji, as Gboard opens them.</summary>
    public event Action? EmojisAsked;

    /// <summary>Text typed from elsewhere (an emoji), as a key's: a swipe's words gone, and known as typed last.</summary>
    public void TypeText(string text)
    {
        LetSpokenGo("an emoji was typed");
        EndSwipe(letter: false);
        ForgetTyping();
        KeySender.Type(text);
        sentence = null;
        wordFix = null;
        boundaryAt = Environment.TickCount64;
        lastTyped = text.Length > 0 ? text[^1] : lastTyped;
    }

    /// <summary>A dictation begun (🎤): its words typed as they come, until it ends or lets go of the text.</summary>
    public void BeginSpoken()
    {
        spokenText = null;
        spokenLetGo = false;
    }

    /// <summary>The text let go of by the dictation listening, told so it stops: what it typed stays and nothing more of it is typed.</summary>
    public event Action? SpokenLetGo;

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
    /// unless after a space or nothing. Its typing assumes the field ends with
    /// what it typed, so a key of the computer's or a click outside the
    /// keyboard since (Enter sending a chat's message) lets go of the text
    /// instead, as the keyboard's own keys do (<see cref="LetSpokenGo"/>).
    /// </summary>
    public void TypeSpokenSoFar(string text)
    {
        if (spokenLetGo)
        {
            return;
        }

        if (spokenText is null)
        {
            if (text.Length == 0)
            {
                return;
            }

            EndSwipe(letter: false);
            ShiftOnceUsed();
            spokenLead = lastTyped is null || char.IsWhiteSpace(lastTyped.Value) ? "" : " ";
            spokenText = "";
        }
        else if (spokenText.Length > 0 && ((KeyedAt?.Invoke() ?? 0) >= spokenAt || (PressedAt?.Invoke() ?? 0) >= spokenAt))
        {
            LetSpokenGo("a key or a click outside the keyboard came");
            return;
        }

        var settled = Settled(text);
        var wanted = settled.Length == 0 ? "" : spokenLead + settled;
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

        var deleted = new StringInfo(spokenText[same..]).LengthInTextElements;
        if (deleted > 0)
        {
            Log.Detail($"Voice typing: {deleted} of the {spokenText.Length} characters typed so far typed again");
        }

        for (var gone = deleted; gone > 0; gone--)
        {
            KeySender.Press(Keycode.Del, KeySender.Held.None);
        }

        KeySender.Type(wanted[same..]);
        sentence = null;
        wordFix = null;
        boundaryAt = 0;
        spokenText = wanted;
        spokenAt = Environment.TickCount64;
        if (wanted.Length > 0)
        {
            lastTyped = wanted[^1];
        }
    }

    /// <summary>
    /// The words heard, those the dictation typed kept but its last
    /// <see cref="SpokenOpenWords"/>: all it heard is read again each time, and
    /// the model may change a comma or a capital far back (ahora → Ahora,),
    /// which would delete the whole sentence and type it again.
    /// </summary>
    private string Settled(string heard)
    {
        var typed = Words(spokenText is { Length: > 0 } text ? text[spokenLead.Length..] : "");
        var kept = Math.Max(0, typed.Length - SpokenOpenWords);
        return string.Join(' ', typed.Take(kept).Concat(Words(heard).Skip(kept)));

        static string[] Words(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>The dictation listening lets go of the text, before anything else touches it: what it typed stays and it is told to stop.</summary>
    private void LetSpokenGo(string why)
    {
        if (spokenText is null || spokenLetGo)
        {
            return;
        }

        Log.Write($"Voice typing: stopped typing, {why}; what it typed stays");
        spokenText = null;
        spokenLetGo = true;
        SpokenLetGo?.Invoke();
    }

    /// <summary>⌫ from elsewhere (the emoji's), as the key's.</summary>
    public void DeleteBack()
    {
        LetSpokenGo("a key of the keyboard's was pressed");
        DeleteOnce();
    }

    /// <summary>A swipe over the letters, to be read as a word, and whether Shift or the capitals were on.</summary>
    public event Action<SwipeEngine.Swipe, bool, bool>? Swiped;

    /// <summary>Whether a drag over the letters is read as a word (Escritura deslizando el dedo); off, the letters are only tapped.</summary>
    public bool Glides { get; set; } = true;

    /// <summary>Whether a key pressed types: off in the settings' preview, which only shows the keyboard and is designed there.</summary>
    public bool Types { get; init; } = true;

    /// <summary>Whether a swipe draws its trail over the keys.</summary>
    public bool ShowsTrail { get; set; } = true;

    /// <summary>Glide typing › Delete a swiped word whole: ⌫ right after a swipe takes all it typed, as Gboard's.</summary>
    public bool DeletesSwipedWord { get; set; } = true;

    /// <summary>The words a swipe read as, the one typed among them, for the toolbar; none, the toolbar as it was.</summary>
    public event Action<IReadOnlyList<string>, string?>? Suggestions;

    /// <summary>The words to offer for a word being typed in a language, the likeliest first (<see cref="WordSuggestions"/>); none, nothing is offered as letters are typed.</summary>
    public Func<StandardLanguage, string, IReadOnlyList<string>>? Suggesting { get; set; }

    /// <summary>The words offered for the word being typed, for the toolbar, each to be taken with a tap (<see cref="TakeSuggestion"/>); none, the toolbar as it was.</summary>
    public event Action<IReadOnlyList<string>>? Offered;

    /// <summary>A word of the toolbar taken in place of a swiped one: the language, the word the swipe read first, the word taken (<see cref="PersonalWords.Chose"/>).</summary>
    public event Action<StandardLanguage, string, string>? SwipeChosen;

    /// <summary>The word the last swipe read first, before what was learnt put another in its place.</summary>
    private string? swipeRead;

    /// <summary>
    /// A language's Spelling as it applies now (<see cref="LanguageSpelling"/>,
    /// nothing with Spelling off from the toolbar): a sentence's accents
    /// corrected by the model (<see cref="AccentModel"/>), its opening mark
    /// put at its start (<see cref="SpanishMarks"/>), and Autocorrect, what
    /// is corrected written at once; off, offered in the toolbar.
    /// </summary>
    public Func<StandardLanguage, LanguageSpelling> SpellingIn { get; set; } = _ => LanguageSpelling.Off;

    /// <summary>The Spelling of the language typed in.</summary>
    private LanguageSpelling Spelling => SpellingIn(language);

    /// <summary>Whether the field typed in is surely a document (<see cref="FocusWatch.Document"/>); none told, it is not.</summary>
    public Func<bool>? InDocument { get; set; }

    /// <summary>
    /// Whether Autocorrect writes by itself (words, the models' sentence, a
    /// sentence's capital, ¿ and ¡): on, and only in a document; in an
    /// address, a search or a form's field, or in doubt, what it would write
    /// is only offered.
    /// </summary>
    private bool Corrects => Spelling.Autocorrect && (InDocument?.Invoke() ?? false);

    /// <summary>Whether a sentence closed is read by a model: the punctuation or the accent model, or the language's context model.</summary>
    private bool SentenceRead => Spelling.Accents || Spelling.Punctuation || Spelling.Context is not null;

    /// <summary>The models' sentence (accents, punctuation), at once, where they are loaded; none, not now. For Enter, which cannot wait for them. With it, whether these keys began the sentence (Enter, or . ? or ! typed).</summary>
    public Func<string, bool, string?>? CorrectNow { get; set; }

    /// <summary>When the mouse, a finger or a pen last pressed outside the keyboard (<see cref="InputWatch.PressedAt"/>), and when a key of the computer's last went down (<see cref="InputWatch.KeyedAt"/>).</summary>
    public Func<long>? PressedAt { get; set; }

    public Func<long>? KeyedAt { get; set; }

    /// <summary>
    /// What these keys typed of the sentence being written, since it began
    /// here (a field come to, Enter, or a . ? or ! typed): Windows tells a
    /// keyboard nothing of the field, so Spelling mends a sentence closed with
    /// ? or ! only from this, and only while nothing else touched the window
    /// since (<see cref="SentenceKnown"/>); none, not known.
    /// </summary>
    private StringBuilder? sentence;
    private long sentenceFrom;

    /// <summary>A sentence begun here: a field come to, Enter, or one closed; or, blind, where a press put the caret, beside what these keys never saw.</summary>
    public void BeginSentence() => BeginSentence(blind: false, byKeys: false);

    private void BeginSentence(bool blind, bool byKeys)
    {
        sentence = new StringBuilder();
        sentenceFrom = Environment.TickCount64;
        sentenceBlind = blind;
        sentenceByKeys = byKeys;
        inAddress = false;
        addressTyped = "";
    }

    /// <summary>Whether these keys began the sentence, with Enter or a . ? or ! they typed: where a field was come to, what is before the caret is not known.</summary>
    private bool sentenceByKeys;

    /// <summary>
    /// Spelling's Capital at the start of a sentence: whether a letter typed now begins one, so it goes out a capital,
    /// Shift left as it is. Only where these keys began the sentence (Enter, or . ? or ! and a space), nothing typed in
    /// it since and nothing else touching the text since, in a text field, with Shift off.
    /// </summary>
    private bool OpensSentence() =>
        CapitalizesSentences && fieldKind == StandardField.Text && !InTerminal && (InDocument?.Invoke() ?? false) && !Combining && shift == Shift.Off
        && lastTyped is ' ' or '\n' && sentenceByKeys && SentenceKnown && sentence!.ToString().Trim().Length == 0;

    /// <summary>Whether the sentence began where a press put the caret: what is before it, maybe a word's first letters, is not known.</summary>
    private bool sentenceBlind;

    private bool SentenceKnown => sentence is not null && (KeyedAt?.Invoke() ?? long.MaxValue) < sentenceFrom && (PressedAt?.Invoke() ?? long.MaxValue) < sentenceFrom;

    /// <summary>
    /// The sentence as these keys see it before they type in it again: a key
    /// of the computer's since, and the text holds what they did not type, so
    /// it is not known; a press since (the caret put somewhere, as one does
    /// before typing), and it begins again here, for what they type from now
    /// on is all they will write again.
    /// </summary>
    private void SentenceSeen()
    {
        if (sentence is null)
        {
            return;
        }

        if ((KeyedAt?.Invoke() ?? long.MaxValue) >= sentenceFrom)
        {
            sentence = null;
        }
        else if ((PressedAt?.Invoke() ?? long.MaxValue) >= sentenceFrom)
        {
            BeginSentence(blind: true, byKeys: false);
        }
    }

    /// <summary>Why the sentence is not known, for the log: Spelling says why it did not correct one.</summary>
    private string SentenceUnknownWhy => sentence is null
        ? "the keys did not type all of it (an arrow, a shortcut, an emoji or dictation moved or wrote in it)"
        : "the mouse or the computer's keys touched the text since it began, after these keys typed in it";

    /// <summary>A sentence Spelling would correct (the model on, in Spanish, not a lone sign) that it cannot: why, in the log.</summary>
    private void NotCorrected(string why) => Log.Detail($"Spelling: a sentence was not corrected: {why}");

    /// <summary>Text these keys typed, in the sentence; a . ? or ! or a new line closes it and begins the next.</summary>
    private void SentenceTyped(string text)
    {
        // Anything typed after a word checked: what ended it is no longer the text's end.
        wordFix = null;
        SentenceSeen();
        // Not known: the next sentence begins here once this one is closed by these keys.
        if (sentence is null)
        {
            if (text.IndexOfAny(['.', '?', '!', '\n']) >= 0)
            {
                BeginSentence(blind: false, byKeys: true);
                ReadClosed(text);
            }

            return;
        }

        sentence.Append(text);

        // A sentence ends with . ? ! or a new line, and with two spaces in a row (for the accent model, which reads it then).
        var twoSpaces = text == " " && sentence.Length >= 2 && sentence[^2] == ' ';
        if (text.IndexOfAny(['.', '?', '!', '\n']) >= 0 || twoSpaces)
        {
            var whole = sentence.ToString();
            var known = SentenceKnown;
            var byKeys = sentenceByKeys;
            BeginSentence(blind: false, byKeys: true);
            if (SentenceRead && whole.Trim().Length > 1 && !EndsInAddress(whole))
            {
                if (!known)
                {
                    ReadClosed(text);
                    return;
                }

                closed = whole;
                SentenceClosed?.Invoke(whole, byKeys);
            }
        }
    }

    /// <summary>
    /// A sentence these keys did not type whole (dictation, a click, the
    /// computer's keys) closed with . ? or !, where Autocorrect corrects by
    /// itself: told with its closing mark, for the window to read it from the
    /// field (<see cref="FieldSentenceClosed"/>); else why it is not corrected.
    /// </summary>
    private void ReadClosed(string text)
    {
        var closing = text.LastOrDefault(mark => mark is '.' or '?' or '!');
        if (closing == default || !SentenceRead)
        {
            return;
        }

        if (Corrects && FieldSentenceClosed is { } read)
        {
            read(closing);
        }
        else
        {
            NotCorrected(SentenceUnknownWhy);
        }
    }

    /// <summary>A sentence these keys did not type whole closed with this mark: for the window to read it from the field and correct it there (<see cref="ReplaceRead"/>).</summary>
    public event Action<char>? FieldSentenceClosed;

    /// <summary>The sentence before the caret read from the field and corrected there at once, where these keys lost it: Enter's, which cannot wait; with what these keys typed last (the word just ended, as corrected), which the field must end with, for it may not have taken it yet. Whether it was.</summary>
    public Func<string, bool>? CorrectFieldNow { get; set; }

    /// <summary>When a key of the keyboard's was last pressed: a sentence read from the field is not written again after one.</summary>
    private long keyActedAt;

    /// <summary>
    /// A sentence read from the field written again corrected: as many
    /// characters as it has deleted before the caret and the correction typed,
    /// only while nothing has touched the text since it was read
    /// (<paramref name="readAt"/>); the sentence known from then on, so ↶
    /// can take it back. Whether it was.
    /// </summary>
    public bool ReplaceRead(string original, string corrected, long readAt)
    {
        if ((KeyedAt?.Invoke() ?? long.MaxValue) >= readAt || (PressedAt?.Invoke() ?? long.MaxValue) >= readAt || keyActedAt >= readAt)
        {
            Log.Write("Spelling: the sentence read from the field was not corrected: the text was touched since it was read");
            return false;
        }

        for (var gone = new StringInfo(original).LengthInTextElements; gone > 0; gone--)
        {
            KeySender.Press(Keycode.Del, KeySender.Held.None);
        }

        KeySender.Type(corrected);
        wordFix = null;
        BeginSentence(blind: false, byKeys: true);
        closed = corrected;
        lastTyped = corrected.Length > 0 ? corrected[^1] : lastTyped;
        Log.Detail("Spelling: a sentence read from the field corrected");
        return true;
    }

    /// <summary>A sentence closed with . ? or !, in Spanish, typed whole by these keys: for the models to read (<see cref="AccentModel"/>, <see cref="PunctuationModel"/>); with it, whether these keys began it (Enter, or . ? or ! typed).</summary>
    public event Action<string, bool>? SentenceClosed;

    /// <summary>The last sentence closed, while what was typed after it is still known: a correction of it can take its place.</summary>
    private string? closed;

    /// <summary>
    /// The models' sentence taken in place of the one closed, what
    /// was typed after it written again behind it; only while nothing but
    /// these keys has touched the window since. Whether it was.
    /// </summary>
    public bool ApplyCorrection(string original, string corrected, bool undoable)
    {
        if (closed != original || !SentenceKnown)
        {
            Log.Write(closed != original
                ? "Spelling: the correction was not applied: another sentence was closed since"
                : "Spelling: the correction was not applied: the mouse or the computer's keys touched the text since, so where it is is not known");
            return false;
        }

        var after = sentence!.ToString();
        for (var gone = original.Length + after.Length; gone > 0; gone--)
        {
            KeySender.Press(Keycode.Del, KeySender.Held.None);
        }

        KeySender.Type(corrected + after);
        wordFix = null;

        // Undoable once: the sentence written again as it was typed, from the toolbar.
        closed = undoable ? corrected : null;
        Log.Detail(undoable ? "Spelling: a sentence corrected" : "Spelling: a sentence written again from the toolbar");
        return true;
    }

    /// <summary>
    /// Enter closing a sentence these keys typed whole, with Autocorrect on:
    /// its accents and punctuation corrected by the models before the line ends,
    /// for after Enter the field may add what it likes (an editor's indent),
    /// and where the sentence is would not be known; no ↶ for it.
    /// </summary>
    private void CorrectBeforeEnter(string ended)
    {
        if (!Corrects || !SentenceRead)
        {
            return;
        }

        if (!SentenceKnown)
        {
            // Lost (dictation, a click, the computer's keys): the sentence as the field holds it.
            if (CorrectFieldNow?.Invoke(ended) != true)
            {
                NotCorrected(SentenceUnknownWhy);
            }

            return;
        }

        var typed = sentence!.ToString();
        if (typed.Trim().Length < 2)
        {
            return;
        }

        if (CorrectNow?.Invoke(typed, sentenceByKeys) is not { } corrected)
        {
            NotCorrected("the models are not loaded yet");
            return;
        }

        if (corrected == typed)
        {
            Log.Detail("Spelling: the model changed nothing in a sentence");
            return;
        }

        for (var gone = typed.Length; gone > 0; gone--)
        {
            KeySender.Press(Keycode.Del, KeySender.Held.None);
        }

        KeySender.Type(corrected);
        Log.Detail("Spelling: a sentence corrected before Enter");
    }

    /// <summary>Characters these keys deleted from the sentence; more than it has, and where the caret is is not known.</summary>
    private void SentenceDeleted(int count)
    {
        wordFix = null;
        SentenceSeen();
        if (sentence is null)
        {
            return;
        }

        if (count > sentence.Length)
        {
            sentence = null;
        }
        else
        {
            sentence.Length -= count;
        }
    }

    /// <summary>
    /// A sentence about to be closed with ? or !, in Spanish, mended as
    /// Spelling has it (<see cref="SpanishMarks.Opened"/>): its opening mark
    /// put at its start; deleted and typed again from where it begins, only
    /// when these keys typed all of it.
    /// </summary>
    private void MendSentence(char closing)
    {
        if (Spelling.OpenMark && Corrects && SentenceKnown
            && SpanishMarks.Opened(sentence!.ToString(), closing) is { } mended)
        {
            for (var gone = 0; gone < mended.Replaced; gone++)
            {
                KeySender.Press(Keycode.Del, KeySender.Held.None);
            }

            KeySender.Type(mended.Text);
            SentenceDeleted(mended.Replaced);
            SentenceTyped(mended.Text);
        }
    }

    /// <summary>
    /// The word being typed letter by letter, as the keys typed it: Windows
    /// tells a keyboard nothing of the field, so it is the keys' own count,
    /// begun again by anything but a letter or ⌫.
    /// </summary>
    private string typingWord = "";

    /// <summary>The word being typed letter by letter; empty, none.</summary>
    public string TypingWord => typingWord;

    /// <summary>When the word being typed began: it is where the keys think only while nothing else touched the window since (<see cref="WordKnown"/>).</summary>
    private long wordFrom;

    private bool WordKnown => wordStartKnown && (KeyedAt?.Invoke() ?? long.MaxValue) < wordFrom && (PressedAt?.Invoke() ?? long.MaxValue) < wordFrom;

    /// <summary>
    /// Whether the word being typed began where these keys know a word
    /// begins: after a space or a sign they typed, or at the start of a
    /// field, a line or a sentence they began; not where a press put the
    /// caret, maybe in the middle of a word ("ka|sa"). Only then is it
    /// corrected by itself: it would take its part for the word. Words are offered all the same, written only with a tap.
    /// </summary>
    private bool wordStartKnown;

    /// <summary>
    /// The word's first letters typed, already in the sentence's record:
    /// whether what is before them is known to end a word, by the record, or
    /// as a space or a sign these keys typed just before with nothing else
    /// since (<see cref="boundaryAt"/>), for a sentence no longer known after
    /// an arrow.
    /// </summary>
    private bool StartsKnown(string first)
    {
        if (boundaryAt > 0 && (KeyedAt?.Invoke() ?? long.MaxValue) < boundaryAt && (PressedAt?.Invoke() ?? long.MaxValue) < boundaryAt)
        {
            return true;
        }

        if (sentence is not { } typed || !SentenceKnown)
        {
            return false;
        }

        var before = typed.Length - first.Length - 1;
        return before >= 0 ? !char.IsLetterOrDigit(typed[before]) : before == -1 && !sentenceBlind;
    }

    /// <summary>When these keys last typed what ends a word (a space, a sign, Enter, a word taken with its space), if nothing else was typed since; 0, none.</summary>
    private long boundaryAt;

    /// <summary>Spelling › Correct words: the words a word typed in a language may have meant, the likeliest first (<see cref="SpellingDictionary"/>, <see cref="CommonErrors"/>); none, it is left as it is.</summary>
    public Func<StandardLanguage, string, WordFixes?>? Fixing { get; set; }

    /// <summary>A misspelled word ended: the word as typed, the words it may have meant, and whether the first was written in its place (Autocorrect), for the toolbar to take it back or offer them (<see cref="ReplaceWord"/>).</summary>
    public event Action<string, IReadOnlyList<string>, bool>? WordFixed;

    /// <summary>The last word Spelling checked, while nothing has been typed after what ended it: what is written, the word as typed, what ended it, and since when.</summary>
    private sealed record WordFix(string Written, string Typed, string Ending, long From);

    private WordFix? wordFix;

    /// <summary>
    /// The word typed letter by letter, ended now, before what ends it is
    /// typed: misspelled, with Autocorrect on, the likeliest word it may have
    /// meant written in its place; the word as typed, those words and whether
    /// one was written, for the toolbar. None when it is left as it is, or
    /// where it is is not known.
    /// </summary>
    private (string Typed, IReadOnlyList<string> Fixes, bool Applied)? FixWord()
    {
        if (typingWord.Length > 0 && !WordKnown)
        {
            Log.Detail("Spelling: a word left as it is: where it begins is not known (a click put the caret in the text, or a key of the computer's was pressed)");
            return null;
        }

        if (typingWord.Length == 0 || Fixing?.Invoke(language, typingWord) is not { Words.Count: > 0 } fixes)
        {
            return null;
        }

        var typed = typingWord;
        var applied = Corrects && fixes.Misspelled;
        if (fixes.Misspelled && Spelling.Autocorrect && !applied)
        {
            Log.Detail("Spelling: a word only offered: the field is not a document");
        }

        if (applied)
        {
            Retype(typed.Length, fixes.Words[0]);
            typingWord = fixes.Words[0];
            Log.Detail("Spelling: a word corrected");
        }

        return (typed, fixes.Words, applied);
    }

    /// <summary>A word ended with a space where the language's context model is on: the sentence so far, the word, and when its space was typed (<see cref="TakeModelWord"/>).</summary>
    public event Action<string, string, long>? WordEnded;

    /// <summary>
    /// A word just ended as the context model writes it (<see cref="WordEnded"/>),
    /// as a word's correction is: with Autocorrect, in a document, written in
    /// its place, ↶ offering it back; else offered with ✓. Only while nothing
    /// has been typed since its space, nor anything else touched the text.
    /// </summary>
    public void TakeModelWord(string typed, string mended, long endedAt)
    {
        if (boundaryAt != endedAt || typingWord.Length > 0)
        {
            Log.Detail("Spelling: the context model's word came after more was typed, so it is left");
            return;
        }

        wordFix = new WordFix(typed, typed, " ", endedAt);
        var applied = Corrects;
        if (applied)
        {
            ReplaceWord(mended, fromToolbar: false);
            wordFix = new WordFix(mended, typed, " ", boundaryAt);
            Log.Detail("Spelling: a word written again by the context model");
        }

        WordFixed?.Invoke(typed, [mended], applied);
    }

    /// <summary>The word checked offered in the toolbar once what ended it is typed: taken back with ↶, or the words it may have meant.</summary>
    private void OfferFix((string Typed, IReadOnlyList<string> Fixes, bool Applied) fixing, string ending)
    {
        wordFix = new WordFix(fixing.Applied ? fixing.Fixes[0] : fixing.Typed, fixing.Typed, ending, Environment.TickCount64);
        WordFixed?.Invoke(fixing.Typed, fixing.Fixes, fixing.Applied);
    }

    /// <summary>
    /// A word of the toolbar taken in place of the last word checked, what
    /// ended it written again after it: the word as typed takes a correction
    /// back, and counts towards learning it; only while nothing but these
    /// keys touched the window and nothing was typed since. The context
    /// model's word goes the same way (<see cref="TakeModelWord"/>), its own
    /// line written to the log in place of the toolbar's.
    /// </summary>
    public void ReplaceWord(string word, bool fromToolbar = true)
    {
        if (wordFix is not { } fix || (KeyedAt?.Invoke() ?? long.MaxValue) >= fix.From || (PressedAt?.Invoke() ?? long.MaxValue) >= fix.From)
        {
            Log.Detail("Spelling: the word was not replaced: something else touched the text since");
            wordFix = null;
            return;
        }

        Retype(fix.Written.Length + fix.Ending.Length, word + fix.Ending);
        wordFix = null;
        boundaryAt = Environment.TickCount64;
        if (fromToolbar)
        {
            Log.Detail(word == fix.Typed ? "Spelling: a word's correction taken back" : "Spelling: a word corrected from the toolbar");
        }
    }

    /// <summary>The last characters these keys typed deleted and the text typed in their place, the sentence's record kept with them.</summary>
    private void Retype(int gone, string text)
    {
        for (var left = gone; left > 0; left--)
        {
            KeySender.Press(Keycode.Del, KeySender.Held.None);
        }

        KeySender.Type(text);
        SentenceDeleted(gone);
        SentenceTyped(text);
    }

    /// <summary>The word being typed changed: the words starting so offered, as Gboard's while a word is typed.</summary>
    private void OfferWords()
    {
        // Offered even where the word's start is not known: a word offered is only written with a tap.
        IReadOnlyList<string> words = addressTyped.Contains('@') && AddressesStarting is { } addresses ? addresses(addressTyped)
            : typingWord.Length > 0 && swipedText is null && Suggesting is { } suggesting ? suggesting(language, typingWord) : [];
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
        LetSpokenGo("a word of the toolbar was taken");

        // An address in place of the one being typed, whole, with nothing after it.
        if (PersonalWords.IsAddress(word))
        {
            Retype(addressTyped.Contains('@') ? addressTyped.Length : typingWord.Length, word);
            addressTyped = word;
            lastTyped = word[^1];
            typingWord = "";
            Offered?.Invoke([]);
            return;
        }

        Retype(typingWord.Length, word + " ");
        lastTyped = ' ';
        boundaryAt = Environment.TickCount64;
        typingWord = "";
        Offered?.Invoke([]);
    }

    /// <summary>
    /// A swipe's words: the likeliest typed, in capitals or with a capital as
    /// Shift had it, with a space before it unless after a space or nothing,
    /// none after; the words offered in the toolbar. <paramref name="read"/> is
    /// the word the swipe read first, which what was learnt may have put
    /// behind another.
    /// </summary>
    public void TypeSwiped(IReadOnlyList<string> words, string read, bool capital, bool capitals)
    {
        if (words.Count == 0)
        {
            return;
        }

        LetSpokenGo("a word was swiped");
        swipeRead = read;
        ForgetTyping();
        var word = Cased(words[0], capital, capitals);
        var text = lastTyped is null || char.IsWhiteSpace(lastTyped.Value) ? word : " " + word;
        KeySender.Type(text);
        SentenceTyped(text);
        lastTyped = text[^1];
        boundaryAt = 0;
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
        Retype(typed.Length, text);
        lastTyped = text[^1];
        boundaryAt = 0;
        swipedText = text;
        Suggestions?.Invoke([], null);
        if (swipeRead is { } read)
        {
            SwipeChosen?.Invoke(language, read, word);
        }
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
    public double HeightAt(double scale) => HeightOf(BaseRows, scale);

    /// <summary>The rows' share of Gboard's height that makes the keys as tall as asked, the gaps Gboard's; half Gboard's at least.</summary>
    public double ScaleFor(double height) =>
        Math.Max(MinRowScale, (height - TopDp - BottomDp - ((BaseRows - 1) * RowGapDp)) / (NumberRowDp + ((BaseRows - 1) * LetterRowDp)));

    /// <summary>The keys' height for a number of rows at a share of Gboard's.</summary>
    public static double HeightOf(int rows, double scale) => TopDp + (NumberRowDp * scale) + ((rows - 1) * ((LetterRowDp * scale) + RowGapDp)) + BottomDp;

    /// <summary>The rows of the keyboard's first page, the one its height is made for, as designed where it is; one at least.</summary>
    private int BaseRows => Math.Max(1, Rows(pc ? StandardPage.Pc : StandardPage.Letters).Count);

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

    /// <summary>
    /// The page's keys in place, as the Android keyboard lays them: the
    /// widest row of the keyboard as made across, a design or not, Gboard's
    /// gaps, the top row lower; a row designed wider than that squeezes its
    /// own keys alone, so a key widened takes its room from its row; a shaped
    /// key's top sized after its lower part (<see cref="KeyWidths"/>).
    /// </summary>
    private void Lay()
    {
        laid = [];
        places = [];
        rowBands = [];
        joints = [];
        wordSizes.Clear();
        var rows = Rows(page);
        var width = ActualWidth;
        if (width <= 0)
        {
            return;
        }

        RowScale = ActualHeight > 0 ? ScaleFor(ActualHeight) : RowScale;
        var made = StandardKeys.Page(page, language);
        var across = Math.Max(1, made.Max(row => row.Sum(key => (double)key.Width)));
        var unit = (width - (2 * SideDp) - ((across - 1) * KeyGapDp)) / across;
        keyUnit = unit;
        glyphScale = Math.Min(1, unit / (LetterRow * GboardKeyAspect));
        double[] heights = page is StandardPage.Letters or StandardPage.Pc
            ? [NumberRowDp * RowScale, .. Enumerable.Repeat(LetterRow, Math.Max(0, rows.Count - 1))]
            : [.. Enumerable.Repeat((ActualHeight - TopDp - BottomDp - ((rows.Count - 1) * RowGapDp)) / rows.Count, rows.Count)];
        var top = TopDp;
        List<int> shapedAbove = [];
        for (var r = 0; r < rows.Count; r++)
        {
            var widths = KeyWidths(rows, r, across, unit, width);
            var rowWidth = widths.Sum() + ((widths.Length - 1) * KeyGapDp);
            var x = (width - rowWidth) / 2;
            // A row with a shaped key's lower part stands so the part is right-aligned under the key.
            if (rows[r].Select((key, i) => key.Lower ? i : -1).FirstOrDefault(i => i >= 0, -1) is var lowerAt and >= 0 && shapedAbove.Count > 0)
            {
                var lowerRight = x + widths.Take(lowerAt + 1).Sum() + (lowerAt * KeyGapDp);
                x += laid[shapedAbove[0]].Box.Right - lowerRight;
            }

            List<int> shaped = [];
            var lowers = 0;
            for (var i = 0; i < rows[r].Count; i++)
            {
                var key = rows[r][i];
                var keyWidth = widths[i];
                laid.Add((key, new Rect(x, top, Math.Max(1, keyWidth), Math.Max(1, heights[r]))));
                places.Add((r, i));
                if (key.Below > 0)
                {
                    shaped.Add(laid.Count - 1);
                }

                if (key.Lower && lowers < shapedAbove.Count)
                {
                    joints.Add((shapedAbove[lowers++], laid.Count - 1));
                }

                x += keyWidth + KeyGapDp;
            }

            shapedAbove = shaped;
            rowBands.Add((top, top + heights[r]));
            top += heights[r] + RowGapDp;
        }

        SizeWords();
        InvalidateVisual();
    }

    /// <summary>
    /// A row's keys' widths: each key's width in keys at the row's key size,
    /// the keyboard as made's widest row across, a row longer than that
    /// squeezing its own keys alone; a shaped key's top as its lower part
    /// makes it, its width in keys at the size of the row below, where that
    /// part is, so the two grow and shrink alike and the shape is kept, the
    /// other keys of its row sharing what is left.
    /// </summary>
    private double[] KeyWidths(IReadOnlyList<IReadOnlyList<StandardKey>> rows, int r, double across, double unit, double width)
    {
        var row = rows[r];
        Dictionary<int, double> following = [];
        if (r + 1 < rows.Count && rows[r + 1].Where(key => key.Lower).ToList() is { Count: > 0 } lowers)
        {
            var belowUnit = RowUnit(rows[r + 1], across, unit, width);
            var tops = row.Select((key, i) => key.Below > 0 ? i : -1).Where(i => i >= 0).ToList();
            for (var k = 0; k < tops.Count && k < lowers.Count; k++)
            {
                following[tops[k]] = (row[tops[k]].Width * belowUnit) + ((lowers[k].Width - 1) * KeyGapDp);
            }
        }

        var others = row.Where((_, i) => !following.ContainsKey(i)).ToList();
        var keys = others.Sum(key => (double)key.Width);
        var room = width - (2 * SideDp) - following.Values.Sum() - ((keys - others.Count + row.Count - 1) * KeyGapDp);
        var rowUnit = keys > 0 && keys * unit > room ? room / keys : unit;
        return [.. row.Select((key, i) => following.TryGetValue(i, out var made) ? made : (key.Width * rowUnit) + ((key.Width - 1) * KeyGapDp))];
    }

    /// <summary>A row's key size: the keyboard's, or smaller where the row is longer than the keyboard as made's widest.</summary>
    private double RowUnit(IReadOnlyList<StandardKey> row, double across, double unit, double width)
    {
        var keys = row.Sum(key => (double)key.Width);
        return keys > across ? (width - (2 * SideDp) - ((keys - 1) * KeyGapDp)) / keys : unit;
    }

    /// <summary>A page's rows: the letters' as designed where the keyboard has a design, else as made; Estándar's letters as the field's kind has them, but while designing.</summary>
    private IReadOnlyList<IReadOnlyList<StandardKey>> Rows(StandardPage shown)
    {
        var rows = design is not null && shown is StandardPage.Letters or StandardPage.Pc
            ? design.Page(StandardKeys.Page(shown, language), language)
            : StandardKeys.Page(shown, language);
        return shown == StandardPage.Letters && !Designing ? StandardKeys.ForField(rows, fieldKind) : rows;
    }

    /// <summary>
    /// The kind of field typed in, as Android's (<see cref="StandardField"/>), Estándar's alone, never Estándar PC's: a
    /// number's opens on 1234; a web or e-mail address's letters have / or @ in the comma's place and .com beside the
    /// space. Windows tells it of the settings' boxes to try them alone (<see cref="FocusWatch.FieldKind"/>). The page
    /// changes only when the kind does, so a click in the same field leaves the page in sight.
    /// </summary>
    public StandardField Field
    {
        get => fieldKind;
        set
        {
            if (value == fieldKind)
            {
                return;
            }

            // Kept on Estándar PC too, whose keys it leaves as they are: Estándar again, the field's page.
            fieldKind = value;
            ShowPage(BasePage);
        }
    }

    private StandardField fieldKind;

    /// <summary>The page a field opens on: a number's 1234, else the letters, Estándar's or Estándar PC's.</summary>
    private StandardPage BasePage => pc ? StandardPage.Pc : fieldKind == StandardField.Number ? StandardPage.Numbers : StandardPage.Letters;

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
        symbolTyped = false;
        Lay();
    }

    /// <summary>A symbol (or a digit) typed on ?123 or =\&lt; since it was opened: the next space or Enter goes back to the letters, as Gboard's and Android's.</summary>
    private bool symbolTyped;

    private bool OnSymbols => page is StandardPage.Symbols or StandardPage.MoreSymbols;

    protected override void OnRender(DrawingContext context)
    {
        context.DrawRectangle(new SolidColorBrush(colors.Ground), null, new Rect(RenderSize));
        foreach (var (upper, lower) in joints)
        {
            if (!(designing && dragged is { } moving && (moving.Place == places[upper] || moving.Place == places[lower])))
            {
                DrawJoint(context, upper, lower);
            }
        }

        // Shaped keys' lower parts first, so the label their top draws across both stays on top.
        foreach (var i in Enumerable.Range(0, laid.Count).OrderBy(i => laid[i].Key.Lower ? 0 : 1))
        {
            if (designing && dragged is { } held && held.Place == places[i])
            {
                continue;
            }

            DrawKey(context, laid[i].Key, laid[i].Box, Marked(i), LabelAt(i));
        }

        if (designing)
        {
            DrawDesigning(context);
        }

        if (pressed is { Swiping: true } now && ShowsTrail)
        {
            DrawTrail(context, now);
        }

        if (pressed is { LongPressed: true, Offered: { } offered } choosing)
        {
            DrawChoices(context, offered, choosing.Chosen);
        }
    }

    /// <summary>
    /// What a key's long press offers, as Gboard's popup (measured on the
    /// emulator): the symbol in its corner first, then the letters the
    /// language has for it (<see cref="StandardLanguage.MoreLetters"/>), in
    /// capitals with Shift; at most <see cref="ChoicesAcross"/> a row, the
    /// rows going up from above the key, as many across as their number asks
    /// (eleven, three rows of four). In each row the first stands over the
    /// key and the next go right, then left, by turns (0, +1, −1, +2, …),
    /// toward the side with room where one has none; above the key, over the
    /// toolbar where it reaches it, as Gboard's and Android's, drawn past the
    /// keys' top (nothing above them clips), and down over the keys only
    /// where the keyboard's card has no room above.
    /// </summary>
    private Choices ChoicesFor(StandardKey key, Rect box)
    {
        List<string> texts = [key.Hint!];
        if (key.Text is { Length: 1 } letter && language.MoreLetters.TryGetValue(char.ToLowerInvariant(letter[0]), out var more))
        {
            var capitals = ShiftedFor(key);
            texts.AddRange(more.Select(character => capitals ? char.ToUpper(character, CultureInfo.CurrentCulture).ToString() : character.ToString()));
        }

        var rows = (texts.Count + ChoicesAcross - 1) / ChoicesAcross;
        var across = (texts.Count + rows - 1) / rows;
        var (width, height) = (box.Width, box.Height);
        var centre = box.X + (width / 2);
        var leftRoom = Math.Max(0, (int)Math.Floor((centre - (width / 2) - SideDp) / width));
        var rightRoom = Math.Max(0, (int)Math.Floor((ActualWidth - SideDp - centre - (width / 2)) / width));
        var left = (across - 1) / 2;
        var right = across - 1 - left;
        if (left > leftRoom)
        {
            (left, right) = (leftRoom, across - 1 - leftRoom);
        }
        else if (right > rightRoom)
        {
            (left, right) = (across - 1 - rightRoom, rightRoom);
        }

        List<int> offsets = [0];
        for (var step = 1; offsets.Count < across; step++)
        {
            if (step <= right)
            {
                offsets.Add(step);
            }

            if (step <= left && offsets.Count < across)
            {
                offsets.Add(-step);
            }
        }

        var cells = texts.Select((_, at) => new Rect(centre + (offsets[at % across] * width) - (width / 2), box.Top - ((at / across) + 1) * height, width, height)).ToList();
        var ground = new Rect(centre - ((left + 0.5) * width), box.Top - (rows * height), across * width, rows * height);
        ground.Inflate(ChoicesPaddingDp, ChoicesPaddingDp);

        // Above the card's top, the popup comes down instead: all of it in sight.
        var down = Math.Max(0, -(ground.Top + TopInCard()));
        if (down > 0)
        {
            cells = [.. cells.Select(cell => new Rect(cell.X, cell.Y + down, cell.Width, cell.Height))];
            ground.Offset(0, down);
        }

        return new Choices(texts, cells, ground);
    }

    /// <summary>How far the keys stand below the top of the keyboard's card (the window's content): the toolbar's room, which the popup may take.</summary>
    private double TopInCard() =>
        Window.GetWindow(this)?.Content is UIElement card ? TranslatePoint(new Point(0, 0), card).Y : 0;

    /// <summary>The popup's choice under the point, or the nearest, as Gboard's follows the finger anywhere.</summary>
    private static int Nearest(Choices offered, Point point) =>
        offered.Cells.Select((cell, at) => (At: at, Far: Math.Pow(point.X - (cell.X + (cell.Width / 2)), 2) + Math.Pow(point.Y - (cell.Y + (cell.Height / 2)), 2)))
            .MinBy(cell => cell.Far).At;

    /// <summary>A long press's popup, as Gboard's: a rounded ground in the keys' colour, an outline, each choice's letter, the one chosen on a circle in the colour of a key that is on.</summary>
    private void DrawChoices(DrawingContext context, Choices offered, int chosen)
    {
        context.DrawRoundedRectangle(new SolidColorBrush(colors.Key), new Pen(new SolidColorBrush(colors.FaintOutline), 1), offered.Ground, ChoicesCornerDp, ChoicesCornerDp);
        var size = LetterRow * glyphScale * LabelShare;
        for (var at = 0; at < offered.Texts.Count; at++)
        {
            var cell = offered.Cells[at];
            var centre = new Point(cell.X + (cell.Width / 2), cell.Y + (cell.Height / 2));
            if (at == chosen)
            {
                var radius = Math.Min(cell.Width, cell.Height) / 2;
                context.DrawEllipse(new SolidColorBrush(colors.On), null, centre, radius, radius);
            }

            DrawText(context, offered.Texts[at], centre, size, colors.Glyph, Regular);
        }
    }

    /// <summary>A key's ground: marked blue while designing, a modifier on, a function key's tint or a letter's, darker while pressed.</summary>
    private Color KeyGround(StandardKey key, bool marked)
    {
        var down = !designing && pressed is { Swiping: false } now && now.Key == key;
        return marked ? colors.Primary
            : IsOn(key) ? colors.On
            : key.Look != StandardLook.Letter ? (down ? colors.FunctionPressed : colors.Function)
            : (down ? colors.Function : colors.Key);
    }

    /// <summary>A shaped key's two parts drawn as one: the room between them filled where both are across, into their corners, so the edge they share runs straight.</summary>
    private void DrawJoint(DrawingContext context, int upper, int lower)
    {
        var top = laid[upper].Box;
        var bottom = laid[lower].Box;
        var left = Math.Max(top.Left, bottom.Left);
        var right = Math.Min(top.Right, bottom.Right);
        if (right <= left)
        {
            return;
        }

        var ground = KeyGround(laid[upper].Key, Marked(upper));
        context.DrawRectangle(new SolidColorBrush(ground), null, new Rect(left, top.Bottom - CornerDp, right - left, bottom.Top - top.Bottom + (2 * CornerDp)));
    }

    /// <summary>Whether a laid key is marked while designing: itself, or the other part of its shaped key, the whole key marked as one.</summary>
    private bool Marked(int i) =>
        designing && (selected == places[i] || joints.Any(joint => (joint.Top == i || joint.Lower == i) && (selected == places[joint.Top] || selected == places[joint.Lower])));

    /// <summary>Where a shaped key's label goes: across, the middle of where its two parts both are; down, halfway from its top to its bottom, inside the shape whichever it is. None for any other key, labelled in its box's middle.</summary>
    private Point? LabelAt(int i)
    {
        var at = joints.FindIndex(joint => joint.Top == i);
        if (at < 0)
        {
            return null;
        }

        var joint = joints[at];
        var top = laid[joint.Top].Box;
        var bottom = laid[joint.Lower].Box;
        return new Point((Math.Max(top.Left, bottom.Left) + Math.Min(top.Right, bottom.Right)) / 2, (top.Top + bottom.Bottom) / 2);
    }

    private void DrawKey(DrawingContext context, StandardKey key, Rect box, bool marked = false, Point? labelAt = null)
    {
        if (key.Look == StandardLook.Gap)
        {
            if (designing)
            {
                context.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(marked ? colors.Primary : colors.FaintOutline), 2), box, CornerDp, CornerDp);
            }

            return;
        }

        var function = key.Look != StandardLook.Letter;
        var corner = key.Look == StandardLook.Pill ? box.Height / 2 : CornerDp;
        context.DrawRoundedRectangle(new SolidColorBrush(KeyGround(key, marked)), null, box, corner, corner);
        if (key.Lower)
        {
            return;
        }

        var glyph = marked ? colors.OnPrimary : function ? colors.FunctionGlyph : colors.Glyph;
        var h = LetterRow * glyphScale;
        var centre = labelAt ?? new Point(box.X + (box.Width / 2), box.Y + (box.Height / 2));

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

        var showsHint = key.Hint is not null && (altGr || pressed is { LongPressed: true, Offered: null } held && held.Key == key);

        // Alt Gr on, a PC's keys show only its symbols: a key with none, blank.
        if (pc && altGr && key.Hint is null)
        {
            return;
        }

        DrawText(context, showsHint ? key.Hint! : LabelOf(key), centre, h * LabelShare, glyph, Regular);

        // A PC's key, as a PC's keycap: its Shift's at the top corner, its Alt Gr's at the bottom one, and with Shift or
        // Alt Gr on only what it types then; Gboard's, its held symbol at the top but while it stands in the key's place.
        var corners = !showsHint && !(pc && (shift != Shift.Off || altGr));
        if (corners && pc && key.ShiftCorner is { } shifted)
        {
            DrawCorner(context, shifted, box, h, glyph, top: true);
        }

        if (corners && key.Hint is not null)
        {
            DrawCorner(context, key.Hint, box, h, glyph, top: !pc);
        }
    }

    /// <summary>A symbol in a key's right corner, Android's size and inset (on the Fold 9 px from the edge and 8 px in, of an 82 px key), at its top or bottom.</summary>
    private void DrawCorner(DrawingContext context, string symbol, Rect box, double h, Color glyph, bool top)
    {
        var corner = Text(symbol, h * HintShare, glyph, Regular);
        var inset = box.Height * glyphScale * HintTopShare;
        var baseline = top ? box.Y + inset + (h * HintShare * CapHeight) : box.Bottom - inset;
        context.DrawText(corner, new Point(box.Right - (box.Height * glyphScale * HintRightShare) - corner.Width, baseline - corner.Baseline));
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

    /// <summary>Whether a key is on, its whole key in Caps Lock's colour: the modifiers, Caps Lock, and on a PC's keys Shift (Gboard's shows it by its sign alone).</summary>
    private bool IsOn(StandardKey key) => key.Action switch
    {
        StandardAction.Shift => pc && shift != Shift.Off,
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
            if (designing)
            {
                DesignDown(e.GetPosition(this));
            }
            else
            {
                Begin(e.GetPosition(this), null);
            }
        }

        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (e.StylusDevice is null)
        {
            // Finished before the capture goes, for losing it forgets the key.
            if (designing)
            {
                DesignUp(e.GetPosition(this));
            }
            else
            {
                Finish(null);
            }

            ReleaseMouseCapture();
        }

        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (pressed is { Touch: null })
        {
            Forget();
        }
    }

    protected override void OnLostTouchCapture(TouchEventArgs e)
    {
        base.OnLostTouchCapture(e);
        if (pressed is { } now && now.Touch == e.TouchDevice.Id)
        {
            Forget();
        }
    }

    /// <summary>
    /// The key let go of without its release, as Android's cancel: the mouse's or the finger's capture lost (the window
    /// hidden or made again, another taking it), the keys out of sight; nothing held goes on and nothing is typed.
    /// </summary>
    private void Forget()
    {
        StopHolding();
        pressed = null;
        InvalidateVisual();
    }

    protected override void OnTouchDown(TouchEventArgs e)
    {
        CaptureTouch(e.TouchDevice);
        if (designing)
        {
            DesignDown(e.GetTouchPoint(this).Position);
        }
        else
        {
            Begin(e.GetTouchPoint(this).Position, e.TouchDevice.Id);
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.StylusDevice is null && e.LeftButton == MouseButtonState.Pressed)
        {
            if (designing)
            {
                DesignMove(e.GetPosition(this));
            }
            else
            {
                Track(e.GetPosition(this), null);
            }
        }
    }

    protected override void OnTouchMove(TouchEventArgs e)
    {
        if (designing)
        {
            DesignMove(e.GetTouchPoint(this).Position);
        }
        else
        {
            Track(e.GetTouchPoint(this).Position, e.TouchDevice.Id);
        }

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

        if (now.LongPressed && now.Offered is { } offered)
        {
            var chosen = Nearest(offered, point);
            if (chosen != now.Chosen)
            {
                now.Chosen = chosen;
                InvalidateVisual();
            }

            return;
        }

        if (now.LongPressed || now.Repeated)
        {
            return;
        }

        var far = Math.Max(Math.Abs(point.X - now.Start.X), Math.Abs(point.Y - now.Start.Y)) > Math.Min(now.Box.Width, now.Box.Height) * 0.6;
        if (far && Glides && page is StandardPage.Letters or StandardPage.Pc && now.Key.Text is { Length: 1 } letter && language.SwipeLetters.Contains(letter))
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
        if (designing)
        {
            DesignUp(e.GetTouchPoint(this).Position);
        }
        else
        {
            Finish(e.TouchDevice.Id);
        }

        ReleaseTouchCapture(e.TouchDevice);
        e.Handled = true;
    }

    /// <summary>A key touched: a second finger down finishes the first's, as fast typing has it; held, what the key does held.</summary>
    private void Begin(Point point, int? touch)
    {
        if (!Types)
        {
            return;
        }

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
                now.Offered = ChoicesFor(now.Key, now.Box);
                now.Chosen = 0;
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
            if ((now.Offered?.Texts[now.Chosen] ?? now.Key.Hint) is { } chosen)
            {
                Type(chosen);
                ModifiersUsed();
            }
        }
        else
        {
            Act(now.Key);
        }

        InvalidateVisual();
    }

    /// <summary>A key pressed, about to act.</summary>
    public event Action? KeyPressed;

    private void Act(StandardKey key)
    {
        LetSpokenGo("a key of the keyboard's was pressed");
        keyActedAt = Environment.TickCount64;
        KeyPressed?.Invoke();

        // Gboard's: on ?123 or =\<, a space or Enter after a symbol typed there goes back to the letters.
        var backToLetters = symbolTyped && !Combining && key.Action is StandardAction.Space or StandardAction.Enter;

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
                ShowPage(StandardPage.Numbers);
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
                // After Enter the field may add what it likes, so a word is corrected only before it, and offers nothing after.
                if (Spelling.Autocorrect)
                {
                    FixWord();
                }

                // What the field must end with before its sentence is read: the word just ended, as corrected.
                var ended = typingWord.Length > 0 ? typingWord : lastTyped?.ToString() ?? "";
                ForgetTyping();
                FlushDead();
                CorrectBeforeEnter(ended);
                Press(Keycode.Enter);
                BeginSentence(blind: false, byKeys: true);
                lastTyped = '\n';
                boundaryAt = Environment.TickCount64;
                break;
        }

        if (backToLetters)
        {
            ShowPage(pc ? StandardPage.Pc : StandardPage.Letters);
        }

        InvalidateVisual();
    }

    /// <summary>Spelling's Capital at the start of a sentence (<see cref="OpensSentence"/>), while Spelling is on.</summary>
    public bool CapitalizesSentences { get; set; }

    /// <summary>The field typed in is a terminal's (<see cref="FocusWatch.InTerminal"/>): no sentences, so no capital opens one.</summary>
    public bool InTerminal { get; set; }

    /// <summary>Shift on for one key, tapped, let go: a dictation wrote in its place. Locked, it stays.</summary>
    private void ShiftOnceUsed()
    {
        if (shift != Shift.Once)
        {
            return;
        }

        shift = Shift.Off;
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
        var capital = shift == Shift.Once || OpensSentence();
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
        if (Combining && PressShortcut(key.Text))
        {
            ModifiersUsed();
            return;
        }

        if (altGr && key.Hint is not null)
        {
            Type(key.Hint);
        }
        else
        {
            // A letter that opens a sentence goes out a capital, Shift left as it is.
            var capital = key.Text is { Length: 1 } letter && char.IsLetter(letter[0]) && OpensSentence();
            var typed = (ShiftedFor(key) || capital) && key.Shifted is not null ? key.Shifted : key.Text!;
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

    /// <summary>
    /// Whether what is being typed is an e-mail or web address: @ . / or :
    /// typed since the last space ("someone@example", "www.berpiztu"), so nothing
    /// in it is corrected; a space, a new line or a word ended by one ends it.
    /// </summary>
    private bool inAddress;

    /// <summary>What these keys typed since the last space, while it is theirs alone: with an @ in it, an e-mail address being typed, the addresses kept offered for it (<see cref="AddressesStarting"/>).</summary>
    private string addressTyped = "";

    /// <summary>The e-mail addresses the dictionary keeps that start as the one typed, every language's.</summary>
    public Func<string, IReadOnlyList<string>>? AddressesStarting { get; set; }

    /// <summary>Whether an e-mail or web address is being typed: no word in it is offered to learn.</summary>
    public bool TypingAddress => inAddress || addressTyped.Contains('@');

    /// <summary>Whether a sentence closed by its . ends in an address ("someone@example."), its accents then left as they are.</summary>
    private static bool EndsInAddress(string sentence)
    {
        var closed = sentence.TrimEnd()[..^1];
        return closed[(closed.LastIndexOfAny([' ', '\n', '\t']) + 1)..].IndexOfAny(['@', '.', '/', ':']) >= 0;
    }

    /// <summary>Text typed, a dead key's accent on it where it has one with it (´ then a: á), or before it; a space types the accent alone.</summary>
    private void Type(string typed)
    {
        symbolTyped |= OnSymbols && typed != " ";
        if (deadKey is { } accent)
        {
            deadKey = null;
            var composed = (typed + DeadMarks[accent]).Normalize(NormalizationForm.FormC);
            typed = typed == " " ? accent : composed.Length == 1 ? composed : accent + typed;
        }

        var letters = typed.Length > 0 && typed.All(char.IsLetter);

        // @ ends no word, and nothing in an address is corrected, as Gboard's and Android's.
        var fixing = letters || typed == "@" || inAddress ? null : FixWord();
        inAddress = typed.Length > 0 && !typed.Any(char.IsWhiteSpace) && (inAddress || typed.IndexOfAny(['@', '.', '/', ':']) >= 0);
        addressTyped = typed.Length == 0 || typed.Any(char.IsWhiteSpace) ? "" : addressTyped + typed;
        if (typed is "?" or "!")
        {
            MendSentence(typed[0]);
        }

        KeySender.Type(typed);
        SentenceTyped(typed);
        lastTyped = typed.Length > 0 ? typed[^1] : lastTyped;
        if (letters)
        {
            if (typingWord.Length == 0)
            {
                wordFrom = Environment.TickCount64;
                wordStartKnown = StartsKnown(typed);
            }

            boundaryAt = 0;

            typingWord += typed;
            OfferWords();
        }
        else
        {
            var ended = typingWord;
            ForgetTyping();
            boundaryAt = typed.Length > 0 && !char.IsLetterOrDigit(typed[^1]) ? Environment.TickCount64 : 0;
            if (fixing is { } offered && typed.Length > 0)
            {
                OfferFix(offered, typed);
            }

            // A word ended with a space where the language's context model is on: its sentence so far, for the model.
            if (typed == " " && ended.Length > 0 && Spelling.Context is not null && SentenceKnown && !EndsInAddress(sentence!.ToString())
                && sentence.ToString().Trim() is { Length: > 0 } sofar && sofar.EndsWith(ended, StringComparison.Ordinal))
            {
                WordEnded?.Invoke(sofar, ended, boundaryAt);
            }
        }
    }

    private void FlushDead()
    {
        if (deadKey is { } accent)
        {
            deadKey = null;
            KeySender.Type(accent);
            SentenceTyped(accent);
        }
    }

    private void DeleteOnce()
    {
        boundaryAt = 0;
        // Right after a swipe, with Delete a swiped word whole on: the word and the space it put before it; held, ⌫ goes on letter by letter.
        if (DeletesSwipedWord && swipedText is { } typed && !Combining && deadKey is null)
        {
            foreach (var _ in typed)
            {
                KeySender.Press(Keycode.Del, KeySender.Held.None);
            }

            SentenceDeleted(typed.Length);
            EndSwipe(letter: false);
            lastTyped = null;
            return;
        }

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
            SentenceDeleted(1);
            if (typingWord.Length > 0)
            {
                typingWord = typingWord[..^1];
                OfferWords();
            }
        }
    }

    private void Press(Keycode code)
    {
        // An arrow, Home or a page moves the caret: the word being typed is no longer the one before it.
        ForgetTyping();
        sentence = null;
        wordFix = null;
        boundaryAt = 0;
        KeySender.Press(code, Held());
    }

    /// <summary>A character's key with Ctrl, Alt or ⊞: its key code where it has one, else the key of the language's layout that types it (Spanish's ç); false when neither.</summary>
    private bool PressShortcut(string? text)
    {
        if (CodeOf(text) is { } code)
        {
            Press(code);
            return true;
        }

        return text is { Length: 1 } one && KeySender.PressCharacter(one[0], language.Locale, Held());
    }

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
        if (shift == Shift.Locked)
        {
            SelectionCase?.Invoke();
        }
    }

    /// <summary>Shift tapped twice: the text selected in the window typed in, where there is some, turned to its next case (<see cref="StandardKeys.NextCase"/>), Shift then off again (<see cref="ShiftOff"/>); none selected, Shift stays locked.</summary>
    public Action? SelectionCase { get; set; }

    /// <summary>Shift off, its two taps having turned a selection's case.</summary>
    public void ShiftOff()
    {
        shift = Shift.Off;
        InvalidateVisual();
    }

    /// <summary>The keys' places as laid out, each its row and its place in the row, alongside <see cref="laid"/>.</summary>
    private List<(int Row, int Index)> places = [];

    /// <summary>Each row's top and bottom as laid out, an empty one's too.</summary>
    private List<(double Top, double Bottom)> rowBands = [];

    /// <summary>Each shaped key's two parts as laid out, by their places in <see cref="laid"/>, drawn joined.</summary>
    private List<(int Top, int Lower)> joints = [];

    private StandardDesign? design;
    private bool designing;

    /// <summary>A key one wide as laid out, without the gaps: the size a key added is drawn at before it is in a row.</summary>
    private double keyUnit;

    /// <summary>While designing: the key marked (its place), or a row (its place -1); the key dragged and where the pointer is; where the pointer went down.</summary>
    private (int Row, int Index)? selected;
    private ((int Row, int Index) Place, Point At)? dragged;
    private (int Row, int Index, Point At)? designDown;

    /// <summary>A key chosen in + Tecla, not in the design yet: drawn loose where it is, marked, until it is dragged and let go into a row; whether the pointer holds it.</summary>
    private readonly record struct LooseKey(string Id, StandardKey Key, Point At, bool Held);

    private LooseKey? added;

    /// <summary>How far a press goes before it is a drag: Android's touch slop, ViewConfiguration's 8 dp.</summary>
    private const double DragSlopDp = 8;

    private const float WidthStep = 0.25f;
    private const float LeastKeyWidth = 0.5f;
    private const float MostKeyWidth = 10f;

    /// <summary>The letters' page as designed (Diseñar); none, as made.</summary>
    public StandardDesign? Design
    {
        get => design;
        set
        {
            design = value;
            selected = null;
            added = null;
            InvalidateMeasure();
            Lay();
        }
    }

    /// <summary>Whether the keys are being designed: a click marks a key, a drag moves it; nothing is typed.</summary>
    public bool Designing
    {
        get => designing;
        set
        {
            designing = value;
            selected = null;
            dragged = null;
            designDown = null;
            added = null;
            StopHolding();
            pressed = null;
            if (value)
            {
                EndSwipe(letter: false);
                ForgetTyping();
                ShowPage(pc ? StandardPage.Pc : StandardPage.Letters);
            }

            InvalidateVisual();
        }
    }

    /// <summary>The design came to have more or fewer rows, told before the keys are laid again: the window made as tall as they need.</summary>
    public event Action? RowsChanged;

    /// <summary>−, +: the marked key a quarter of a key narrower or wider, a shaped Enter's two parts alike.</summary>
    public void ResizeMarked(bool wider) => ChangeMarked((row, index) =>
        design!.Resize(row, index, wider ? WidthStep : -WidthStep, LeastKeyWidth, MostKeyWidth));

    /// <summary>Quitar: the marked key out; a marked empty row out.</summary>
    public void RemoveMarked()
    {
        if (design is null || selected is not { } at)
        {
            return;
        }

        if (at.Index < 0)
        {
            design.Rows.RemoveAt(at.Row);
            selected = null;
        }
        else
        {
            design.Remove(at.Row, at.Index);
            selected = design.Rows[at.Row].Count == 0 ? (at.Row, -1) : (at.Row, Math.Min(at.Index, design.Rows[at.Row].Count - 1));
        }

        Redesigned();
    }

    /// <summary>+ Tecla: a key from the list loose in the keys' middle, marked, to be dragged to its place; in the design only once let go there.</summary>
    public void AddKey(string id)
    {
        if (design is null || StandardKeys.Extra(id, language) is not { } key)
        {
            return;
        }

        selected = null;
        added = new LooseKey(id, key, new Point(ActualWidth / 2, ActualHeight / 2), Held: false);
        InvalidateVisual();
    }

    /// <summary>Whether the keyboard as designed has a key of + Tecla's list already, which the list then leaves out.</summary>
    public bool HasKey(string id) => StandardKeys.HasExtra(Rows(pc ? StandardPage.Pc : StandardPage.Letters), id, language);

    /// <summary>A loose key's box: as wide as it is in keys, a letter row tall, centred where it is.</summary>
    private Rect LooseBox(LooseKey loose)
    {
        var width = (loose.Key.Width * keyUnit) + ((loose.Key.Width - 1) * KeyGapDp);
        return new Rect(loose.At.X - (width / 2), loose.At.Y - (LetterRow / 2), Math.Max(1, width), LetterRow);
    }

    /// <summary>The key added let go, as a key moved.</summary>
    private void PlaceAdded(LooseKey loose, Point point) => LetGo(new DesignedKey("x:" + loose.Id, loose.Key.Width), null, point);

    /// <summary>A marked Enter clicked again: its next shape, a shaped one's top going up right-aligned over the Enter's own part; false for any other key.</summary>
    private bool ReshapeEnter((int Row, int Index) at)
    {
        if (design is null)
        {
            return false;
        }

        var made = StandardKeys.Page(pc ? StandardPage.Pc : StandardPage.Letters, language);
        var own = design.LowerOf(at.Row, at.Index) ?? at;
        if (places.IndexOf(own) is not (var laidAt and >= 0))
        {
            return false;
        }

        // The top goes in over the Enter's right edge: placed by its right half, whatever its width.
        var topMiddle = laid[laidAt].Box.Right - (keyUnit / 2);
        var upperIndex = laid.Where((laidKey, i) => places[i].Row == own.Row - 1 && Middle(laidKey.Box) < topMiddle).Count();
        if (design.Reshape(at.Row, at.Index, made, language, upperIndex) is not { } now)
        {
            return false;
        }

        selected = now;
        Redesigned();
        return true;
    }

    /// <summary>+ Columna: a new column at the right, as + Fila adds a row at the top: a hole a key wide at the end of every row with keys, to be filled; the top one marked.</summary>
    public void AddColumn()
    {
        if (design is null)
        {
            return;
        }

        selected = null;
        for (var r = 0; r < design.Rows.Count; r++)
        {
            if (design.Rows[r].Count == 0)
            {
                continue;
            }

            design.Rows[r].Add(new DesignedKey(StandardKeys.GapId, 1f));
            selected ??= (r, design.Rows[r].Count - 1);
        }

        Redesigned();
    }

    /// <summary>
    /// A key let go, moved (from its place) or added: on a hole, in the
    /// hole's place; else into the row under it, before the first key whose
    /// middle is right of it; a shaped key starting there, its lower part in
    /// the row below, ending under its right edge; marked. False where
    /// refused: a shaped key in the last row.
    /// </summary>
    private bool LetGo(DesignedKey key, (int Row, int Index)? from, Point point)
    {
        if (design is null)
        {
            return false;
        }

        if (design.Rows.Count == 0)
        {
            design.Rows.Add([]);
        }

        var gap = laid.FindIndex(laidKey => laidKey.Key.Look == StandardLook.Gap && laidKey.Box.Contains(point));
        var fills = gap >= 0 && places[gap] != from;
        var row = fills ? places[gap].Row : Math.Max(0, RowAt(point.Y));
        // Counted with the key moved still in its place: the design puts it first and takes it out after.
        var index = fills ? places[gap].Index : laid.Where((laidKey, i) => places[i].Row == row && Middle(laidKey.Box) < point.X).Count();
        var lowerIndex = 0;
        if (StandardDesign.Shaped(key.Id) && StandardKeys.Extra(key.Id[2..], language) is { } shape)
        {
            var right = point.X + (key.Width * keyUnit) + ((key.Width - 1) * KeyGapDp);
            var lowerMiddle = right - (((shape.Below * keyUnit) + ((shape.Below - 1) * KeyGapDp)) / 2);
            lowerIndex = laid.Where((laidKey, i) => places[i].Row == row + 1 && Middle(laidKey.Box) < lowerMiddle).Count();
        }

        if (design.Put(key, row, index, fills, lowerIndex, from) is not { } now)
        {
            return false;
        }

        selected = now;
        Redesigned();
        return true;
    }

    private static double Middle(Rect box) => box.X + (box.Width / 2);

    /// <summary>+ Fila: an empty row over the marked key's row, or at the top, as the owner asked; marked.</summary>
    public void AddRow()
    {
        if (design is null)
        {
            return;
        }

        var row = selected is { } at ? at.Row : 0;
        design.Rows.Insert(row, []);
        selected = (row, -1);
        Redesigned();
    }

    private void ChangeMarked(Action<int, int> change)
    {
        if (design is null || selected is not { Index: >= 0 } at)
        {
            return;
        }

        change(at.Row, at.Index);
        Redesigned();
    }

    private void Redesigned()
    {
        if (Rows(page).Count != rowBands.Count)
        {
            RowsChanged?.Invoke();
        }

        InvalidateMeasure();
        Lay();
    }

    /// <summary>Pressed while designing: on the key added, held at once; on a key, or on an empty row, to be marked or, a key, dragged.</summary>
    private void DesignDown(Point point)
    {
        if (added is { } loose && LooseBox(loose).Contains(point))
        {
            added = loose with { Held = true };
            return;
        }

        var hit = laid.FindIndex(laidKey => laidKey.Box.Contains(point));
        var row = RowAt(point.Y);
        designDown = hit >= 0 ? (places[hit].Row, places[hit].Index, point) : row >= 0 && design?.Rows[row].Count == 0 ? (row, -1, point) : null;
    }

    /// <summary>A key pressed and moved past the slop is dragged, marked, under the pointer.</summary>
    private void DesignMove(Point point)
    {
        if (added is { Held: true } carried)
        {
            added = carried with { At = point };
            InvalidateVisual();
        }
        else if (dragged is { } moving)
        {
            dragged = (moving.Place, point);
            InvalidateVisual();
        }
        else if (designDown is { Index: >= 0 } down && Math.Max(Math.Abs(point.X - down.At.X), Math.Abs(point.Y - down.At.Y)) > DragSlopDp)
        {
            selected = (down.Row, down.Index);
            dragged = ((down.Row, down.Index), point);
            InvalidateVisual();
        }
    }

    /// <summary>Let go while designing: a key dragged stands where it was let go, in that row before the first key whose middle is right of it; a click marks the key, or the empty row.</summary>
    private void DesignUp(Point point)
    {
        if (added is { Held: true } placed)
        {
            added = null;
            PlaceAdded(placed, point);
        }
        else if (dragged is { } held)
        {
            MoveKey(held.Place, point);
            dragged = null;
            designDown = null;
        }
        else if (designDown is { } down)
        {
            // A marked Enter clicked again takes its next shape; anything else clicked is marked.
            if (!(down.Index >= 0 && selected == (down.Row, down.Index) && ReshapeEnter((down.Row, down.Index))))
            {
                selected = (down.Row, down.Index);
            }

            designDown = null;
            InvalidateVisual();
        }
    }

    /// <summary>The row whose band, gaps shared, holds a height; the nearest above or below the keys; -1 with none.</summary>
    private int RowAt(double y)
    {
        if (rowBands.Count == 0)
        {
            return -1;
        }

        var half = RowGapDp / 2;
        for (var r = 0; r < rowBands.Count; r++)
        {
            if (y >= rowBands[r].Top - half && y < rowBands[r].Bottom + half)
            {
                return r;
            }
        }

        return y < rowBands[0].Top ? 0 : rowBands.Count - 1;
    }

    private void MoveKey((int Row, int Index) from, Point point)
    {
        if (design is null)
        {
            return;
        }

        // A shaped key's lower part moves the whole key.
        var top = design.UpperOf(from.Row, from.Index) ?? from;
        if (!LetGo(design.Rows[top.Row][top.Index], top, point))
        {
            InvalidateVisual();
        }
    }

    /// <summary>While designing: each empty row outlined, to be marked and filled; the key dragged, or the one added, under the pointer.</summary>
    private void DrawDesigning(DrawingContext context)
    {
        if (design is not null)
        {
            for (var r = 0; r < rowBands.Count && r < design.Rows.Count; r++)
            {
                if (design.Rows[r].Count > 0)
                {
                    continue;
                }

                var outline = new Pen(new SolidColorBrush(selected == (r, -1) ? colors.Primary : colors.FaintOutline), 2);
                var band = new Rect(SideDp, rowBands[r].Top, Math.Max(1, ActualWidth - (2 * SideDp)), Math.Max(1, rowBands[r].Bottom - rowBands[r].Top));
                context.DrawRoundedRectangle(null, outline, band, CornerDp, CornerDp);
            }
        }

        if (dragged is { } held && places.IndexOf(held.Place) is var i and >= 0)
        {
            var box = laid[i].Box;
            DrawKey(context, laid[i].Key, new Rect(held.At.X - (box.Width / 2), held.At.Y - (box.Height / 2), box.Width, box.Height), marked: true);
        }

        if (added is { } loose)
        {
            DrawKey(context, loose.Key, LooseBox(loose), marked: true);
        }
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
