using Android.Graphics;
using Android.Graphics.Drawables;

namespace Berpiztu.KeyPlus;

/// <summary>
/// A character key's legends, drawn over the key as on a table of three by
/// three cells: the shifted character in the top left corner (0:0), the
/// plain one in the middle (1:1) and the Alt Gr one in the bottom right
/// corner (2:2). Every legend is one size, <see cref="TextSize"/>; the one
/// the key sends now is drawn strong, the others weak. The size is the
/// largest at which the legends of every key stand whole in it and apart
/// (<see cref="Fitting"/>), so they grow and shrink with the keys.
/// A light symbol, one that draws less ink than a small letter (a point, a
/// comma, a dash, an accent), is drawn half as large again where its key has
/// room, as a key's plain legend; a shifted or Alt Gr legend is drawn at
/// <see cref="SecondaryShare"/> of the plain one, on every key alike. On a punctuation key, its shifted and plain legends alone and both
/// light, each takes two rows of the table, the shifted one rows 0 and 1,
/// the plain one rows 1 and 2. A
/// symbol that draws as much as a letter (? ¿ @) stays as it is. A legend
/// wider than its key still (an F12 under Fn) is drawn smaller, never cut.
/// </summary>
public sealed class KeyLegends(int inset, int gap, Color strong, Color weak) : Drawable
{
    /// <summary>The cells of the table the legends stand in.</summary>
    public const int Shifted = 0;
    public const int Plain = 1;
    public const int AltGr = 2;

    /// <summary>A shifted or Alt Gr legend's size, of the key's plain one's: smaller, so the plain one leads.</summary>
    private const float SecondaryShare = 0.7f;

    /// <summary>How far past its key's height a legend's size may go, its ink still inside the key.</summary>
    private const float InkReach = 4f;

    /// <summary>The most of the plain legend's ink surface a secondary's ink takes: a half.</summary>
    private const float SecondaryInk = 0.5f;

    /// <summary>The punctuation keys whose two legends take two rows of the table each, at one size (; over , and : over .; _ over -).</summary>
    private static readonly HashSet<string> PunctuationPairs = [",", ".", "-"];

    /// <summary>How much larger a light symbol is drawn, primary or secondary, where its key has room: half as large again.</summary>
    private const float LightGrowth = 1.5f;

    /// <summary>Whether each symbol is light, measured once: it depends on the glyph, not on its size.</summary>
    private static readonly Dictionary<string, bool> Lights = [];

    private readonly Paint paint = new(PaintFlags.AntiAlias);
    private IReadOnlyList<Legend> legends = [];

    /// <summary>A legend: its text, its cell (<see cref="Shifted"/>, <see cref="Plain"/> or <see cref="AltGr"/>) and whether the key sends it now.</summary>
    public readonly record struct Legend(string Text, int Cell, bool Sent);

    /// <summary>The one size every legend has, in pixels.</summary>
    public float TextSize { get; set; }

    /// <summary>The legends' size before A− and A+, which a key keeps whatever its margins: grown past it, a key's legends keep LegendShare of the key.</summary>
    public float BaseSize { get; set; }

    /// <summary>The most of a key's width and height its legends take as they grow, the rest its margins: three quarters.</summary>
    private const float LegendShare = 0.75f;

    /// <summary>The room inside a key of this size that its legends stand in.</summary>
    public RectF Room(int width, int height) => new(inset, inset, width - inset, height - inset);

    public void Show(IReadOnlyList<Legend> shown)
    {
        legends = shown;
        InvalidateSelf();
    }

