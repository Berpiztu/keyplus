using System.Text.Json;

namespace Berpiztu.KeyPlus;

/// <summary>A key of a designed standard keyboard: which (its place on the keyboard it was made from, or a key added) and how wide, in keys.</summary>
public sealed record DesignedKey(string Id, float Width);

/// <summary>
/// A standard keyboard designed (⊞ › Diseñar): its letters' page as rows of
/// keys, each the key at a place of the keyboard it was made from
/// ("b:row:column", so it follows the language: Spanish's ñ is English's
/// key there), one added from the list (<see cref="StandardKeys.Extras"/>,
/// "x:id") or a hole ("gap", + Columna), with its width. Kept with the
/// keyboard as JSON.
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
            Android.Util.Log.Warn("KeyPlus", $"A standard keyboard's design could not be read: {unreadable.Message}");
            return null;
        }
    }

    public StandardDesign Copy() => new() { Rows = [.. Rows.Select(row => row.ToList())] };

    /// <summary>The page as designed, in a language: each key its own or the one added, as wide as designed; a place the language has no key at, an empty key.</summary>
    public IReadOnlyList<IReadOnlyList<StandardKey>> Page(IReadOnlyList<IReadOnlyList<StandardKey>> made, StandardLanguage language) =>
        [.. Rows.Select(row => (IReadOnlyList<StandardKey>)[.. row.Select(key => Resolve(key.Id, made, language) with { Width = key.Width })])];

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
