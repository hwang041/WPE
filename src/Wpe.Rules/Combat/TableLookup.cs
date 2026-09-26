namespace Wpe.Rules.Combat;

/// <summary>Shared interval lookup used by the table-based combat variants.</summary>
internal static class TableLookup
{
    /// <summary>Result code for (row, column); "" when no interval matches.</summary>
    public static string Find(Wpe.Core.Definition.TableDef table, double row, double col)
    {
        foreach (var r in table.Rows)
        {
            if (row < r.Min || row > r.Max) continue;
            foreach (var c in r.Columns)
                if (col >= c.Min && col <= c.Max) return c.Result;
        }
        return "";
    }
}
