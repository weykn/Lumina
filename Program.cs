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
        public long Number;
        public string Str;
        public bool Bool;

        public static Value FromNumber(long n) => new Value { Type = ValueType.Number, Number = n };
        public static Value FromString(string s) => new Value { Type = ValueType.String, Str = s };
        public static Value FromBoolean(bool b) => new Value { Type = ValueType.Boolean, Bool = b };

        public override string ToString() => Type switch
        {
            ValueType.Number => Number.ToString(),
            ValueType.String => Str,
            ValueType.Boolean => Bool.ToString().ToLower(),
            _ => ""
        };

        public int CompareTo(Value other)
        {
            if (Type == ValueType.Number && other.Type == ValueType.Number)
                return Number.CompareTo(other.Number);
            if (Type == ValueType.String && other.Type == ValueType.String)
                return string.Compare(Str, other.Str, StringComparison.Ordinal);
            if (Type == ValueType.Boolean && other.Type == ValueType.Boolean)
                return Bool.CompareTo(other.Bool);
            throw new Exception($"Cannot compare {Type} with {other.Type}");
        }
    }

    public class Context
    {
        public Stack<Value> ArgStack = new();
        public Stack<Frame> CallStack = new();
        public List<Assembly> ImportedAssemblies = new();
        public HashSet<string> DisabledTokens = new(StringComparer.OrdinalIgnoreCase);
        public bool Reverse = false;
        public Value LastReturn = Value.FromNumber(0);

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

            // Built-ins
            if (BuiltIns.Map.TryGetValue(name, out var bi))
            {
                LastReturn = bi(this, new());
                return;
            }
            // Script functions
            if (Function.Definitions.TryGetValue(name, out var fn))
            {
                Call(fn);
                return;
            }
            // .NET imports
            foreach (var asm in ImportedAssemblies)
            {
                foreach (var type in asm.GetTypes())
                {
                    var m = type.GetMethod(name,
                        BindingFlags.Public | BindingFlags.Static);
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
                        _ when m.ReturnType == typeof(void)
                            => Value.FromNumber(0),
                        _ when m.ReturnType == typeof(string)
                            => Value.FromString((string)raw),
                        _ when m.ReturnType == typeof(bool)
                            => Value.FromBoolean((bool)raw),
                        _ when m.ReturnType.IsPrimitive
                            => Value.FromNumber(Convert.ToInt64(raw)),
                        _ when m.ReturnType == typeof(Value)
                            => (Value)raw,
                        _ => throw new Exception($"Unsupported return type {m.ReturnType}")
                    };
                    return;
                }
            }
            throw new Exception($"Unknown function: {name}");
        }

        public void LoadAssembly(string path)
        {
            var asm = Assembly.LoadFrom(path);
            ImportedAssemblies.Add(asm);
        }
    }

    public class Frame
    {
        public Dictionary<string, Value> Variables = new();
        public Value Lookup(string name)
        {
            if (!Variables.TryGetValue(name, out var v))
                throw new Exception($"Undefined variable or literal: {name}");
            return v;
        }
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

        public static List<string> TokenizeExpression(string expr)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < expr.Length)
            {
                char c = expr[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '"')
                {
                    int j = expr.IndexOf('"', i + 1);
                    if (j < 0) throw new Exception("Unterminated string literal");
                    tokens.Add(expr.Substring(i, j - i + 1));
                    i = j + 1;
                }
                else if ("+-*/%()".IndexOf(c) >= 0)
                {
                    tokens.Add(c.ToString());
                    i++;
                }
                else
                {
                    int j = i;
                    while (j < expr.Length && "+-*/%()\" \t\r\n".IndexOf(expr[j]) < 0)
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
                    while (ops.Count > 0
                        && Prec.ContainsKey(ops.Peek())
                        && Prec[ops.Peek()] >= Prec[t])
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
                    Value res = tok switch
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

        static Value EvaluateAtom(Context ctx, string tok)
        {
            if (ctx.DisabledTokens.Contains(tok))
                throw new Exception($"Unknown token: {tok}");
            if (ctx.CurrentFrame.Variables.ContainsKey(tok))
                return ctx.CurrentFrame.Lookup(tok);
            if (tok.StartsWith("\"") && tok.EndsWith("\"") && tok.Length >= 2)
                return Value.FromString(tok[1..^1]);
            if (tok.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                return Value.FromBoolean(true);
            if (tok.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                return Value.FromBoolean(false);
            if (long.TryParse(tok, out var n))
                return Value.FromNumber(n);
            throw new Exception($"Undefined token: {tok}");
        }
    }

    static class BuiltIns
    {
        public static readonly Dictionary<string, Func<Context, List<Value>, Value>> Map
          = new(StringComparer.OrdinalIgnoreCase)
          {
              ["PRINTLINE"] = (ctx, args) =>
              {
                  foreach (var v in args)
                      Console.WriteLine(v.ToString());
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
        }
        protected abstract void ExecuteImpl(Context ctx);
    }

    public class AssignExprStmt : Statement
    {
        public override string Keyword => ":";
        string VarName, Expr;
        public AssignExprStmt(string varName, string expr)
        { VarName = varName; Expr = expr; }
        protected override void ExecuteImpl(Context ctx)
        {
            var val = ExpressionParser.EvaluateExpression(ctx, Expr);
            ctx.CurrentFrame.Variables[VarName] = val;
        }
    }

    public class InlineCallStmt : Statement
    {
        public override string Keyword => FuncName;
        string FuncName; List<string> ArgExprs;
        public InlineCallStmt(string fn, List<string> args)
        { FuncName = fn; ArgExprs = args; }
        protected override void ExecuteImpl(Context ctx)
        {
            var vals = ArgExprs
                .Select(e => ExpressionParser.EvaluateExpression(ctx, e))
                .ToList();
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
        public DeleteStmt(string tok) { Token = tok; }
        protected override void ExecuteImpl(Context ctx)
        {
            if (ctx.CurrentFrame.Variables.Remove(Token))
                return;
            ctx.DisabledTokens.Add(Token);
        }
    }

    public class ReturnStmt : Statement
    {
        public override string Keyword => "RETURN";
        string Expr;
        public ReturnStmt(string expr) { Expr = expr; }
        protected override void ExecuteImpl(Context ctx)
        {
            ctx.LastReturn = ExpressionParser.EvaluateExpression(ctx, Expr);
            throw new ReturnException();
        }
    }

    public class ReverseStmt : Statement
    {
        public override string Keyword => "REVERSE";
        protected override void ExecuteImpl(Context ctx)
        {
            ctx.Reverse = !ctx.Reverse;
        }
    }

    public class Parser
    {
        static readonly Regex TokenRx = new("\"[^\"]*\"|[^\\s]+");
        string[] Lines;
        public List<string> Imports = new();
        public List<Statement> TopLevel = new();
        int Index;

        public Parser(string[] lines)
        {
            Lines = lines;
            Index = 0;
        }

        public void Parse()
        {
        CallStackBootstrap:
            while (Index < Lines.Length)
            {
                var raw = Lines[Index++];
                var ln = raw.Trim();
                if (string.IsNullOrWhiteSpace(ln) || ln.StartsWith("#"))
                    continue;

                var parts = TokenRx.Matches(ln)
                                .Cast<Match>()
                                .Select(m => m.Value)
                                .ToList();

                if (parts[0].Equals("IMPORT", StringComparison.OrdinalIgnoreCase) && parts.Count == 2)
                {
                    Imports.Add(parts[1].Trim('"'));
                }
                else if (IsFnKeyword(parts[0]) && parts.Count == 2)
                {
                    ParseFunction(parts[1]);
                }
                else
                {
                    TopLevel.Add(ParseStmt(parts));
                }
            }
        }

        void ParseFunction(string name)
        {
            var func = new Function(name);
            while (Index < Lines.Length)
            {
                var raw = Lines[Index++];
                var ln = raw.Trim();
                if (string.IsNullOrWhiteSpace(ln) || ln.StartsWith("#"))
                    continue;
                if (ln.Equals("END", StringComparison.OrdinalIgnoreCase))
                    break;
                var parts = TokenRx.Matches(ln)
                                .Cast<Match>()
                                .Select(m => m.Value)
                                .ToList();
                func.Body.Add(ParseStmt(parts));
            }
            Function.Definitions[name] = func;
        }

        Statement ParseStmt(List<string> p)
        {
            if (p[0].StartsWith("!"))
                return new InlineCallStmt(p[0].Substring(1), p.Skip(1).ToList());
            if (p[0].Equals("DELETE", StringComparison.OrdinalIgnoreCase))
                return new DeleteStmt(p[1]);
            if (p[0].Equals("RETURN", StringComparison.OrdinalIgnoreCase))
                return new ReturnStmt(string.Join(" ", p.Skip(1)));
            if (p[0].Equals("REVERSE", StringComparison.OrdinalIgnoreCase))
                return new ReverseStmt();
            if (p[0].EndsWith(":"))
                return new AssignExprStmt(p[0].TrimEnd(':'), string.Join(" ", p.Skip(1)));
            throw new Exception($"Unknown statement: {p[0]}");
        }

        static bool IsFnKeyword(string token)
        {
            var t = token.ToUpperInvariant();
            var target = "FUNCTION";
            int ti = 0, ki = 0;
            while (ti < t.Length && ki < target.Length)
            {
                if (t[ti] == target[ki]) ti++;
                ki++;
            }
            return ti == t.Length && ti > 0;
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

            var ctx = new Context();
            ctx.CallStack.Push(new Frame());
            foreach (var dll in parser.Imports)
                ctx.LoadAssembly(dll);

            int ip = ctx.Reverse ? parser.TopLevel.Count - 1 : 0;
            while (ip >= 0 && ip < parser.TopLevel.Count)
            {
                try
                {
                    parser.TopLevel[ip].Execute(ctx);
                }
                catch (ReturnException)
                {
                    break;
                }
                ip += ctx.Reverse ? -1 : 1;
            }

            var r = ctx.LastReturn;
            return r.Type == ValueType.Number ? (int)r.Number : 0;
        }
    }
}
