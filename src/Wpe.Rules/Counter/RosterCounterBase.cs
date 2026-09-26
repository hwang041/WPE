using System.Text.Json;
using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Loading;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Counter;

/// <summary>
/// Shared roster handling for counter variants: reads units.json (counter key -> base
/// attributes), builds off-board counters, and contributes every declared attribute name
/// to the name contract. Concrete variants add the unit model (effective strength/move)
/// and any loss effects (step loss, damage).
/// </summary>
public abstract class RosterCounterBase : RuleVariantBase, INameContributor
{
    protected readonly List<(string Key, Dictionary<string, object> Attrs)> Roster = new();

    public virtual void ContributeNames(NameContract contract)
    {
        foreach (var (_, attrs) in Roster)
            foreach (var key in attrs.Keys)
                contract.AddCounterAttr(key);
    }

    public override void Load(string? configJson, GameDefinition def)
    {
        if (configJson == null) return;
        using var doc = JsonDocument.Parse(configJson);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var attrs = new Dictionary<string, object>();
            foreach (var a in prop.Value.EnumerateObject())
                attrs[a.Name] = JsonUtil.Scalar(a.Value) ?? "";
            Roster.Add((prop.Name, attrs));
        }
    }

    public override void Apply(GameState state, GameEngine? engine)
    {
        foreach (var (key, attrs) in Roster)
        {
            var c = new CounterState { Id = state.Counters.Count, Side = Side.Front };
            c.Attributes[ContractNames.Key] = key;
            foreach (var (k, v) in attrs) c.Attributes[k] = v;
            state.Counters.Add(c);
        }
    }

    public override void Validate(GameDefinition def, List<string> issues)
    {
        var keys = Roster.Select(r => r.Key).ToList();
        if (keys.Count != keys.Distinct().Count())
            issues.Add("[counter] units.json 中存在重复的算子键");
    }

    /// <summary>Penalty applied to strength/move when a counter is on its damaged (back) side.</summary>
    protected static double StrengthPenalty(CounterState c) => c.AttributeFloat(ContractNames.PenaltyStrength);
    protected static double MovePenalty(CounterState c) => c.AttributeFloat(ContractNames.PenaltyMove);
}
