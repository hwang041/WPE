using System.Text.Json;
using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Expressions;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Control;

/// <summary>
/// control / controlPoints — persistent ownership of map cells (keyed by
/// <see cref="IMap.CellKey"/>), plus the `control`/`capture` effect that writes it.
/// Works on any map kind (hex "q,r" / point-to-point node id). Victory variants and
/// scenario rules read it to decide control-based objectives.
/// </summary>
public sealed class ControlPoints : RuleVariantBase
{
    private bool _initialFromUnits = true;

    public override VariantInfo Info => new()
    {
        Subsystem = "control",
        Id = "controlPoints",
        Description = "控制点归属：按格记录控制权 + control/capture 效果",
        Requires = new[] { "map" },
        Provides = new[] { "controlPoints", "control", "hexControl", "capture" },
        Status = "stable"
    };

    public override void Load(string? configJson, GameDefinition def)
    {
        if (configJson == null) return;
        using var doc = JsonDocument.Parse(configJson);
        if (doc.RootElement.TryGetProperty("initialFromUnits", out var v) &&
            v.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _initialFromUnits = v.GetBoolean();
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
        if (!_initialFromUnits || state.Map == null) return;
        foreach (var c in state.CountersOnBoard())
        {
            var key = state.Map.CellKey(c.Hex);
            if (string.IsNullOrEmpty(key)) continue;
            state.Control[key] = c.AttributeInt(ContractNames.Owner, -1).ToString();
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
        ctx.State.Control[key] = value;
        ctx.State.LogMessage($"控制 {key} → {(string.IsNullOrEmpty(value) ? "无" : value)}");
    }

    // ---- helpers ----

    private static string ControlOf(RuleContext ctx, HexCoord cell)
    {
        var map = ctx.State.Map;
        if (map == null) return "";
        var key = map.CellKey(cell);
        return ctx.State.Control.TryGetValue(key, out var v) ? v : "";
    }

    private static bool IsControlledBy(RuleContext ctx, HexCoord cell, string owner)
        => ControlOf(ctx, cell) == owner;

    private static int CountControlled(RuleContext ctx, string owner)
        => ctx.State.Control.Count(kv => kv.Value == owner);
}
