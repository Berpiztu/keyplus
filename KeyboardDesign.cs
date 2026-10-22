using System.Globalization;
using Android.Content;

namespace Berpiztu.KeyPlus;

/// <summary>
/// A design of the whole keyboard (ABC): its keys in rows, where the user
/// dragged them, and those taken out, kept aside to be put back; kept for
/// each screen (docs/tODO/designs.md). A key is its place in
/// <see cref="Catalog"/>; a hole, a negative number, its width in
/// hundredths. Nothing moves by itself: a key taken from a row leaves a hole
/// as wide as it, a key put on a hole takes it, and keys keep their size, a
/// row longer than <see cref="Width"/> running past the keyboard's edge;
/// <see cref="Spread"/> closes the holes and sizes the keys again when asked
/// for. Entrar stands as tall as two rows where both rows have it.
/// </summary>
public sealed class KeyboardDesign
{
    private const string Preference = "abcDesign";

    /// <summary>Written with designs imported, so the keyboard open is made again with them at once.</summary>
    public const string ImportedPreference = "abcDesignImported";

    /// <summary>
    /// A row's whole weight as the keyboard is made: the rows under the
    /// digits' weigh 15.5 and the side column's keys 1 (PanelKeys.Spanish).
    /// </summary>
    private const float UsualWidth = 16.5f;

    /// <summary>Every key of the whole keyboard, the side column's too: a key's place here is its id in a design.</summary>
    public static readonly IReadOnlyList<PanelKey> Catalog =
    [
        .. PanelKeys.Spanish.SelectMany(block => block.Tall is { } tall ? block.Rows.SelectMany(row => row).Append(tall) : block.Rows.SelectMany(row => row)),
        .. PanelKeys.SpanishSide,
    ];

    /// <summary>Entrar, as tall as two rows where both have it.</summary>
    public static readonly int TallId = PanelKeys.Spanish.Where(block => block.Tall is not null).Select(block => Id(block.Tall!)).First();

    private KeyboardDesign(List<List<int>> rows, List<int> removed, float width, Dictionary<int, float> widths)
    {
        Rows = rows;
        Removed = removed;
        Width = width;
        Widths = widths;
    }

    /// <summary>The keys made wider or narrower than they are made (− and +), by id.</summary>
    public Dictionary<int, float> Widths { get; }

    /// <summary>How much − and + change a key's width: a quarter of a key.</summary>
    private const float WidthStep = 0.25f;

    /// <summary>The narrowest − makes a key: half a key.</summary>
    private const float NarrowestKey = 0.5f;

    /// <summary>Each row from the left: keys by their ids, holes as negative widths in hundredths.</summary>
    public List<List<int>> Rows { get; }

    /// <summary>The keys taken out, in the order they were, to be put back.</summary>
    public List<int> Removed { get; }

    /// <summary>The weight a row as wide as the keyboard takes: a row of less leaves the rest empty, a row of more runs past the edge.</summary>
    public float Width { get; private set; }

    public static PanelKey Key(int id) => Catalog[id];

    public static bool IsHole(int cell) => cell < 0;

    /// <summary>A key's or a hole's weight in its row: a key as − and + left it, else as it is made.</summary>
    public float WeightOf(int cell) =>
        IsHole(cell) ? -cell / 100f : Widths.TryGetValue(cell, out var width) ? width : Catalog[cell].Weight;

    /// <summary>The weight of some of a row's cells, side by side.</summary>
    public float WeightOf(IEnumerable<int> cells) => cells.Sum(WeightOf);

    /// <summary>
    /// − or +: a key a quarter narrower or wider, Entrar standing tall in
    /// both its rows. Nothing else moves: narrower, what it gives up stays a
    /// hole right after it; wider, it takes the hole right after it first,
    /// and only past that moves the keys at its right on.
    /// </summary>
    public void Resize(int id, bool wider)
    {
        var old = WeightOf(id);
        var now = wider ? old + WidthStep : Math.Max(NarrowestKey, old - WidthStep);
        var change = now - old;
        if (Math.Abs(change) < 0.01f)
        {
            return;
        }

        foreach (var row in Rows)
        {
            var at = row.IndexOf(id);
            if (at < 0)
            {
                continue;
            }

            var next = at + 1 < row.Count && IsHole(row[at + 1]) ? WeightOf(row[at + 1]) : 0f;
            var hole = next - change;
            if (next > 0)
            {
                row.RemoveAt(at + 1);
            }

            if (hole > 0.01f)
            {
                row.Insert(at + 1, Hole(hole));
            }
        }

        if (Math.Abs(now - Catalog[id].Weight) < 0.01f)
        {
            Widths.Remove(id);
        }
        else
        {
            Widths[id] = now;
        }
    }