    public override void Draw(Canvas canvas)
    {
        var room = Room(Bounds.Width(), Bounds.Height());
        room.Offset(Bounds.Left, Bounds.Top);

        // The legends at the one size first, then the light symbols grown in
        // the room they leave: the key's own (its plain legend) and a
        // punctuation key's two. A shifted or Alt Gr legend beside a plain
        // one is SecondaryShare of it on every key alike, so the legend the
        // key is for stays the largest, and ? and ¿, $ and @, are one size.
        var shown = legends.Where(legend => legend.Text.Length > 0).ToList();
        if (shown.Count == 0)
        {
            return;
        }

        if (QuartersTrial(shown, room) is { } trial)
        {
            foreach (var (legend, size, x, baseline) in trial)
            {
                paint.TextSize = size;
                paint.Color = legend.Sent ? strong : weak;
                canvas.DrawText(legend.Text, x, baseline, paint);
            }

            return;
        }

        // One rule for every key. Its base is the letters' size (LetterSize),
        // as large as A+ asks until a letter with a tail (g) would leave
        // LegendShare of the key: so a key whose plain legend is a symbol
        // (+ < `) is no larger than a letter's. Its plain legend is the base;
        // its shifted and Alt Gr ones SecondaryShare of it, smaller where they
        // would meet it. A light symbol, plain or secondary, is drawn
        // LightGrowth larger where it fits. A punctuation key's two (; over ,)
        // take two rows each, both at one size.
        var share = Shared(room);
        var letter = LetterSize(room);
        var spanning = shown.Where(legend => Spans(paint, legend, shown)).ToList();

        var boxes = new List<RectF>();
        var sized = new List<(Legend Legend, float Size, bool Spans)>();

        // The plain legend's size as drawn, which its secondaries keep their share of.
        var plainSize = letter;
        var plainGrows = 1f;
        var plainInk = Ink("x", letter);
        foreach (var legend in shown.OrderBy(legend => IsSecondary(paint, legend, shown)))
        {
            var spans = spanning.Count == 2 && spanning.Contains(legend);
            var grows = IsLight(paint, legend.Text) ? LightGrowth : 1f;
            var size = spans ? PairGrown(legend, room, boxes, letter)
                // A light secondary (" · = ~) at the letters' size half as large
                // again, as a light plain legend, its ink kept under the plain
                // one's; any other at SecondaryShare.
                : IsSecondary(paint, legend, shown) ? Yielded(legend, room, boxes,
                    Smaller(legend.Text, plainSize / plainGrows * (grows > 1f ? LightGrowth : SecondaryShare), plainInk))
                : grows > 1f ? Grown(legend, spans: false, room, share, boxes, letter, letter * grows)
                : letter;
            if (legend.Cell == Plain && !spans)
            {
                (plainSize, plainGrows) = (size, grows);

                // What a secondary is measured against: the plain legend's ink,
                // or a letter's where the plain one is a speck (' `).
                var (ink, x) = (Ink(legend.Text, size), Ink("x", letter));
                plainInk = ink.Width() * ink.Height() >= x.Width() * x.Height() ? ink : x;
            }
            boxes.Add(Place(paint, legend, size, room, spans, shrinkToFit: true).Box);
            sized.Add((legend, size, spans));
        }

        // A punctuation key's two weigh alike: the one whose ink covers more
        // (a comma, a point grown beside a tall ; or :) is brought down to the
        // other's surface.
        if (sized.Count(item => item.Spans) == 2)
        {
            var areas = sized.Select(item => item.Spans ? Ink(item.Legend.Text, item.Size) : new RectF())
                .Select(ink => ink.Width() * ink.Height()).ToList();
            var least = areas.Where((area, index) => sized[index].Spans && area > 0).DefaultIfEmpty(0).Min();
            for (var index = 0; index < sized.Count; index++)
            {
                if (sized[index].Spans && areas[index] > least && least > 0)
                {
                    sized[index] = sized[index] with { Size = sized[index].Size * MathF.Sqrt(least / areas[index]) };
                }
            }
        }

        foreach (var (legend, size, spans) in sized)
        {
            var (x, baseline, _) = Place(paint, legend, size, room, spans, shrinkToFit: true);
            paint.Color = legend.Sent ? strong : weak;
            canvas.DrawText(legend.Text, x, baseline, paint);
        }
    }

