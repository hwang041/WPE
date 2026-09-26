using Wpe.Core.Definition;

namespace Wpe.Core.Model;

/// <summary>
/// Complete runtime state of a game: counters, map, turn/phase, per-game variables,
/// log and last dice. Supports deep snapshot (Clone) for undo / replays / AI.
/// </summary>
public sealed class GameState
{
    public GameDefinition Def { get; }
    public IMap? Map { get; set; }
    public List<CounterState> Counters { get; } = new();

    /// <summary>All card instances (deck/hand/discard/played) for card-driven games.</summary>
    public List<CardState> Cards { get; } = new();

    public int ActivePlayer { get; set; }
    public int TurnNumber { get; set; } = 1;
    public string CurrentPhase { get; set; } = "";

    public bool GameOver { get; set; }
    public string? ResultMessage { get; set; }

    /// <summary>Game-defined variables (victory points, flag markers, ...).</summary>
    public Dictionary<string, object> Vars { get; } = new();

    /// <summary>Persistent territory map: cell key -> controlling faction/owner (see
    /// <see cref="IMap.CellKey"/>). Populated by the `territory` capability, read by
    /// victory/scenario rules.</summary>
    public Dictionary<string, string> Territory { get; } = new();

    public List<string> Log { get; } = new();
    public List<DieResult> LastDice { get; } = new();

    public int PlayerCount => Def.PlayerCount;

    public GameState(GameDefinition def) => Def = def;

    public IEnumerable<CounterState> CountersOnBoard() => Counters.Where(c => c.OnBoard);

    public IEnumerable<CardState> HandOf(int player)
        => Cards.Where(c => c.Owner == player && c.Zone == CardZone.Hand);

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
        foreach (var (k, v) in Territory) c.Territory[k] = v;
        c.Log.AddRange(Log);
        c.LastDice.AddRange(LastDice);
        foreach (var card in Cards)
            c.Cards.Add(new CardState { Id = card.Id, DefId = card.DefId, Deck = card.Deck, Owner = card.Owner, Zone = card.Zone });
        foreach (var s in Counters)
        {
            var cc = new CounterState { Id = s.Id, Side = s.Side, Position = s.Position, RotationDeg = s.RotationDeg };
            foreach (var (k, v) in s.Attributes) cc.Attributes[k] = v;
            c.Counters.Add(cc);
        }
        return c;
    }
}
