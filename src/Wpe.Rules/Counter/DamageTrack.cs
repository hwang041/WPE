using Wpe.Core.Definition;
using Wpe.Core.Expressions;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Counter;

/// <summary>
/// counter / damageTrack — a numeric damage track instead of a binary flip. A counter
/// carries `damage`; effective strength and move are reduced by the accumulated damage,
/// and the `damage` effect increments it.
/// </summary>
public sealed class DamageTrack : RosterCounterBase, ICounterModule
{
    public override VariantInfo Info => new()
    {
        Subsystem = "counter",
        Id = "damageTrack",
        Description = "损伤轨算子：damage 累积，战力/移动按损伤衰减",
        Provides = new[] { "roster", "effStr", "effMove", "damage" },
        Status = "stable"
    };

    public override void ContributeNames(NameContract contract)
    {
        base.ContributeNames(contract);
        contract.AddCounterAttr(ContractNames.Damage);
    }

    public override void Register(ModuleHost host)
    {
        host.Counter = this;
        host.AddFunctionAliases((ctx, a) =>
        {
            var c = ExprArgs.Counter(a, 0);
            return c == null ? 0d : EffectiveStrength(c);
        }, "effStr", "effective_strength");
        host.AddFunctionAliases((ctx, a) =>
        {
            var c = ExprArgs.Counter(a, 0);
            return c == null ? 0d : EffectiveMove(c);
        }, "effMove", "effective_move");
        host.AddFunctionAliases((ctx, a) => (double)(ExprArgs.Counter(a, 0)?.AttributeInt(ContractNames.Damage) ?? 0),
            "damageValue", "damage_value");
        host.AddEffect("damage", DamageEffect);
    }

    // ---- ICounterModule ----

    public double EffectiveStrength(CounterState counter)
        => Math.Max(0, counter.AttributeFloat(ContractNames.Strength) - counter.AttributeInt(ContractNames.Damage));

    public double EffectiveMove(CounterState counter)
        => Math.Max(0, counter.AttributeFloat(ContractNames.Move) - counter.AttributeInt(ContractNames.Damage));

    // ---- effect ----

    private void DamageEffect(RuleContext ctx, EffectDef e)
    {
        var c = e.Counter == "target" ? ctx.TargetCounter : ctx.Counter;
        if (c == null) return;
        var amount = (int)Math.Round(ctx.Engine.Compile(e.Value).EvalNumber(ctx));
        if (amount <= 0) return;
        var cur = c.AttributeInt(ContractNames.Damage) + amount;
        c.Attributes[ContractNames.Damage] = cur;
        ctx.State.LogMessage($"{c.Name} 受创 +{amount}（损伤 {cur}）");
    }
}
