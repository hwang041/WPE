using System.Text.Json;
using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Loading;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Counter;

/// <summary>
/// counter / generic — the full roster (units.json): counter key -> base attributes.
/// Every roster entry becomes a counter held off-board; the scenario layer then picks
/// which ones fight and where. Attribute keys are constrained by the attribute contract.
/// </summary>
public sealed class GenericCounter : RuleVariantBase, INameContributor
{
    private readonly List<(string key, Dictionary<string, object> attrs)> _roster = new();

    /// <summary>Every attribute key declared in units.json is a legitimate counter attribute.</summary>
    public void ContributeNames(NameContract contract)
    {
        foreach (var (_, attrs) in _roster)
            foreach (var key in attrs.Keys)
                contract.AddCounterAttr(key);
    }

    public override VariantInfo Info => new()
    {
        Subsystem = "counter",
        Id = "generic",
        Description = "通用算子目录：units.json 键 → 基础属性",
        Provides = new[] { "roster" },
        Status = "stable"
    };

    public override void Load(string? configJson, GameDefinition def)
    {
        if (configJson == null) return;
        using var doc = JsonDocument.Parse(configJson);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var attrs = new Dictionary<string, object>();
            foreach (var a in prop.Value.EnumerateObject())
                attrs[a.Name] = JsonUtil.Scalar(a.Value) ?? "";
            _roster.Add((prop.Name, attrs));
        }
    }

    public override void Apply(GameState state, GameEngine? engine)
    {
        foreach (var (key, attrs) in _roster)
        {
            var c = new CounterState { Id = state.Counters.Count, Side = Side.Front };
            c.Attributes[ContractNames.Key] = key;
            foreach (var (k, v) in attrs) c.Attributes[k] = v;
            state.Counters.Add(c);
        }
    }

    public override void Validate(GameDefinition def, List<string> issues)
    {
        var keys = _roster.Select(r => r.key).ToList();
        if (keys.Count != keys.Distinct().Count())
            issues.Add("[counter] units.json 中存在重复的算子键");
    }
}
