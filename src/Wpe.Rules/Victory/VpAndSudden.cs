using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Modules;

namespace Wpe.Rules.Victory;

/// <summary>
/// victory / vpAndSudden — victory point goals plus sudden-death end conditions,
/// both declared in game.json `endConditions`. Provides the vpHeld() function
/// (count of victory hexes currently occupied by a player's units).
/// </summary>
public sealed class VpAndSudden : RuleVariantBase, IVictoryModule
{
    public override VariantInfo Info => new()
    {
        Subsystem = "victory",
        Id = "vpAndSudden",
        Description = "胜利点 + 突然死亡，endConditions 全部满足即终局",
        Provides = new[] { "endConditions", "vpHeld" },
        Status = "stable"
    };

    public override void Register(ModuleHost host)
    {
        host.Victory = this;
        host.AddFunctionAliases((ctx, a) =>
        {
            var p = (int)ExprArgs.Number(a, 0);
            return (double)ctx.State.CountersOnBoard().Count(c =>
                c.AttributeInt(ContractNames.Owner, -1) == p &&
                ctx.State.Map != null && ctx.State.Map.IsVictoryHex(c.Hex));
        }, "vpHeld", "victoryheld");
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
            if (engine.Compile(end.When).EvalBool(ctx))
            {
                engine.State.GameOver = true;
                engine.State.ResultMessage = end.Message;
                var winner = end.Winner is int w && w >= 0 ? w : engine.State.ActivePlayer;
                engine.State.LogMessage($"游戏结束: {end.Message} (玩家{winner + 1})");
                return;
            }
        }
    }
}
