using Wpe.Core.Definition;
using Wpe.Core.Expressions;
using Wpe.Core.Modules;

namespace Wpe.Rules.Victory;

/// <summary>
/// victory / exit — contributes the `exit` effect (a unit leaves the map for victory
/// points) and `exitedStrength(player)`. End conditions are evaluated by the engine core.
/// </summary>
public sealed class Exit : RuleVariantBase
{
    public override VariantInfo Info => new()
    {
        Subsystem = "victory",
        Id = "exit",
        Description = "退场：exit 效果离场并计数，exitedStrength 统计",
        Provides = new[] { "exit", "exitedStrength" },
        Status = "stable"
    };

    public override void Register(ModuleHost host)
    {
        host.AddEffect("exit", ExitEffect);
        host.AddFunctionAliases((ctx, a) => (double)ExitedStrength(ctx, (int)ExprArgs.Number(a, 0)),
            "exitedStrength", "exited_strength");
    }

    public override void Validate(GameDefinition def, List<string> issues)
    {
        if (def.EndConditions.Count == 0)
            issues.Add("[victory] 未配置 endConditions，游戏将无法正常结束");
    }

    private static void ExitEffect(RuleContext ctx, EffectDef e)
    {
        var c = e.Counter == "target" ? ctx.TargetCounter : ctx.Counter;
        if (c == null) return;
        c.Position = null;
        c.Attributes[ContractNames.Exited] = 1;
        ctx.State.LogMessage($"{c.Name} 退场");
    }

    private static double ExitedStrength(RuleContext ctx, int player)
        => ctx.State.Counters
            .Where(c => c.AttributeInt(ContractNames.Exited, 0) == 1 &&
                        c.AttributeInt(ContractNames.Owner, -1) == player)
            .Sum(c => (double)c.AttributeFloat(ContractNames.Strength));
}
