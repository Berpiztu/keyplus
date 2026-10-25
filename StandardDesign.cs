using System.Text.Json;

namespace Berpiztu.KeyPlus;

/// <summary>A key of a designed standard keyboard: which (its place on the keyboard it was made from, or a key added) and how wide, in keys.</summary>
public sealed record DesignedKey(string Id, float Width);

/// <summary>
/// A standard keyboard designed (⊞ › Diseñar): its letters' page as rows of
/// keys, each the key at a place of the keyboard it was made from
/// ("b:row:column", so it follows the language: Spanish's ñ is English's
/// key there), one added from the list (<see cref="StandardKeys.Extras"/>,
/// "x:id"), a hole ("gap", + Columna) or a shaped key's lower part ("below",
/// the k-th of a row for the k-th shaped key of the row above), with its
/// width. Kept with the keyboard as JSON.
/// </summary>
public sealed class StandardDesign
{
    public List<List<DesignedKey>> Rows { get; init; } = [];

    /// <summary>A design of a keyboard's own page, every key where it is.</summary>
    public static StandardDesign From(IReadOnlyList<IReadOnlyList<StandardKey>> page) => new()
    {
        Rows = [.. page.Select((row, r) => row.Select((key, c) => new DesignedKey($"b:{r}:{c}", key.Width)).ToList())],
    };

    public string Text => JsonSerializer.Serialize(Rows);

