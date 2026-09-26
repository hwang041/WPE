using System.Text.Json;
using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Movement;

/// <summary>
/// movement / zoc — move-points movement that respects zones of control (control
/// subsystem): a unit must stop upon entering an enemy ZOC and pays extra to leave one.
/// Falls back to plain move-points behaviour when no control subsystem is active.
/// </summary>
public sealed class ZocMove : RuleVariantBase, IMovementModule
{
    private Dictionary<string, float>? _terrainCost;
    private Dictionary<string, float>? _terrainDefense;
    private float _riverCrossCost = 2f;

    public override VariantInfo Info => new()
    {
        Subsystem = "movement",
        Id = "zoc",
        Description = "ZOC 移动：行动点移动 + 进入敌控制区必停/离开加费",
        Requires = new[] { "map", "counter", "control" },
        RequiresVariants = new[] { "map:hexGrid" },
        Provides = new[] { "reachability", "moveCost", "riverCost", "zocReachability" },
        Status = "stable"
    };

    public override void Load(string? configJson, GameDefinition def)
    {
        if (configJson == null) return;
        using var doc = JsonDocument.Parse(configJson);
        if (doc.RootElement.TryGetProperty("terrainCost", out var cost) && cost.ValueKind == JsonValueKind.Object)
            _terrainCost = ParseFloatDict(cost);
        if (doc.RootElement.TryGetProperty("terrainDefenseBonus", out var defB) && defB.ValueKind == JsonValueKind.Object)
            _terrainDefense = ParseFloatDict(defB);
        if (doc.RootElement.TryGetProperty("riverCrossCost", out var rcc) && rcc.ValueKind == JsonValueKind.Number)
            _riverCrossCost = rcc.GetSingle();
    }

    public override void Register(ModuleHost host)
    {
        host.Movement = this;
        host.AddFunctionAliases((ctx, a) => (double)StepCost(ctx.State, ExprArgs.Coord(a, 0), ExprArgs.Coord(a, 1)),
            "moveCost", "move_cost");
        host.AddFunctionAliases((ctx, a) =>
            (double)((ctx.State.Map as GridMap)?.RiverCost(ExprArgs.Coord(a, 0), ExprArgs.Coord(a, 1)) ?? 0),
            "riverCost", "river_cost");
    }

    public override void Apply(GameState state, GameEngine? engine)
    {
        if (state.Map is not GridMap gm) return;
        if (_terrainCost != null) gm.OverlayCosts(_terrainCost, null);
        if (_terrainDefense != null) gm.OverlayCosts(null, _terrainDefense);
        gm.RiverCrossCost = _riverCrossCost;
    }

    public override void Validate(GameDefinition def, List<string> issues)
    {
        if (def.Moves.Values.Any(m => m.Kind == "movement") && _terrainCost == null)
            issues.Add("[movement] 存在移动行动但未提供 terrainCost（移动费将全部默认 1）");
    }

    // ---- IMovementModule ----

    public float MoveCost(GameState state, HexCoord a, HexCoord b) => StepCost(state, a, b);

    public List<HexCoord> Reachable(CounterState unit, GameEngine engine)
    {
        var result = new List<HexCoord>();
        var gm = engine.State.Map as GridMap;
        if (gm == null || !unit.OnBoard) return result;

        var control = engine.Host.Control;
        var owner = unit.AttributeInt(ContractNames.Owner, -1);
        var moveLeft = unit.AttributeFloat(ContractNames.MoveLeft);
        var visited = new Dictionary<HexCoord, float> { [unit.Hex] = 0f };
        var queue = new Queue<HexCoord>();
        queue.Enqueue(unit.Hex);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            var curCost = visited[cur];
            bool curInZoc = control != null && control.IsEnemyZoc(engine.State, cur, owner);
            // entering an enemy ZOC ends movement — do not expand further from it
            if (curInZoc && control!.StopsMovementOnEnter) continue;

            foreach (var n in HexMath.Neighbors(cur, gm.PointyTop))
            {
                if (!gm.InBounds(n)) continue;
                var step = StepCost(engine.State, cur, n) + (curInZoc ? control?.ZocExitCost ?? 0 : 0);
                var cost = curCost + step;
                if (cost > moveLeft) continue;
                bool occupied = engine.State.CountersOnBoard()
                    .Any(x => !ReferenceEquals(x, unit) && x.Hex == n);
                if (occupied) continue;
                if (visited.TryGetValue(n, out var existing) && existing <= cost) continue;
                visited[n] = cost;
                queue.Enqueue(n);
                result.Add(n);
            }
        }
        return result;
    }

    // ---- helpers ----

    private static float StepCost(GameState state, HexCoord from, HexCoord to)
    {
        if (state.Map is not GridMap gm) return 1f;
        return gm.TerrainCostAt(to) + gm.RiverCost(from, to);
    }

    private static Dictionary<string, float> ParseFloatDict(JsonElement obj)
    {
        var dict = new Dictionary<string, float>();
        foreach (var p in obj.EnumerateObject())
        {
            float f;
            switch (p.Value.ValueKind)
            {
                case JsonValueKind.Number: f = p.Value.GetSingle(); break;
                case JsonValueKind.String: if (!float.TryParse(p.Value.GetString(), out f)) continue; break;
                default: continue;
            }
            dict[p.Name] = f;
        }
        return dict;
    }
}
