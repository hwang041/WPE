using Wpe.Core.Modules;
using Wpe.Rules.Combat;
using Wpe.Rules.Counter;
using Wpe.Rules.Dice;
using Wpe.Rules.Map;
using Wpe.Rules.Movement;
using Wpe.Rules.Scenario;
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
                ["victory"] = "vpAndSudden"
            }
        };

    /// <summary>Load order for Apply (state-dependent variants first, config merges after).</summary>
    public static readonly string[] ApplyOrder =
        { "counter", "scenario", "movement" };

    public static IRuleVariant? Create(string subsystem, string variantId)
    {
        return (subsystem, variantId) switch
        {
            ("map", "hexGrid") => new HexGrid(),
            ("counter", "generic") => new GenericCounter(),
            ("scenario", "generic") => new GenericScenario(),
            ("movement", "movePoints") => new MovePoints(),
            ("combat", "crTable") => new CrTable(),
            ("dice", "d6") => new D6(),
            ("turn", "phases") => new Phases(),
            ("victory", "vpAndSudden") => new VpAndSudden(),
            _ => null
        };
    }

    /// <summary>All catalogued variants (for `wpe rule list`).</summary>
    public static IEnumerable<VariantInfo> List()
    {
        foreach (var subsystem in Presets[Default].Keys)
        {
            var id = Presets[Default][subsystem];
            var v = Create(subsystem, id);
            if (v != null) yield return v.Info;
        }
    }
}
