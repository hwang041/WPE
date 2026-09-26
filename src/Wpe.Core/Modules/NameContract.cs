using Wpe.Core.Definition;
using Wpe.Core.Expressions;

namespace Wpe.Core.Modules;

/// <summary>
/// The single source of truth for the names an expression may reference: root variables,
/// context variables, counter attributes and game variables. Built from the core contract,
/// variant contributions (<see cref="INameContributor"/>) and the game's own data; used by
/// the loader to flag unknown names in expressions (warnings today, errors later).
/// </summary>
public sealed class NameContract
{
    private readonly HashSet<string> _roots = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ctxVars = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _counterAttrs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _stateVars = new(StringComparer.OrdinalIgnoreCase);

    public void AddRoot(params string[] names) { foreach (var n in names) _roots.Add(n); }
    public void AddCtxVar(params string[] names) { foreach (var n in names) _ctxVars.Add(n); }
    public void AddCounterAttr(params string[] names) { foreach (var n in names) _counterAttrs.Add(n); }
    public void AddStateVar(params string[] names) { foreach (var n in names) _stateVars.Add(n); }

    public bool IsRoot(string name) => _roots.Contains(name);
    public bool IsCtxVar(string name) => _ctxVars.Contains(name);
    public bool IsCounterAttr(string name) => _counterAttrs.Contains(name);
    public bool IsStateVar(string name) => _stateVars.Contains(name);

    /// <summary>Core contract every game shares.</summary>
    public static NameContract Create()
    {
        var c = new NameContract();
        c.AddRoot("me", "turn", "phase", "player", "counter", "target", "targetcounter", "pos", "state");
        c.AddCtxVar("roll", "raw", "result", "attLoss", "defLoss", "card", "cardValue", "cardKind");
        c.AddCounterAttr(
            ContractNames.Name, ContractNames.Owner, ContractNames.Strength, ContractNames.Move,
            ContractNames.MoveLeft, ContractNames.Acted, ContractNames.Faction, ContractNames.Type,
            ContractNames.Key, ContractNames.Color, ContractNames.PenaltyStrength, ContractNames.PenaltyMove,
            ContractNames.EntryTurn, ContractNames.EntryHex, ContractNames.Node,
            ContractNames.Eliminated, ContractNames.Entrenched, ContractNames.Exited, "hex", "rotation");
        return c;
    }

    /// <summary>Harvest names created by a list of effects (setattr -> counter, setvar/addvar/roll -> state).</summary>
    public void ContributeEffects(IEnumerable<EffectDef> effects)
    {
        foreach (var e in effects)
            switch (e.Effect)
            {
                case "setattr":
                    if (!string.IsNullOrEmpty(e.Key)) AddCounterAttr(e.Key);
                    break;
                case "setvar":
                case "addvar":
                case "roll":
                    if (!string.IsNullOrEmpty(e.Key)) AddStateVar(e.Key);
                    break;
            }
    }

    /// <summary>Harvest names the game itself declares/creates (effects, turnReset).</summary>
    public void ContributeFromGame(GameDefinition def)
    {
        ContributeEffects(def.Setup);
        foreach (var m in def.Moves.Values)
        {
            ContributeEffects(m.Effects);
            foreach (var effs in m.ResultEffects.Values) ContributeEffects(effs);
        }
        foreach (var t in def.Triggers.Values) ContributeEffects(t.Do);
        foreach (var r in def.TurnReset)
            if (!string.IsNullOrEmpty(r.Attr)) AddCounterAttr(r.Attr);
    }

    /// <summary>Check an expression's references against the contract; append human-readable warnings.</summary>
    public void Check(ExpressionParser.AstRefs refs, string where, List<string> warnings)
    {
        foreach (var (root, path) in refs.Props)
        {
            var segments = path.Split('.');
            if (root.Equals("counter", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("target", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("targetcounter", StringComparison.OrdinalIgnoreCase))
            {
                var attr = segments[0];
                if (attr is "q" or "r" || IsCounterAttr(attr)) continue;
                warnings.Add($"[names] {where}: 未知算子属性 '{root}.{path}'");
            }
            else if (root.Equals("state", StringComparison.OrdinalIgnoreCase))
            {
                if (segments[0] is "turn" or "phase" or "activeplayer" or "playercount") continue;
                if (segments[0].Equals("vars", StringComparison.OrdinalIgnoreCase))
                {
                    if (segments.Length == 1) continue;
                    if (IsStateVar(segments[1])) continue;
                    warnings.Add($"[names] {where}: 未知游戏变量 'state.vars.{segments[1]}'");
                    continue;
                }
                warnings.Add($"[names] {where}: 未知 state 属性 'state.{path}'");
            }
            else if (root.Equals("pos", StringComparison.OrdinalIgnoreCase))
            {
                if (segments[0] is "q" or "r") continue;
                warnings.Add($"[names] {where}: 未知位置属性 'pos.{path}'");
            }
            else
            {
                warnings.Add($"[names] {where}: 未知根变量 '{root}'");
            }
        }

        foreach (var r in refs.Roots)
            if (!IsRoot(r) && !IsCtxVar(r))
                warnings.Add($"[names] {where}: 未知标识符 '{r}'");
    }
}