    /// <summary>A design kept as text; none, or one unreadable, the keyboard as made.</summary>
    public static StandardDesign? FromText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<List<DesignedKey>>>(text) is { } rows ? new StandardDesign { Rows = rows } : null;
        }
        catch (JsonException unreadable)
        {
            Log.Write($"A standard keyboard's design could not be read: {unreadable.Message}");
            return null;
        }
    }

    public StandardDesign Copy() => new() { Rows = [.. Rows.Select(row => row.ToList())] };

    /// <summary>
    /// The page as designed, in a language: each key its own or the one
    /// added, as wide as designed; a place the language has no key at, an
    /// empty key; a lower part, the shaped key above it marked Lower, or a
    /// hole where the row above has no shaped key left for it.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<StandardKey>> Page(IReadOnlyList<IReadOnlyList<StandardKey>> made, StandardLanguage language)
    {
        List<IReadOnlyList<StandardKey>> page = [];
        IReadOnlyList<StandardKey> above = [];
        foreach (var row in Rows)
        {
            var shaped = above.Where(key => key.Below > 0).ToList();
            var lowers = 0;
            List<StandardKey> keys = [];
            foreach (var key in row)
            {
                keys.Add(key.Id != StandardKeys.BelowId ? Resolve(key.Id, made, language) with { Width = key.Width }
                    : lowers < shaped.Count ? shaped[lowers++] with { Width = key.Width, Below = 0, Lower = true }
                    : StandardKeys.Gap with { Width = key.Width });
            }

            page.Add(keys);
            above = keys;
        }

        return page;
    }

    /// <summary>Whether a key of a design goes on into the row below (a shaped Enter).</summary>
    public static bool Shaped(string id) =>
        id.StartsWith("x:", StringComparison.Ordinal) && StandardKeys.Extra(id[2..], StandardKeys.Spanish) is { Below: > 0 };

    /// <summary>A shaped key's lower part: the k-th "below" of the next row for the k-th shaped key of its row; none, for any other key or one without it.</summary>
    public (int Row, int Index)? LowerOf(int row, int index)
    {
        if (!Shaped(Rows[row][index].Id) || row + 1 >= Rows.Count)
        {
            return null;
        }

        var k = Rows[row].Take(index).Count(key => Shaped(key.Id));
        return Rows[row + 1].Select((key, i) => (key.Id, i)).Where(part => part.Id == StandardKeys.BelowId).Skip(k).Select(part => ((int Row, int Index)?)(row + 1, part.i)).FirstOrDefault();
    }

    /// <summary>A lower part's shaped key, in the row above; none, for any other key or one left without it.</summary>
    public (int Row, int Index)? UpperOf(int row, int index)
    {
        if (Rows[row][index].Id != StandardKeys.BelowId || row == 0)
        {
            return null;
        }

        var k = Rows[row].Take(index).Count(key => key.Id == StandardKeys.BelowId);
        return Rows[row - 1].Select((key, i) => (key.Id, i)).Where(top => Shaped(top.Id)).Skip(k).Select(top => ((int Row, int Index)?)(row - 1, top.i)).FirstOrDefault();
    }

    /// <summary>
    /// A key put in place, moved (from its place) or new: in a hole's place
    /// (fills) or before an index of a row, both counted with the key moved
    /// still where it was; a shaped key's lower part before an index of the
    /// row below, and none in the last row, which refuses it. The key moved
    /// and its lower part then leave their old places. Where the key now is;
    /// none where refused.
    /// </summary>
    public (int Row, int Index)? Put(DesignedKey key, int row, int index, bool fills, int lowerIndex, (int Row, int Index)? from)
    {
        var shaped = Shaped(key.Id);
        if (shaped && row + 1 >= Rows.Count)
        {
            return null;
        }

        var oldKey = from is { } at ? Rows[at.Row][at.Index] : null;
        var oldLower = from is { } top && LowerOf(top.Row, top.Index) is { } part ? Rows[part.Row][part.Index] : null;
        var placed = key with { };
        if (fills)
        {
            Rows[row][index] = placed;
        }
        else
        {
            Rows[row].Insert(Math.Min(index, Rows[row].Count), placed);
        }

        if (shaped)
        {
            var below = oldLower?.Width ?? StandardKeys.Extra(key.Id[2..], StandardKeys.Spanish)!.Below;
            Rows[row + 1].Insert(Math.Min(lowerIndex, Rows[row + 1].Count), new DesignedKey(StandardKeys.BelowId, below));
        }

        Take(oldKey);
        Take(oldLower);
        return Find(placed);
    }

    /// <summary>A key taken out, a shaped key's lower part with it, or the shaped key with its lower part.</summary>
    public void Remove(int row, int index)
    {
        var top = UpperOf(row, index) ?? (row, index);
        var lower = LowerOf(top.Row, top.Index) is { } part ? Rows[part.Row][part.Index] : null;
        Take(Rows[top.Row][top.Index]);
        Take(lower);
    }

    /// <summary>
    /// An Enter's next shape (<see cref="StandardKeys.EnterShapes"/>), as a
    /// marked Enter clicked again asks. The Enter keeps its own row, place and
    /// width, so that row never changes: in one row as it is; shaped, its
    /// lower part there as wide, and its top going up into the row above,
    /// before an index of it (counted with an old top still there), as much
    /// wider or narrower than the lower part as the shape's top is (ISO's a
    /// quarter wider, the old ANSI L's three quarters narrower, the square
    /// one as wide); the first row, with none above, keeps it in one row.
    /// Where the Enter's own part is; none, for a key that is no Enter.
    /// </summary>
    public (int Row, int Index)? Reshape(int row, int index, IReadOnlyList<IReadOnlyList<StandardKey>> made, StandardLanguage language, int upperIndex)
    {
        if (Page(made, language)[row][index].Action != StandardAction.Enter)
        {
            return null;
        }

        var own = LowerOf(row, index) ?? (row, index);
        var top = UpperOf(own.Row, own.Index);
        var shapes = StandardKeys.EnterShapes;
        var now = top is { } shaped ? Array.IndexOf(shapes, Rows[shaped.Row][shaped.Index].Id[2..]) : 0;
        var next = own.Row == 0 ? shapes[0] : shapes[(now + 1) % shapes.Length];
        var oldTop = top is { } old ? Rows[old.Row][old.Index] : null;
        var width = Rows[own.Row][own.Index].Width;
        if (next == shapes[0])
        {
            Rows[own.Row][own.Index] = new DesignedKey("x:" + next, width);
        }
        else
        {
            var shape = StandardKeys.Extra(next, language)!;
            Rows[own.Row][own.Index] = new DesignedKey(StandardKeys.BelowId, width);
            var topWidth = Math.Max(MinShapedTop, width + shape.Width - shape.Below);
            Rows[own.Row - 1].Insert(Math.Min(upperIndex, Rows[own.Row - 1].Count), new DesignedKey("x:" + next, topWidth));
        }

        Take(oldTop);
        return own;
    }

    /// <summary>A key a step narrower or wider (−, +), a shaped key's two parts alike as the one key they are, its shape kept; each part within the least and most widths.</summary>
    public void Resize(int row, int index, float step, float least, float most)
    {
        var top = UpperOf(row, index) ?? (row, index);
        List<(int Row, int Index)> parts = [top];
        if (LowerOf(top.Row, top.Index) is { } lower)
        {
            parts.Add(lower);
        }

        foreach (var (r, i) in parts)
        {
            Rows[r][i] = Rows[r][i] with { Width = Math.Clamp(Rows[r][i].Width + step, least, most) };
        }
    }

    /// <summary>The narrowest a shaped Enter's top is made, half a key, the designer's narrowest key.</summary>
    private const float MinShapedTop = 0.5f;

    /// <summary>A key taken out of the rows by itself, not one equal to it.</summary>
    private void Take(DesignedKey? key)
    {
        if (key is null || Find(key) is not { } at)
        {
            return;
        }

        Rows[at.Row].RemoveAt(at.Index);
    }

    private (int Row, int Index)? Find(DesignedKey key)
    {
        for (var r = 0; r < Rows.Count; r++)
        {
            if (Rows[r].FindIndex(other => ReferenceEquals(other, key)) is var i and >= 0)
            {
                return (r, i);
            }
        }

        return null;
    }

    private static StandardKey Resolve(string id, IReadOnlyList<IReadOnlyList<StandardKey>> made, StandardLanguage language)
    {
        if (id == StandardKeys.GapId)
        {
            return StandardKeys.Gap;
        }

        if (id.StartsWith("x:", StringComparison.Ordinal) && StandardKeys.Extra(id[2..], language) is { } added)
        {
            return added;
        }

        if (id.Split(':') is ["b", var r, var c] && int.TryParse(r, out var row) && int.TryParse(c, out var column)
            && row < made.Count && column < made[row].Count)
        {
            return made[row][column];
        }

        return StandardKeys.Blank;
    }
}
