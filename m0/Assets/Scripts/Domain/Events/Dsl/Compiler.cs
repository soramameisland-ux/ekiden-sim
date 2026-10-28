using System;
using System.Collections.Generic;
using Ekiden.Domain.Entities;

namespace Ekiden.Domain.Events
{
    /// <summary>条件式の中の変数の種類(06 §3.2)。名前は読み込み時に一度だけ解決する。</summary>
    public enum VarKind
    {
        CalYear, CalMonth, CalWeek, CalNextRace, CalNextRaceKind, RaceKind, RaceTeamRank, RaceExpectedRank,
        Fac, TFunds, TPrestige, TMorale, TSize, TCoachRank, TScoutPts, TGuidance, TGrade, TGrade3Plus, ObTotal, ObCorp, TRank, TLastRank,
        RId, RStat, RInjury, RInjuryWeeks, RInjuryAge, RWeeksIn, RGift, RStyle, RSkills, RTaper, RGrade, RFatigue, RForm, RMotivation,
        RGroup, RCaptain, ROverseas, RFar, RNear, RPref, RBondAvg, RPRank, RRegular, RRaces, RSectionWins, RBrakes, RGapAvg, RRaceFlag,
    }

    public enum FuncKind { HasSkill, Ctr, Bond, SamePrefN, SeniorSamePref, ObLevel, RivalSameHs, Min, Max, Abs, Clamp }

    /// <summary>実行できる式の木。評価は副作用なし・乱数なし・例外なし(ゼロ除算は 0)。真偽は 1 / 0 の数。</summary>
    public abstract class Expr
    {
        public abstract double Eval(EventContext c);
    }

    internal sealed class NumExpr : Expr
    {
        private readonly double _v;
        public NumExpr(double v) { _v = v; }
        public override double Eval(EventContext c) { return _v; }
    }

    internal sealed class NegExpr : Expr
    {
        private readonly Expr _a;
        public NegExpr(Expr a) { _a = a; }
        public override double Eval(EventContext c) { return -_a.Eval(c); }
    }

    internal sealed class NotExpr : Expr
    {
        private readonly Expr _a;
        public NotExpr(Expr a) { _a = a; }
        public override double Eval(EventContext c) { return _a.Eval(c) != 0 ? 0.0 : 1.0; }
    }

    internal sealed class AndExpr : Expr
    {
        private readonly Expr _a, _b;
        public AndExpr(Expr a, Expr b) { _a = a; _b = b; }
        public override double Eval(EventContext c) { return _a.Eval(c) != 0 && _b.Eval(c) != 0 ? 1.0 : 0.0; }
    }

    internal sealed class OrExpr : Expr
    {
        private readonly Expr _a, _b;
        public OrExpr(Expr a, Expr b) { _a = a; _b = b; }
        public override double Eval(EventContext c) { return _a.Eval(c) != 0 || _b.Eval(c) != 0 ? 1.0 : 0.0; }
    }

    internal sealed class CmpExpr : Expr
    {
        private readonly string _op;
        private readonly Expr _a, _b;
        public CmpExpr(string op, Expr a, Expr b) { _op = op; _a = a; _b = b; }

        public override double Eval(EventContext c)
        {
            double a = _a.Eval(c), b = _b.Eval(c);
            bool r;
            switch (_op)
            {
                case "<": r = a < b; break;
                case "<=": r = a <= b; break;
                case ">": r = a > b; break;
                case ">=": r = a >= b; break;
                case "==": r = a == b; break;
                default: r = a != b; break;
            }
            return r ? 1.0 : 0.0;
        }
    }

    internal sealed class BinExpr : Expr
    {
        private readonly char _op;
        private readonly Expr _a, _b;
        public BinExpr(char op, Expr a, Expr b) { _op = op; _a = a; _b = b; }

        public override double Eval(EventContext c)
        {
            double a = _a.Eval(c), b = _b.Eval(c);
            switch (_op)
            {
                case '+': return a + b;
                case '-': return a - b;
                case '*': return a * b;
                default: return b != 0 ? a / b : 0.0;
            }
        }
    }

    internal sealed class VarExpr : Expr
    {
        private readonly VarKind _kind;
        private readonly bool _pRole;
        private readonly int _arg;

        public VarExpr(VarKind kind, bool pRole, int arg) { _kind = kind; _pRole = pRole; _arg = arg; }

        public override double Eval(EventContext c)
        {
            return c.Get(_kind, _pRole ? c.P : c.R, _arg);
        }
    }

    internal sealed class CallExpr : Expr
    {
        private readonly FuncKind _f;
        private readonly Expr[] _args;
        private readonly bool[] _pRole;       // 役割の引数(r / p)。数の引数のときは使わない
        private readonly string _str;

