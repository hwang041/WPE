using System.Globalization;
using System.Text;

namespace Wpe.Core.Expressions;

public sealed class ExprException : Exception
{
    public ExprException(string message) : base(message) { }
}

/// <summary>
/// A tiny expression language for rule conditions, e.g.:
///   me == 0 && phase == "action"
///   dist(counter, target) <= counter.moveLeft
///   effStr(counter) / effStr(target)
/// Supports: numbers, strings, bools, identifiers, property access (a.b),
/// calls (f(a,b)), arithmetic + - * / %, comparisons, == !=, && || !, parens.
/// </summary>
public static class ExpressionParser
{
    public interface INode { object? Eval(EvaluatorContext ctx); }

    public sealed class EvaluatorContext
    {
        public required IEvalContext Vars { get; init; }
        public required Func<string, object?[], object?> Call { get; init; }
    }

    public static INode Parse(string source)
    {
        var tokens = Tokenize(source);
        var pos = 0;
        var node = ParseExpr(tokens, ref pos, 0);
        if (pos < tokens.Count)
            throw new ExprException($"Unexpected token '{tokens[pos].Text}' at position {tokens[pos].Pos}");
        return node;
    }

    // ---- tokens -----------------------------------------------------------

    private sealed record Tok(string Text, TokKind Kind, int Pos);
    private enum TokKind { Num, Str, Ident, Op }