    private static int Hole(float weight) => -(int)Math.Round(weight * 100);

    /// <summary>
    /// The keyboard as it is made, as a design: its rows in order, Entrar in
    /// both rows beside it, and each side column's key at the end of its row.
    /// </summary>
    public static KeyboardDesign Usual()
    {
        var rows = new List<List<int>>();
        foreach (var block in PanelKeys.Spanish)
        {
            foreach (var row in block.Rows)
            {
                var ids = row.Select(Id).ToList();
                if (block.Tall is { } tall)
                {
                    ids.Add(Id(tall));
                }

                rows.Add(ids);
            }
        }

        for (var i = 0; i < PanelKeys.SpanishSide.Count && i < rows.Count; i++)
        {
            rows[i].Add(Id(PanelKeys.SpanishSide[i]));
        }

        return new KeyboardDesign(rows, [], UsualWidth, []);
    }

    /// <summary>The screen's design, or none where its keyboard is as made (or what was kept no longer reads).</summary>
    public static KeyboardDesign? Read(ISharedPreferences preferences) => Parse(preferences.GetString(Preference, null));

    /// <summary>The screen's design as it is kept, to export it; none where its keyboard is as made.</summary>
    public static string? Kept(ISharedPreferences preferences) => preferences.GetString(Preference, null);

    /// <summary>Whether a text is a design this keyboard reads, before it is imported.</summary>
    public static bool Reads(string text) => Parse(text) is not null;

    /// <summary>A design imported for the screen, the keyboard open told to take it.</summary>
    public static void Import(ISharedPreferences preferences, string text) =>
        preferences.Edit()!.PutString(Preference, text)!.PutLong(ImportedPreference, DateTime.UtcNow.Ticks)!.Apply();

    /// <summary>A design as it is kept, or none where it does not read.</summary>
    private static KeyboardDesign? Parse(string? kept)
    {
        // "width|row;row;…|removed|id:width,…": each row's cells and the removed keys,
        // comma-separated, a row maybe empty; then the keys − and + resized, which a
        // design kept before them has not.
        if (string.IsNullOrEmpty(kept) || kept.Split('|') is not [var width, var rows, var removed, .. var more]
            || !float.TryParse(width, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight)
            || ReadWidths(more.Length > 0 ? more[0] : "") is not { } widths)
        {
            return null;
        }

        var read = new List<List<int>>();
        foreach (var row in rows.Split(';'))
        {
            if (Cells(row) is not { } cells)
            {
                return null;
            }

            read.Add(cells);
        }

        return read.Count > 0 && Cells(removed) is { } aside && !aside.Any(IsHole) ? new KeyboardDesign(read, aside, weight, widths) : null;
    }

    /// <summary>The keys resized, "id:width" comma-separated, or none where one does not read.</summary>
    private static Dictionary<int, float>? ReadWidths(string text)
    {
        var widths = new Dictionary<int, float>();
        foreach (var pair in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (pair.Split(':') is not [var id, var width]
                || !int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var key) || key < 0 || key >= Catalog.Count
                || !float.TryParse(width, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) || weight <= 0)
            {
                return null;
            }

            widths[key] = weight;
        }