        public CallExpr(FuncKind f, Expr[] args, bool[] pRole, string str) { _f = f; _args = args; _pRole = pRole; _str = str; }

        private Runner Who(EventContext c, int i) { return _pRole[i] ? c.P : c.R; }

        public override double Eval(EventContext c)
        {
            switch (_f)
            {
                case FuncKind.HasSkill: return Who(c, 0).Skills.Contains(EventConstants.SkillPrefix + _str) ? 1.0 : 0.0;
                case FuncKind.Ctr: return Who(c, 0).Ctr(_str);
                case FuncKind.Bond: return c.State.Bonds.Get(Who(c, 0).Id, Who(c, 1).Id);
                case FuncKind.SamePrefN: return c.SamePrefN(Who(c, 0));
                case FuncKind.SeniorSamePref: return c.SeniorSamePref(Who(c, 0)) ? 1.0 : 0.0;
                case FuncKind.ObLevel: return c.State.ObLevel(Who(c, 0));
                case FuncKind.RivalSameHs: return c.World.RivalSameHs(Who(c, 0), c.State.AbsWeek) ? 1.0 : 0.0;
                case FuncKind.Min: return Math.Min(_args[0].Eval(c), _args[1].Eval(c));
                case FuncKind.Max: return Math.Max(_args[0].Eval(c), _args[1].Eval(c));
                case FuncKind.Abs: return Math.Abs(_args[0].Eval(c));
                default:
                    {
                        double x = _args[0].Eval(c), lo = _args[1].Eval(c), hi = _args[2].Eval(c);
                        return Math.Max(lo, Math.Min(hi, x));
                    }
            }
        }
    }

    /// <summary>読み込み・検証の済んだ条件式。</summary>
    public sealed class CompiledExpr
    {
        public string Source { get; }
        private readonly Expr _root;

        internal CompiledExpr(string source, Expr root) { Source = source; _root = root; }

        public double Eval(EventContext c) { return _root.Eval(c); }

        public bool Test(EventContext c) { return _root.Eval(c) != 0; }
    }

    /// <summary>条件式の検証と実行形への変換(06 §3、§13 V-02)。変数・関数・引数の数・役割の束縛を調べ、実行時に誤りが起きない形にする。</summary>
    public static class DslCompiler
    {
        public const int RoleR = 1, RoleP = 2;

        private static readonly string[] FacNames = { "dorm", "track", "hill", "gym", "medical", "coach", "trainer", "nutri", "scout" };
        private static readonly string[] RankNames = { "fall", "national", "newyear" };
        private static readonly string[] GiftNames = { "climber", "descender", "ace" };
        private static readonly string[] StyleNames = { "is_speed", "is_long", "is_pack", "is_solo", "is_chase", "is_lead" };
        private static readonly string[] RaceFlagNames = { "race.in_lineup", "race.brake", "race.section_rank", "race.section_win", "race.record" };
        private static readonly string[] StatNames = { "SPD", "STA", "PAC", "KCK", "CLB", "DWN", "CON", "MEN", "REC", "DUR" };

        private static readonly Dictionary<string, VarKind> TeamVars = new Dictionary<string, VarKind>
        {
            { "cal.year", VarKind.CalYear }, { "cal.month", VarKind.CalMonth }, { "cal.week", VarKind.CalWeek }, { "cal.next_race", VarKind.CalNextRace },
            { "cal.next_race_kind", VarKind.CalNextRaceKind }, { "race.kind", VarKind.RaceKind }, { "race.team_rank", VarKind.RaceTeamRank },
            { "race.expected_rank", VarKind.RaceExpectedRank }, { "t.funds", VarKind.TFunds }, { "t.prestige", VarKind.TPrestige }, { "t.morale", VarKind.TMorale },
            { "t.size", VarKind.TSize }, { "t.coach_rank", VarKind.TCoachRank }, { "t.scout_pts", VarKind.TScoutPts }, { "t.guidance", VarKind.TGuidance },
            { "t.n_g3plus", VarKind.TGrade3Plus }, { "ob.total", VarKind.ObTotal }, { "ob.corp", VarKind.ObCorp },
        };

