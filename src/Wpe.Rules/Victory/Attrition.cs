using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Expressions;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Victory;

/// <summary>
/// victory / attrition — victory points from casualties. Requires counters to be removed
/// with the generic `eliminate` effect (which marks them). `lostStrength(player)` sums a
/// side's own losses; `destroyedStrength(player)` sums the losses it has inflicted.
/// </summary>
public sealed class Attrition : RuleVariantBase, IVictoryModule
{
    public override VariantInfo Info => new()
    {
        Subsystem = "victory",
        Id = "attrition",
        Description = "战损胜利：按歼灭标记统计双方损失",
        Requires = new[] { "counter" },
        Provides = new[] { "endConditions", "destroyedStrength", "lostStrength" },
        Status = "stable"
    };

    public override void Register(ModuleHost host)
    {
        host.Victory = this;
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

    public void Check(GameEngine engine)
    {
        foreach (var end in engine.Def.EndConditions)
        {
            var ctx = engine.MakeContext(null, null, null);
            if (!engine.Compile(end.When).EvalBool(ctx)) continue;
            engine.State.GameOver = true;
            engine.State.ResultMessage = end.Message;
            var winner = end.Winner is int w && w >= 0 ? w : engine.State.ActivePlayer;
            engine.State.LogMessage($"游戏结束: {end.Message} (玩家{winner + 1})");
            return;
        }
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
