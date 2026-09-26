namespace Wpe.Core.Modules;

/// <summary>
/// The engine's exclusive capabilities: at most one selected rule may provide each.
/// Every other name a rule provides is a contribution (an expression function or an
/// effect keyword) and must be unique across rules unless the rule declares an override.
/// This is what makes rules orthogonal: two rules are mutually exclusive only when they
/// provide the same exclusive capability — never merely because they share a category.
/// </summary>
public static class Capabilities
{
    public static readonly IReadOnlyCollection<string> Exclusive = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "map", "counter", "movement", "combat", "dice", "turn",
        "stacking", "scenario", "cards", "zoc", "territory", "supply"
    };

    public static bool IsExclusive(string name) => Exclusive.Contains(name);
}