        private static readonly Dictionary<string, VarKind> RunnerVars = new Dictionary<string, VarKind>
        {
            { "id", VarKind.RId }, { "grade", VarKind.RGrade }, { "fatigue", VarKind.RFatigue }, { "form", VarKind.RForm }, { "motivation", VarKind.RMotivation },
            { "injury", VarKind.RInjury }, { "injury_weeks", VarKind.RInjuryWeeks }, { "injury_age", VarKind.RInjuryAge }, { "group", VarKind.RGroup },
            { "captain", VarKind.RCaptain }, { "overseas", VarKind.ROverseas }, { "far", VarKind.RFar }, { "near", VarKind.RNear }, { "pref", VarKind.RPref },
            { "weeks_in", VarKind.RWeeksIn }, { "bond_avg", VarKind.RBondAvg }, { "p_rank", VarKind.RPRank }, { "regular", VarKind.RRegular },
            { "taper", VarKind.RTaper }, { "races", VarKind.RRaces }, { "section_wins", VarKind.RSectionWins }, { "brakes", VarKind.RBrakes },
            { "skills", VarKind.RSkills }, { "gap_avg", VarKind.RGapAvg },
        };

        private static readonly Dictionary<string, int> FuncArity = new Dictionary<string, int>
        {
            { "has_skill", 2 }, { "ctr", 2 }, { "bond", 2 }, { "same_pref_n", 1 }, { "senior_same_pref", 1 }, { "ob_level", 1 }, { "rival_same_hs", 1 },
            { "min", 2 }, { "max", 2 }, { "abs", 1 }, { "clamp", 3 },
        };

        private static readonly Dictionary<string, FuncKind> FuncKinds = new Dictionary<string, FuncKind>
        {
            { "has_skill", FuncKind.HasSkill }, { "ctr", FuncKind.Ctr }, { "bond", FuncKind.Bond }, { "same_pref_n", FuncKind.SamePrefN },
            { "senior_same_pref", FuncKind.SeniorSamePref }, { "ob_level", FuncKind.ObLevel }, { "rival_same_hs", FuncKind.RivalSameHs },
            { "min", FuncKind.Min }, { "max", FuncKind.Max }, { "abs", FuncKind.Abs }, { "clamp", FuncKind.Clamp },
        };

        /// <summary>構文を読み、検証し、実行形にする。誤りは <see cref="DslException"/>(メッセージは参照モデルと同じ)。</summary>
        /// <param name="roles">束縛できる役割の集合(<see cref="RoleR"/> | <see cref="RoleP"/>)。team のイベントは 0。</param>
        public static CompiledExpr Compile(string src, int roles)
        {
            Node n = Parser.Parse(src);
            return new CompiledExpr(src, Build(n, roles, src));
        }

        public static bool TryCompile(string src, int roles, out CompiledExpr expr, out string error)
        {
            try
            {
                expr = Compile(src, roles);
                error = null;
                return true;
            }
            catch (DslException x)
            {
                expr = null;
                error = x.Message;
                return false;
            }
        }

        /// <summary>イベントの範囲(scope)ごとに束縛できる役割。</summary>
        public static int RolesOf(EventScope scope)
        {
            return scope == EventScope.Pair ? RoleR | RoleP : scope == EventScope.Runner ? RoleR : 0;
        }

        private static Expr Build(Node n, int roles, string src)
        {
            switch (n.Kind)
            {
                case NodeKind.Num: return new NumExpr(n.Num);
                case NodeKind.Bool: return new NumExpr(n.Num);
                case NodeKind.Str: throw new DslException("文字列は特殊能力・カウンターの指定にだけ使える: " + src);
                case NodeKind.Var: return BuildVar(n.Text, roles, src);
                case NodeKind.Neg: return new NegExpr(Build(n.Left, roles, src));
                case NodeKind.Not: return new NotExpr(Build(n.Left, roles, src));
                case NodeKind.And: { Expr a = Build(n.Left, roles, src); return new AndExpr(a, Build(n.Right, roles, src)); }
                case NodeKind.Or: { Expr a = Build(n.Left, roles, src); return new OrExpr(a, Build(n.Right, roles, src)); }
                case NodeKind.Cmp: { Expr a = Build(n.Left, roles, src); return new CmpExpr(n.Text, a, Build(n.Right, roles, src)); }
                case NodeKind.Bin: { Expr a = Build(n.Left, roles, src); return new BinExpr(n.Text[0], a, Build(n.Right, roles, src)); }
                default: return BuildCall(n, roles, src);
            }
        }

        private static Expr BuildVar(string name, int roles, string src)
        {
            // 役割そのもの(r / p)は、関数の引数にだけ使える
            if (name == "r" || name == "p")
            {
                CheckRole(name, roles, src);
                throw new DslException("役割 " + name + " は関数の引数にだけ使える: " + src);
            }
            VarKind kind;
            int arg = 0;
            bool pRole = false;
            if (!Resolve(name, out kind, out arg, out pRole)) throw new DslException("未知の変数 " + name + ": " + src);
            if (name.StartsWith("r.", StringComparison.Ordinal) || name.StartsWith("p.", StringComparison.Ordinal)) CheckRole(name.Substring(0, 1), roles, src);
            return new VarExpr(kind, pRole, arg);
        }

