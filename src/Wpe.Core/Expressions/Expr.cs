namespace Wpe.Core.Expressions;

/// <summary>A compiled rule expression, evaluated against a RuleContext.</summary>
public sealed class Expr
{
    private readonly ExpressionParser.INode _node;

    public string Source { get; }

    private Expr(string source, ExpressionParser.INode node)
    {
        Source = source;
        _node = node;
    }

    public static Expr Compile(string source) => new(source, ExpressionParser.Parse(source));

    public object? Eval(RuleContext ctx)
        => _node.Eval(new ExpressionParser.EvaluatorContext
        {
            Vars = ctx,
            Call = (name, args) => ctx.Host.Call(ctx, name, args)
        });

    public double EvalNumber(RuleContext ctx) => ValueAccessor.AsNumber(Eval(ctx));

    public bool EvalBool(RuleContext ctx) => ValueAccessor.AsBool(Eval(ctx));

    public string EvalString(RuleContext ctx) => Eval(ctx)?.ToString() ?? "";
}
