using Wpe.Core.Modules;

namespace Wpe.Rules.Zoc;

/// <summary>
/// zoc / off — no zones of control. Registers neutral query functions so games can
/// reference them safely even when ZOC is disabled.
/// </summary>
public sealed class OffZoc : RuleVariantBase
{
    public override VariantInfo Info => new()
    {
        Subsystem = "zoc",
        Id = "off",
        Description = "关闭控制区：无 ZOC（中性函数）",
        Provides = new[] { "zoc" },
        Status = "stable"
    };

    public override void Register(ModuleHost host)
    {
        host.AddFunctionAliases((ctx, a) => false, "inZoc", "in_zoc");
        host.AddFunctionAliases((ctx, a) => -1d, "zocOwner", "zoc_owner");
        host.AddFunctionAliases((ctx, a) => false, "zocProjected", "zoc_projected");
    }
}