    private static List<Tok> Tokenize(string s)
    {
        var list = new List<Tok>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (char.IsDigit(c))
            {
                int start = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                list.Add(new Tok(s[start..i], TokKind.Num, start));
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                list.Add(new Tok(s[start..i], TokKind.Ident, start));
                continue;
            }
            if (c is '"' or '\'')
            {
                int start = i;
                char q = c; i++;
                var sb = new StringBuilder();
                while (i < s.Length && s[i] != q)
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        i++;
                        sb.Append(s[i] switch { 'n' => '\n', 't' => '\t', var ch => ch });
                    }
                    else sb.Append(s[i]);
                    i++;
                }
                if (i >= s.Length) throw new ExprException("Unterminated string literal");
                i++;
                list.Add(new Tok(sb.ToString(), TokKind.Str, start));
                continue;
            }
            string two = i + 1 < s.Length ? s.Substring(i, 2) : "";
            if (two is "==" or "!=" or "<=" or ">=" or "&&" or "||")
            {
                list.Add(new Tok(two, TokKind.Op, i));
                i += 2;
                continue;
            }
            if ("+-*/%<>=!(),.".Contains(c))
            {
                list.Add(new Tok(c.ToString(), TokKind.Op, i));
                i++;
                continue;
            }
            throw new ExprException($"Unexpected character '{c}' at position {i}");
        }
        return list;
    }

    // ---- AST --------------------------------------------------------------

    private sealed record Lit(double D, string? S, bool B, bool IsNull) : INode
    {
        public object? Eval(EvaluatorContext ctx) => IsNull ? null : S != null ? S : B ? true : D;
    }

    private sealed record VarRef(string Name) : INode
    {
        public object? Eval(EvaluatorContext ctx) => ctx.Vars.Resolve(Name);
    }

    private sealed record Prop(INode Obj, string Name) : INode
    {
        public object? Eval(EvaluatorContext ctx) => ValueAccessor.GetProperty(Obj.Eval(ctx), Name);
    }

    private sealed record Call(INode? Obj, string Name, List<INode> Args) : INode
    {
        public object? Eval(EvaluatorContext ctx)
        {
            var args = Args.Select(a => a.Eval(ctx)).ToArray();
            return ctx.Call(Name, args);
        }
    }

    private sealed record Unary(string Op, INode Operand) : INode
    {
        public object? Eval(EvaluatorContext ctx)
        {
            var v = ValueAccessor.AsNumber(Operand.Eval(ctx));
            return Op switch
            {
                "-" => -v,
                "+" => v,
                _ => throw new ExprException($"Unknown unary op '{Op}'")
            };
        }
    }

    private sealed record LogicalNot(INode Operand) : INode
    {
        public object? Eval(EvaluatorContext ctx) => !ValueAccessor.AsBool(Operand.Eval(ctx));
    }

    private sealed record Binary(string Op, INode L, INode R) : INode
    {
        public object? Eval(EvaluatorContext ctx)
        {
            var lv = L.Eval(ctx);
            var rv = R.Eval(ctx);
            switch (Op)
            {
                case "&&": return ValueAccessor.AsBool(lv) && ValueAccessor.AsBool(rv);
                case "||": return ValueAccessor.AsBool(lv) || ValueAccessor.AsBool(rv);
                case "==":
                    if (lv is string ls) return ls == (rv?.ToString() ?? "");
                    if (rv is string rs) return lv?.ToString() == rs;
                    return ValueAccessor.AsNumber(lv) == ValueAccessor.AsNumber(rv);
                case "!=":
                    if (lv is string ls2) return ls2 != (rv?.ToString() ?? "");
                    if (rv is string rs2) return lv?.ToString() != rs2;
                    return ValueAccessor.AsNumber(lv) != ValueAccessor.AsNumber(rv);
            }
            var a = ValueAccessor.AsNumber(lv);
            var b = ValueAccessor.AsNumber(rv);
            if (Op == "+" && (lv is string || rv is string))
                return (lv?.ToString() ?? "") + (rv?.ToString() ?? "");
            return Op switch
            {
                "+" => a + b, "-" => a - b, "*" => a * b, "/" => b == 0 ? 0 : a / b,
                "%" => b == 0 ? 0 : a % b,
                "<" => a < b, "<=" => a <= b, ">" => a > b, ">=" => a >= b,
                _ => throw new ExprException($"Unknown operator '{Op}'")
            };
        }
    }

    // ---- Pratt parser -----------------------------------------------------

    private static readonly Dictionary<string, (int lbp, int rbp)> BinaryPrec = new()
    {
        ["||"] = (1, 1),
        ["&&"] = (2, 2),
        ["=="] = (3, 3), ["!="] = (3, 3),
        ["<"] = (4, 4), ["<="] = (4, 4), [">"] = (4, 4), [">="] = (4, 4),
        ["+"] = (5, 5), ["-"] = (5, 5),
        ["*"] = (6, 6), ["/"] = (6, 6), ["%"] = (6, 6)
    };

    private static INode ParseExpr(List<Tok> t, ref int pos, int minPrec)
    {
        var left = ParsePrefix(t, ref pos);
        while (pos < t.Count && t[pos].Kind == TokKind.Op && BinaryPrec.ContainsKey(t[pos].Text))
        {
            var op = t[pos].Text;
            var (lbp, rbp) = BinaryPrec[op];
            if (lbp < minPrec) break;
            pos++;
            var right = ParseExpr(t, ref pos, rbp + 1);
            left = new Binary(op, left, right);
        }
        return left;
    }

    private static INode ParsePrefix(List<Tok> t, ref int pos)
    {
        if (pos >= t.Count) throw new ExprException("Unexpected end of expression");
        var tok = t[pos];
        switch (tok.Kind)
        {
            case TokKind.Num:
                pos++;
                return new Lit(double.Parse(tok.Text, CultureInfo.InvariantCulture), null, false, false);
            case TokKind.Str:
                pos++;
                return new Lit(0, tok.Text, false, false);
            case TokKind.Op when tok.Text == "(":
            {
                pos++;
                var e = ParseExpr(t, ref pos, 0);
                Expect(t, ref pos, ")");
                return e;
            }
            case TokKind.Op when tok.Text is "-" or "+":
                pos++;
                return new Unary(tok.Text, ParsePrefix(t, ref pos));
            case TokKind.Op when tok.Text == "!":
                pos++;
                return new LogicalNot(ParsePrefix(t, ref pos));
            case TokKind.Ident:
            {
                pos++;
                var name = tok.Text;
                if (pos < t.Count && t[pos].Text == "(")
                {
                    pos++;
                    var args = new List<INode>();
                    if (pos < t.Count && t[pos].Text != ")")
                    {
                        args.Add(ParseExpr(t, ref pos, 0));
                        while (pos < t.Count && t[pos].Text == ",")
                        {
                            pos++;
                            args.Add(ParseExpr(t, ref pos, 0));
                        }
                    }
                    Expect(t, ref pos, ")");
                    return new Call(null, name, args);
                }
                INode node = new VarRef(name);
                while (pos < t.Count && t[pos].Text == ".")
                {
                    pos++;
                    if (pos >= t.Count || t[pos].Kind != TokKind.Ident) throw new ExprException("Expected property name after '.'");
                    var propName = t[pos].Text; pos++;
                    node = new Prop(node, propName);
                }
                return node;
            }
            default:
                throw new ExprException($"Unexpected token '{tok.Text}' at position {tok.Pos}");
        }
    }

    private static void Expect(List<Tok> t, ref int pos, string text)
    {
        if (pos >= t.Count || t[pos].Text != text)
            throw new ExprException($"Expected '{text}'");
        pos++;
    }

    /// <summary>Collect the names of every function called by a parsed expression (for verification).</summary>
    public static List<string> CollectCallNames(INode node)
    {
        var names = new List<string>();
        void Walk(INode? n)
        {
            switch (n)
            {
                case Call c:
                    names.Add(c.Name);
                    if (c.Obj != null) Walk(c.Obj);
                    foreach (var a in c.Args) Walk(a);
                    break;
                case Prop p:
                    Walk(p.Obj);
                    break;
                case Unary u:
                    Walk(u.Operand);
                    break;
                case LogicalNot ln:
                    Walk(ln.Operand);
                    break;
                case Binary b:
                    Walk(b.L);
                    Walk(b.R);
                    break;
            }
        }
        Walk(node);
        return names;
    }
}
