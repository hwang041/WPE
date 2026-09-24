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
public sealed class PointToPoint : RuleVariantBase
{
    private SpaceMap? _map;

    public override VariantInfo Info => new()
    {
        Subsystem = "map",
        Id = "pointToPoint",
        Description = "数据驱动点对点地图（城镇节点 + 道路），节点城防/势力",
        Provides = new[] { "nodes", "roads", "nodetype", "controllednodes", "occupiedby", "terrainDefense" },
        Status = "stable"
    };

    public override void Load(string? configJson, GameDefinition def)
    {
        if (configJson == null)
            throw new InvalidDataException("[map] 需要 spacemap.json 配置文件");
        _map = SpaceMap.Parse(configJson);
    }

    public override void Register(ModuleHost host)
    {
        host.MapData = _map;

        host.AddFunctionAliases((ctx, a) => Map(ctx).TerrainAt(ExprArgs.Cell(a, ctx, 0)), "terrainAt", "terrain", "nodetype", "space_type");
        host.AddFunctionAliases((ctx, a) => (double)Map(ctx).TerrainDefenseAt(ExprArgs.Cell(a, ctx, 0)), "terrainDefense", "terrain_defense");
        host.AddFunctionAliases((ctx, a) => (double)CountControlled(ctx, a), "controllednodes", "nodesheld");
        host.AddFunctionAliases((ctx, a) => OccupiedBy(ctx, a), "occupiedby", "occupied_by");

        host.AddEffect("control", ControlEffect);
    }

    public override void Apply(GameState state, GameEngine? engine)
    {
        if (_map == null) return;
        // initial territory declared on each node
        foreach (var n in _map.Nodes)
        {
            var f = n.AttrStr("faction", "");
            if (!string.IsNullOrEmpty(f)) state.Control[n.Id] = f;
        }
    }

    public override void Validate(GameDefinition def, List<string> issues)
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
        var owner = (int)ExprArgs.Number(a, 1);
        var cell = ExprArgs.Cell(a, ctx, 0);
        return ctx.State.CountersOnBoard().Any(c =>
            c.AttributeInt(ContractNames.Owner, -1) == owner && c.Hex == cell);
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
}
