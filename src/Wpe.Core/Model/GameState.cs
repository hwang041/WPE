using Wpe.Core.Definition;

namespace Wpe.Core.Model;

/// <summary>
/// Complete runtime state of a game: counters, map, turn/phase, per-game variables,
/// log and last dice. Supports deep snapshot (Clone) for undo / replays / AI.
/// </summary>
public sealed class GameState
{
    public GameDefinition Def { get; }
    public GridMap? Map { get; set; }
    public List<CounterState> Counters { get; } = new();

    public int ActivePlayer { get; set; }
    public int TurnNumber { get; set; } = 1;
    public string CurrentPhase { get; set; } = "";

    public bool GameOver { get; set; }
    public string? ResultMessage { get; set; }

    /// <summary>Game-defined variables (victory points, flag markers, ...).</summary>
    public Dictionary<string, object> Vars { get; } = new();

    public List<string> Log { get; } = new();
    public List<DieResult> LastDice { get; } = new();

    public int PlayerCount => Def.PlayerCount;

    public GameState(GameDefinition def) => Def = def;

    public IEnumerable<CounterState> CountersOnBoard() => Counters.Where(c => c.OnBoard);

    public CounterState? CounterAt(HexCoord c) => Counters.FirstOrDefault(x => x.OnBoard && x.Hex == c);

    public void LogMessage(string message)
        => Log.Add($"[T{TurnNumber} P{ActivePlayer + 1} {CurrentPhase}] {message}");

    /// <summary>Deep snapshot; Map/Def are shared by reference (immutable after load).</summary>
    public GameState Clone()
    {
        var c = new GameState(Def)
        {
            Map = Map,
            ActivePlayer = ActivePlayer,
            TurnNumber = TurnNumber,
            CurrentPhase = CurrentPhase,
            GameOver = GameOver,
            ResultMessage = ResultMessage
        };
        foreach (var (k, v) in Vars) c.Vars[k] = v;
        c.Log.AddRange(Log);
        c.LastDice.AddRange(LastDice);
        foreach (var s in Counters)
        {
            var cc = new CounterState { Id = s.Id, Side = s.Side, Position = s.Position, RotationDeg = s.RotationDeg };
            foreach (var (k, v) in s.Attributes) cc.Attributes[k] = v;
            c.Counters.Add(cc);
        }
        return c;
    }
}
