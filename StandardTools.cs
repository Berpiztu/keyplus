using Android.Content;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Where the standard keyboard's buttons stand, as the user placed them
/// (⊞ › ✏, Gboard's "Mantén pulsado y arrastra para personalizar"): those on
/// the toolbar and those in the grid, each in its order, by their ids, kept
/// for every screen. A button the keyboard gained since stands where it
/// comes first; one it no longer has is left out.
/// </summary>
public static class StandardTools
{
    private const string BarKey = "toolbarTools";
    private const string GridKey = "gridTools";

    /// <summary>The toolbar's buttons and the grid's, as kept; at first as the keyboard has them.</summary>
    public static (List<string> Bar, List<string> Grid) Read(Context context, IReadOnlyList<string> barFirst, IReadOnlyList<string> gridFirst)
    {
        var kept = KeyPlusPreferences.Global(context);
        var known = barFirst.Concat(gridFirst).ToHashSet();
        List<string>? Ids(string key) =>
            kept.GetString(key, null) is { } ids ? [.. ids.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(known.Contains).Distinct()] : null;
        var bar = Ids(BarKey) ?? [.. barFirst];
        var grid = Ids(GridKey)?.Where(id => !bar.Contains(id)).ToList() ?? [.. gridFirst.Where(id => !bar.Contains(id))];
        bar.AddRange(barFirst.Where(id => !bar.Contains(id) && !grid.Contains(id)));
        grid.AddRange(gridFirst.Where(id => !bar.Contains(id) && !grid.Contains(id)));
        return (bar, grid);
    }

    public static void Save(Context context, IEnumerable<string> bar, IEnumerable<string> grid) =>
        KeyPlusPreferences.Global(context).Edit()!
            .PutString(BarKey, string.Join(',', bar))!
            .PutString(GridKey, string.Join(',', grid))!
            .Apply();
}
