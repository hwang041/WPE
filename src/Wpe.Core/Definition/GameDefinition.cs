using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wpe.Core.Definition;

/// <summary>A resolved rule instance: which variant to load and where its config lives.</summary>
public sealed class RuleSelection
{
    /// <summary>Category (the key under `rules` in game.json / the family preset).</summary>
    public string Category { get; init; } = "";
    public string Variant { get; init; } = "";
    public string? File { get; init; }
    public Dictionary<string, object?>? Config { get; init; }
}

/// <summary>
/// Reads `game.json` `rules`, where each category value is either a single selection
/// object (backward compatible) or an array of them (several orthogonal rules in one
/// category, e.g. two victory rules).
/// </summary>
public sealed class RuleSetConverter : JsonConverter<Dictionary<string, List<VariantSelection>>>
{
    public override Dictionary<string, List<VariantSelection>> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("[game.json] rules 必须是对象");
        var result = new Dictionary<string, List<VariantSelection>>();
        using var doc = JsonDocument.ParseValue(ref reader);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var list = new List<VariantSelection>();
            switch (prop.Value.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var el in prop.Value.EnumerateArray())
                    {
                        var v = el.Deserialize<VariantSelection>(options);
                        if (v != null) list.Add(v);
                    }
                    break;
                case JsonValueKind.Object:
                    var single = prop.Value.Deserialize<VariantSelection>(options);
                    if (single != null) list.Add(single);
                    break;
            }
            result[prop.Name] = list;
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, List<VariantSelection>> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (category, list) in value)
        {
            writer.WritePropertyName(category);
            if (list.Count == 1)
            {
                JsonSerializer.Serialize(writer, list[0], options);
            }
            else
            {
                writer.WriteStartArray();
                foreach (var v in list) JsonSerializer.Serialize(writer, v, options);
                writer.WriteEndArray();
            }
        }
        writer.WriteEndObject();
    }
}

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

    /// <summary>Per-category rule selection + config file pointers (overrides the family).
    /// Each value may be a single selection or an array of orthogonal rules.</summary>
    [JsonConverter(typeof(RuleSetConverter))]
    public Dictionary<string, List<VariantSelection>> Rules { get; set; } = new();

    /// <summary>Cycle of phases that repeat for each player's turn.</summary>
    public List<string> PhaseOrder { get; set; } = new();

    public Dictionary<string, MoveDef> Moves { get; set; } = new();
    public Dictionary<string, TriggerDef> Triggers { get; set; } = new();
    public List<EndDef> EndConditions { get; set; } = new();
    public List<TurnResetDef> TurnReset { get; set; } = new();
    public DamageConfig Damage { get; set; } = new();
    public bool AutoActWhenExhausted { get; set; }

    /// <summary>
    /// Phase whose start triggers a per-turn reset (turnReset). Defaults to "action" so
    /// existing packages keep working; games with other phase names can override it.
    /// </summary>
    public string TurnResetPhase { get; set; } = "action";

    /// <summary>Attribute marking a counter as having acted this turn (engine control flow).</summary>
    public string ActedAttr { get; set; } = ContractNames.Acted;

    /// <summary>When true (default), a counter on its damaged/back side cannot move.</summary>
    public bool MoveBlockedWhenBack { get; set; } = true;

    /// <summary>Optional UI labels for phases (phase id -> display text); renderers fall back to the raw id.</summary>
    public Dictionary<string, string> PhaseLabels { get; set; } = new();

    /// <summary>Optional state-variable keys to surface on the GUI status bar (e.g. victory points, activations).</summary>
    public List<string> HudVars { get; set; } = new();

    /// <summary>Faction display palette: faction key -> color/label (used by renderers; keeps games' factions out of the shared code).</summary>
    public Dictionary<string, FactionDef> Factions { get; set; } = new();

    /// <summary>Node-type display styles for point-to-point maps: type key -> shape/scale/short label.</summary>
    public Dictionary<string, NodeTypeDef> NodeTypes { get; set; } = new();

    /// <summary>Effects run once when the engine starts (e.g. deal opening hands).</summary>
    public List<EffectDef> Setup { get; set; } = new();

    /// <summary>Seed for reproducible shuffles/dice (replay, AI, tests).</summary>
    public int Seed { get; set; } = 20240913;

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
    /// <summary>Whether the move takes a card from the player's hand (card-driven play).</summary>
    public bool NeedsCard { get; set; }
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
    /// <summary>oddsShift: expressions summed into the die column before the table lookup
    /// (terrain, combined arms, supply, flanking modifiers).</summary>
    public List<string> Shifts { get; set; } = new();
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
    /// <summary>For card effects ("draw"/"shuffle"): which deck.</summary>
    public string Deck { get; set; } = "";
    /// <summary>For card effects ("draw"/"roll"): how many cards / dice.</summary>
    public int Count { get; set; } = 1;
    /// <summary>For the "roll" effect: number of sides.</summary>
    public int Sides { get; set; } = 6;
    /// <summary>For card effects ("draw"): which player (expression, e.g. "me"/"0"). Empty = active player.</summary>
    public string Player { get; set; } = "";
}

/// <summary>A card definition: an action (numeric value) or an event (data-driven effects).</summary>
public sealed class CardDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>"action" (value used by the card-driven rule) or "event" (effects resolved on play).</summary>
    public string Kind { get; set; } = "action";
    public double Value { get; set; }
    public string? Text { get; set; }
    /// <summary>Effects executed when the card is played (event cards; action cards may set activation vars too).</summary>
    public List<EffectDef> Effects { get; set; } = new();
}

/// <summary>A deck built from a list of (card id, count) entries.</summary>
public sealed class DeckDef
{
    public string Id { get; set; } = "";
    public List<DeckEntryDef> Entries { get; set; } = new();
}

public sealed class DeckEntryDef
{
    public string Card { get; set; } = "";
    public int Count { get; set; } = 1;
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

/// <summary>Display metadata for a faction (colour + human label). Purely presentational.</summary>
public sealed class FactionDef
{
    /// <summary>"#RRGGBB" colour string.</summary>
    public string Color { get; set; } = "";
    public string Label { get; set; } = "";
}

/// <summary>Display style for a point-to-point node type (shape glyph, scale, short label).</summary>
public sealed class NodeTypeDef
{
    /// <summary>"castle" | "gate" | "anchor" | "dot".</summary>
    public string Shape { get; set; } = "dot";
    public float Scale { get; set; } = 1f;
    /// <summary>Short suffix appended to the node name (e.g. "州").</summary>
    public string Short { get; set; } = "";
}
