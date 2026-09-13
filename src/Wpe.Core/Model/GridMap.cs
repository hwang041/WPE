using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wpe.Core.Model;

/// <summary>
/// A data-driven battle map: a rectangular hex grid with per-hex terrain codes,
/// terrain movement costs, terrain combat bonuses, river edges and victory hexes.
/// Everything is authored as data — no image recognition, no game-specific code.
/// </summary>
public sealed class GridMap : IMap
{
    public int Columns { get; set; } = 18;
    public int Rows { get; set; } = 12;
    public float HexRadius { get; set; } = 90;
    public bool PointyTop { get; set; } = true;

    public float CellRadius => HexRadius;

    public IReadOnlyList<HexCoord> Neighbors(HexCoord c) => HexMath.Neighbors(c, PointyTop).ToList();

    public int Distance(HexCoord a, HexCoord b) => HexMath.Distance(a, b);

    public HexCoord CellAt(PointF p) => PixelToAxial(p);

    public bool TryResolveCell(string key, out HexCoord cell) { cell = default; return false; }

    /// <summary>Per-hex terrain codes, one string per row (each of length == Columns).</summary>
    public string[] Terrain { get; set; } = Array.Empty<string>();

    public Dictionary<string, float> TerrainCost { get; set; } = new();
    public Dictionary<string, float> TerrainDefenseBonus { get; set; } = new();
    public List<VictoryHexDef> VictoryHexes { get; set; } = new();

    /// <summary>Rivers run along hex edges (not through hexes).</summary>
    public List<RiverDef> Rivers { get; set; } = new();

    /// <summary>Extra MP to cross a river edge; fords/bridges cost 0.</summary>
    public float RiverCrossCost { get; set; } = 2f;

    private HashSet<(int, int, int, int)>? _riverEdges;
    private HashSet<(int, int, int, int)>? _fordEdges;

    private static (int, int, int, int) Norm(int qA, int rA, int qB, int rB)
        => qA < qB || (qA == qB && rA <= rB) ? (qA, rA, qB, rB) : (qB, rB, qA, rA);

    private void BuildRiverLookup()
    {
        _riverEdges = new HashSet<(int, int, int, int)>();
        _fordEdges = new HashSet<(int, int, int, int)>();
        foreach (var r in Rivers)
        {
            if (r.BetweenRows is { Length: 2 } bRows)
                foreach (var q in r.Columns)
                {
                    var e = Norm(q, bRows[0], q, bRows[1]);
                    _riverEdges.Add(e);
                    if (r.Fords.Contains(q)) _fordEdges.Add(e);
                }
            if (r.BetweenCols is { Length: 2 } bCols)
                foreach (var rr in r.Rows)
                {
                    var e = Norm(bCols[0], rr, bCols[1], rr);
                    _riverEdges.Add(e);
                    if (r.Fords.Contains(rr)) _fordEdges.Add(e);
                }
            foreach (var s in r.Segments)
            {
                if (s.A.Length < 2 || s.B.Length < 2) continue;
                var e = Norm(s.A[0], s.A[1], s.B[0], s.B[1]);
                _riverEdges.Add(e);
                if (s.Ford) _fordEdges.Add(e);
            }
        }
    }

    /// <summary>Extra MP to move from A to adjacent B across their shared edge (0 if no river/ford).</summary>
    public float RiverCost(HexCoord a, HexCoord b)
    {
        if (_riverEdges == null) BuildRiverLookup();
        var e = Norm(a.Q, a.R, b.Q, b.R);
        if (_fordEdges!.Contains(e)) return 0;
        if (_riverEdges!.Contains(e)) return RiverCrossCost;
        return 0;
    }

    public IEnumerable<(int qA, int rA, int qB, int rB, bool ford)> RiverEdgeList()
    {
        if (_riverEdges == null) BuildRiverLookup();
        foreach (var e in _riverEdges!)
            yield return (e.Item1, e.Item2, e.Item3, e.Item4, _fordEdges!.Contains(e));
    }

    public bool InBounds(HexCoord c) => c.Q >= 0 && c.Q < Columns && c.R >= 0 && c.R < Rows;

    public string TerrainAt(HexCoord c)
        => InBounds(c) ? Terrain[c.R][c.Q].ToString() : "";

    public float TerrainCostAt(HexCoord c)
        => TerrainCost.TryGetValue(TerrainAt(c), out var v) ? v : 1f;

    public float TerrainDefenseAt(HexCoord c)
        => TerrainDefenseBonus.TryGetValue(TerrainAt(c), out var v) ? v : 0f;

    public bool IsVictoryHex(HexCoord c) => VictoryHexes.Any(v => v.Q == c.Q && v.R == c.R);

    // ---- pixel <-> axial (rendering / interaction) ----

