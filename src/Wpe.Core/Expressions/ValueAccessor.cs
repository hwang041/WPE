using Wpe.Core.Model;

namespace Wpe.Core.Expressions;

/// <summary>Value environment that an expression is evaluated against.</summary>
public interface IEvalContext
{
    object? Resolve(string name);
}

/// <summary>Resolve a property access on a value (counter.attack, pos.q, ...).</summary>
public static class ValueAccessor
{
    public static object? GetProperty(object? value, string name)
    {
        switch (value)
        {
            case null:
                return null;
            case System.Collections.IDictionary dict:
                return dict.Contains(name) ? dict[name] : null;
            case CounterState c:
                return GetCounterProperty(c, name);
            case HexCoord p:
                return name.ToLowerInvariant() switch
                {
                    "q" => (double)p.Q,
                    "r" => (double)p.R,
                    _ => null
                };
            case GameState g:
                return name.ToLowerInvariant() switch
                {
                    "turn" => (double)g.TurnNumber,
                    "phase" => g.CurrentPhase,
                    "activeplayer" => (double)g.ActivePlayer,
                    "playercount" => (double)g.PlayerCount,
                    "vars" => g.Vars,
                    _ => g.Vars.TryGetValue(name, out var vv) ? vv : null
                };
            default:
                return null;
        }
    }

    private static object? GetCounterProperty(CounterState c, string name)
    {
        switch (name.ToLowerInvariant())
        {
            case "id": return (double)c.Id;
            case "q": return (double)c.Hex.Q;
            case "r": return (double)c.Hex.R;
            case "side": return c.Side.ToString().ToLowerInvariant();
            case "front": return c.Side == Side.Front;
            case "back": return c.Side == Side.Back;
            case "name": return c.Name;
            default:
                return c.Attributes.TryGetValue(name, out var v) ? v : null;
        }
    }

    public static double AsNumber(object? v)
    {
        return v switch
        {
            null => 0,
            double d => d,
            int i => i,
            float f => f,
            long l => l,
            bool b => b ? 1 : 0,
            string s => double.TryParse(s, out var d2) ? d2 : 0,
            _ => 0
        };
    }

    public static bool AsBool(object? v) => AsNumber(v) != 0;
}
