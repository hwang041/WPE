using Wpe.Core.Modules;

namespace Wpe.Rules.Control;

/// <summary>
/// control / off — neutral control subsystem: no zones of control, no ownership tracking.
/// Registers the neutral query functions so games can reference them safely even when the
/// subsystem is disabled.
/// </summary>
public sealed class OffControl : RuleVariantBase
{
    public override VariantInfo Info => new()
    {
        Subsystem = "control",
        Id = "off",
        Description = "关闭控制子系统：无 ZOC、无归属跟踪（中性函数）",
        Provides = Array.Empty<string>(),
        Status = "stable"
    };

    public override void Register(ModuleHost host)
    {
        host.AddFunctionAliases((ctx, a) => false, "inZoc", "in_zoc");
        host.AddFunctionAliases((ctx, a) => -1d, "zocOwner", "zoc_owner");
        host.AddFunctionAliases((ctx, a) => false, "zocProjected", "zoc_projected");
    }
}
