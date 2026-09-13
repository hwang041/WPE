using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Movement;

/// <summary>
/// movement / roadNetwork — units move along the roads of a point-to-point map one hop
/// at a time. Reachable nodes are a BFS bounded by the unit's remaining moveLeft; a node
/// occupied by an enemy may not be entered (and blocks further movement through it).
/// </summary>
public sealed class RoadNetwork : IRuleVariant, IMovementModule
{
    public VariantInfo Info => new()
    {
        Subsystem = "movement",
        Id = "roadNetwork",
        Description = "点对点道路移动：逐段走，每段 1 点，禁入敌占节点",
        Requires = new[] { "map" },
        Status = "stable"
    };

    public void Load(string? configJson, GameDefinition def) { }
    public void Register(ModuleHost host) => host.Movement = this;
    public void Apply(GameState state, GameEngine? engine) { }
    public void Validate(GameDefinition def, List<string> issues)
    {
        if (def.Moves.Values.Any(m => m.Kind == "movement") && !def.Moves.Values.Any(m => m.NeedsPosition))
            issues.Add("[movement] 存在移动行动但没有任何 needsPosition 行动");
    }

    public float MoveCost(GameState state, HexCoord a, HexCoord b) => 1f;

    public List<HexCoord> Reachable(CounterState unit, GameEngine engine)
    {
        var result = new List<HexCoord>();
        if (engine.State.Map is not SpaceMap sm) return result;
        if (!unit.OnBoard) return result;

        var moveLeft = unit.AttributeFloat("moveLeft");
        var visited = new Dictionary<HexCoord, int> { [unit.Hex] = 0 };
        var queue = new Queue<HexCoord>();
        queue.Enqueue(unit.Hex);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            var cost = visited[cur];
            foreach (var nb in sm.Neighbors(cur))
            {
                var step = cost + 1;
                if (step > moveLeft) continue;
                if (visited.TryGetValue(nb, out var existing) && existing <= step) continue;
                int myOwner = unit.AttributeInt("owner", -1);
                bool enemyHeld = engine.State.CountersOnBoard().Any(c =>
                    c.Id != unit.Id && c.AttributeInt("owner", -1) != myOwner && c.Hex == nb);
                if (enemyHeld) continue;
                visited[nb] = step;
                queue.Enqueue(nb);
                result.Add(nb);
            }
        }
        return result;
    }
}
