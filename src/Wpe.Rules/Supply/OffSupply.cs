using Wpe.Core.Modules;

namespace Wpe.Rules.Supply;

/// <summary>
/// supply / off — units are always in supply. Registers the neutral query functions so
/// games can reference them without enabling a supply model.
/// </summary>
public sealed class OffSupply : RuleVariantBase
{
    public override VariantInfo Info => new()
    {
        Subsystem = "supply",
        Id = "off",
        Description = "关闭补给子系统：所有单位始终在补给中",
        Provides = Array.Empty<string>(),
        Status = "stable"
    };

    public override void Register(ModuleHost host)
    {
        host.AddFunctionAliases((ctx, a) => true, "inSupply", "in_supply");
        host.AddFunctionAliases((ctx, a) => false, "outOfSupply", "out_of_supply");
        host.AddFunctionAliases((ctx, a) => 0d, "supplyDistance", "supply_distance");
        host.AddFunctionAliases((ctx, a) => true, "isSupplySource", "is_supply_source");
    }
}
