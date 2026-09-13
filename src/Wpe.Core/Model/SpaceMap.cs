using System.Drawing;
using System.Text.Json;

namespace Wpe.Core.Model;

/// <summary>
/// A point-to-point battle map authored as data (spacemap.json): named nodes joined by
/// roads, plus optional decorative polylines (rivers). Implements <see cref="IMap"/> by
/// encoding each node as a cell <c>HexCoord(nodeIndex, 0)</c>; distance is road hop count.
/// Game-specific per-node data (faction/type/defense) lives in <see cref="SpaceNodeDef.Attributes"/>.
/// </summary>
public sealed class SpaceMap : IMap
{
    public const int Unreachable = 9999;

    public string Name { get; set; } = "spacemap";
    public float NodeRadius { get; set; } = 56f;
    public List<SpaceNodeDef> Nodes { get; set; } = new();
    public List<SpaceEdgeDef> Edges { get; set; } = new();
    public List<SpaceLineDef> Lines { get; set; } = new();

    private Dictionary<string, int>? _indexById;
    private Dictionary<int, string>? _idByIndex;
    private Dictionary<int, List<int>>? _adjacency;

    private void Build()
    {
        if (_indexById != null) return;
        _indexById = new Dictionary<string, int>();
        _idByIndex = new Dictionary<int, string>();
        for (int i = 0; i < Nodes.Count; i++)
        {
            _indexById[Nodes[i].Id] = i;
            _idByIndex[i] = Nodes[i].Id;
        }
        _adjacency = new Dictionary<int, List<int>>();
        for (int i = 0; i < Nodes.Count; i++) _adjacency[i] = new List<int>();
        foreach (var e in Edges)
        {
            if (!_indexById.TryGetValue(e.A, out var a) || !_indexById.TryGetValue(e.B, out var b)) continue;
            if (!_adjacency[a].Contains(b)) _adjacency[a].Add(b);
            if (!_adjacency[b].Contains(a)) _adjacency[b].Add(a);
        }
    }

    public SpaceNodeDef? Node(string id)
    {
        Build();
        return _indexById!.TryGetValue(id, out var i) ? Nodes[i] : null;
    }

    public IReadOnlyList<string> NeighborIds(string id)
    {
        Build();
        if (!_indexById!.TryGetValue(id, out var i)) return Array.Empty<string>();
        return _adjacency![i].Select(n => Nodes[n].Id).ToList();
    }

    public bool AreAdjacent(string a, string b)
    {
        Build();
        if (!_indexById!.TryGetValue(a, out var ia) || !_indexById.TryGetValue(b, out var ib)) return false;
        return _adjacency![ia].Contains(ib);
    }

