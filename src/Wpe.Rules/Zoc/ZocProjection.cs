using System.Text.Json;
using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Zoc;

/// <summary>
/// zoc / zoc — zones of control, and nothing else. A counter projects control into every
/// adjacent cell; entering an enemy-controlled cell stops movement (and may cost extra to
/// leave). Projection can be limited by unit type, blocked across rivers, and blocked in
/// certain terrain. Movement variants query this through <see cref="IZocModule"/>.
/// </summary>
public sealed class ZocProjection : RuleVariantBase, IZocModule
{
    private readonly HashSet<string> _projectTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _noProjectTerrain = new(StringComparer.OrdinalIgnoreCase);
    private bool _stopOnEnter = true;
    private float _exitCost;
    private bool _noProjectAcrossRiver;

    public override VariantInfo Info => new()
    {
        Subsystem = "zoc",
        Id = "zoc",
        Description = "控制区：相邻格投影 ZOC，进入敌 ZOC 必停/离开加费",
        Requires = new[] { "map", "counter" },
        Provides = new[] { "zoc", "inZoc", "zocOwner" },
        Status = "stable"
    };

    public override void Load(string? configJson, GameDefinition def)
    {
        if (configJson == null) return;
        using var doc = JsonDocument.Parse(configJson);
        var root = doc.RootElement;
        if (root.TryGetProperty("projectTypes", out var pt) && pt.ValueKind == JsonValueKind.Array)
            foreach (var t in pt.EnumerateArray())
                if (t.GetString() is { Length: > 0 } s) _projectTypes.Add(s);
        if (root.TryGetProperty("noProjectTerrain", out var npt) && npt.ValueKind == JsonValueKind.Array)
            foreach (var t in npt.EnumerateArray())
                if (t.GetString() is { Length: > 0 } s) _noProjectTerrain.Add(s);
        if (root.TryGetProperty("stopOnEnter", out var so) && so.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _stopOnEnter = so.GetBoolean();
        if (root.TryGetProperty("exitCost", out var ec) && ec.ValueKind == JsonValueKind.Number)
            _exitCost = ec.GetSingle();
        if (root.TryGetProperty("noProjectAcrossRiver", out var nr) && nr.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _noProjectAcrossRiver = nr.GetBoolean();
    }

    public override void Register(ModuleHost host)
    {
        host.Zoc = this;
        host.AddFunctionAliases((ctx, a) => IsEnemyZoc(ctx.State, ExprArgs.Cell(a, ctx, 0), ctx.State.ActivePlayer),
            "inZoc", "in_zoc");
        host.AddFunctionAliases((ctx, a) => (double)ZocProjectorOwner(ctx.State, ExprArgs.Cell(a, ctx, 0)),
            "zocOwner", "zoc_owner");
        host.AddFunctionAliases((ctx, a) =>
        {
            var c = ExprArgs.Counter(a, 0);
            return c != null && IsEnemyZoc(ctx.State, c.Hex, c.AttributeInt(ContractNames.Owner, -1));
        }, "zocProjected", "zoc_projected");
    }

    // ---- IZocModule ----

    public bool StopsMovementOnEnter => _stopOnEnter;
    public float ZocExitCost => _exitCost;

    public int ZocProjectorOwner(GameState state, HexCoord hex)
    {
        if (state.Map == null) return -1;
        int owner = -1;
        foreach (var c in state.CountersOnBoard())
        {
            if (!Projects(c)) continue;
            if (state.Map.Distance(c.Hex, hex) != 1) continue;
            if (!ProjectsInto(state, c, hex)) continue;
            if (owner < 0) owner = c.AttributeInt(ContractNames.Owner, -1);
        }
        return owner;
    }

    public bool IsEnemyZoc(GameState state, HexCoord hex, int movingOwner)
    {
        var owner = ZocProjectorOwner(state, hex);
        return owner >= 0 && owner != movingOwner;
    }

    // ---- helpers ----

    private bool Projects(CounterState c)
        => _projectTypes.Count == 0 || _projectTypes.Contains(c.AttributeStr(ContractNames.Type));

    /// <summary>Can this counter's control reach across the edge into <paramref name="hex"/>?</summary>
    private bool ProjectsInto(GameState state, CounterState projector, HexCoord hex)
    {
        if (state.Map == null) return false;
        if (_noProjectTerrain.Contains(state.Map.TerrainAt(hex))) return false;
        if (_noProjectAcrossRiver && state.Map is GridMap gm && gm.RiverCost(projector.Hex, hex) > 0) return false;
        return true;
    }
}
