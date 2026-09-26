namespace Wpe.Core.Definition;

/// <summary>
/// The single place where the engine's well-known attribute names are spelled. These are
/// part of the cross-variant attribute contract (see docs/属性与数据契约.md) — the "ABI"
/// that map/movement/combat/turn/victory all rely on. Keep literals out of engine/variant
/// code and reference these instead, so the contract has one source of truth.
/// </summary>
public static class ContractNames
{
    public const string Name = "name";
    public const string Owner = "owner";
    public const string Strength = "strength";
    public const string Move = "move";
    public const string MoveLeft = "moveLeft";
    public const string Acted = "acted";
    public const string Faction = "faction";
    public const string Type = "type";
    public const string Key = "key";
    public const string Color = "color";
    public const string PenaltyStrength = "penaltyStrength";
    public const string PenaltyMove = "penaltyMove";
    public const string EntryTurn = "entryTurn";
    public const string EntryHex = "entryHex";
    public const string Node = "node";

    // step loss / damage models (counter subsystem variants)
    public const string Steps = "steps";
    public const string MaxSteps = "maxSteps";
    public const string Damage = "damage";

    // unit status flags written by generic effects / subsystems
    public const string Eliminated = "eliminated";
    public const string Entrenched = "entrenched";
    public const string Exited = "exited";

    // supply subsystem
    public const string InSupply = "inSupply";
}