    public SpaceNodeDef? NearestNode(PointF p)
    {
        SpaceNodeDef? best = null;
        float bestD = float.MaxValue;
        foreach (var n in Nodes)
        {
            var dx = n.X - p.X; var dy = n.Y - p.Y;
            var d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = n; }
        }
        return best;
    }

    public PointF CenterOf(string id)
    {
        var n = Node(id);
        return n == null ? PointF.Empty : new PointF(n.X, n.Y);
    }

    /// <summary>Cell (nodeIndex,0) for a node id, or (-1,0) when unknown.</summary>
    public HexCoord CellOf(string id)
    {
        Build();
        return _indexById!.TryGetValue(id, out var i) ? new HexCoord(i, 0) : new HexCoord(-1, 0);
    }

    public string NodeIdOf(HexCoord cell)
    {
        Build();
        return _idByIndex!.TryGetValue(cell.Q, out var id) ? id : "";
    }

    public RectangleF TotalArea
    {
        get
        {
            if (Nodes.Count == 0) return RectangleF.Empty;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var n in Nodes)
            {
                minX = Math.Min(minX, n.X); minY = Math.Min(minY, n.Y);
                maxX = Math.Max(maxX, n.X); maxY = Math.Max(maxY, n.Y);
            }
            var m = NodeRadius * 2.4f;
            return RectangleF.FromLTRB(minX - m, minY - m, maxX + m, maxY + m);
        }
    }

    // ---- IMap ----

    public float CellRadius => NodeRadius;

    public bool InBounds(HexCoord c) => c.R == 0 && c.Q >= 0 && c.Q < Nodes.Count;

    public IReadOnlyList<HexCoord> Neighbors(HexCoord c)
    {
        Build();
        if (!_adjacency!.TryGetValue(c.Q, out var list)) return Array.Empty<HexCoord>();
        return list.Select(i => new HexCoord(i, 0)).ToList();
    }

    public int Distance(HexCoord a, HexCoord b)
    {
        Build();
        if (a == b) return 0;
        if (!_adjacency!.ContainsKey(a.Q) || !_adjacency.ContainsKey(b.Q)) return Unreachable;
        var visited = new Dictionary<int, int> { [a.Q] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(a.Q);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            var cost = visited[cur];
            foreach (var nb in _adjacency[cur])
            {
                if (nb == b.Q) return cost + 1;
                if (visited.ContainsKey(nb)) continue;
                visited[nb] = cost + 1;
                queue.Enqueue(nb);
            }
        }
        return Unreachable;
    }

    public string TerrainAt(HexCoord c)
    {
        var n = InBounds(c) ? Nodes[c.Q] : null;
        return n?.AttrStr("type", "") ?? "";
    }

    public float TerrainCostAt(HexCoord c) => 1f;

    public float TerrainDefenseAt(HexCoord c)
    {
        var n = InBounds(c) ? Nodes[c.Q] : null;
        return n?.AttrInt("defense", 0) ?? 0;
    }

    public bool IsVictoryHex(HexCoord c) => false;

    public PointF CenterOf(HexCoord c)
        => InBounds(c) ? new PointF(Nodes[c.Q].X, Nodes[c.Q].Y) : PointF.Empty;

    public HexCoord CellAt(PointF p)
    {
        Build();
        var n = NearestNode(p);
        return n == null ? new HexCoord(-1, 0) : new HexCoord(_indexById![n.Id], 0);
    }

    public bool TryResolveCell(string key, out HexCoord cell)
    {
        Build();
        if (!string.IsNullOrEmpty(key) && _indexById!.TryGetValue(key, out var i))
        {
            cell = new HexCoord(i, 0);
            return true;
        }
        cell = default;
        return false;
    }

    public static SpaceMap Parse(string json)
    {
        var spec = JsonSerializer.Deserialize<SpaceMap>(json, JsonOpts)
            ?? throw new InvalidDataException("Failed to parse spacemap json");
        spec.Build();
        return spec;
    }

    public static SpaceMap Load(string jsonPath)
        => Parse(File.ReadAllText(jsonPath));

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

public sealed class SpaceNodeDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public Dictionary<string, object> Attributes { get; set; } = new();

    public int AttrInt(string key, int fallback = -1)
    {
        if (!Attributes.TryGetValue(key, out var v) || v == null) return fallback;
        if (v is JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.Number when el.TryGetInt32(out var i) => i,
                JsonValueKind.String when int.TryParse(el.GetString(), out var s) => s,
                _ => fallback
            };
        }
        return int.TryParse(v.ToString(), out var r) ? r : fallback;
    }

    public string AttrStr(string key, string fallback = "")
    {
        if (!Attributes.TryGetValue(key, out var v) || v == null) return fallback;
        if (v is JsonElement el) return el.ValueKind == JsonValueKind.String ? el.GetString() ?? fallback : el.ToString();
        return v.ToString() ?? fallback;
    }
}

public sealed class SpaceEdgeDef
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    /// <summary>Render-only hint: "major" | "minor" | "road".</summary>
    public string Type { get; set; } = "road";
}

public sealed class SpaceLineDef
{
    /// <summary>Render style: "river" gets a two-stroke render; anything else a plain line.</summary>
    public string Style { get; set; } = "line";
    public float Width { get; set; } = 6f;
    /// <summary>Polyline points, each [x, y].</summary>
    public List<float[]> Points { get; set; } = new();
}