    /// <summary>
    /// The owner's design for the ; , and : . and _ - keys (doc/diseño.md, drawing 2): the
    /// shifted legend centred in the top left quarter, the plain one centred
    /// in the bottom half, both at one size, the one A− and A+ ask for
    /// (<see cref="TextSize"/>). Grown till they would meet, they move apart
    /// across, the shifted one to the left and the plain one to the right; they
    /// stop growing only where even so they would meet or leave the key. None
    /// for any other key.
    /// </summary>
    private IReadOnlyList<(Legend Legend, float Size, float X, float Baseline)>? QuartersTrial(IReadOnlyList<Legend> shown, RectF room)
    {
        if (shown.Count != 2 || shown.FirstOrDefault(legend => legend.Cell == Plain) is not { Text: "," or "." or "-" } plain
            || shown.FirstOrDefault(legend => legend.Cell == Shifted) is not { Text: not null } shifted)
        {
            return null;
        }

        var inner = new RectF(room.Left + gap, room.Top + gap, room.Right - gap, room.Bottom - gap);
        (float ShiftedX, float ShiftedY, float PlainX, float PlainY)? At(float size)
        {
            var (top, bottom) = (Ink(shifted.Text, size), Ink(plain.Text, size));
            if (top.Width() > inner.Width() || top.Height() > inner.Height() || bottom.Width() > inner.Width() || bottom.Height() > inner.Height())
            {
                return null;
            }

            // Centred in its part, kept inside the key.
            float Within(float wanted, float low, float high, float lowest, float highest) =>
                Math.Clamp(wanted, lowest - low, highest - high);
            var (middleX, middleY) = (room.CenterX(), room.CenterY());
            var topY = Within(room.Top + (room.Height() / 4) - top.CenterY(), top.Top, top.Bottom, inner.Top, inner.Bottom);
            var bottomY = Within(middleY + (room.Height() / 4) - bottom.CenterY(), bottom.Top, bottom.Bottom, inner.Top, inner.Bottom);
            var topX = Within(room.Left + (room.Width() / 4) - top.CenterX(), top.Left, top.Right, inner.Left, inner.Right);
            var bottomX = Within(middleX - bottom.CenterX(), bottom.Left, bottom.Right, inner.Left, inner.Right);

            // Within a gap of each other: they would meet.
            var (upper, lower) = (Moved(top, topX, topY), Moved(bottom, bottomX, bottomY));
            if (upper.Intersects(lower.Left - gap, lower.Top - gap, lower.Right + gap, lower.Bottom + gap))
            {
                // Apart across: the shifted one to the left edge, the plain one
                // right of it, as far as the right edge.
                topX = inner.Left - top.Left;
                bottomX = Math.Max(bottomX, topX + top.Right + gap - bottom.Left);
                if (bottomX + bottom.Right > inner.Right)
                {
                    return null;
                }
            }

            return (topX, topY, bottomX, bottomY);
        }

        var (fits, misses) = (1f, TextSize);
        if (At(misses) is null)
        {
            while (misses - fits > 0.5f)
            {
                var tried = (fits + misses) / 2;
                if (At(tried) is not null)
                {
                    fits = tried;
                }
                else
                {
                    misses = tried;
                }
            }
        }
        else
        {
            fits = misses;
        }

        var (shiftedX, shiftedY, plainX, plainY) = At(fits) ?? (inner.Left, inner.Top, inner.Left, inner.Bottom);
        return [(shifted, fits, shiftedX, shiftedY), (plain, fits, plainX, plainY)];
    }

    /// <summary>An ink box moved to where it is drawn.</summary>
    private static RectF Moved(RectF ink, float x, float y) => new(ink.Left + x, ink.Top + y, ink.Right + x, ink.Bottom + y);

    /// <summary>
    /// The largest size this key's legends stand whole in it and apart at,
    /// measured once for each room and set of legends.
    /// </summary>
    /// <summary>The size a letter is drawn at in a key of this size: every key's base, and what the words of the other keys keep to.</summary>
    public float LetterSize(int width, int height) => LetterSize(Room(width, height));

