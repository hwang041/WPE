using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Expressions;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Victory;

/// <summary>
/// victory / exit — units leave the map for victory points (classic "exit N strength off
/// the friendly edge"). The `exit` effect removes the counter and marks it exited;
/// `exitedStrength(player)` sums the strength that has left the map.
/// </summary>
public sealed class Exit : RuleVariantBase, IVictoryModule
{
    public override VariantInfo Info => new()
    {
        Subsystem = "victory",
        Id = "exit",
        Description = "退场胜利：exit 效果离场并计数，exitedStrength 统计",
        Provides = new[] { "endConditions", "exit", "exitedStrength" },
        Status = "stable"
    };

    public override void Register(ModuleHost host)
    {
        host.Victory = this;
        host.AddEffect("exit", ExitEffect);
        host.AddFunctionAliases((ctx, a) => (double)ExitedStrength(ctx, (int)ExprArgs.Number(a, 0)),
            "exitedStrength", "exited_strength");
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
