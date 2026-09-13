namespace Wpe.Core.Model;

/// <summary>
/// Runtime state of one counter (unit). Attributes are a free dictionary whose keys
/// are defined by the game package (and constrained by the attribute contract).
/// Position is null while the counter is off-board (reserve/reinforcement tray).
/// </summary>
public sealed class CounterState
{
    public int Id { get; init; }
    public Side Side { get; set; } = Side.Front;
    public HexCoord? Position { get; set; }
    /// <summary>Rotation in degrees (e.g. 45° marks a unit as "acted").</summary>
    public float RotationDeg { get; set; }
    public Dictionary<string, object> Attributes { get; } = new();

    public bool OnBoard => Position.HasValue;
    public bool IsBack => Side == Side.Back;

    public string Name
    {
        get => AttributeStr("name");
        set => Attributes["name"] = value;
    }

    public HexCoord Hex => Position ?? new HexCoord(0, 0);

    public float AttributeFloat(string key, float defaultValue = 0f)
        => Attributes.TryGetValue(key, out var v) && float.TryParse(v?.ToString(), out var f) ? f : defaultValue;

    public int AttributeInt(string key, int defaultValue = 0)
        => Attributes.TryGetValue(key, out var v) && int.TryParse(v?.ToString(), out var i) ? i : defaultValue;

    public string AttributeStr(string key, string defaultValue = "")
        => Attributes.TryGetValue(key, out var v) ? v?.ToString() ?? defaultValue : defaultValue;

    public override string ToString()
        => $"[{Id}] {Name} @{(OnBoard ? $"({Hex.Q},{Hex.R})" : "off-board")} {Side}";
}