    private float LetterSize(RectF room) =>
        Math.Min(TextSize, Math.Max(BaseSize, Most([new Legend("g", Plain, Sent: true)], Shared(room))));

    /// <summary>
    /// A punctuation key's legend's size (; over , and the like): each on its
    /// own grows with A+ (TextSize), half as large again, until it would leave
    /// its two rows of the table (the shifted one the top two, the plain one
    /// the bottom two) or meet the other.
    /// </summary>
    private float PairGrown(Legend legend, RectF room, IReadOnlyList<RectF> others, float letter)
    {
        var rows = legend.Cell == Shifted
            ? new RectF(room.Left, room.Top, room.Right, room.Top + (room.Height() * 2 / 3))
            : new RectF(room.Left, room.Top + (room.Height() / 3), room.Right, room.Bottom);
        bool Fits(float tried)
        {
            var box = Place(paint, legend, tried, room, spans: true, shrinkToFit: false).Box;
            return Clear(box, rows, others, gap);
        }

        var (fits, misses) = (letter / 4, Math.Max(letter, TextSize) * LightGrowth);
        if (Fits(misses))
        {
            return misses;
        }

        while (misses - fits > 0.5f)
        {
            var tried = (fits + misses) / 2;
            if (Fits(tried))
            {
                fits = tried;
            }
            else
            {
                misses = tried;
            }
        }

        return fits;
    }

    /// <summary>The middle LegendShare of a key's room, its legends' room as they grow.</summary>
    private static RectF Shared(RectF room)
    {
        var (across, down) = (room.Width() * (1 - LegendShare) / 2, room.Height() * (1 - LegendShare) / 2);
        return new RectF(room.Left + across, room.Top + down, room.Right - across, room.Bottom - down);
    }

    private float Most(IReadOnlyList<Legend> shown, RectF room)
    {
        var asked = $"{room.Width()}x{room.Height()}:{string.Join("|", shown.Select(legend => $"{legend.Text}@{legend.Cell}"))}";
        if (!mosts.TryGetValue(asked, out var most))
        {
            var origin = new RectF(0, 0, room.Width(), room.Height());
            // Up to well past the key's height: what must stay in the key is the
            // ink, and a point or a comma draws a speck of ink at the key's height.
            most = Fitting([(shown, origin)], gap, room.Height() * InkReach);
            mosts[asked] = most;
        }

        return most;
    }

    /// <summary>Each set of legends' most in a room, measured once.</summary>
    private readonly Dictionary<string, float> mosts = [];

    /// <summary>The box of a text's ink at a size.</summary>
    private RectF Ink(string text, float size)
    {
        paint.TextSize = size;
        var ink = new Rect();
        paint.GetTextBounds(text, 0, text.Length, ink);
        return new RectF(ink);
    }

    /// <summary>
    /// A secondary legend's size made smaller where its ink would weigh as
    /// much as the plain legend's (<paramref name="plain"/>): never taller
    /// than it, and at most SecondaryInk of its surface, so ^ beside `, @
    /// beside 2, stay the smaller.
    /// </summary>
    private float Smaller(string text, float size, RectF plain)
    {
        var ink = Ink(text, size);
        if (ink.Width() <= 0 || ink.Height() <= 0)
        {
            return size;
        }

        var byHeight = plain.Height() / ink.Height();
        var bySurface = MathF.Sqrt(SecondaryInk * plain.Width() * plain.Height() / (ink.Width() * ink.Height()));
        return size * Math.Min(1f, Math.Min(byHeight, bySurface));
    }

    /// <summary>
    /// A shifted or Alt Gr legend's size: <paramref name="size"/> where it
    /// stands whole in the key and apart from the legends already placed, or
    /// as much smaller as it takes, down to a quarter of it.
    /// </summary>
    private float Yielded(Legend legend, RectF room, IReadOnlyList<RectF> others, float size)
    {
        bool Fits(float tried) => Clear(Place(paint, legend, tried, room, spans: false, shrinkToFit: false).Box, room, others, gap);

        var least = size / 4;
        if (Fits(size))
        {
            return size;
        }

        if (!Fits(least))
        {
            return least;
        }

        var (fits, misses) = (least, size);
        while (misses - fits > 0.5f)
        {
            var tried = (fits + misses) / 2;
            if (Fits(tried))
            {
                fits = tried;
            }
            else
            {
                misses = tried;
            }
        }

        return fits;
    }

