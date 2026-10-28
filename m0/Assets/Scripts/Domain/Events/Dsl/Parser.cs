using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ekiden.Domain.Events
{
    /// <summary>条件式の読み込み時(字句・構文・検証)の誤り。実行時には投げない(06 §3)。</summary>
    public sealed class DslException : Exception
    {
        public DslException(string message) : base(message) { }
    }

    public enum NodeKind { Num, Str, Bool, Var, Call, Neg, Not, And, Or, Cmp, Bin }

    /// <summary>構文木(型検査の前)。ref_model_06.py の構文木のタプルと同じ形。</summary>
    public sealed class Node
    {
        public NodeKind Kind;
        public double Num;             // Num / Bool(1 or 0)
        public string Text;            // Str の中身 / Var・Call の名前 / Cmp・Bin の演算子
        public Node Left, Right;       // Neg・Not は Left だけ
        public List<Node> Args;        // Call

        /// <summary>構文木の確認用の文字列。例: <c>(or (and (== r.grade 1) (== r.far 1)) (== r.overseas 1))</c>。</summary>
        public override string ToString()
        {
            switch (Kind)
            {
                case NodeKind.Num: return Num == Math.Floor(Num) && Math.Abs(Num) < 1e15 ? ((long)Num).ToString(CultureInfo.InvariantCulture) : Num.ToString("R", CultureInfo.InvariantCulture);
                case NodeKind.Str: return "'" + Text + "'";
                case NodeKind.Bool: return Num != 0 ? "true" : "false";
                case NodeKind.Var: return Text;
                case NodeKind.Neg: return "(neg " + Left + ")";
                case NodeKind.Not: return "(not " + Left + ")";
                case NodeKind.And: return "(and " + Left + " " + Right + ")";
                case NodeKind.Or: return "(or " + Left + " " + Right + ")";
                case NodeKind.Cmp:
                case NodeKind.Bin: return "(" + Text + " " + Left + " " + Right + ")";
                default:
                    var sb = new StringBuilder("(" + Text);
                    foreach (Node a in Args) sb.Append(' ').Append(a);
                    return sb.Append(')').ToString();
            }
        }
    }

    internal enum TokKind { Num, Str, Kw, Id, Op, End }

    internal struct Token
    {
        public TokKind Kind;
        public string Text;
        public double Num;
        public Token(TokKind k, string text, double num = 0) { Kind = k; Text = text; Num = num; }
        public bool Is(TokKind k, string text) { return Kind == k && Text == text; }
        public override string ToString() { return Kind + ":" + Text; }
    }

    /// <summary>条件式の字句解析と構文解析(06 §3.1)。</summary>
    public sealed class Parser
    {
        private readonly string _src;
        private readonly List<Token> _toks;
        private int _i;

        private Parser(string src)
        {
            _src = src;
            _toks = Tokenize(src);
        }

        public static Node Parse(string src)
        {
            var p = new Parser(src);
            Node e = p.POr();
            p.Eat(TokKind.End, null);
            return e;
        }

        private static bool IsKeyword(string w)
        {
            return w == "and" || w == "or" || w == "not" || w == "true" || w == "false";
        }

        private static List<Token> Tokenize(string src)
        {
            var toks = new List<Token>();
            int i = 0, n = src.Length;
            while (i < n)
            {
                char c = src[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                }
                else if (IsAsciiDigit(c) || (c == '.' && i + 1 < n && IsAsciiDigit(src[i + 1])))
                {
                    int j = i;
                    while (j < n && (IsAsciiDigit(src[j]) || src[j] == '.')) j++;
                    string text = src.Substring(i, j - i);
                    double v;
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) throw new DslException("不正な数値 " + text + ": " + src);
                    toks.Add(new Token(TokKind.Num, text, v));
                    i = j;
                }
                else if (c == '\'')
                {
                    int j = src.IndexOf('\'', i + 1);
                    if (j < 0) throw new DslException("文字列が閉じていない: " + src);
                    toks.Add(new Token(TokKind.Str, src.Substring(i + 1, j - i - 1)));
                    i = j + 1;
                }
                else if (char.IsLetter(c) || c == '_')
                {
                    int j = i;
                    while (j < n && (char.IsLetterOrDigit(src[j]) || src[j] == '.' || src[j] == '_')) j++;
                    string w = src.Substring(i, j - i);
                    toks.Add(new Token(IsKeyword(w) ? TokKind.Kw : TokKind.Id, w));
                    i = j;
                }
                else
                {
                    string two = i + 1 < n ? src.Substring(i, 2) : null;
                    if (two == "<=" || two == ">=" || two == "==" || two == "!=")
                    {
                        toks.Add(new Token(TokKind.Op, two));
                        i += 2;
                    }
                    else if ("<>+-*/(),".IndexOf(c) >= 0)
                    {
                        toks.Add(new Token(TokKind.Op, c.ToString()));
                        i++;
                    }
                    else
                    {
                        throw new DslException("不正な文字 '" + c + "': " + src);
                    }
                }
            }
            toks.Add(new Token(TokKind.End, null));
            return toks;
        }

        private static bool IsAsciiDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        private Token Peek() { return _toks[_i]; }

        private Token Eat(TokKind kind, string text)
        {
            Token t = _toks[_i];
            if (t.Kind != kind || (text != null && t.Text != text)) throw new DslException("構文エラー(" + t + "): " + _src);
            _i++;
            return t;
        }

        private Node POr()
        {
            Node e = PAnd();
            while (Peek().Is(TokKind.Kw, "or"))
            {
                _i++;
                e = new Node { Kind = NodeKind.Or, Left = e, Right = PAnd() };
            }
            return e;
        }

        private Node PAnd()
        {
            Node e = PNot();
            while (Peek().Is(TokKind.Kw, "and"))
            {
                _i++;
                e = new Node { Kind = NodeKind.And, Left = e, Right = PNot() };
            }
            return e;
        }

        private Node PNot()
        {
            if (Peek().Is(TokKind.Kw, "not"))
            {
                _i++;
                return new Node { Kind = NodeKind.Not, Left = PNot() };
            }
            return PCmp();
        }

        private static bool IsCmp(Token t)
        {
            return t.Kind == TokKind.Op && (t.Text == "<" || t.Text == "<=" || t.Text == ">" || t.Text == ">=" || t.Text == "==" || t.Text == "!=");
        }

        private Node PCmp()
        {
            Node e = PSum();
            Token t = Peek();
            if (IsCmp(t))
            {
                _i++;
                return new Node { Kind = NodeKind.Cmp, Text = t.Text, Left = e, Right = PSum() };
            }
            return e;
        }

        private Node PSum()
        {
            Node e = PTerm();
            while (Peek().Kind == TokKind.Op && (Peek().Text == "+" || Peek().Text == "-"))
            {
                string op = _toks[_i++].Text;
                e = new Node { Kind = NodeKind.Bin, Text = op, Left = e, Right = PTerm() };
            }
            return e;
        }

        private Node PTerm()
        {
            Node e = PUnary();
            while (Peek().Kind == TokKind.Op && (Peek().Text == "*" || Peek().Text == "/"))
            {
                string op = _toks[_i++].Text;
                e = new Node { Kind = NodeKind.Bin, Text = op, Left = e, Right = PUnary() };
            }
            return e;
        }

        private Node PUnary()
        {
            if (Peek().Is(TokKind.Op, "-"))
            {
                _i++;
                return new Node { Kind = NodeKind.Neg, Left = PUnary() };
            }
            return PAtom();
        }

        private Node PAtom()
        {
            Token t = Peek();
            if (t.Kind == TokKind.Num)
            {
                _i++;
                return new Node { Kind = NodeKind.Num, Num = t.Num };
            }
            if (t.Kind == TokKind.Str)
            {
                _i++;
                return new Node { Kind = NodeKind.Str, Text = t.Text };
            }
            if (t.Is(TokKind.Kw, "true") || t.Is(TokKind.Kw, "false"))
            {
                _i++;
                return new Node { Kind = NodeKind.Bool, Num = t.Text == "true" ? 1 : 0 };
            }
            if (t.Is(TokKind.Op, "("))
            {
                _i++;
                Node e = POr();
                Eat(TokKind.Op, ")");
                return e;
            }
            if (t.Kind == TokKind.Id)
            {
                _i++;
                if (Peek().Is(TokKind.Op, "("))
                {
                    _i++;
                    var args = new List<Node>();
                    if (!Peek().Is(TokKind.Op, ")"))
                    {
                        args.Add(POr());
                        while (Peek().Is(TokKind.Op, ","))
                        {
                            _i++;
                            args.Add(POr());
                        }
                    }
                    Eat(TokKind.Op, ")");
                    return new Node { Kind = NodeKind.Call, Text = t.Text, Args = args };
                }
                return new Node { Kind = NodeKind.Var, Text = t.Text };
            }
            throw new DslException("構文エラー(" + t + "): " + _src);
        }
    }
}
