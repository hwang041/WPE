using Wpe.Core.Definition;
using Wpe.Core.Expressions;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Counter;

/// <summary>
/// counter / stepped — multi-step units. A counter carries `steps` (current) and
/// `maxSteps`; effective strength scales with remaining steps, the `steploss` effect
/// removes steps (flipping at half strength and eliminating at zero).
/// </summary>
public sealed class Stepped : RosterCounterBase, ICounterModule
{
    public override VariantInfo Info => new()
    {
        Subsystem = "counter",
        Id = "stepped",
        Description = "多步算子：steps/maxSteps 派生战力，steploss 削减（半力翻面、归零歼灭）",
        Provides = new[] { "counter", "roster", "effStr", "steploss" },
        Status = "stable"
    };

    public override void ContributeNames(NameContract contract)
    {
        base.ContributeNames(contract);
        contract.AddCounterAttr(ContractNames.Steps, ContractNames.MaxSteps);
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
        host.AddFunctionAliases((ctx, a) => (double)StepsOf(ExprArgs.Counter(a, 0)), "steps", "step_count");
        host.AddEffect("steploss", StepLossEffect);
        host.AddEffect("stepLoss", StepLossEffect);
    }

    // ---- ICounterModule ----

    public double EffectiveStrength(CounterState counter)
    {
        var max = counter.AttributeInt(ContractNames.MaxSteps);
        if (max <= 0) return counter.AttributeFloat(ContractNames.Strength);
        var steps = Math.Clamp(counter.AttributeInt(ContractNames.Steps, max), 0, max);
        return counter.AttributeFloat(ContractNames.Strength) * steps / max;
    }

    public double EffectiveMove(CounterState counter)
        => counter.AttributeFloat(ContractNames.Move)
           - (counter.IsBack ? MovePenalty(counter) : 0);

    private static int StepsOf(CounterState? c)
    {
        if (c == null) return 0;
        var max = c.AttributeInt(ContractNames.MaxSteps);
        return c.AttributeInt(ContractNames.Steps, max);
    }

    // ---- effect ----

    private void StepLossEffect(RuleContext ctx, EffectDef e)
    {
        var c = e.Counter == "target" ? ctx.TargetCounter : ctx.Counter;
        if (c == null) return;
        var loss = (int)Math.Round(ctx.Engine.Compile(e.Value).EvalNumber(ctx));
        if (loss <= 0) return;
        var max = c.AttributeInt(ContractNames.MaxSteps);
        var steps = c.AttributeInt(ContractNames.Steps, max > 0 ? max : 1);
        steps -= loss;
        if (steps <= 0)
        {
            c.Position = null;
            c.Attributes[ContractNames.Eliminated] = 1;
            c.Attributes[ContractNames.Steps] = 0;
            ctx.State.LogMessage($"{c.Name} 步数耗尽，被歼灭");
            return;
        }
        c.Attributes[ContractNames.Steps] = steps;
        if (max > 0 && steps <= max / 2) c.Side = Side.Back;
        ctx.State.LogMessage($"{c.Name} 损失 {loss} 步，剩 {steps}/{max}");
    }
}
