using Wpe.Core.Expressions;
using Wpe.Core.Model;

namespace Wpe.Core.Modules;

/// <summary>
/// Shared argument coercion for expression functions, so every variant doesn't re-implement
/// the same "first arg is a counter or a hex?" plumbing. Used by CoreFunctions and variants.
/// </summary>
public static class ExprArgs
{
    /// <summary>Argument i as a counter (or null).</summary>
    public static CounterState? Counter(object?[] a, int i)
        => a.Length > i ? a[i] as CounterState : null;

    /// <summary>Argument i as a hex: HexCoord directly or a counter's hex; else (0,0).</summary>
    public static HexCoord Coord(object?[] a, int i)
        => a.Length > i
            ? a[i] switch
            {
                HexCoord c => c,
                CounterState c => c.Hex,
                _ => new HexCoord(0, 0)
            }
            : new HexCoord(0, 0);

    /// <summary>
    /// Argument i as a cell (HexCoord / counter's hex), falling back to the action's target
    /// position or the acting counter's hex — for functions like terrainAt(pos) / nodeType().
    /// </summary>
    public static HexCoord Cell(object?[] a, RuleContext ctx, int i)
        => a.Length > i
            ? a[i] switch
            {
                HexCoord c => c,
                CounterState c => c.Hex,
                _ => Fallback(ctx)
            }
            : Fallback(ctx);

    private static HexCoord Fallback(RuleContext ctx)
        => ctx.TargetPos ?? ctx.Counter?.Hex ?? new HexCoord(-1, 0);

    public static double Number(object?[] a, int i)
        => ValueAccessor.AsNumber(a.Length > i ? a[i] : 0);

    public static string Str(object?[] a, int i)
        => a.Length > i ? a[i]?.ToString() ?? "" : "";

    public static bool Bool(object?[] a, int i)
        => ValueAccessor.AsBool(a.Length > i ? a[i] : false);
}