        return widths;
    }

    /// <summary>Comma-separated cells, or none where one is neither a key's id nor a hole.</summary>
    private static List<int>? Cells(string text)
    {
        var cells = new List<int>();
        foreach (var cell in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out var read) || read >= Catalog.Count)
            {
                return null;
            }

            cells.Add(read);
        }

        return cells;
    }

    public void Save(ISharedPreferences preferences) =>
        preferences.Edit()!.PutString(Preference,
            $"{Width.ToString(CultureInfo.InvariantCulture)}|{string.Join(';', Rows.Select(row => string.Join(',', row)))}|{string.Join(',', Removed)}|"
            + string.Join(',', Widths.Select(pair => $"{pair.Key}:{pair.Value.ToString(CultureInfo.InvariantCulture)}")))!.Apply();

    /// <summary>The screen's keyboard as made again.</summary>
    public static void Forget(ISharedPreferences preferences) => preferences.Edit()!.Remove(Preference)!.Apply();

    /// <summary>A key taken from where it was, a row (both, for Entrar standing tall) or the keys aside: a hole left as wide as it, so nothing else moves.</summary>
    private void Take(int id)
    {
        foreach (var row in Rows)
        {
            for (var i = 0; i < row.Count; i++)
            {
                if (row[i] == id)
                {
                    row[i] = Hole(WeightOf(id));
                }
            }
        }

        Removed.Remove(id);
    }

    /// <summary>A key dragged off the keys: its hole left, the key kept aside, last.</summary>
    public void PutAside(int id)
    {
        Take(id);
        Removed.Add(id);
    }

    /// <summary>
    /// A key let go in a row: on a hole, it takes it, what the hole had more
    /// than the key left as a hole after it; else put in at that place, the
    /// keys right of it moved on. Where it came from, a hole stays.
    /// </summary>
    public void Put(int id, int row, int index, bool onHole)
    {
        Take(id);
        var cells = Rows[Math.Clamp(row, 0, Rows.Count - 1)];
        index = Math.Clamp(index, 0, cells.Count);
        if (!onHole || index >= cells.Count || !IsHole(cells[index]))
        {
            cells.Insert(index, id);
            return;
        }

        var spare = WeightOf(cells[index]) - WeightOf(id);
        cells[index] = id;
        if (spare > 0.01f)
        {
            cells.Insert(index + 1, Hole(spare));
        }
    }

    /// <summary>A new empty row at the top, to drag keys into.</summary>
    public void AddRow() => Rows.Insert(0, []);

    /// <summary>
    /// Repartir: the holes and empty rows gone, and every key the same size
    /// again, as large as lets the longest row fill the keyboard's width.
    /// </summary>
    public void Spread()
    {
        foreach (var row in Rows)
        {
            row.RemoveAll(IsHole);
        }

        Rows.RemoveAll(row => row.Count == 0);
        var widest = 0f;
        for (var r = 0; r < Rows.Count; r++)
        {
            if (TallWith(r) is { } pair)
            {
                widest = Math.Max(widest, pair.Weight);
                r++;
            }
            else
            {
                widest = Math.Max(widest, WeightOf(Rows[r]));
            }
        }

        if (widest > 0)
        {
            Width = widest;
        }
    }

    /// <summary>
    /// Rows <paramref name="row"/> and the next, where both have Entrar, which
    /// stands as tall as both: where it is in each, and the weights of the
    /// keys before and after it and of the two rows side by side with it.
    /// </summary>
    public (List<int> Upper, List<int> Lower, int UpperAt, int LowerAt, float Before, float After, float Weight)? TallWith(int row)
    {
        if (row + 1 >= Rows.Count || !Rows[row].Contains(TallId) || !Rows[row + 1].Contains(TallId))
        {
            return null;
        }

        var (upper, lower) = (Rows[row], Rows[row + 1]);
        var (upperAt, lowerAt) = (upper.IndexOf(TallId), lower.IndexOf(TallId));
        var before = Math.Max(WeightOf(upper[..upperAt]), WeightOf(lower[..lowerAt]));
        var after = Math.Max(WeightOf(upper[(upperAt + 1)..]), WeightOf(lower[(lowerAt + 1)..]));
        return (upper, lower, upperAt, lowerAt, before, after, before + WeightOf(TallId) + after);
    }

    private static int Id(PanelKey key)
    {
        for (var i = 0; i < Catalog.Count; i++)
        {
            if (ReferenceEquals(Catalog[i], key))
            {
                return i;
            }
        }

        throw new InvalidOperationException($"{key.Label} is not on the whole keyboard.");
    }
}
