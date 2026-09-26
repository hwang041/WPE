using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Expressions;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Victory;

/// <summary>
/// victory / objectives — control-point victory. End conditions are declared in game.json
/// `endConditions` and can use `objectivesHeld(player)` (count of victory hexes controlled)
/// and `objective(pos)` (is this cell a victory hex). Reads the control subsystem's data.
/// </summary>
public sealed class Objectives : RuleVariantBase, IVictoryModule
{
    public override VariantInfo Info => new()
    {
        Subsystem = "victory",
        Id = "objectives",
        Description = "目标点胜利：按控制权统计胜利格 + endConditions",
        Requires = new[] { "map" },
        Provides = new[] { "endConditions", "objectivesHeld", "objective" },
        Status = "stable"
    };

    public override void Register(ModuleHost host)
    {
        host.Victory = this;
        host.AddFunctionAliases((ctx, a) => (double)ObjectivesHeld(ctx, (int)ExprArgs.Number(a, 0)),
            "objectivesHeld", "objectives_held");
        host.AddFunctionAliases((ctx, a) => ctx.State.Map?.IsVictoryHex(ExprArgs.Cell(a, ctx, 0)) == true,
            "objective", "isObjective", "is_objective");
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

    private static int ObjectivesHeld(RuleContext ctx, int player)
    {
        var map = ctx.State.Map;
        if (map is not GridMap gm) return 0;
        var owner = player.ToString();
        int count = 0;
        foreach (var vh in gm.VictoryHexes)
        {
            var cell = new HexCoord(vh.Q, vh.R);
            var key = gm.CellKey(cell);
            bool controlled = ctx.State.Control.TryGetValue(key, out var v) && v == owner;
            bool occupied = ctx.State.CountersOnBoard().Any(c =>
                c.Hex == cell && c.AttributeInt(ContractNames.Owner, -1) == player);
            if (controlled || occupied) count++;
        }
        return count;
    }
}