        private static void CheckRole(string role, int roles, string src)
        {
            int bit = role == "r" ? RoleR : RoleP;
            if ((roles & bit) == 0) throw new DslException("束縛されていない役割 " + role + ": " + src);
        }

        private static bool Resolve(string name, out VarKind kind, out int arg, out bool pRole)
        {
            arg = 0;
            pRole = false;
            if (TeamVars.TryGetValue(name, out kind)) return true;
            if (name.Length == 6 && name.StartsWith("t.n_g", StringComparison.Ordinal) && name[5] >= '1' && name[5] <= '4')
            {
                kind = VarKind.TGrade; arg = name[5] - '0';
                return true;
            }
            if (name.StartsWith("fac.", StringComparison.Ordinal))
            {
                int i = Array.IndexOf(FacNames, name.Substring(4));
                if (i >= 0 && i < 5) { kind = VarKind.Fac; arg = i; return true; }
            }
            if (name.StartsWith("staff.", StringComparison.Ordinal))
            {
                int i = Array.IndexOf(FacNames, name.Substring(6));
                if (i >= 5) { kind = VarKind.Fac; arg = i; return true; }
            }
            if (name.StartsWith("t.rank.", StringComparison.Ordinal))
            {
                int i = Array.IndexOf(RankNames, name.Substring(7));
                if (i >= 0) { kind = VarKind.TRank; arg = i; return true; }
            }
            if (name.StartsWith("t.last_rank.", StringComparison.Ordinal))
            {
                int i = Array.IndexOf(RankNames, name.Substring(12));
                if (i >= 0) { kind = VarKind.TLastRank; arg = i; return true; }
            }
            if (name.StartsWith("r.", StringComparison.Ordinal) || name.StartsWith("p.", StringComparison.Ordinal))
            {
                pRole = name[0] == 'p';
                string f = name.Substring(2);
                if (RunnerVars.TryGetValue(f, out kind)) return true;
                if (f.StartsWith("stat.", StringComparison.Ordinal))
                {
                    int i = Array.IndexOf(StatNames, f.Substring(5));
                    if (i >= 0) { kind = VarKind.RStat; arg = i; return true; }
                }
                if (f.StartsWith("gift.", StringComparison.Ordinal))
                {
                    int i = Array.IndexOf(GiftNames, f.Substring(5));
                    if (i >= 0) { kind = VarKind.RGift; arg = i; return true; }
                }
                int s = Array.IndexOf(StyleNames, f);
                if (s >= 0) { kind = VarKind.RStyle; arg = s; return true; }
                int rf = Array.IndexOf(RaceFlagNames, f);
                if (rf >= 0) { kind = VarKind.RRaceFlag; arg = rf; return true; }
            }
            kind = VarKind.CalYear;
            return false;
        }

        private static Expr BuildCall(Node n, int roles, string src)
        {
            int arity;
            if (!FuncArity.TryGetValue(n.Text, out arity) || n.Args.Count != arity) throw new DslException("未知の関数または引数の数 " + n.Text + ": " + src);
            FuncKind f = FuncKinds[n.Text];
            var args = new Expr[arity];
            var pRole = new bool[arity];
            string str = null;
            switch (f)
            {
                case FuncKind.HasSkill:
                case FuncKind.Ctr:
                    pRole[0] = RoleArg(n.Args[0], roles, src);
                    if (n.Args[1].Kind != NodeKind.Str) throw new DslException("第2引数は文字列: " + n.Text + ": " + src);
                    str = n.Args[1].Text;
                    if (f == FuncKind.Ctr && Array.IndexOf(Counters.All, str) < 0) throw new DslException("未知のカウンター " + str + ": " + src);
                    break;
                case FuncKind.Bond:
                    pRole[0] = RoleArg(n.Args[0], roles, src);
                    pRole[1] = RoleArg(n.Args[1], roles, src);
                    break;
                case FuncKind.SamePrefN:
                case FuncKind.SeniorSamePref:
                case FuncKind.ObLevel:
                case FuncKind.RivalSameHs:
                    pRole[0] = RoleArg(n.Args[0], roles, src);
                    break;
                default:
                    for (int i = 0; i < arity; i++) args[i] = Build(n.Args[i], roles, src);
                    break;
            }
            return new CallExpr(f, args, pRole, str);
        }

        /// <summary>役割の引数(r または p)。戻り値は p かどうか。</summary>
        private static bool RoleArg(Node a, int roles, string src)
        {
            if (a.Kind != NodeKind.Var || (a.Text != "r" && a.Text != "p")) throw new DslException("役割(r か p)を指定する: " + src);
            CheckRole(a.Text, roles, src);
            return a.Text == "p";
        }
    }
}
