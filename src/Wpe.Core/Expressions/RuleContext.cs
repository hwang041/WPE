using Wpe.Core.Engine;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Core.Expressions;

/// <summary>
/// Evaluation context for game-rule expressions: what counter/target/position are
/// in scope, plus access to the engine (for seam dispatch), host (function registry),
/// and the runtime state. This is the "blackboard" that modules share.
/// </summary>
public sealed class RuleContext : IEvalContext
{
    public GameState State { get; }
    public ModuleHost Host { get; }
    public GameEngine Engine { get; }
    public CounterState? Counter { get; set; }
    public CounterState? TargetCounter { get; set; }
    public HexCoord? TargetPos { get; set; }
    public Dictionary<string, object> Vars { get; } = new();

    public RuleContext(GameState state, ModuleHost host, GameEngine engine)
    {
        State = state;
        Host = host;
        Engine = engine;
    }

    public object? Resolve(string name)
    {
        switch (name.ToLowerInvariant())
        {
            case "me": return (double)State.ActivePlayer;
            case "turn": return (double)State.TurnNumber;
            case "phase": return State.CurrentPhase;
            case "player": return (double)State.ActivePlayer;
            case "counter": return Counter;
            case "target": return (object?)TargetCounter ?? (object?)TargetPos ?? null;
            case "targetcounter": return TargetCounter;
            case "pos": return TargetPos;
            case "state": return State;
            default:
                return Vars.TryGetValue(name, out var v) ? v : null;
        }
    }
}
