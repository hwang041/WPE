using System.Text.Json;
using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Expressions;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Stacking;

/// <summary>
/// stacking / unlimited — any number of counters may share a cell. This is the default
/// preset behaviour; per-cell occupancy is then left to the game's own validators.
/// Provides `stackcount(pos)` and `stacklimit()` (0 = unlimited).
/// </summary>
public sealed class UnlimitedStacking : IRuleVariant, IStackingModule
{
    public VariantInfo Info => new()
    {
        Subsystem = "stacking",
        Id = "unlimited",
        Description = "无限堆叠：一格可放任意数量算子",
        Status = "stable"
    };

    public int MaxPerHex => 0;

    public void Load(string? configJson, GameDefinition def) { }
    public void Apply(GameState state, GameEngine? engine) { }
    public void Validate(GameDefinition def, List<string> issues) { }

    public void Register(ModuleHost host)
    {
        host.Stacking = this;
        StackingFunctions.Register(host);
    }
}

/// <summary>
/// stacking / perHex — at most `maxPerHex` counters per cell (config `{ "maxPerHex": N }`).
/// The engine enforces the limit for any position-taking move.
/// </summary>
public sealed class PerHexStacking : IRuleVariant, IStackingModule
{
    private int _maxPerHex = 1;

    public VariantInfo Info => new()
    {
        Subsystem = "stacking",
        Id = "perHex",
        Description = "每格堆叠上限：maxPerHex 个算子/格",
        Status = "stable"
    };

    public int MaxPerHex => _maxPerHex;

    public void Load(string? configJson, GameDefinition def)
    {
        if (configJson == null) return;
        using var doc = JsonDocument.Parse(configJson);
        if (doc.RootElement.TryGetProperty("maxPerHex", out var m) && m.ValueKind == JsonValueKind.Number)
            _maxPerHex = Math.Max(1, m.GetInt32());
    }

    public void Apply(GameState state, GameEngine? engine) { }

    public void Validate(GameDefinition def, List<string> issues)
    {
        if (_maxPerHex < 1) issues.Add("[stacking] maxPerHex 必须 >= 1");
    }

    public void Register(ModuleHost host)
    {
        host.Stacking = this;
        StackingFunctions.Register(host);
    }
}

internal static class StackingFunctions
{
    public static void Register(ModuleHost host)
    {
        host.AddFunction("stackcount", (ctx, a) => (double)Count(ctx, Cell(a, ctx, 0)));
        host.AddFunction("stacklimit", (ctx, a) => (double)(host.Stacking?.MaxPerHex ?? 0));
    }

    private static int Count(RuleContext ctx, HexCoord cell)
        => ctx.State.CountersOnBoard().Count(c => c.Hex == cell);

    private static HexCoord Cell(object?[] a, RuleContext ctx, int i)
    {
        if (a.Length > i)
        {
            switch (a[i])
            {
                case HexCoord c: return c;
                case CounterState c: return c.Hex;
            }
        }
        return ctx.TargetPos ?? ctx.Counter?.Hex ?? new HexCoord(-1, 0);
    }
}
