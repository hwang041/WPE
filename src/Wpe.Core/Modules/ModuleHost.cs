using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Expressions;
using Wpe.Core.Model;

namespace Wpe.Core.Modules;

/// <summary>Metadata for a rule variant (the unit of the rule library).</summary>
public sealed class VariantInfo
{
    /// <summary>Category (grouping label / folder): e.g. "movement". Not not a mutual-exclusion
    /// constraint — exclusivity is decided by the capabilities in <see cref="Provides"/>.</summary>
    public required string Subsystem { get; init; }
    public required string Id { get; init; }          // e.g. "movePoints"
    public string Description { get; init; } = "";
    public string Version { get; init; } = "1.0.0";
    /// <summary>draft | stable | battle-tested.</summary>
    public string Status { get; init; } = "stable";
    /// <summary>Capabilities that must be provided by some selected rule (exclusive seams,
    /// additive hooks or named contributions).</summary>
    public IReadOnlyList<string> Requires { get; init; } = Array.Empty<string>();
    /// <summary>Specific variant pins, e.g. "map:hexGrid" (a variant compatible only with one map kind).</summary>
    public IReadOnlyList<string> RequiresVariants { get; init; } = Array.Empty<string>();
    /// <summary>Capabilities this variant provides: exclusive capability names plus the
    /// expression-function / effect names it registers.</summary>
    public IReadOnlyList<string> Provides { get; init; } = Array.Empty<string>();
    /// <summary>Contribution names this rule intentionally overrides (suppresses the
    /// duplicate-provider conflict).</summary>
    public IReadOnlyList<string> Overrides { get; init; } = Array.Empty<string>();
}

/// <summary>
/// A pluggable rule variant. Lifecycle:
///   Load(configJson, def)  -> parse the variant's own JSON config
///   Register(host)         -> register functions/effects/seams it provides
///   Apply(state, engine)   -> apply side effects on the built state (deployment, costs...)
///   Validate(def, issues)  -> check its config; append human-readable issues
/// </summary>
public interface IRuleVariant
{
    VariantInfo Info { get; }
    void Load(string? configJson, GameDefinition def);
    void Register(ModuleHost host);
    void Apply(GameState state, GameEngine? engine);
    void Validate(GameDefinition def, List<string> issues);
}

// ---- seam interfaces: the algorithmic surface a capability provides ------------

/// <summary>Stacking capability: how many counters may share one cell.</summary>
public interface IStackingModule
{
    /// <summary>Maximum counters per cell; 0 = unlimited.</summary>
    int MaxPerHex { get; }
}

/// <summary>Counter capability: the unit model — how effective strength/move are derived
/// (e.g. binary front/back, multi-step, damage track).</summary>
public interface ICounterModule
{
    /// <summary>Effective combat strength of a counter under this unit model.</summary>
    double EffectiveStrength(CounterState counter);

    /// <summary>Effective movement allowance of a counter under this unit model.</summary>
    double EffectiveMove(CounterState counter);
}

/// <summary>ZOC capability: zone-of-control projection (consumed by movement). Owner
/// values use the engine's player numbering; -1 = none. This is ONLY about zones of
/// control — cell ownership lives in the separate `territory` capability.</summary>
public interface IZocModule
{
    /// <summary>Owner projecting a zone of control into the given cell, or -1.</summary>
    int ZocProjectorOwner(GameState state, HexCoord hex);

    /// <summary>Whether the given moving owner is entering an enemy zone of control.</summary>
    bool IsEnemyZoc(GameState state, HexCoord hex, int movingOwner);

    /// <summary>Movement stops upon entering an enemy zone of control.</summary>
    bool StopsMovementOnEnter { get; }

    /// <summary>Extra movement cost to leave an enemy zone of control.</summary>
    float ZocExitCost { get; }
}

/// <summary>Supply capability: whether a unit is in supply and how far it traces.</summary>
public interface ISupplyModule
{
    bool InSupply(GameState state, CounterState unit);

    /// <summary>Trace distance to the nearest source; -1 when out of supply / not traceable.</summary>
    int SupplyDistance(GameState state, CounterState unit);
}

/// <summary>Movement capability: how far units can go and what stepping costs.</summary>
public interface IMovementModule
{
    /// <summary>All hexes reachable by the unit within its remaining moveLeft (BFS flood-fill).</summary>
    List<HexCoord> Reachable(CounterState unit, GameEngine engine);

    /// <summary>Movement points to step from a to adjacent b (terrain + river).</summary>
    float MoveCost(GameState state, HexCoord a, HexCoord b);
}

/// <summary>Combat capability: resolve an attack into a CRT result code.</summary>
public interface ICombatModule
{
    string Resolve(RuleContext ctx, CombatDef def);
}

/// <summary>Dice capability: how random results are generated.</summary>
public interface IDiceModule
{
    int[] Roll(RollSpec spec, int seed);
}

