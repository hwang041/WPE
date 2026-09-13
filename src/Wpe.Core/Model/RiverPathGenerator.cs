namespace Wpe.Core.Model;

/// <summary>
/// Quick river generation: describe a river as a path of adjacent hexes the water
/// flows through, and produce the connected edge segments for map.json `rivers`.
/// (The old framework authored river segments by hand; this automates it and
/// guarantees the "adjacent edges share a vertex" continuity.)
/// </summary>
public static class RiverPathGenerator
{
    /// <summary>
    /// Build edge segments for a river following `path` (consecutive hexes must be
    /// adjacent). `fordEdges` marks 0-based path edge indices as fords (free crossing).
    /// </summary>
    public static List<RiverSegmentDef> Build(IReadOnlyList<HexCoord> path, IEnumerable<int>? fordEdges = null)
    {
        var fords = new HashSet<int>(fordEdges ?? Array.Empty<int>());
        var segments = new List<RiverSegmentDef>();
        if (path.Count < 2)
            throw new ArgumentException("河流路径至少需要 2 个格");

        for (int i = 0; i + 1 < path.Count; i++)
        {
            var a = path[i];
            var b = path[i + 1];
            if (HexMath.Distance(a, b) != 1)
                throw new InvalidDataException(
                    $"河流路径第 {i}->{i + 1} 步 ({a.Q},{a.R})->({b.Q},{b.R}) 不相邻：河流必须沿相邻六角格之间的边延伸");
            segments.Add(new RiverSegmentDef
            {
                A = new[] { a.Q, a.R },
                B = new[] { b.Q, b.R },
                Ford = fords.Contains(i)
            });
        }

        // continuity contract: consecutive river edges must SHARE a vertex. Two sides of
        // the middle hex share a corner iff the two neighbouring hexes are themselves
        // adjacent (their directions from the middle hex are 60° apart). So every turn
        // must satisfy Distance(prev, next) == 1. Straight runs / U-turns leave the two
        // edges on opposite (or non-touching) sides with no common vertex.
        for (int i = 1; i + 1 < path.Count; i++)
        {
            var prev = path[i - 1];
            var cur = path[i];
            var next = path[i + 1];
            if (HexMath.Distance(prev, next) != 1)
                throw new InvalidDataException(
                    $"河流路径第 {i} 步 ({cur.Q},{cur.R}) 转向不合规：河段 ({(prev.Q)},{prev.R})-({cur.Q},{cur.R}) 与 " +
                    $"({cur.Q},{cur.R})-({next.Q},{next.R}) 不共享顶点。河流必须沿六角格边 120° 转向，相邻边才能共用同一顶点连成一线");
        }
        return segments;
    }

    /// <summary>Parse a path string like "5,3 6,4 7,5 ..." into hex coords.</summary>
    public static List<HexCoord> ParsePath(string spec)
    {
        var list = new List<HexCoord>();
        foreach (var tok in spec.Split(new[] { ' ', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = tok.Split(new[] { ',', ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) continue;
            if (int.TryParse(parts[0], out var q) && int.TryParse(parts[1], out var r))
                list.Add(new HexCoord(q, r));
        }
        return list;
    }
}
