using System.Text.Json;
using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Expressions;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Territory;

/// <summary>
/// territory / controlPoints — persistent ownership of map cells (keyed by
/// <see cref="IMap.CellKey"/>), plus the `control`/`capture` effect that writes it.
/// Works on any map kind (hex "q,r" / point-to-point node id). Victory and scenario rules
/// read it to decide control-based objectives. This is ownership, NOT ZOC (see zoc/*).
/// </summary>
public sealed class ControlPoints : RuleVariantBase
{
    private bool _initialFromUnits;
    private bool _initialFromMap = true;

    public override VariantInfo Info => new()
    {
        Subsystem = "territory",
        Id = "controlPoints",
        Description = "领地归属：按格记录控制权 + control/capture 效果",
        Requires = new[] { "map", "counter", "scenario" },
        Provides = new[] { "territory", "control", "capture", "hexControl", "controlledBy", "controlledCount" },
        Status = "stable"
    };

    public override void Load(string? configJson, GameDefinition def)
    {
        if (configJson == null) return;
        using var doc = JsonDocument.Parse(configJson);
        if (doc.RootElement.TryGetProperty("initialFromUnits", out var v) &&
            v.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _initialFromUnits = v.GetBoolean();
        if (doc.RootElement.TryGetProperty("initialFromMap", out var m) &&
            m.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _initialFromMap = m.GetBoolean();
    }

    public override void Register(ModuleHost host)
    {
        host.AddEffect("control", ControlEffect);
        host.AddEffect("capture", ControlEffect);

        host.AddFunctionAliases((ctx, a) => ControlOf(ctx, ExprArgs.Cell(a, ctx, 0)),
            "hexControl", "hex_control", "controlOf", "control_of");
        host.AddFunctionAliases((ctx, a) => IsControlledBy(ctx, ExprArgs.Cell(a, ctx, 0), ExprArgs.Str(a, 1)),
            "controlledBy", "controlled_by");
        host.AddFunctionAliases((ctx, a) => (double)CountControlled(ctx, ExprArgs.Str(a, 0)),
            "controlledCount", "controlled_count");
    }

    public override void Apply(GameState state, GameEngine? engine)
    {
        var map = state.Map;
        if (map == null) return;

        // 1) seed from the map's declared starting controllers (e.g. node factions).
        //    Only fill gaps, so earlier deployments (scenario node overrides) win.
        if (_initialFromMap)
            foreach (var (key, controller) in map.InitialControllers())
                if (!state.Territory.ContainsKey(key)) state.Territory[key] = controller;

        // 2) optionally seed from the positions units were deployed at.
        if (!_initialFromUnits) return;
        foreach (var c in state.CountersOnBoard())
        {
            var key = map.CellKey(c.Hex);
            if (string.IsNullOrEmpty(key)) continue;
            state.Territory[key] = c.AttributeInt(ContractNames.Owner, -1).ToString();
        }
    }

    // ---- effect ----

    private static void ControlEffect(RuleContext ctx, EffectDef e)
    {
        var map = ctx.State.Map;
        if (map == null) return;
        var c = e.Counter == "target" ? ctx.TargetCounter : ctx.Counter;
        var cell = c?.Hex ?? ctx.TargetPos;
        if (cell == null) return;
        var key = map.CellKey(cell.Value);
        if (string.IsNullOrEmpty(key)) return;
        var value = ctx.Engine.Compile(e.Value).EvalString(ctx);
        ctx.State.Territory[key] = value;
        ctx.State.LogMessage($"控制 {key} → {(string.IsNullOrEmpty(value) ? "无" : value)}");
    }

    // ---- helpers ----

    private static string ControlOf(RuleContext ctx, HexCoord cell)
    {
        var map = ctx.State.Map;
        if (map == null) return "";
        var key = map.CellKey(cell);
        return ctx.State.Territory.TryGetValue(key, out var v) ? v : "";
    }

    private static bool IsControlledBy(RuleContext ctx, HexCoord cell, string owner)
        => ControlOf(ctx, cell) == owner;

    private static int CountControlled(RuleContext ctx, string owner)
        => ctx.State.Territory.Count(kv => kv.Value == owner);
}
