namespace Wpe.Core.Definition;

/// <summary>
/// A game's complete rule definition, deserialized from game.json (the "main rules"
/// config the designer fills first). It declares which rule variants to use, points
/// at the table files (combat/movement/map/units/scenario), and carries the core
/// orchestration data: phases, moves, triggers, end conditions, damage, turn reset.
/// </summary>
public sealed class GameDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int PlayerCount { get; set; } = 2;

    /// <summary>Family preset that supplies default variant choices (e.g. "default").</summary>
    public string Family { get; set; } = "default";

    /// <summary>Per-subsystem variant selection + config file pointers (overrides the family).</summary>
    public Dictionary<string, VariantSelection> Rules { get; set; } = new();

    /// <summary>Cycle of phases that repeat for each player's turn.</summary>
    public List<string> PhaseOrder { get; set; } = new();

    public Dictionary<string, MoveDef> Moves { get; set; } = new();
    public Dictionary<string, TriggerDef> Triggers { get; set; } = new();
    public List<EndDef> EndConditions { get; set; } = new();
    public List<TurnResetDef> TurnReset { get; set; } = new();
    public DamageConfig Damage { get; set; } = new();
    public bool AutoActWhenExhausted { get; set; }

    /// <summary>Optional per-game C# hook (assembly-qualified type name implementing IHook).</summary>
    public string? HookType { get; set; }
}

/// <summary>Selects a rule variant for one subsystem and (optionally) points at its config file.</summary>
public sealed class VariantSelection
{
    public string Variant { get; set; } = "";
    public string? File { get; set; }
    /// <summary>Inline config object (takes precedence over File when present).</summary>
    public Dictionary<string, object?>? Config { get; set; }
}

public sealed class TurnResetDef
{
    /// <summary>Attribute to set on every unit of the acting player.</summary>
    public string Attr { get; set; } = "";
    /// <summary>Literal number, "expr:..." expression, or name of another attribute to copy.</summary>
    public string Value { get; set; } = "0";
}

public sealed class DamageConfig
{
    public double StrengthPenalty { get; set; } = 1;
    public double MovePenalty { get; set; } = 1;
}

public sealed class MoveDef
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>Interaction kind: "movement" | "combat" | "recover" | "pass" | "endphase" | ""</summary>
    public string Kind { get; set; } = "";
    /// <summary>Phase this move is available in; empty = any phase.</summary>
    public string? Phase { get; set; }
    public bool NeedsCounter { get; set; }
    public bool NeedsPosition { get; set; }
    public bool NeedsTargetCounter { get; set; }
    public string? CounterFilter { get; set; }
    public string? TargetFilter { get; set; }
    public List<string> Validators { get; set; } = new();
    public bool IsEndAction { get; set; }
    public RollSpec? Roll { get; set; }
    /// <summary>Combat definition id (from combat.json).</summary>
    public string? Combat { get; set; }
    public List<EffectDef> Effects { get; set; } = new();
    /// <summary>Maps a table result code to effects (e.g. "Adr" -> [...]).</summary>
    public Dictionary<string, List<EffectDef>> ResultEffects { get; set; } = new();
}

public sealed class RollSpec
{
    public string Var { get; set; } = "roll";
    public int Count { get; set; } = 1;
    public int Sides { get; set; } = 6;
    public int Hand { get; set; } = 0;
    public string? Drm { get; set; }
}

public sealed class CombatDef
{
    public string RowExpr { get; set; } = "";
    public string ColExpr { get; set; } = "";
    public string ResultVar { get; set; } = "result";
    public TableDef? Table { get; set; }
}

public sealed class TableDef
{
    public string Id { get; set; } = "";
    public List<TableRowDef> Rows { get; set; } = new();
}

public sealed class TableRowDef
{
    public double Min { get; set; }
    public double Max { get; set; }
    public List<TableColumnDef> Columns { get; set; } = new();
}

public sealed class TableColumnDef
{
    public double Min { get; set; }
    public double Max { get; set; }
    public string Result { get; set; } = "";
}

public sealed class EffectDef
{
    public string Effect { get; set; } = "";
    /// <summary>Which counter the effect applies to: "counter" or "target".</summary>
    public string Counter { get; set; } = "counter";
    /// <summary>Optional gate expression; the effect is skipped when it evaluates false.</summary>
    public string When { get; set; } = "";
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string X { get; set; } = "";
    public string Y { get; set; } = "";
    /// <summary>For "move": "pos" uses the action target, else {x,y} expressions become a hex.</summary>
    public string To { get; set; } = "pos";
    public string Text { get; set; } = "";
    /// <summary>For "retreat": number of hexes to fall back away from the enemy.</summary>
    public int Hexes { get; set; } = 1;
    /// <summary>For "retreat": behavior when no retreat hex exists ("none"/"flip"/"remove").</summary>
    public string? OnFail { get; set; }
}

public sealed class TriggerDef
{
    public string On { get; set; } = "";
    public string? Phase { get; set; }
    public List<EffectDef> Do { get; set; } = new();
}

public sealed class EndDef
{
    public string When { get; set; } = "";
    public string Message { get; set; } = "";
    public int? Winner { get; set; }
}
