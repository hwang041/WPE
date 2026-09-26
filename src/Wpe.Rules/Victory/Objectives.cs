using Wpe.Core.Definition;
using Wpe.Core.Expressions;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Victory;

/// <summary>
/// victory / objectives — contributes `objectivesHeld(player)` (count of victory hexes
/// controlled or occupied by a player) and `objective(pos)`. End conditions are evaluated
/// by the engine core, so this composes freely with other victory rules.
/// </summary>
public sealed class Objectives : RuleVariantBase
{
    public override VariantInfo Info => new()
    {
        Subsystem = "victory",
        Id = "objectives",
        Description = "目标点：objectivesHeld(玩家) 按领地控制权统计胜利格",
        Requires = new[] { "map", "territory" },
        Provides = new[] { "objectivesHeld", "objective" },
        Status = "stable"
    };

    public override void Register(ModuleHost host)
    {
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
            if (ctx.State.Territory.TryGetValue(key, out var v) && v == owner) count++;
        }
        return count;
    }
}
