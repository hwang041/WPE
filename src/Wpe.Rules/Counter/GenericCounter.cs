using Wpe.Core.Definition;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Counter;

/// <summary>
/// counter / generic — the full roster (units.json) plus the classic binary unit model:
/// effective strength/move drop by the damaged-side penalties when the counter is flipped.
/// </summary>
public sealed class GenericCounter : RosterCounterBase, ICounterModule
{
    public override VariantInfo Info => new()
    {
        Subsystem = "counter",
        Id = "generic",
        Description = "通用算子目录：units.json 键 → 基础属性；正背两态战力/移动",
        Provides = new[] { "roster", "effStr", "effMove" },
        Status = "stable"
    };

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
    }

    // ---- ICounterModule: binary front/back model ----

    public double EffectiveStrength(CounterState counter)
        => counter.AttributeFloat(ContractNames.Strength)
           - (counter.IsBack ? StrengthPenalty(counter) : 0);

    public double EffectiveMove(CounterState counter)
        => counter.AttributeFloat(ContractNames.Move)
           - (counter.IsBack ? MovePenalty(counter) : 0);
}