    /// <summary>
    /// A light symbol's size: from <paramref name="size"/> as large as its
    /// key lets it, up to <paramref name="most"/>, whole in the key and apart
    /// from the legends already placed.
    /// </summary>
    private float Grown(Legend legend, bool spans, RectF room, RectF share, IReadOnlyList<RectF> others, float size, float most)
    {
        // Placed where it is drawn, in the key's room, apart from the others,
        // and no larger than the key's share for its legends.
        bool Fits(float tried)
        {
            var box = Place(paint, legend, tried, room, spans, shrinkToFit: false).Box;
            return Clear(box, room, others, gap) && box.Width() <= share.Width() && box.Height() <= share.Height();
        }

        if (most <= size || !Fits(size))
        {
            return size;
        }

        if (Fits(most))
        {
            return most;
        }

        var (fits, misses) = (size, most);
        while (misses - fits > 0.5f)
        {
            var tried = (fits + misses) / 2;
            if (Fits(tried))
            {
                fits = tried;
            }
            else
            {
                misses = tried;
            }
        }

        return fits;
    }

    /// <summary>
    /// The largest one size, up to <paramref name="most"/>, at which the
    /// legends of every one of these keys stand whole in its room and at
    /// least <paramref name="gap"/> apart, each placed as it is drawn.
    /// </summary>
    public static float Fitting(IReadOnlyList<(IReadOnlyList<Legend> Legends, RectF Room)> keys, float gap, float most)
    {
        using var paint = new Paint(PaintFlags.AntiAlias);
        var (fits, misses) = (1f, most);
        if (Fits(paint, keys, most, gap))
        {
            return most;
        }

        // Halved until a pixel apart: the fit is measured on the glyphs as
        // drawn, not on a formula.
        while (misses - fits > 0.5f)
        {
            var size = (fits + misses) / 2;
            if (Fits(paint, keys, size, gap))
            {
                fits = size;
            }
            else
            {
                misses = size;
            }
        }

        return fits;
    }

    private static bool Fits(Paint paint, IReadOnlyList<(IReadOnlyList<Legend> Legends, RectF Room)> keys, float size, float gap)
    {
        foreach (var (all, room) in keys)
        {
            var shown = all.Where(legend => legend.Text.Length > 0).ToList();
            var boxes = new List<RectF>();
            foreach (var legend in shown)
            {
                var legendSize = IsSecondary(paint, legend, shown) ? size * SecondaryShare : size;
                var box = Place(paint, legend, legendSize, room, Spans(paint, legend, shown), shrinkToFit: false).Box;
                if (!Clear(box, room, boxes, gap))
                {
                    return false;
                }

                boxes.Add(box);
            }
        }

        return true;
    }

    /// <summary>Whether a box stands whole in the room and at least a gap apart from the others.</summary>
    private static bool Clear(RectF box, RectF room, IEnumerable<RectF> others, float gap) =>
        box.Left >= room.Left && box.Top >= room.Top && box.Right <= room.Right && box.Bottom <= room.Bottom
        && !others.Any(other => other.Intersects(box.Left - gap, box.Top - gap, box.Right + gap, box.Bottom + gap));