/// <summary>Turn capability: per-turn bookkeeping (resets, reinforcements).</summary>
public interface ITurnModule
{
    void OnTurnStart(GameEngine engine);
}

// ---- host: the registration center / dispatch of the pipeline ------------------

public sealed class ModuleHost
{
    public delegate object? ExprFunc(RuleContext ctx, object?[] args);
    public delegate void EffectHandler(RuleContext ctx, EffectDef e);

    private readonly List<IRuleVariant> _rules = new();
    private readonly Dictionary<(string Category, string Id), IRuleVariant> _ruleIndex = new();
    private readonly Dictionary<string, ExprFunc> _functions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EffectHandler> _effects = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _functionOwner = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _effectOwner = new(StringComparer.OrdinalIgnoreCase);
    private string _provider = "";
    private readonly HashSet<string> _overrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Duplicate contribution names detected during registration (composition errors).</summary>
    public List<string> NameConflicts { get; } = new();

    /// <summary>Map data produced by the map capability (set at Register time).</summary>
    public IMap? MapData { get; set; }

    /// <summary>Combat tables (from combat.json), keyed by combat id referenced from moves.</summary>
    public Dictionary<string, CombatDef> CombatDefs { get; } = new();

    /// <summary>Card definitions (from cards.json), keyed by card id — populated by the cards capability.</summary>
    public Dictionary<string, CardDef> Cards { get; } = new();

    /// <summary>Deck composition (from cards.json) — populated by the cards capability.</summary>
    public List<DeckDef> Decks { get; } = new();

    /// <summary>Deterministic RNG shared by card draws and the "roll" effect (reseeded on reset).</summary>
    public SeededRandom Rng { get; set; } = new(20240913);

    public ICounterModule? Counter { get; set; }
    public IZocModule? Zoc { get; set; }
    public ISupplyModule? Supply { get; set; }
    public IMovementModule? Movement { get; set; }
    public IStackingModule? Stacking { get; set; }
    public ICombatModule? Combat { get; set; }
    public IDiceModule? Dice { get; set; }
    public ITurnModule? Turn { get; set; }

    // ---- rules ----
    public void AddRule(IRuleVariant variant)
    {
        _rules.Add(variant);
        _ruleIndex[(variant.Info.Subsystem, variant.Info.Id)] = variant;
    }

    public IRuleVariant? GetRule(string category, string id)
        => _ruleIndex.TryGetValue((category, id), out var v) ? v : null;

    public IEnumerable<IRuleVariant> RulesOf(string category)
        => _rules.Where(r => string.Equals(r.Info.Subsystem, category, StringComparison.OrdinalIgnoreCase));

    public bool HasRule(string category, string id) => _ruleIndex.ContainsKey((category, id));

    public IEnumerable<IRuleVariant> AllVariants => _rules;

    // ---- provider tracking (duplicate contribution detection) ----

    /// <summary>Mark which rule the next Register calls belong to (for conflict detection).</summary>
    public void BeginRule(string provider, IEnumerable<string>? overrides = null)
    {
        _provider = provider ?? "";
        _overrides.Clear();
        if (overrides != null)
            foreach (var o in overrides) _overrides.Add(o);
    }

    private void Claim(Dictionary<string, string> owners, string name, string kind)
    {
        if (owners.TryGetValue(name, out var prev) && prev.Length > 0 && _provider.Length > 0 &&
            !string.Equals(prev, _provider, StringComparison.Ordinal) && !_overrides.Contains(name))
            NameConflicts.Add($"[组合] {kind} '{name}' 由 {prev} 与 {_provider} 重复提供（未声明 override）");
        owners[name] = _provider;
    }

    // ---- functions ----
    public void AddFunction(string name, ExprFunc f)
    {
        Claim(_functionOwner, name, "函数");
        _functions[name] = f;
    }

    /// <summary>Register the same handler under several names (camel/snake aliases).</summary>
    public void AddFunctionAliases(ExprFunc f, params string[] names)
    {
        foreach (var n in names)
        {
            Claim(_functionOwner, n, "函数");
            _functions[n] = f;
        }
    }

    public object? Call(RuleContext ctx, string name, object?[] args)
    {
        if (_functions.TryGetValue(name, out var f)) return f(ctx, args);
        throw new ExprException($"未知函数: {name}");
    }

    public bool HasFunction(string name) => _functions.ContainsKey(name);

    // ---- effects ----
    public void AddEffect(string keyword, EffectHandler h)
    {
        Claim(_effectOwner, keyword, "效果");
        _effects[keyword] = h;
    }

    public bool TryApplyEffect(RuleContext ctx, EffectDef e)
    {
        if (_effects.TryGetValue(e.Effect, out var h))
        {
            h(ctx, e);
            return true;
        }
        ctx.State.LogMessage($"未知效果: {e.Effect}");
        return false;
    }
}
