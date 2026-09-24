using System.Text.Json;
using System.Text.Json.Serialization;
using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Scenario;

/// <summary>
/// scenario / generic — the scenario layer (scenario.json): which counters participate,
/// their faction/owner/per-scenario stats, and the deployment. Supports explicit hex
/// placement and turn-gated reinforcements (entryTurn + entryHex).
/// </summary>
public sealed class GenericScenario : RuleVariantBase
{
    private ScenarioDef? _scenario;

    public override VariantInfo Info => new()
    {
        Subsystem = "scenario",
        Id = "generic",
        Description = "通用剧本：选算子 + 势力/数值覆盖 + 部署/援军",
        Requires = new[] { "map" },
        Provides = new[] { "deployment", "reinforcements" },
        Status = "stable"
    };

    public override void Load(string? configJson, GameDefinition def)
    {
        if (configJson == null) return;
        _scenario = JsonSerializer.Deserialize<ScenarioDef>(configJson, JsonOpts)
            ?? throw new InvalidDataException("[scenario] scenario.json 解析失败");
    }

    public override void Apply(GameState state, GameEngine? engine)
    {
        if (_scenario == null) return;
        foreach (var u in _scenario.Units)
        {
            var c = state.Counters.FirstOrDefault(x => x.AttributeStr(ContractNames.Key) == u.Key);
            if (c == null)
            {
                c = new CounterState { Id = state.Counters.Count, Side = Side.Front };
                c.Attributes[ContractNames.Key] = u.Key;
                state.Counters.Add(c);
            }
            if (u.Name != null) c.Attributes[ContractNames.Name] = u.Name;
            if (u.Type != null) c.Attributes[ContractNames.Type] = u.Type;
            if (u.Faction != null) c.Attributes[ContractNames.Faction] = u.Faction;
            if (u.Owner.HasValue) c.Attributes[ContractNames.Owner] = (double)u.Owner.Value;
            if (u.Strength.HasValue) c.Attributes[ContractNames.Strength] = u.Strength.Value;
            if (u.Move.HasValue) c.Attributes[ContractNames.Move] = u.Move.Value;

            if (!string.IsNullOrEmpty(u.Node) && state.Map != null && state.Map.TryResolveCell(u.Node, out var cell))
            {
                c.Position = cell;
                c.Attributes[ContractNames.Node] = u.Node;
                c.Attributes.Remove(ContractNames.EntryTurn);
                c.Attributes.Remove(ContractNames.EntryHex);
                if (c.Attributes.TryGetValue(ContractNames.Faction, out var f) && f != null)
                    state.Control[u.Node] = f.ToString() ?? "";
            }
            else if (u.Hex is { Length: >= 2 })
            {
                c.Position = new HexCoord(u.Hex[0], u.Hex[1]);
                c.Attributes.Remove(ContractNames.EntryTurn);
                c.Attributes.Remove(ContractNames.EntryHex);
            }
            else if (u.EntryTurn.HasValue && u.EntryHex is { Length: >= 2 })
            {
                c.Position = null;
                c.Attributes[ContractNames.EntryTurn] = (double)u.EntryTurn.Value;
                c.Attributes[ContractNames.EntryHex] = new[] { u.EntryHex[0], u.EntryHex[1] };
            }
        }
    }

    public override void Validate(GameDefinition def, List<string> issues)
    {
        if (_scenario == null) return;
        foreach (var u in _scenario.Units)
        {
            if (u.Key.Length == 0)
                issues.Add("[scenario] 存在缺少 counter 键的算子");
            if (u.Owner.HasValue && (u.Owner.Value < 0 || u.Owner.Value >= def.PlayerCount))
                issues.Add($"[scenario] 算子 '{u.Key}' 的 owner {u.Owner.Value} 超出玩家数 {def.PlayerCount}");
            if (u.Hex is { Length: >= 2 } h && !BoundsOk(h))
                issues.Add($"[scenario] 算子 '{u.Key}' 的部署格 ({h[0]},{h[1]}) 无效");
            if (u.EntryHex is { Length: >= 2 } e && !BoundsOk(e))
                issues.Add($"[scenario] 算子 '{u.Key}' 的进场格 ({e[0]},{e[1]}) 无效");
            if (u.EntryTurn.HasValue && u.EntryHex == null)
                issues.Add($"[scenario] 算子 '{u.Key}' 有 entryTurn 但缺 entryHex");
            if (u.Hex == null && u.EntryTurn == null && string.IsNullOrEmpty(u.Node))
                issues.Add($"[scenario] 算子 '{u.Key}' 既无 hex 也无 node/entryTurn，将不会上战场");
        }
    }

    private static bool BoundsOk(int[] c)
        => c[0] >= 0 && c[0] < 100 && c[1] >= 0 && c[1] < 100; // refined against the real map in the loader

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

public sealed class ScenarioDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<ScenarioUnitDef> Units { get; set; } = new();
}

public sealed class ScenarioUnitDef
{
    /// <summary>Roster key (units.json). Number keys are accepted and stringified.</summary>
    public object? Counter { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Faction { get; set; }
    public int? Owner { get; set; }
    public double? Strength { get; set; }
    public double? Move { get; set; }
    public int[]? Hex { get; set; }
    /// <summary>Point-to-point deployment: node id (spacemap.json).</summary>
    public string? Node { get; set; }
    public int? EntryTurn { get; set; }
    public int[]? EntryHex { get; set; }

    [JsonIgnore]
    public string Key => Counter?.ToString() ?? "";
}