    /// <summary>
    /// Whether a symbol is light: neither a letter nor a digit, and drawing
    /// less ink than a small letter does (its ink's width by its height,
    /// against an x's). A point, a comma or an accent is; a ? or a @ is not.
    /// </summary>
    private static bool IsLight(Paint paint, string text)
    {
        if (text.Any(char.IsLetterOrDigit))
        {
            return false;
        }

        if (Lights.TryGetValue(text, out var known))
        {
            return known;
        }

        var size = paint.TextSize;
        paint.TextSize = 100f;
        var (ink, letter, capital) = (new Rect(), new Rect(), new Rect());
        paint.GetTextBounds(text, 0, text.Length, ink);
        paint.GetTextBounds("x", 0, 1, letter);
        paint.GetTextBounds("H", 0, 1, capital);
        paint.TextSize = size;

        // Less ink than an x, and lower than a capital: a bracket, a slash or
        // a ! draw little ink but stand as tall as a letter, and are read.
        var light = (float)ink.Width() * ink.Height() < (float)letter.Width() * letter.Height() && ink.Height() < capital.Height();
        Lights[text] = light;
        return light;
    }

    /// <summary>A shifted or Alt Gr legend beside a plain one, drawn smaller (<see cref="SecondaryShare"/>); a punctuation key's two are not.</summary>
    private static bool IsSecondary(Paint paint, Legend legend, IReadOnlyList<Legend> shown) =>
        legend.Cell != Plain && !Spans(paint, legend, shown) && shown.Any(other => other.Cell == Plain);

    /// <summary>
    /// A legend of a punctuation key, one with its shifted and plain legends
    /// alone and both light symbols (; over , or : over .): it takes two rows
    /// of the table. A digit's key (= over 0) keeps its symbol in the corner.
    /// </summary>
    private static bool Spans(Paint paint, Legend legend, IReadOnlyList<Legend> shown) =>
        legend.Cell != AltGr && shown.Count == 2
        && shown.Any(other => other.Cell == Shifted) && shown.Any(other => other.Cell == Plain && PunctuationPairs.Contains(other.Text));

    /// <summary>
    /// Where a legend is drawn in a room at a size, and the box of what it
    /// draws: its glyphs' own top left at the room's top left (0:0), their
    /// bottom right at its bottom right (2:2), or in its middle (1:1) — a
    /// symbol by what it draws, any other on the line of a capital, so
    /// letters stand on one line from key to key. Taking two rows, the
    /// shifted one is centred between rows 0 and 1, the plain one between
    /// rows 1 and 2.
    /// </summary>
    private static (float X, float Baseline, RectF Box) Place(Paint paint, Legend legend, float size, RectF room, bool spans, bool shrinkToFit)
    {
        var text = legend.Text;
        paint.TextSize = size;
        var glyph = new Rect();
        paint.GetTextBounds(text, 0, text.Length, glyph);
        if (shrinkToFit && glyph.Width() > room.Width())
        {
            paint.TextSize *= room.Width() / glyph.Width();
            paint.GetTextBounds(text, 0, text.Length, glyph);
        }

        var capital = new Rect();
        paint.GetTextBounds("H", 0, 1, capital);
        var symbol = !text.Any(char.IsLetterOrDigit);
        var x = legend.Cell switch
        {
            Shifted => room.Left - glyph.Left,
            AltGr => room.Right - glyph.Right,
            _ => room.CenterX() - glyph.ExactCenterX(),
        };
        var baseline = legend.Cell switch
        {
            Shifted when spans => room.Top + (room.Height() / 3) - glyph.ExactCenterY(),
            Shifted => room.Top - glyph.Top,
            AltGr => room.Bottom - glyph.Bottom,
            _ when spans => room.Top + (room.Height() * 2 / 3) - glyph.ExactCenterY(),
            _ when symbol => room.CenterY() - glyph.ExactCenterY(),
            _ => room.CenterY() - capital.ExactCenterY(),
        };
        return (x, baseline, new RectF(x + glyph.Left, baseline + glyph.Top, x + glyph.Right, baseline + glyph.Bottom));
    }

    public override void SetAlpha(int alpha) => paint.Alpha = alpha;

    public override void SetColorFilter(ColorFilter? colorFilter) => paint.SetColorFilter(colorFilter);

    // Drawn over a key, letting it show through.
    public override int Opacity => (int)Format.Translucent;
}
