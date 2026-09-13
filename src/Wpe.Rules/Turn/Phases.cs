using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Turn;

/// <summary>
/// turn / phases — the classic IGO-UGO phase cycle. Per-turn attributes (acted,
/// moveLeft, ...) defined by game.json `turnReset` are re-applied to the acting
/// player's units when their action phase begins.
/// </summary>
public sealed class Phases : IRuleVariant, ITurnModule
{
    public VariantInfo Info => new()
    {
        Subsystem = "turn",
        Id = "phases",
        Description = "IGO-UGO 阶段回合：回合重置按 game.json turnReset",
        Status = "stable"
    };

    public void Load(string? configJson, GameDefinition def) { }
    public void Register(ModuleHost host) => host.Turn = this;
    public void Apply(GameState state, GameEngine? engine) { }
    public void Validate(GameDefinition def, List<string> issues)
    {
        if (def.TurnReset.Count > 0 && def.PhaseOrder.Contains("action") == false)
            issues.Add("[turn] 配置了 turnReset 但 phaseOrder 中没有 action 阶段（重置可能不会触发）");
    }

    public void OnTurnStart(GameEngine engine)
    {
        var def = engine.Def;
        if (def.TurnReset.Count == 0) return;
        foreach (var c in engine.State.Counters.Where(c => c.AttributeInt("owner", -1) == engine.State.ActivePlayer))
        {
            foreach (var r in def.TurnReset)
            {
                if (r.Value.StartsWith("expr:"))
                {
                    var ctx = engine.MakeContext(c, null, null);
                    c.Attributes[r.Attr] = engine.Compile(r.Value.Substring(5)).EvalNumber(ctx);
                }
                else if (double.TryParse(r.Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var num))
                {
                    c.Attributes[r.Attr] = num;
                }
                else if (c.Attributes.TryGetValue(r.Value, out var src))
                {
                    c.Attributes[r.Attr] = src;
                }
                else
                {
                    c.Attributes[r.Attr] = 0;
                }
            }
        }
    }
}
