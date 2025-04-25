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
        public HashSet<string> DisabledTokens = new(StringComparer.Ordinal);
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
            // 1) built-in?
            if (BuiltIns.Map.TryGetValue(name, out var bi))
            {
                LastReturn = bi(this, new());
                return;
            }

            // 2) script function?
            if (Function.Definitions.TryGetValue(name, out var fn))
            {
                Call(fn);
                return;
            }

            // 3) external assembly
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
        // operator precedence
        static readonly Dictionary<string, int> Prec = new(StringComparer.Ordinal)
        {
            { "+",1 },{ "-",1 },{ "*",2 },{ "/",2 },{ "%",2 }
        };

        // Lex an expression string into tokens
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

        // Shunting-yard → RPN
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
                else if (t == "(")
                {
                    ops.Push(t);
                }
                else if (t == ")")
                {
                    while (ops.Count > 0 && ops.Peek() != "(")
                        outq.Add(ops.Pop());
                    if (ops.Count == 0) throw new Exception("Mismatched parentheses");
                    ops.Pop();
                }
                else
                {
                    outq.Add(t);
                }
            }
            while (ops.Count > 0)
            {
                var o = ops.Pop();
                if (o == "(" || o == ")") throw new Exception("Mismatched parentheses");
                outq.Add(o);
            }
            return outq;
        }

        // Evaluate an RPN expression, checking disabled tokens for operators
        public static Value EvalRPN(Context ctx, List<string> rpn)
        {
            var st = new Stack<Value>();
            foreach (var tok in rpn)
            {
                if (Prec.ContainsKey(tok))
                {
                    // operator
                    if (ctx.DisabledTokens.Contains(tok))
                        throw new Exception($"Unknown token: {tok}");
                    if (st.Count < 2) throw new Exception("Bad expression");
                    var b = st.Pop();
                    var a = st.Pop();
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
                    // operand
                    st.Push(EvaluateAtom(ctx, tok));
                }
            }
            if (st.Count != 1) throw new Exception("Bad expression");
            return st.Pop();
        }

        // Evaluate either a single atom or a full expression
        public static Value EvaluateExpression(Context ctx, string expr)
        {
            var toks = TokenizeExpression(expr);
            // single token? no operators/parentheses
            if (toks.Count == 1 && !Prec.ContainsKey(toks[0]) && toks[0] != "(" && toks[0] != ")")
                return EvaluateAtom(ctx, toks[0]);
            // else full expression
            var rpn = ToRPN(toks);
            return EvalRPN(ctx, rpn);
        }

        // Lookup a literal, variable, or boolean
        static Value EvaluateAtom(Context ctx, string tok)
        {
            if (ctx.DisabledTokens.Contains(tok))
                throw new Exception($"Unknown token: {tok}");

            // variable first
            if (ctx.CurrentFrame.Variables.ContainsKey(tok))
                return ctx.CurrentFrame.Lookup(tok);

            // string literal
            if (tok.StartsWith("\"") && tok.EndsWith("\"") && tok.Length >= 2)
                return Value.FromString(tok[1..^1]);

            // boolean
            if (tok.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                return Value.FromBoolean(true);
            if (tok.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                return Value.FromBoolean(false);

            // numeric literal
            if (long.TryParse(tok, out var n))
                return Value.FromNumber(n);

            throw new Exception($"Undefined variable or literal: {tok}");
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
        /// <summary>
        /// The token that invoked this statement (keyword or function name or ":").
        /// If DisabledTokens contains this, execution errors.
        /// </summary>
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
        string VarName;
        string Expr;
        public AssignExprStmt(string varName, string expr)
        {
            VarName = varName;
            Expr = expr;
        }
        protected override void ExecuteImpl(Context ctx)
        {
            var val = ExpressionParser.EvaluateExpression(ctx, Expr);
            ctx.CurrentFrame.Variables[VarName] = val;
        }
    }

    public class InlineCallStmt : Statement
    {
        public override string Keyword => FuncName;
        string FuncName;
        List<string> ArgExprs;
        public InlineCallStmt(string fn, List<string> args)
        {
            FuncName = fn;
            ArgExprs = args;
        }
        protected override void ExecuteImpl(Context ctx)
        {
            var vals = ArgExprs
                .Select(expr => ExpressionParser.EvaluateExpression(ctx, expr))
                .ToList();

            // built-in?
            if (BuiltIns.Map.TryGetValue(FuncName, out var bi))
            {
                ctx.LastReturn = bi(ctx, vals);
                return;
            }

            // otherwise push and call
            foreach (var v in vals)
                ctx.ArgStack.Push(v);
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
            // remove variable if present
            if (ctx.CurrentFrame.Variables.Remove(Token))
                return;
            // else disable every use of this token
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

    public class Parser
    {
        static readonly Regex TokenRx = new("\"[^\"]*\"|[^\\s]+");
        string[] Lines;
        public List<string> Imports = new();
        int Index;

        public Parser(string[] lines)
        {
            Lines = lines;
            Index = 0;
        }

        public void Parse()
        {
            while (Index < Lines.Length)
            {
                var raw = Lines[Index++];
                var ln = raw.Trim();
                if (string.IsNullOrWhiteSpace(ln) || ln.StartsWith("#"))
                    continue;

                var parts = Tokenize(ln);
                if (parts[0] == "IMPORT" && parts.Count == 2)
                {
                    Imports.Add(parts[1].Trim('"'));
                }
                else if (parts[0] == "DEFINE" && parts.Count == 2)
                {
                    ParseFunction(parts[1]);
                }
                else
                {
                    throw new Exception($"Unexpected token: {ln}");
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
                if (ln == "END")
                    break;

                var parts = Tokenize(ln);
                Statement stmt;

                if (parts[0].StartsWith("!"))
                {
                    // inline call
                    stmt = new InlineCallStmt(parts[0].Substring(1), parts.Skip(1).ToList());
                }
                else if (parts[0] == "DELETE")
                {
                    stmt = new DeleteStmt(parts[1]);
                }
                else if (parts[0] == "RETURN")
                {
                    var expr = string.Join(" ", parts.Skip(1));
                    stmt = new ReturnStmt(expr);
                }
                else if (parts[0].EndsWith(":"))
                {
                    var varName = parts[0].TrimEnd(':');
                    var expr = string.Join(" ", parts.Skip(1));
                    stmt = new AssignExprStmt(varName, expr);
                }
                else
                {
                    throw new Exception($"Unknown statement: {parts[0]}");
                }

                func.Body.Add(stmt);
            }

            Function.Definitions[name] = func;
        }

        List<string> Tokenize(string ln)
        {
            var ms = TokenRx.Matches(ln);
            return ms.Cast<Match>().Select(m => m.Value).ToList();
        }
    }

    public class Program
    {
        // MAIN's numeric return becomes the process exit code
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

            try
            {
                parser.Parse();
                var ctx = new Context();
                // push initial frame so CurrentFrame is valid
                ctx.Call(new Function("__bootstrap__") { Body = new List<Statement>() });
                foreach (var dll in parser.Imports)
                    ctx.LoadAssembly(dll);

                if (!Function.Definitions.ContainsKey("MAIN"))
                    throw new Exception("MAIN not defined.");

                ctx.Call(Function.Definitions["MAIN"]);
                var r = ctx.LastReturn;
                return r.Type == ValueType.Number
                    ? (int)r.Number
                    : 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }
    }
}
