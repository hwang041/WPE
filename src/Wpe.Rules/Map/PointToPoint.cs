using System.Text.Json;
using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Expressions;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Map;

/// <summary>
/// map / pointToPoint — a data-driven node network (spacemap.json): named nodes joined
/// by roads. Provides the node-oriented expressions (`nodetype`, `controllednodes`,
/// `occupiedby`) and the `control` effect that persists node ownership in Control.
/// </summary>
public sealed class PointToPoint : IRuleVariant
{
    private SpaceMap? _map;

    public VariantInfo Info => new()
    {
        Subsystem = "map",
        Id = "pointToPoint",
        Description = "数据驱动点对点地图（城镇节点 + 道路），节点城防/势力",
        Status = "stable"
    };

    public void Load(string? configJson, GameDefinition def)
    {
        if (configJson == null)
            throw new InvalidDataException("[map] 需要 spacemap.json 配置文件");
        _map = SpaceMap.Parse(configJson);
    }

    public void Register(ModuleHost host)
    {
        host.MapData = _map;

        host.AddFunction("terrainAt", (ctx, a) => Map(ctx).TerrainAt(Cell(a, ctx, 0)));
        host.AddFunction("terrain", (ctx, a) => Map(ctx).TerrainAt(Cell(a, ctx, 0)));
        host.AddFunction("terrainDefense", (ctx, a) => (double)Map(ctx).TerrainDefenseAt(Cell(a, ctx, 0)));
        host.AddFunction("terrain_defense", (ctx, a) => (double)Map(ctx).TerrainDefenseAt(Cell(a, ctx, 0)));
        host.AddFunction("nodetype", (ctx, a) => Map(ctx).TerrainAt(Cell(a, ctx, 0)));
        host.AddFunction("space_type", (ctx, a) => Map(ctx).TerrainAt(Cell(a, ctx, 0)));

        host.AddFunction("controllednodes", (ctx, a) => (double)CountControlled(ctx, a));
        host.AddFunction("nodesheld", (ctx, a) => (double)CountControlled(ctx, a));

        host.AddFunction("occupiedby", (ctx, a) => OccupiedBy(ctx, a));
        host.AddFunction("occupied_by", (ctx, a) => OccupiedBy(ctx, a));

        host.AddEffect("control", ControlEffect);
    }

    public void Apply(GameState state, GameEngine? engine)
    {
        if (_map == null) return;
        // initial territory declared on each node
        foreach (var n in _map.Nodes)
        {
            var f = n.AttrStr("faction", "");
            if (!string.IsNullOrEmpty(f)) state.Control[n.Id] = f;
        }
    }

    public void Validate(GameDefinition def, List<string> issues)
    {
        if (_map == null) { issues.Add("[map] spacemap.json 未加载"); return; }
        foreach (var e in _map.Edges)
        {
            if (_map.Node(e.A) == null) issues.Add($"[map] 道路引用了未知节点 '{e.A}'");
            if (_map.Node(e.B) == null) issues.Add($"[map] 道路引用了未知节点 '{e.B}'");
        }
    }

    // ---- helpers ----

    private static SpaceMap Map(RuleContext ctx)
        => ctx.State.Map as SpaceMap ?? throw new ExprException("点对点地图未加载");

    private static int CountControlled(RuleContext ctx, object?[] a)
    {
        var f = a.Length > 0 ? a[0]?.ToString() ?? "" : "";
        return ctx.State.Control.Count(kv => kv.Value == f);
    }

    private static bool OccupiedBy(RuleContext ctx, object?[] a)
    {
        if (a.Length < 2) return false;
        var owner = (int)ValueAccessor.AsNumber(a[1]);
        var cell = Cell(a, ctx, 0);
        return ctx.State.CountersOnBoard().Any(c =>
            c.AttributeInt("owner", -1) == owner && c.Hex == cell);
    }

    private static void ControlEffect(RuleContext ctx, EffectDef e)
    {
        if (ctx.State.Map is not SpaceMap sm) return;
        var c = e.Counter == "target" ? ctx.TargetCounter : ctx.Counter;
        var cell = c?.Hex ?? ctx.TargetPos;
        if (cell == null) return;
        var id = sm.NodeIdOf(cell.Value);
        if (string.IsNullOrEmpty(id)) return;
        ctx.State.Control[id] = ctx.Engine.Compile(e.Value).EvalString(ctx);
    }

    /// <summary>Resolve an argument to a cell: counter -> its hex, HexCoord -> itself, else target pos / actor.</summary>
    private static HexCoord Cell(object?[] a, RuleContext ctx, int i)
    {
        if (a.Length > i)
        {
            switch (a[i])
            {
                case HexCoord c: return c;
                case CounterState c: return c.Hex;
            }
        }
        return ctx.TargetPos ?? ctx.Counter?.Hex ?? new HexCoord(-1, 0);
    }
}
