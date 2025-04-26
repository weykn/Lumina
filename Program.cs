using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace LuminaInterpreter
{
    public enum ValueType { Number, String, Boolean }

    public struct Value
    {
        public ValueType Type;
        public double Number;
        public string Str;
        public bool Bool;

        public static Value FromNumber(double n) => new() { Type = ValueType.Number, Number = n };
        public static Value FromString(string s) => new() { Type = ValueType.String, Str = s };
        public static Value FromBoolean(bool b) => new() { Type = ValueType.Boolean, Bool = b };

        public override string ToString() => Type switch
        {
            ValueType.Number => Number.ToString("G"),
            ValueType.String => Str,
            ValueType.Boolean => Bool.ToString().ToLower(),
            _ => ""
        };

        public int CompareTo(Value o) => Type switch
        {
            ValueType.Number when o.Type == ValueType.Number => Number.CompareTo(o.Number),
            ValueType.String when o.Type == ValueType.String => string.Compare(Str, o.Str, StringComparison.Ordinal),
            ValueType.Boolean when o.Type == ValueType.Boolean => Bool.CompareTo(o.Bool),
            _ => throw new Exception($"Cannot compare {Type} with {o.Type}")
        };
    }

    public static class Utils
    {
        public static bool IsTruthy(Value v) => v.Type switch
        {
            ValueType.Boolean => v.Bool,
            ValueType.Number => v.Number != 0,
            ValueType.String => v.Str.Length > 0,
            _ => false
        };
    }

    public class Context
    {
        public Stack<Value> ArgStack = new();
        public Stack<Frame> CallStack = new();
        public List<Assembly> ImportedAssemblies = new();
        public HashSet<string> DisabledTokens = new(StringComparer.OrdinalIgnoreCase);
        public bool Reverse = false;
        public Value LastReturn = Value.FromNumber(0);

        public int CurrentLine = 0;
        public Dictionary<string, int> LineExpirations = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Var, DateTime Expire)> TimeExpirations = new();

        public Frame CurrentFrame => CallStack.Peek();

        public void Call(Function func)
        {
            var frame = new Frame();
            CallStack.Push(frame);
            try
            {
                foreach (var stmt in func.Body)
                    stmt.Execute(this);
            }
            catch (ReturnException) { }
            CallStack.Pop();
        }

        public void ExternalCall(string name)
        {
            if (DisabledTokens.Contains(name))
                throw new Exception($"Unknown function: {name}");

            if (BuiltIns.Map.TryGetValue(name, out var bi))
            {
                LastReturn = bi(this, new());
                return;
            }
            if (Function.Definitions.TryGetValue(name, out var fn))
            {
                Call(fn);
                return;
            }
            foreach (var asm in ImportedAssemblies)
                foreach (var type in asm.GetTypes())
                {
                    var m = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
                    if (m == null) continue;
                    object raw;
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(Context))
                        raw = m.Invoke(null, new object[] { this });
                    else if (ps.Length == 0)
                        raw = m.Invoke(null, null);
                    else
                        continue;

                    LastReturn = m.ReturnType switch
                    {
                        Type t when t == typeof(void) => Value.FromNumber(0),
                        Type t when t == typeof(string) => Value.FromString((string)raw),
                        Type t when t == typeof(bool) => Value.FromBoolean((bool)raw),
                        Type t when t.IsPrimitive => Value.FromNumber(Convert.ToDouble(raw)),
                        Type t when t == typeof(Value) => (Value)raw,
                        _ => throw new Exception($"Unsupported return type {m.ReturnType}")
                    };
                    return;
                }
            throw new Exception($"Unknown function: {name}");
        }

        public void LoadAssembly(string path)
            => ImportedAssemblies.Add(Assembly.LoadFrom(path));

        public void ExpireLifetimes()
        {
            var expiredLines = LineExpirations
                .Where(kv => kv.Value <= CurrentLine)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var v in expiredLines)
            {
                CurrentFrame.Variables.Remove(v);
                CurrentFrame.History.Remove(v);
                Function.Definitions.Remove(v);
                LineExpirations.Remove(v);
            }
            var now = DateTime.Now;
            for (int i = TimeExpirations.Count - 1; i >= 0; i--)
            {
                if (TimeExpirations[i].Expire <= now)
                {
                    var v = TimeExpirations[i].Var;
                    CurrentFrame.Variables.Remove(v);
                    CurrentFrame.History.Remove(v);
                    Function.Definitions.Remove(v);
                    TimeExpirations.RemoveAt(i);
                }
            }
        }
    }

    public class Frame
    {
        public Dictionary<string, Value> Variables = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<Value>> History = new(StringComparer.OrdinalIgnoreCase);
        public Value Lookup(string name)
            => Variables.TryGetValue(name, out var v)
               ? v
               : throw new Exception($"Undefined variable or literal: {name}");
    }

    public class Function
    {
        public string Name;
        public List<Statement> Body = new();
        public static Dictionary<string, Function> Definitions = new();
        public Function(string name) { Name = name; }
    }

    public class ReturnException : Exception { }

    static class ExpressionParser
    {
        static readonly Dictionary<string, int> Prec = new(StringComparer.Ordinal)
        {
            { "+",1 },{ "-",1 },{ "*",2 },{ "/",2 },{ "%",2 }
        };
        static readonly Dictionary<string, double> NumberWords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["zero"] = 0,
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3,
            ["four"] = 4,
            ["five"] = 5,
            ["six"] = 6,
            ["seven"] = 7,
            ["eight"] = 8,
            ["nine"] = 9,
            ["ten"] = 10
        };
        static readonly Random _rand = new();
        static readonly Dictionary<string, double> Probabilities = new(StringComparer.OrdinalIgnoreCase)
        {
            ["TRUE"] = 1.00,
            ["ALMOSTCERTAIN"] = 0.99,
            ["EXTREMELYLIKELY"] = 0.98,
            /* … etc. … */
            ["IMPOSSIBLE"] = 0.01,
            ["FALSE"] = 0.00
        };

        public static List<string> TokenizeExpression(string expr)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < expr.Length)
            {
                char c = expr[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '"' || c == '\'')
                {
                    char q = c; int qlen = 1;
                    while (i + qlen < expr.Length && expr[i + qlen] == q) qlen++;
                    string delim = new string(q, qlen);
                    int j = expr.IndexOf(delim, i + qlen, StringComparison.Ordinal);
                    if (j < 0) throw new Exception("Unterminated string literal");
                    tokens.Add(expr.Substring(i, j + qlen - i));
                    i = j + qlen;
                }
                else if ("+-*/%()".IndexOf(c) >= 0)
                {
                    tokens.Add(c.ToString());
                    i++;
                }
                else
                {
                    int j = i;
                    while (j < expr.Length &&
                           "+-*/%()\"'\t\r\n ".IndexOf(expr[j]) < 0)
                        j++;
                    tokens.Add(expr.Substring(i, j - i));
                    i = j;
                }
            }
            return tokens;
        }

        public static List<string> ToRPN(List<string> toks)
        {
            var outq = new List<string>();
            var ops = new Stack<string>();
            foreach (var t in toks)
            {
                if (Prec.ContainsKey(t))
                {
                    while (ops.Count > 0 && Prec.ContainsKey(ops.Peek()) && Prec[ops.Peek()] >= Prec[t])
                        outq.Add(ops.Pop());
                    ops.Push(t);
                }
                else if (t == "(") ops.Push(t);
                else if (t == ")")
                {
                    while (ops.Count > 0 && ops.Peek() != "(")
                        outq.Add(ops.Pop());
                    if (ops.Count == 0) throw new Exception("Mismatched parentheses");
                    ops.Pop();
                }
                else outq.Add(t);
            }
            while (ops.Count > 0)
            {
                var o = ops.Pop();
                if (o == "(" || o == ")") throw new Exception("Mismatched parentheses");
                outq.Add(o);
            }
            return outq;
        }

        public static Value EvalRPN(Context ctx, List<string> rpn)
        {
            var st = new Stack<Value>();
            foreach (var tok in rpn)
            {
                if (Prec.ContainsKey(tok))
                {
                    if (ctx.DisabledTokens.Contains(tok))
                        throw new Exception($"Unknown token: {tok}");
                    if (st.Count < 2) throw new Exception("Bad expression");
                    var b = st.Pop(); var a = st.Pop();
                    var res = tok switch
                    {
                        "+" when a.Type == ValueType.Number && b.Type == ValueType.Number
                            => Value.FromNumber(a.Number + b.Number),
                        "+" => Value.FromString(a.ToString() + b.ToString()),
                        "-" when a.Type == ValueType.Number && b.Type == ValueType.Number
                            => Value.FromNumber(a.Number - b.Number),
                        "*" when a.Type == ValueType.Number && b.Type == ValueType.Number
                            => Value.FromNumber(a.Number * b.Number),
                        "/" when a.Type == ValueType.Number && b.Type == ValueType.Number && b.Number != 0
                            => Value.FromNumber(a.Number / b.Number),
                        "/" => throw new DivideByZeroException(),
                        "%" when a.Type == ValueType.Number && b.Type == ValueType.Number
                            => Value.FromNumber(a.Number % b.Number),
                        _ => throw new Exception($"Unknown operator: {tok}")
                    };
                    st.Push(res);
                }
                else
                {
                    st.Push(EvaluateAtom(ctx, tok));
                }
            }
            if (st.Count != 1) throw new Exception("Bad expression");
            return st.Pop();
        }

        public static Value EvaluateExpression(Context ctx, string expr)
        {
            var toks = TokenizeExpression(expr);
            if (toks.Count == 1 && !Prec.ContainsKey(toks[0]) && toks[0] != "(" && toks[0] != ")")
                return EvaluateAtom(ctx, toks[0]);
            return EvalRPN(ctx, ToRPN(toks));
        }

        public static bool EvaluateCondition(Value a, Value b, string op)
        {
            return op.ToUpperInvariant() switch
            {
                "<" => a.CompareTo(b) < 0,
                "LESS" => a.CompareTo(b) < 0,
                ">" => a.CompareTo(b) > 0,
                "GREATER" => a.CompareTo(b) > 0,
                "<=" => a.CompareTo(b) <= 0,
                "LESSEQ" => a.CompareTo(b) <= 0,
                ">=" => a.CompareTo(b) >= 0,
                "GREATEREQ" => a.CompareTo(b) >= 0,
                "==" => a.CompareTo(b) == 0,
                "EQUAL" => a.CompareTo(b) == 0,
                "!=" => a.CompareTo(b) != 0,
                "NOTEQUAL" => a.CompareTo(b) != 0,
                _ => throw new Exception($"Unknown comparator: {op}")
            };
        }

        static Value EvaluateAtom(Context ctx, string tok)
        {
            if (ctx.DisabledTokens.Contains(tok))
                throw new Exception($"Unknown token: {tok}");

            // 1) variable
            if (ctx.CurrentFrame.Variables.TryGetValue(tok, out var v))
                return v;
            // 2) probabilistic boolean
            if (Probabilities.TryGetValue(tok, out var p))
                return Value.FromBoolean(_rand.NextDouble() < p);
            // 3) number-word
            if (NumberWords.TryGetValue(tok, out var wn))
                return Value.FromNumber(wn);
            // 4) quoted literal
            if (tok.Length >= 2 && (tok[0] == '"' || tok[0] == '\'') && tok[0] == tok[^1])
            {
                string s = tok;
                while (s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[0] == s[^1])
                    s = s[1..^1];
                return Value.FromString(s);
            }
            // 5) numeric literal
            if (double.TryParse(tok, out var d))
                return Value.FromNumber(d);
            // 6) bare→string
            return Value.FromString(tok);
        }
    }

    static class BuiltIns
    {
        public static readonly Dictionary<string, Func<Context, List<Value>, Value>> Map
          = new(StringComparer.OrdinalIgnoreCase)
          {
              ["PRINTLINE"] = (ctx, args) =>
              {
                  foreach (var v in args) Console.WriteLine(v.ToString());
                  return Value.FromNumber(0);
              }
          };
    }

    public abstract class Statement
    {
        public abstract string Keyword { get; }
        public void Execute(Context ctx)
        {
            if (ctx.DisabledTokens.Contains(Keyword))
                throw new Exception($"Unknown statement: {Keyword}");
            ExecuteImpl(ctx);
            ctx.CurrentLine++;
            ctx.ExpireLifetimes();
        }
        protected abstract void ExecuteImpl(Context ctx);
    }

    public class ImportStmt : Statement
    {
        public override string Keyword => "IMPORT";
        string Path;
        public ImportStmt(string path) { Path = path; }
        protected override void ExecuteImpl(Context ctx) => ctx.LoadAssembly(Path);
    }

    public class FunctionDefStmt : Statement
    {
        public override string Keyword => FnKeyword;
        string FnKeyword, Name;
        List<Statement> Body;
        public FunctionDefStmt(string fk, string name, List<Statement> b)
        { FnKeyword = fk; Name = name; Body = b; }
        protected override void ExecuteImpl(Context ctx)
        {
            var f = new Function(Name);
            f.Body.AddRange(Body);
            Function.Definitions[Name] = f;
        }
    }

    public class AssignWithLifetimeStmt : Statement
    {
        public override string Keyword => ":";
        public string Var, Expr;
        public int LineLifetime;
        public double TimeLifetime;
        public AssignWithLifetimeStmt(string v, int ll, double tl, string e)
        { Var = v; Expr = e; LineLifetime = ll; TimeLifetime = tl; }
        protected override void ExecuteImpl(Context ctx)
        {
            var frame = ctx.CurrentFrame;
            // record history
            if (frame.Variables.TryGetValue(Var, out var old))
            {
                if (!frame.History.ContainsKey(Var))
                    frame.History[Var] = new List<Value>();
                frame.History[Var].Add(old);
            }
            var val = ExpressionParser.EvaluateExpression(ctx, Expr);
            frame.Variables[Var] = val;

            if (LineLifetime > 0)
                ctx.LineExpirations[Var] = ctx.CurrentLine + LineLifetime;
            else if (LineLifetime < 0)
                ctx.LineExpirations[Var] = ctx.CurrentLine;
            if (TimeLifetime > 0)
                ctx.TimeExpirations.Add((Var, DateTime.Now.AddSeconds(TimeLifetime)));
        }
    }

    public class AssignExprStmt : Statement
    {
        public override string Keyword => ":";
        string Var, Expr;
        public AssignExprStmt(string v, string e) { Var = v; Expr = e; }
        protected override void ExecuteImpl(Context ctx)
        {
            var frame = ctx.CurrentFrame;
            if (frame.Variables.TryGetValue(Var, out var old))
            {
                if (!frame.History.ContainsKey(Var))
                    frame.History[Var] = new List<Value>();
                frame.History[Var].Add(old);
            }
            var val = ExpressionParser.EvaluateExpression(ctx, Expr);
            frame.Variables[Var] = val;
        }
    }

    public class InlineCallStmt : Statement
    {
        public override string Keyword => FuncName;
        string FuncName; List<string> Args;
        public InlineCallStmt(string fn, List<string> a) { FuncName = fn; Args = a; }
        protected override void ExecuteImpl(Context ctx)
        {
            var vals = Args.Select(x => ExpressionParser.EvaluateExpression(ctx, x)).ToList();
            if (BuiltIns.Map.TryGetValue(FuncName, out var bi))
            {
                ctx.LastReturn = bi(ctx, vals);
                return;
            }
            foreach (var v in vals) ctx.ArgStack.Push(v);
            ctx.ExternalCall(FuncName);
        }
    }

    public class DeleteStmt : Statement
    {
        public override string Keyword => "DELETE";
        string Token;
        public DeleteStmt(string t) { Token = t; }
        protected override void ExecuteImpl(Context ctx)
        {
            var frame = ctx.CurrentFrame;
            // If a variable exists with that name, delete it (and its history and lifetimes)
            if (frame.Variables.Remove(Token))
            {
                frame.History.Remove(Token);
                ctx.LineExpirations.Remove(Token);
                ctx.TimeExpirations.RemoveAll(x => x.Var == Token);
            }
            else
            {
                // Otherwise disable the token globally
                Function.Definitions.Remove(Token);
                ctx.DisabledTokens.Add(Token);
            }
        }
    }

    public class PreviousStmt : Statement
    {
        public override string Keyword => "PREVIOUS";
        string Var;
        public PreviousStmt(string var) { Var = var; }
        protected override void ExecuteImpl(Context ctx)
        {
            var frame = ctx.CurrentFrame;
            if (!frame.History.TryGetValue(Var, out var list) || list.Count == 0)
                throw new Exception($"No previous value for '{Var}'");
            var prev = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            frame.Variables[Var] = prev;
        }
    }

    public class ReturnStmt : Statement
    {
        public override string Keyword => "RETURN";
        string Expr;
        public ReturnStmt(string e) { Expr = e; }
        protected override void ExecuteImpl(Context ctx)
        {
            ctx.LastReturn = ExpressionParser.EvaluateExpression(ctx, Expr);
            throw new ReturnException();
        }
    }

    public class ReverseStmt : Statement
    {
        public override string Keyword => "REVERSE";
        protected override void ExecuteImpl(Context ctx) => ctx.Reverse = !ctx.Reverse;
    }

    public class IfStmt : Statement
    {
        public override string Keyword => "IF";
        string Left, Op, Right;
        List<Statement> Body;
        public IfStmt(string l, string op, string r, List<Statement> b)
        { Left = l; Op = op; Right = r; Body = b; }
        protected override void ExecuteImpl(Context ctx)
        {
            var a = ExpressionParser.EvaluateExpression(ctx, Left);
            var b = ExpressionParser.EvaluateExpression(ctx, Right);
            if (ExpressionParser.EvaluateCondition(a, b, Op))
                foreach (var s in Body) s.Execute(ctx);
        }
    }

    public class IfExprStmt : Statement
    {
        public override string Keyword => "IF";
        string Cond;
        List<Statement> Body;
        public IfExprStmt(string c, List<Statement> b) { Cond = c; Body = b; }
        protected override void ExecuteImpl(Context ctx)
        {
            var v = ExpressionParser.EvaluateExpression(ctx, Cond);
            if (Utils.IsTruthy(v))
                foreach (var s in Body) s.Execute(ctx);
        }
    }

    public class WhileStmt : Statement
    {
        public override string Keyword => "WHILE";
        string Left, Op, Right;
        List<Statement> Body;
        public WhileStmt(string l, string op, string r, List<Statement> b)
        { Left = l; Op = op; Right = r; Body = b; }
        protected override void ExecuteImpl(Context ctx)
        {
            while (ExpressionParser.EvaluateCondition(
                ExpressionParser.EvaluateExpression(ctx, Left),
                ExpressionParser.EvaluateExpression(ctx, Right),
                Op))
            {
                foreach (var s in Body) s.Execute(ctx);
            }
        }
    }

    public class WhileExprStmt : Statement
    {
        public override string Keyword => "WHILE";
        string Cond;
        List<Statement> Body;
        public WhileExprStmt(string c, List<Statement> b) { Cond = c; Body = b; }
        protected override void ExecuteImpl(Context ctx)
        {
            while (Utils.IsTruthy(ExpressionParser.EvaluateExpression(ctx, Cond)))
                foreach (var s in Body) s.Execute(ctx);
        }
    }

    public class Parser
    {
        static readonly Regex TokenRx = new("\"[^\"]*\"|'[^']*'|[^\\s]+");
        string[] Lines;
        public List<Statement> TopLevel = new();
        public List<string> Imports = new();
        int Index;

        public Parser(string[] lines) { Lines = lines; Index = 0; }

        public void Parse()
        {
            while (Index < Lines.Length)
            {
                var raw = Lines[Index++];
                var ln = raw.Trim();
                if (string.IsNullOrWhiteSpace(ln) || ln.StartsWith("#")) continue;
                var parts = TokenRx.Matches(ln).Cast<Match>().Select(m => m.Value).ToList();
                Statement stmt;
                if (parts[0].Equals("IMPORT", StringComparison.OrdinalIgnoreCase))
                    stmt = new ImportStmt(parts[1].Trim('"', '\''));
                else if (IsFnKeyword(parts[0]) && parts.Count == 2)
                    stmt = ParseFunctionDef(parts[0], parts[1]);
                else if (parts[0].Equals("IF", StringComparison.OrdinalIgnoreCase))
                    stmt = parts.Count == 2 ? ParseIfExpr(parts) : ParseIf(parts);
                else if (parts[0].Equals("WHILE", StringComparison.OrdinalIgnoreCase))
                    stmt = parts.Count == 2 ? ParseWhileExpr(parts) : ParseWhile(parts);
                else if (parts[0].Equals("PREVIOUS", StringComparison.OrdinalIgnoreCase))
                    stmt = new PreviousStmt(parts[1]);
                else if (parts.Count >= 3 && parts[1].EndsWith(":") && !parts[0].EndsWith(":"))
                    stmt = ParseLifetimeAssign(parts);
                else
                    stmt = ParseSimpleStmt(parts);

                TopLevel.Add(stmt);
            }
        }

        FunctionDefStmt ParseFunctionDef(string fk, string name)
        {
            var body = new List<Statement>();
            while (Index < Lines.Length)
            {
                var raw = Lines[Index++];
                var ln = raw.Trim();
                if (string.IsNullOrWhiteSpace(ln) || ln.StartsWith("#")) continue;
                if (ln.Equals("END", StringComparison.OrdinalIgnoreCase)) break;
                var p = TokenRx.Matches(ln).Cast<Match>().Select(m => m.Value).ToList();
                body.Add(ParseSimpleOrBlock(p));
            }
            return new FunctionDefStmt(fk, name, body);
        }

        Statement ParseSimpleOrBlock(List<string> p)
        {
            if (p[0].Equals("PREVIOUS", StringComparison.OrdinalIgnoreCase))
                return new PreviousStmt(p[1]);
            if (p[0].Equals("IF", StringComparison.OrdinalIgnoreCase))
                return p.Count == 2 ? ParseIfExpr(p) : ParseIf(p);
            if (p[0].Equals("WHILE", StringComparison.OrdinalIgnoreCase))
                return p.Count == 2 ? ParseWhileExpr(p) : ParseWhile(p);
            if (p.Count >= 3 && p[1].EndsWith(":") && !p[0].EndsWith(":"))
                return ParseLifetimeAssign(p);
            return ParseSimpleStmt(p);
        }

        IfExprStmt ParseIfExpr(List<string> p)
        {
            var body = new List<Statement>();
            while (Index < Lines.Length)
            {
                var raw = Lines[Index++]; var ln = raw.Trim();
                if (string.IsNullOrWhiteSpace(ln) || ln.StartsWith("#")) continue;
                if (ln.Equals("END", StringComparison.OrdinalIgnoreCase)) break;
                var q = TokenRx.Matches(ln).Cast<Match>().Select(m => m.Value).ToList();
                body.Add(ParseSimpleOrBlock(q));
            }
            return new IfExprStmt(p[1], body);
        }

        IfStmt ParseIf(List<string> p)
        {
            var (l, op, r) = SplitCondition(p);
            var body = new List<Statement>();
            while (Index < Lines.Length)
            {
                var raw = Lines[Index++]; var ln = raw.Trim();
                if (string.IsNullOrWhiteSpace(ln) || ln.StartsWith("#")) continue;
                if (ln.Equals("END", StringComparison.OrdinalIgnoreCase)) break;
                var q = TokenRx.Matches(ln).Cast<Match>().Select(m => m.Value).ToList();
                body.Add(ParseSimpleOrBlock(q));
            }
            return new IfStmt(l, op, r, body);
        }

        WhileExprStmt ParseWhileExpr(List<string> p)
        {
            var body = new List<Statement>();
            while (Index < Lines.Length)
            {
                var raw = Lines[Index++]; var ln = raw.Trim();
                if (string.IsNullOrWhiteSpace(ln) || ln.StartsWith("#")) continue;
                if (ln.Equals("END", StringComparison.OrdinalIgnoreCase)) break;
                var q = TokenRx.Matches(ln).Cast<Match>().Select(m => m.Value).ToList();
                body.Add(ParseSimpleOrBlock(q));
            }
            return new WhileExprStmt(p[1], body);
        }

        WhileStmt ParseWhile(List<string> p)
        {
            var (l, op, r) = SplitCondition(p);
            var body = new List<Statement>();
            while (Index < Lines.Length)
            {
                var raw = Lines[Index++]; var ln = raw.Trim();
                if (string.IsNullOrWhiteSpace(ln) || ln.StartsWith("#")) continue;
                if (ln.Equals("END", StringComparison.OrdinalIgnoreCase)) break;
                var q = TokenRx.Matches(ln).Cast<Match>().Select(m => m.Value).ToList();
                body.Add(ParseSimpleOrBlock(q));
            }
            return new WhileStmt(l, op, r, body);
        }

        (string left, string op, string right) SplitCondition(List<string> p)
        {
            var comps = new[]{ "<=",">=","==","!=","<",">","LESS","GREATER",
                "LESSEQ","GREATEREQ","EQUAL","NOTEQUAL" };
            int idx = p.FindIndex(1, x => comps.Contains(x.ToUpperInvariant()));
            if (idx < 0) throw new Exception("Invalid IF/WHILE condition");
            string left = string.Join(" ", p.Skip(1).Take(idx - 1));
            string op = p[idx];
            string right = string.Join(" ", p.Skip(idx + 1));
            return (left, op, right);
        }

        Statement ParseLifetimeAssign(List<string> p)
        {
            string var = p[0];
            string life = p[1].TrimEnd(':');
            string expr = string.Join(" ", p.Skip(2));
            int lines = 0; double secs = 0;
            if (life.EndsWith("s", StringComparison.OrdinalIgnoreCase))
                secs = double.Parse(life[..^1]);
            else
                lines = int.Parse(life);
            return new AssignWithLifetimeStmt(var, lines, secs, expr);
        }

        Statement ParseSimpleStmt(List<string> p)
        {
            if (p[0].StartsWith("!"))
                return new InlineCallStmt(p[0].Substring(1), p.Skip(1).ToList());
            if (p[0].Equals("DELETE", StringComparison.OrdinalIgnoreCase))
                return new DeleteStmt(p[1]);
            if (p[0].Equals("PREVIOUS", StringComparison.OrdinalIgnoreCase))
                return new PreviousStmt(p[1]);
            if (p[0].Equals("RETURN", StringComparison.OrdinalIgnoreCase))
                return new ReturnStmt(string.Join(" ", p.Skip(1)));
            if (p[0].Equals("REVERSE", StringComparison.OrdinalIgnoreCase))
                return new ReverseStmt();
            if (p[0].EndsWith(":"))
                return new AssignExprStmt(p[0].TrimEnd(':'), string.Join(" ", p.Skip(1)));
            throw new Exception($"Unknown statement: {p[0]}");
        }

        static bool IsFnKeyword(string t)
        {
            var u = t.ToUpperInvariant(); const string target = "FUNCTION";
            int ti = 0, ki = 0;
            while (ti < u.Length && ki < target.Length)
            { if (u[ti] == target[ki]) ti++; ki++; }
            return ti == u.Length && ti > 0;
        }
    }

    public class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: lumina <file>");
                return 1;
            }
            var path = args[0];
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"File not found: {path}");
                return 1;
            }

            var lines = File.ReadAllLines(path);
            var parser = new Parser(lines);
            parser.Parse();

            // Retroactive negative‐line lifetimes
            var syntheticDefs = new Dictionary<int, List<(string Var, string Expr)>>();
            for (int i = 0; i < parser.TopLevel.Count; i++)
            {
                if (parser.TopLevel[i] is AssignWithLifetimeStmt aws && aws.LineLifetime < 0)
                {
                    int defLine = i + 1;
                    int start = Math.Max(1, defLine + aws.LineLifetime);
                    for (int L = start; L < defLine; L++)
                    {
                        if (!syntheticDefs.ContainsKey(L))
                            syntheticDefs[L] = new List<(string, string)>();
                        syntheticDefs[L].Add((aws.Var, aws.Expr));
                    }
                }
            }

            var ctx = new Context();
            ctx.CallStack.Push(new Frame());
            foreach (var imp in parser.Imports)
                ctx.LoadAssembly(imp);

            int ip = ctx.Reverse ? parser.TopLevel.Count - 1 : 0;
            while (ip >= 0 && ip < parser.TopLevel.Count)
            {
                int nextLine = ctx.CurrentLine + 1;
                if (syntheticDefs.TryGetValue(nextLine, out var defs))
                {
                    foreach (var (v, e) in defs)
                        ctx.CurrentFrame.Variables[v]
                          = ExpressionParser.EvaluateExpression(ctx, e);
                }

                try { parser.TopLevel[ip].Execute(ctx); }
                catch (ReturnException) { break; }
                ip += ctx.Reverse ? -1 : 1;
            }

            var r = ctx.LastReturn;
            return r.Type == ValueType.Number ? (int)r.Number : 0;
        }
    }
}
