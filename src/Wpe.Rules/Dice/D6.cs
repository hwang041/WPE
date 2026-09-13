using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Dice;

/// <summary>
/// dice / d6 — a straight d6 (or dN) roll per RollSpec.
/// </summary>
public sealed class D6 : IRuleVariant, IDiceModule
{
    public VariantInfo Info => new()
    {
        Subsystem = "dice",
        Id = "d6",
        Description = "标准 dN 骰子",
        Status = "stable"
    };

    public void Load(string? configJson, GameDefinition def) { }
    public void Register(ModuleHost host) => host.Dice = this;
    public void Apply(GameState state, GameEngine? engine) { }
    public void Validate(GameDefinition def, List<string> issues) { }

    public int[] Roll(RollSpec spec, int seed)
        => new SeededRandom(seed).RollDice(Math.Max(1, spec.Count), Math.Max(1, spec.Sides));
}
