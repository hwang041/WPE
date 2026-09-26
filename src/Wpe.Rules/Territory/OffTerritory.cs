using Wpe.Core.Modules;

namespace Wpe.Rules.Territory;

/// <summary>
/// territory / off — no ownership tracking. Registers neutral query functions so games
/// can reference them safely; the `control`/`capture` effects are only provided by
/// territory/controlPoints.
/// </summary>
public sealed class OffTerritory : RuleVariantBase
{
    public override VariantInfo Info => new()
    {
        Subsystem = "territory",
        Id = "off",
        Description = "关闭领地归属：不记录控制权（中性函数）",
        Provides = new[] { "territory" },
        Status = "stable"
    };

    public override void Register(ModuleHost host)
    {
        host.AddFunctionAliases((ctx, a) => "", "hexControl", "hex_control", "controlOf", "control_of");
        host.AddFunctionAliases((ctx, a) => false, "controlledBy", "controlled_by");
        host.AddFunctionAliases((ctx, a) => 0d, "controlledCount", "controlled_count");
    }
}
