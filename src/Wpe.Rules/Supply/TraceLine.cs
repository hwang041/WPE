using System.Text.Json;
using Wpe.Core.Definition;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Supply;

/// <summary>
/// supply / traceLine — a unit is in supply when a path of adjacent cells connects it to
/// one of its side's supply sources, without crossing prohibited terrain or enemy
/// units/zones. Evaluated on demand (no per-turn state), so it stays reproducible.
/// </summary>
public sealed class TraceLine : RuleVariantBase, ISupplyModule
{
    private readonly Dictionary<int, HashSet<HexCoord>> _sources = new();
    private readonly HashSet<string> _allowedTerrain = new(StringComparer.OrdinalIgnoreCase);
    private ModuleHost? _host;
    private int _range;            // 0 = unlimited
    private float _costBudget;     // 0 = ignore movement cost
    private bool _blockedByEnemyUnit = true;
    private bool _blockedByEnemyZoc;

    public override VariantInfo Info => new()
    {
        Subsystem = "supply",
        Id = "traceLine",
        Description = "补给线：从单位沿相邻格追溯至本方补给源，敌单位/敌 ZOC/地形可阻断",
        Requires = new[] { "map", "counter" },
        Provides = new[] { "supply", "inSupply", "traceLine" },
        Status = "stable"
    };

    public override void Load(string? configJson, GameDefinition def)
    {
        if (configJson == null) return;
        using var doc = JsonDocument.Parse(configJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("sources", out var src) && src.ValueKind == JsonValueKind.Object)
            foreach (var p in src.EnumerateObject())
            {
                if (!int.TryParse(p.Name, out var owner)) continue;
                var set = new HashSet<HexCoord>();
                if (p.Value.ValueKind == JsonValueKind.Array)
                    foreach (var cell in p.Value.EnumerateArray())
                        if (ParseCell(cell.GetString(), out var h)) set.Add(h);
                _sources[owner] = set;
            }

        if (root.TryGetProperty("allowedTerrain", out var at) && at.ValueKind == JsonValueKind.Array)
            foreach (var t in at.EnumerateArray())
                if (t.GetString() is { Length: > 0 } s) _allowedTerrain.Add(s);

        if (root.TryGetProperty("range", out var rg) && rg.ValueKind == JsonValueKind.Number) _range = rg.GetInt32();
        if (root.TryGetProperty("costBudget", out var cb) && cb.ValueKind == JsonValueKind.Number) _costBudget = cb.GetSingle();
        if (root.TryGetProperty("blockedByEnemyUnit", out var b1) && b1.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _blockedByEnemyUnit = b1.GetBoolean();
        if (root.TryGetProperty("blockedByEnemyZoc", out var b2) && b2.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _blockedByEnemyZoc = b2.GetBoolean();
    }

    public override void Register(ModuleHost host)
    {
        _host = host;
        host.Supply = this;
        host.AddFunctionAliases((ctx, a) => InSupplyOf(ctx.State, ExprArgs.Counter(a, 0)), "inSupply", "in_supply");
        host.AddFunctionAliases((ctx, a) => !InSupplyOf(ctx.State, ExprArgs.Counter(a, 0)), "outOfSupply", "out_of_supply");
        host.AddFunctionAliases((ctx, a) => (double)(Trace(ctx.State, ExprArgs.Counter(a, 0)) ?? -1), "supplyDistance", "supply_distance");
        host.AddFunctionAliases((ctx, a) => IsSource(ExprArgs.Cell(a, ctx, 0)), "isSupplySource", "is_supply_source");
    }

    public override void Validate(GameDefinition def, List<string> issues)
    {
        if (_sources.Count == 0)
            issues.Add("[supply] 未配置任何补给源 (sources)");
    }

    // ---- ISupplyModule ----

    public bool InSupply(GameState state, CounterState unit) => Trace(state, unit).HasValue;

    public int SupplyDistance(GameState state, CounterState unit) => Trace(state, unit) ?? -1;

    // ---- helpers ----

    private bool InSupplyOf(GameState state, CounterState? unit)
        => unit != null && Trace(state, unit).HasValue;

    private int? Trace(GameState state, CounterState? unit)
    {
        if (unit == null || !unit.OnBoard) return null;
        var map = state.Map;
        if (map == null) return null;
        var owner = unit.AttributeInt(ContractNames.Owner, -1);
        if (!_sources.TryGetValue(owner, out var sources) || sources.Count == 0) return null;
        if (sources.Contains(unit.Hex)) return 0;

        var hops = new Dictionary<HexCoord, int> { [unit.Hex] = 0 };
        var costs = new Dictionary<HexCoord, float> { [unit.Hex] = 0f };
        var queue = new Queue<HexCoord>();
        queue.Enqueue(unit.Hex);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var n in map.Neighbors(cur))
            {
                if (!map.InBounds(n) || hops.ContainsKey(n)) continue;
                if (_allowedTerrain.Count > 0 && !_allowedTerrain.Contains(map.TerrainAt(n))) continue;

                var stepCost = _costBudget > 0 && _host?.Movement != null ? _host.Movement.MoveCost(state, cur, n) : 1f;
                var nhops = hops[cur] + 1;
                var ncost = costs[cur] + stepCost;
                if (_range > 0 && nhops > _range) continue;
                if (_costBudget > 0 && ncost > _costBudget) continue;

                if (sources.Contains(n)) return nhops;

                if (_blockedByEnemyUnit && state.CountersOnBoard().Any(c =>
                        c.Hex == n && c.AttributeInt(ContractNames.Owner, -1) != owner)) continue;
                if (_host?.Control != null && _blockedByEnemyZoc && _host.Control.IsEnemyZoc(state, n, owner)) continue;

                hops[n] = nhops;
                costs[n] = ncost;
                queue.Enqueue(n);
            }
        }
        return null;
    }

    private bool IsSource(HexCoord cell) => _sources.Values.Any(s => s.Contains(cell));

    private static bool ParseCell(string? text, out HexCoord cell)
    {
        cell = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split(',', 2);
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0].Trim(), out var q) || !int.TryParse(parts[1].Trim(), out var r)) return false;
        cell = new HexCoord(q, r);
        return true;
    }
}
