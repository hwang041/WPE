using Wpe.Core.Definition;
using Wpe.Core.Expressions;
using Wpe.Core.Modules;

namespace Wpe.Rules.Victory;

/// <summary>
/// victory / attrition — contributes `lostStrength(player)` (a side's own losses) and
/// `destroyedStrength(player)` (losses it has inflicted). Units must be removed with the
/// core `eliminate` effect (which marks them). End conditions are evaluated by the engine core.
/// </summary>
public sealed class Attrition : RuleVariantBase
{
    public override VariantInfo Info => new()
    {
        Subsystem = "victory",
        Id = "attrition",
        Description = "战损：按歼灭标记统计双方损失",
        Requires = new[] { "counter" },
        Provides = new[] { "destroyedStrength", "lostStrength" },
        Status = "stable"
    };

    public override void Register(ModuleHost host)
    {
        host.AddFunctionAliases((ctx, a) => (double)LostStrength(ctx, (int)ExprArgs.Number(a, 0)),
            "lostStrength", "lost_strength");
        host.AddFunctionAliases((ctx, a) => (double)DestroyedStrength(ctx, (int)ExprArgs.Number(a, 0)),
            "destroyedStrength", "destroyed_strength", "enemyLostStrength");
    }

    public override void Validate(GameDefinition def, List<string> issues)
    {
        if (def.EndConditions.Count == 0)
            issues.Add("[victory] 未配置 endConditions，游戏将无法正常结束");
    }

    private static double LostStrength(RuleContext ctx, int player)
        => ctx.State.Counters
            .Where(c => c.AttributeInt(ContractNames.Eliminated, 0) == 1 &&
                        c.AttributeInt(ContractNames.Owner, -1) == player)
            .Sum(c => (double)c.AttributeFloat(ContractNames.Strength));

    private static double DestroyedStrength(RuleContext ctx, int player)
        => ctx.State.Counters
            .Where(c => c.AttributeInt(ContractNames.Eliminated, 0) == 1 &&
                        c.AttributeInt(ContractNames.Owner, -1) != player)
            .Sum(c => (double)c.AttributeFloat(ContractNames.Strength));
}
