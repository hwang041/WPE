using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Expressions;
using Wpe.Core.Model;

namespace Wpe.Core.Modules;

/// <summary>Metadata for a rule variant (the unit of the rule library).</summary>
public sealed class VariantInfo
{
    public required string Subsystem { get; init; }   // e.g. "movement"
    public required string Id { get; init; }          // e.g. "movePoints"
    public string Description { get; init; } = "";
    public string Version { get; init; } = "1.0.0";
    /// <summary>draft | stable | battle-tested.</summary>
    public string Status { get; init; } = "stable";
    /// <summary>Subsystems that must be selected for this variant to work.</summary>
    public IReadOnlyList<string> Requires { get; init; } = Array.Empty<string>();
    /// <summary>Capabilities this variant provides to others.</summary>
    public IReadOnlyList<string> Provides { get; init; } = Array.Empty<string>();
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

// ---- seam interfaces: the algorithmic surface a subsystem provides -------------

/// <summary>Movement subsystem: how far units can go and what stepping costs.</summary>
public interface IMovementModule
{
    /// <summary>All hexes reachable by the unit within its remaining moveLeft (BFS flood-fill).</summary>
    List<HexCoord> Reachable(CounterState unit, GameEngine engine);

    /// <summary>Movement points to step from a to adjacent b (terrain + river).</summary>
    float MoveCost(GameState state, HexCoord a, HexCoord b);
}

/// <summary>Combat subsystem: resolve an attack into a CRT result code.</summary>
public interface ICombatModule
{
    string Resolve(RuleContext ctx, CombatDef def);
}

/// <summary>Dice subsystem: how random results are generated.</summary>
public interface IDiceModule
{
    int[] Roll(RollSpec spec, int seed);
}

/// <summary>Turn subsystem: per-turn bookkeeping (resets, reinforcements).</summary>
public interface ITurnModule
{
    void OnTurnStart(GameEngine engine);
}

/// <summary>Victory subsystem: decide whether the game has ended.</summary>
public interface IVictoryModule
{
    void Check(GameEngine engine);
}

// ---- host: the registration center / dispatch of the pipeline ------------------

public sealed class ModuleHost
{
    public delegate object? ExprFunc(RuleContext ctx, object?[] args);
    public delegate void EffectHandler(RuleContext ctx, EffectDef e);

    private readonly Dictionary<string, IRuleVariant> _variants = new();
    private readonly Dictionary<string, ExprFunc> _functions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EffectHandler> _effects = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Map data produced by the map subsystem (set at Register time).</summary>
    public GridMap? MapData { get; set; }

    /// <summary>Combat tables (from combat.json), keyed by combat id referenced from moves.</summary>
    public Dictionary<string, CombatDef> CombatDefs { get; } = new();

    public IMovementModule? Movement { get; set; }
    public ICombatModule? Combat { get; set; }
    public IDiceModule? Dice { get; set; }
    public ITurnModule? Turn { get; set; }
    public IVictoryModule? Victory { get; set; }

    // ---- variants ----
    public void AddVariant(IRuleVariant variant) => _variants[variant.Info.Subsystem] = variant;
    public IRuleVariant? GetVariant(string subsystem) => _variants.TryGetValue(subsystem, out var v) ? v : null;
    public IEnumerable<IRuleVariant> AllVariants => _variants.Values;

    // ---- functions ----
    public void AddFunction(string name, ExprFunc f) => _functions[name] = f;

    public object? Call(RuleContext ctx, string name, object?[] args)
    {
        if (_functions.TryGetValue(name, out var f)) return f(ctx, args);
        throw new ExprException($"未知函数: {name}");
    }

    public bool HasFunction(string name) => _functions.ContainsKey(name);

    // ---- effects ----
    public void AddEffect(string keyword, EffectHandler h) => _effects[keyword] = h;

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
