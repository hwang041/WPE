using Wpe.Core.Expressions;
using Wpe.Core.Model;

namespace Wpe.Core.Modules;

/// <summary>
/// The engine's built-in generic function set: geometry, counter attributes,
/// effective values (damaged side), math and logic. Independent of any rule variant.
/// </summary>
public static class CoreFunctions
{
    public static void Seed(ModuleHost host)
    {
        // --- geometry / state ---
        host.AddFunction("dist", (ctx, a) => Dist(AsCoord(a, ctx, 0), AsCoord(a, ctx, 1)));
        host.AddFunction("distance", (ctx, a) => Dist(AsCoord(a, ctx, 0), AsCoord(a, ctx, 1)));
        host.AddFunction("adjacent", (ctx, a) => Dist(AsCoord(a, ctx, 0), AsCoord(a, ctx, 1)) == 1);
        host.AddFunction("sameboard", (ctx, a) =>
            C(a, ctx, 0)?.OnBoard == true && C(a, ctx, 1)?.OnBoard == true);
        host.AddFunction("occupied", (ctx, a) =>
            ctx.State.CountersOnBoard().Any(c => c.Hex == AsCoord(a, ctx, 0)));
        host.AddFunction("inBounds", (ctx, a) =>
            ctx.State.Map?.InBounds(AsCoord(a, ctx, 0)) == true);
        host.AddFunction("in_bounds", (ctx, a) =>
            ctx.State.Map?.InBounds(AsCoord(a, ctx, 0)) == true);

        // --- counter attributes ---
        host.AddFunction("hasattr", (ctx, a) =>
            C(a, ctx, 0)?.Attributes.ContainsKey(ArgStr(a, 1)) == true);
        host.AddFunction("attr", (ctx, a) =>
        {
            var c = C(a, ctx, 0);
            var k = ArgStr(a, 1);
            if (c == null || !c.Attributes.TryGetValue(k, out var v)) return a.Length > 2 ? a[2] : 0;
            return v;
        });
        host.AddFunction("owner", (ctx, a) =>
        {
            var c = C(a, ctx, 0);
            return c == null ? -1.0 : (double)c.AttributeInt("owner", -1);
        });
        host.AddFunction("onboard", (ctx, a) => C(a, ctx, 0)?.OnBoard == true);
        host.AddFunction("counterofplayer", (ctx, a) =>
            C(a, ctx, 0)?.AttributeInt("owner", -1) == (int)ValueAccessor.AsNumber(a.Length > 1 ? a[1] : 0));
        host.AddFunction("isback", (ctx, a) => C(a, ctx, 0)?.IsBack == true);
        host.AddFunction("isfront", (ctx, a) => C(a, ctx, 0)?.IsBack == false);
        host.AddFunction("notacted", (ctx, a) => C(a, ctx, 0)?.AttributeInt("acted", 0) == 0);

        // --- effective values (damaged-side penalties) ---
        host.AddFunction("effstr", (ctx, a) =>
        {
            var c = C(a, ctx, 0);
            if (c == null) return 0;
            var p = c.AttributeFloat("penaltyStrength", 0);
            return c.AttributeFloat("strength") - (c.IsBack ? p : 0);
        });
        host.AddFunction("effective_strength", (ctx, a) =>
        {
            var c = C(a, ctx, 0);
            if (c == null) return 0;
            var p = c.AttributeFloat("penaltyStrength", 0);
            return c.AttributeFloat("strength") - (c.IsBack ? p : 0);
        });
        host.AddFunction("effmove", (ctx, a) =>
        {
            var c = C(a, ctx, 0);
            if (c == null) return 0;
            var p = c.AttributeFloat("penaltyMove", 0);
            return c.AttributeFloat("move") - (c.IsBack ? p : 0);
        });
        host.AddFunction("effective_move", (ctx, a) =>
        {
            var c = C(a, ctx, 0);
            if (c == null) return 0;
            var p = c.AttributeFloat("penaltyMove", 0);
            return c.AttributeFloat("move") - (c.IsBack ? p : 0);
        });

        // --- counts ---
        host.AddFunction("enemycount", (ctx, a) =>
        {
            var p = (int)ValueAccessor.AsNumber(a.Length > 0 ? a[0] : 0);
            return (double)ctx.State.CountersOnBoard().Count(c => c.AttributeInt("owner", -1) != p);
        });
        host.AddFunction("friendlycount", (ctx, a) =>
        {
            var p = (int)ValueAccessor.AsNumber(a.Length > 0 ? a[0] : 0);
            return (double)ctx.State.CountersOnBoard().Count(c => c.AttributeInt("owner", -1) == p);
        });

        // --- math / logic ---
        host.AddFunction("abs", (ctx, a) => Math.Abs(N(a, 0)));
        host.AddFunction("floor", (ctx, a) => Math.Floor(N(a, 0)));
        host.AddFunction("ceil", (ctx, a) => Math.Ceiling(N(a, 0)));
        host.AddFunction("round", (ctx, a) => Math.Round(N(a, 0)));
        host.AddFunction("sqrt", (ctx, a) => Math.Sqrt(Math.Max(0, N(a, 0))));
        host.AddFunction("min", (ctx, a) => a.Length > 0 ? a.Min(x => ValueAccessor.AsNumber(x)) : 0);
        host.AddFunction("max", (ctx, a) => a.Length > 0 ? a.Max(x => ValueAccessor.AsNumber(x)) : 0);
        host.AddFunction("len", (ctx, a) => (double)(a.Length > 0 ? (a[0]?.ToString()?.Length ?? 0) : 0));
        host.AddFunction("count", (ctx, a) => (double)(a.Length > 0 ? (a[0]?.ToString()?.Length ?? 0) : 0));
        host.AddFunction("size", (ctx, a) => (double)(a.Length > 0 ? (a[0]?.ToString()?.Length ?? 0) : 0));
        host.AddFunction("if", (ctx, a) => ValueAccessor.AsBool(a.Length > 0 ? a[0] : false) && a.Length > 1 ? a[1] : a.Length > 2 ? a[2] : null);
        host.AddFunction("not", (ctx, a) => !ValueAccessor.AsBool(a.Length > 0 ? a[0] : false));
    }

    private static HexCoord AsCoord(object?[] a, RuleContext ctx, int i)
    {
        if (a.Length <= i) return new HexCoord(0, 0);
        return a[i] switch
        {
            HexCoord c => c,
            CounterState c => c.Hex,
            _ => new HexCoord(0, 0)
        };
    }

    private static int Dist(HexCoord a, HexCoord b) => HexMath.Distance(a, b);

    private static CounterState? C(object?[] a, RuleContext ctx, int i)
        => a.Length > i ? a[i] as CounterState : null;

    private static string ArgStr(object?[] a, int i)
        => a.Length > i ? a[i]?.ToString() ?? "" : "";

    private static double N(object?[] a, int i)
        => ValueAccessor.AsNumber(a.Length > i ? a[i] : 0);
}
