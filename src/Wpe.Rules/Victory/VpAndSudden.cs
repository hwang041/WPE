using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Expressions;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Victory;

/// <summary>
/// victory / vpAndSudden — victory point goals plus sudden-death end conditions,
/// both declared in game.json `endConditions`. Provides the vpHeld() function
/// (count of victory hexes currently occupied by a player's units).
/// </summary>
public sealed class VpAndSudden : IRuleVariant, IVictoryModule
{
    public VariantInfo Info => new()
    {
        Subsystem = "victory",
        Id = "vpAndSudden",
        Description = "胜利点 + 突然死亡，endConditions 全部满足即终局",
        Status = "stable"
    };

    public void Load(string? configJson, GameDefinition def) { }
    public void Register(ModuleHost host)
    {
        host.Victory = this;
        host.AddFunction("vpHeld", (ctx, a) =>
        {
            var p = (int)ValueAccessor.AsNumber(a.Length > 0 ? a[0] : 0);
            return (double)ctx.State.CountersOnBoard().Count(c =>
                c.AttributeInt("owner", -1) == p &&
                ctx.State.Map != null && ctx.State.Map.IsVictoryHex(c.Hex));
        });
        host.AddFunction("victoryheld", (ctx, a) =>
        {
            var p = (int)ValueAccessor.AsNumber(a.Length > 0 ? a[0] : 0);
            return (double)ctx.State.CountersOnBoard().Count(c =>
                c.AttributeInt("owner", -1) == p &&
                ctx.State.Map != null && ctx.State.Map.IsVictoryHex(c.Hex));
        });
    }

    public void Apply(GameState state, GameEngine? engine) { }
    public void Validate(GameDefinition def, List<string> issues)
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
