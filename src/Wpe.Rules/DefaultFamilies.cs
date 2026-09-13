using Wpe.Core.Modules;
using Wpe.Rules.Cards;
using Wpe.Rules.Combat;
using Wpe.Rules.Counter;
using Wpe.Rules.Dice;
using Wpe.Rules.Map;
using Wpe.Rules.Movement;
using Wpe.Rules.Scenario;
using Wpe.Rules.Stacking;
using Wpe.Rules.Turn;
using Wpe.Rules.Victory;

namespace Wpe.Rules;

/// <summary>
/// The built-in rule library catalog: the set of mature, reliable variants the middle
/// platform ships with. `default` is the recommended family preset; games pick and
/// choose by overriding individual subsystems in game.json.
/// </summary>
public static class DefaultFamilies
{
    public const string Default = "default";

    /// <summary>Family preset -> subsystem -> variant id.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Presets =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            [Default] = new Dictionary<string, string>
            {
                ["map"] = "hexGrid",
                ["counter"] = "generic",
                ["scenario"] = "generic",
                ["movement"] = "movePoints",
                ["combat"] = "crTable",
                ["dice"] = "d6",
                ["turn"] = "phases",
                ["victory"] = "vpAndSudden",
                ["stacking"] = "unlimited"
            }
        };

    /// <summary>Load order for Apply (state-dependent variants first, config merges after).</summary>
    public static readonly string[] ApplyOrder =
        { "map", "counter", "scenario", "cards", "movement" };

    public static IRuleVariant? Create(string subsystem, string variantId)
    {
        return (subsystem, variantId) switch
        {
            ("map", "hexGrid") => new HexGrid(),
            ("map", "pointToPoint") => new PointToPoint(),
            ("counter", "generic") => new GenericCounter(),
            ("scenario", "generic") => new GenericScenario(),
            ("movement", "movePoints") => new MovePoints(),
            ("movement", "roadNetwork") => new RoadNetwork(),
            ("combat", "crTable") => new CrTable(),
            ("dice", "d6") => new D6(),
            ("turn", "phases") => new Phases(),
            ("victory", "vpAndSudden") => new VpAndSudden(),
            ("cards", "standard") => new StandardDeck(),
            ("stacking", "unlimited") => new UnlimitedStacking(),
            ("stacking", "perHex") => new PerHexStacking(),
            _ => null
        };
    }

    /// <summary>Every catalogued variant, preset and optional (for `wpe rule list`).</summary>
    private static readonly (string subsystem, string id)[] Catalog =
    {
        ("map", "hexGrid"), ("map", "pointToPoint"),
        ("counter", "generic"), ("scenario", "generic"),
        ("movement", "movePoints"), ("movement", "roadNetwork"),
        ("combat", "crTable"), ("dice", "d6"),
        ("turn", "phases"), ("victory", "vpAndSudden"),
        ("cards", "standard"), ("stacking", "unlimited"), ("stacking", "perHex")
    };

    /// <summary>All catalogued variants (for `wpe rule list`).</summary>
    public static IEnumerable<VariantInfo> List()
    {
        foreach (var (subsystem, id) in Catalog)
        {
            var v = Create(subsystem, id);
            if (v != null) yield return v.Info;
        }
    }
}
