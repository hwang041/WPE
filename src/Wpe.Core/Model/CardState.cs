namespace Wpe.Core.Model;

/// <summary>Where a card currently lives. Generic card-zone model shared by all games.</summary>
public enum CardZone
{
    Deck,
    Hand,
    Discard,
    Played,
    Removed
}

/// <summary>
/// Runtime state of one card instance. The card's behavior (action value / event effects)
/// is defined by its <c>CardDef</c> in the game package — this type stays game-agnostic.
/// </summary>
public sealed class CardState
{
    public int Id { get; init; }
    public string DefId { get; set; } = "";
    /// <summary>Deck id this card was built from.</summary>
    public string Deck { get; set; } = "";
    /// <summary>Owning player, or -1 while in the deck.</summary>
    public int Owner { get; set; } = -1;
    public CardZone Zone { get; set; } = CardZone.Deck;
}