    /// <summary>Center of a hex in board units.</summary>
    public PointF CenterOf(HexCoord c)
    {
        if (PointyTop)
        {
            var x = HexRadius * HexMath.SQRT3 * (c.Q + c.R / 2f);
            var y = HexRadius * 3f / 2f * c.R;
            return new PointF(x, y);
        }
        var fx = HexRadius * 3f / 2f * c.Q;
        var fy = HexRadius * HexMath.SQRT3 * (c.R + c.Q / 2f);
        return new PointF(fx, fy);
    }

    /// <summary>The six corner points of a hex (same formula the grid renderer uses to draw it).</summary>
    public PointF[] CornersOf(HexCoord c)
    {
        var center = CenterOf(c);
        var off = HexMath.CornerOffsets(HexRadius, PointyTop);
        var pts = new PointF[6];
        for (int i = 0; i < 6; i++)
            pts[i] = new PointF(center.X + off[i].x, center.Y + off[i].y);
        return pts;
    }

    /// <summary>The two endpoints of the hex side shared by two adjacent hexes — i.e. the
    /// exact grid edge between them. Returns null when the hexes are not adjacent.</summary>
    public (PointF P1, PointF P2)? SharedEdge(HexCoord a, HexCoord b)
    {
        if (HexMath.Distance(a, b) != 1) return null;
        var ca = CornersOf(a);
        var cb = CornersOf(b);
        PointF? p1 = null, p2 = null;
        foreach (var p in ca)
            foreach (var q in cb)
                if (MathF.Abs(p.X - q.X) < 0.01f && MathF.Abs(p.Y - q.Y) < 0.01f)
                {
                    if (p1 is null) p1 = p;
                    else { p2 = p; break; }
                }
        return p1 is not null && p2 is not null ? (p1.Value, p2.Value) : null;
    }

    /// <summary>Nearest hex to a board point.</summary>
    public HexCoord PixelToAxial(PointF p)
    {
        if (PointyTop)
        {
            var q = (MathF.Sqrt(3) / 3f * p.X - 1f / 3f * p.Y) / HexRadius;
            var r = (2f / 3f * p.Y) / HexRadius;
            var (rq, rr, _) = RoundCube(q, r, -q - r);
            return new HexCoord((int)rq, (int)rr);
        }
        var q2 = (2f / 3f * p.X) / HexRadius;
        var r2 = (-1f / 3f * p.X + MathF.Sqrt(3) / 3f * p.Y) / HexRadius;
        var (rq2, rr2, _) = RoundCube(q2, r2, -q2 - r2);
        return new HexCoord((int)rq2, (int)rr2);
    }

    private static (float q, float r, float s) RoundCube(float q, float r, float s)
    {
        var rq = MathF.Round(q);
        var rr = MathF.Round(r);
        var rs = MathF.Round(s);
        var dq = Math.Abs(rq - q);
        var dr = Math.Abs(rr - r);
        var ds = Math.Abs(rs - s);
        if (dq > dr && dq > ds) rq = -rr - rs;
        else if (dr > ds) rr = -rq - rs;
        else rs = -rq - rr;
        return (rq, rr, rs);
    }

    /// <summary>Bounding rectangle of all in-bounds hexes (board units), plus the hex-radius margin.</summary>
    public RectangleF TotalArea
    {
        get
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int r = 0; r < Rows; r++)
                for (int q = 0; q < Columns; q++)
                {
                    var c = CenterOf(new HexCoord(q, r));
                    minX = Math.Min(minX, c.X); minY = Math.Min(minY, c.Y);
                    maxX = Math.Max(maxX, c.X); maxY = Math.Max(maxY, c.Y);
                }
            var m = HexRadius;
            return RectangleF.FromLTRB(minX - m, minY - m, maxX + m, maxY + m);
        }
    }

    public static GridMap Load(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        var spec = JsonSerializer.Deserialize<GridMap>(json, JsonOpts)
            ?? throw new InvalidDataException($"Failed to parse map file: {jsonPath}");
        if (spec.Terrain.Length == 0)
            spec.Terrain = Enumerable.Repeat(new string('P', spec.Columns), spec.Rows).ToArray();
        return spec;
    }

    public void OverlayCosts(IReadOnlyDictionary<string, float>? cost, IReadOnlyDictionary<string, float>? defense)
    {
        if (cost != null)
        {
            TerrainCost.Clear();
            foreach (var (k, v) in cost) TerrainCost[k] = v;
        }
        if (defense != null)
        {
            TerrainDefenseBonus.Clear();
            foreach (var (k, v) in defense) TerrainDefenseBonus[k] = v;
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

public sealed class VictoryHexDef
{
    public int Q { get; set; }
    public int R { get; set; }
}

public sealed class RiverDef
{
    public int[]? BetweenRows { get; set; }
    public int[]? BetweenCols { get; set; }
    public List<int> Columns { get; set; } = new();
    public List<int> Rows { get; set; } = new();
    public List<int> Fords { get; set; } = new();
    public List<RiverSegmentDef> Segments { get; set; } = new();
}

public sealed class RiverSegmentDef
{
    public int[] A { get; set; } = new int[2];
    public int[] B { get; set; } = new int[2];
    public bool Ford { get; set; }
}
