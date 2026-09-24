using System.Text.Json;
using Wpe.Core.Definition;
using Wpe.Core.Expressions;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Map;

/// <summary>
/// map / hexGrid — a rectangular hex grid authored entirely as data (map.json):
/// terrain codes per hex, victory hexes, rivers along edges.
/// </summary>
public sealed class HexGrid : RuleVariantBase
{
    private GridMap? _grid;

    public override VariantInfo Info => new()
    {
        Subsystem = "map",
        Id = "hexGrid",
        Description = "数据驱动六角格地图（地形/河流/胜利点）",
        Provides = new[] { "terrain", "rivers", "victoryHexes" },
        Status = "stable"
    };

    public override void Load(string? configJson, GameDefinition def)
    {
        if (configJson == null)
            throw new InvalidDataException("[map] 需要 map.json 配置文件");
        _grid = JsonSerializer.Deserialize<GridMap>(configJson, JsonOpts)
            ?? throw new InvalidDataException("[map] map.json 解析失败");
        if (_grid.Terrain.Length == 0)
            _grid.Terrain = Enumerable.Repeat(new string('P', _grid.Columns), _grid.Rows).ToArray();
    }

    public override void Register(ModuleHost host)
    {
        host.MapData = _grid;
        host.AddFunctionAliases((ctx, a) => Map(ctx).TerrainAt(ExprArgs.Coord(a, 0)), "terrainAt", "terrain");
        host.AddFunctionAliases((ctx, a) => (double)Map(ctx).TerrainCostAt(ExprArgs.Coord(a, 0)), "terrainCost", "terrain_cost");
        host.AddFunctionAliases((ctx, a) => (double)Map(ctx).TerrainDefenseAt(ExprArgs.Coord(a, 0)), "terrainDefense", "terrain_defense");
        host.AddFunction("isVictoryHex", (ctx, a) => Map(ctx).IsVictoryHex(ExprArgs.Coord(a, 0)));
    }

    public override void Validate(GameDefinition def, List<string> issues)
    {
        if (_grid == null) return;
        for (int r = 0; r < _grid.Terrain.Length; r++)
        {
            var row = _grid.Terrain[r];
            if (row.Length != _grid.Columns)
                issues.Add($"[map] 第{r}行地形长度 {row.Length} != columns {_grid.Columns}");
        }
        foreach (var v in _grid.VictoryHexes)
            if (!_grid.InBounds(new HexCoord(v.Q, v.R)))
                issues.Add($"[map] 胜利点 ({v.Q},{v.R}) 超出地图边界");

        // river connectivity / bounds: every river edge must be a pair of adjacent in-bounds hexes
        for (int i = 0; i < _grid.Rivers.Count; i++)
        {
            var r = _grid.Rivers[i];
            if (r.Segments.Count > 0)
            {
                foreach (var s in r.Segments)
                {
                    if (s.A.Length < 2 || s.B.Length < 2)
                    {
                        issues.Add($"[map] 河流[{i}] 存在缺少端点的段");
                        continue;
                    }
                    var a = new HexCoord(s.A[0], s.A[1]);
                    var b = new HexCoord(s.B[0], s.B[1]);
                    if (!_grid.InBounds(a) || !_grid.InBounds(b))
                        issues.Add($"[map] 河流[{i}] 段 ({a.Q},{a.R})-({b.Q},{b.R}) 越界");
                    else if (HexMath.Distance(a, b) != 1)
                        issues.Add($"[map] 河流[{i}] 段 ({a.Q},{a.R})-({b.Q},{b.R}) 两端不相邻（河流必须沿相邻格边）");
                }
            }
            else
            {
                if (r.BetweenRows is { Length: 2 } br)
                {
                    if (br[0] < 0 || br[1] >= _grid.Rows) issues.Add($"[map] 河流[{i}] betweenRows 越界");
                    foreach (var q in r.Columns)
                        if (q < 0 || q >= _grid.Columns) issues.Add($"[map] 河流[{i}] 列 {q} 越界");
                }
                if (r.BetweenCols is { Length: 2 } bc)
                {
                    if (bc[0] < 0 || bc[1] >= _grid.Columns) issues.Add($"[map] 河流[{i}] betweenCols 越界");
                    foreach (var rr in r.Rows)
                        if (rr < 0 || rr >= _grid.Rows) issues.Add($"[map] 河流[{i}] 行 {rr} 越界");
                }
            }
        }
    }

    private static IMap Map(RuleContext ctx) => ctx.State.Map ?? throw new ExprException("地图未加载");

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
