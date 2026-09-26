using Wpe.Core.Definition;
using Wpe.Core.Expressions;
using Wpe.Core.Model;

namespace Wpe.Core.Modules;

/// <summary>
/// The engine's built-in generic function set: geometry, counter attributes,
/// effective values (damaged side), math and logic. Independent of any rule variant.
/// </summary>
public static class CoreFunctions
{
    public static void Seed(ModuleHost host, GameDefinition def)
    {
        // --- geometry / state ---
        host.AddFunction("dist", (ctx, a) => (double)Dist(ctx, ExprArgs.Coord(a, 0), ExprArgs.Coord(a, 1)));
        host.AddFunction("distance", (ctx, a) => (double)Dist(ctx, ExprArgs.Coord(a, 0), ExprArgs.Coord(a, 1)));
        host.AddFunction("adjacent", (ctx, a) => Dist(ctx, ExprArgs.Coord(a, 0), ExprArgs.Coord(a, 1)) == 1);
        host.AddFunction("sameboard", (ctx, a) =>
            ExprArgs.Counter(a, 0)?.OnBoard == true && ExprArgs.Counter(a, 1)?.OnBoard == true);
        host.AddFunction("occupied", (ctx, a) =>
            ctx.State.CountersOnBoard().Any(c => c.Hex == ExprArgs.Coord(a, 0)));
        host.AddFunction("inBounds", (ctx, a) =>
            ctx.State.Map?.InBounds(ExprArgs.Coord(a, 0)) == true);
        host.AddFunction("in_bounds", (ctx, a) =>
            ctx.State.Map?.InBounds(ExprArgs.Coord(a, 0)) == true);

        // --- counter attributes ---
        host.AddFunction("hasattr", (ctx, a) =>
            ExprArgs.Counter(a, 0)?.Attributes.ContainsKey(ExprArgs.Str(a, 1)) == true);
        host.AddFunction("attr", (ctx, a) =>
        {
            var c = ExprArgs.Counter(a, 0);
            var k = ExprArgs.Str(a, 1);
            if (c == null || !c.Attributes.TryGetValue(k, out var v)) return a.Length > 2 ? a[2] : 0;
            return v;
        });
        host.AddFunction("owner", (ctx, a) =>
        {
            var c = ExprArgs.Counter(a, 0);
            return c == null ? -1.0 : (double)c.AttributeInt(ContractNames.Owner, -1);
        });
        host.AddFunction("onboard", (ctx, a) => ExprArgs.Counter(a, 0)?.OnBoard == true);
        host.AddFunction("counterofplayer", (ctx, a) =>
            ExprArgs.Counter(a, 0)?.AttributeInt(ContractNames.Owner, -1) == (int)ExprArgs.Number(a, 1));
        host.AddFunction("isback", (ctx, a) => ExprArgs.Counter(a, 0)?.IsBack == true);
        host.AddFunction("isfront", (ctx, a) => ExprArgs.Counter(a, 0)?.IsBack == false);
        host.AddFunction("notacted", (ctx, a) => ExprArgs.Counter(a, 0)?.AttributeInt(def.ActedAttr, 0) == 0);

        // --- effective values (effStr/effMove) are provided by the counter subsystem,
        //     which owns the unit model (binary front/back, multi-step, damage track). ---

        // --- counts ---
        host.AddFunction("enemycount", (ctx, a) =>
        {
            var p = (int)ExprArgs.Number(a, 0);
            return (double)ctx.State.CountersOnBoard().Count(c => c.AttributeInt(ContractNames.Owner, -1) != p);
        });
        host.AddFunction("friendlycount", (ctx, a) =>
        {
            var p = (int)ExprArgs.Number(a, 0);
            return (double)ctx.State.CountersOnBoard().Count(c => c.AttributeInt(ContractNames.Owner, -1) == p);
        });

        // --- math / logic ---
        host.AddFunction("abs", (ctx, a) => Math.Abs(ExprArgs.Number(a, 0)));
        host.AddFunction("floor", (ctx, a) => Math.Floor(ExprArgs.Number(a, 0)));
        host.AddFunction("ceil", (ctx, a) => Math.Ceiling(ExprArgs.Number(a, 0)));
        host.AddFunction("round", (ctx, a) => Math.Round(ExprArgs.Number(a, 0)));
        host.AddFunction("sqrt", (ctx, a) => Math.Sqrt(Math.Max(0, ExprArgs.Number(a, 0))));
        host.AddFunction("min", (ctx, a) => a.Length > 0 ? a.Min(x => ValueAccessor.AsNumber(x)) : 0);
        host.AddFunction("max", (ctx, a) => a.Length > 0 ? a.Max(x => ValueAccessor.AsNumber(x)) : 0);
        host.AddFunction("len", (ctx, a) => (double)(a.Length > 0 ? (a[0]?.ToString()?.Length ?? 0) : 0));
        host.AddFunction("count", (ctx, a) => (double)(a.Length > 0 ? (a[0]?.ToString()?.Length ?? 0) : 0));
        host.AddFunction("size", (ctx, a) => (double)(a.Length > 0 ? (a[0]?.ToString()?.Length ?? 0) : 0));
        host.AddFunction("if", (ctx, a) => ValueAccessor.AsBool(a.Length > 0 ? a[0] : false) && a.Length > 1 ? a[1] : a.Length > 2 ? a[2] : null);
        host.AddFunction("not", (ctx, a) => !ValueAccessor.AsBool(a.Length > 0 ? a[0] : false));
    }

    private static int Dist(RuleContext ctx, HexCoord a, HexCoord b)
        => ctx.State.Map?.Distance(a, b) ?? HexMath.Distance(a, b);
}
