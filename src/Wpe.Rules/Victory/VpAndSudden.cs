using Wpe.Core.Definition;
using Wpe.Core.Modules;

namespace Wpe.Rules.Victory;

/// <summary>
/// victory / vpAndSudden — contributes the `vpHeld(player)` function (count of victory
/// hexes currently occupied by a player's units). End conditions themselves are evaluated
/// by the engine's core `endConditions` loop, so several victory rules can be selected at
/// the same time.
/// </summary>
public sealed class VpAndSudden : RuleVariantBase
{
    public override VariantInfo Info => new()
    {
        Subsystem = "victory",
        Id = "vpAndSudden",
        Description = "胜利点：vpHeld(玩家) = 占据的胜利格数",
        Requires = new[] { "map" },
        Provides = new[] { "vpHeld" },
        Status = "stable"
    };

    public override void Register(ModuleHost host)
    {
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
}
