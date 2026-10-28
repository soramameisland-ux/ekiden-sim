using System;
using System.Collections.Generic;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Numerics;

namespace Ekiden.Domain.Race
{
    public enum PlanMode { Plain, Auto, Skilled, SkilledSwap, SkilledInstr, Gauge, AllAttack, AllHold }

    /// <summary>当日の計画: 区間の期待時間・余裕の目安・指示・当日変更(04 §5.3・§7.4・§9.1)。</summary>
    public static class DayPlanner
    {
        private static readonly Instruction[] All = { Instruction.Attack, Instruction.Keep, Instruction.Hold };

        /// <summary>区間 si を r が走ったときの期待時間(秒)。調子・環境・ペース配分を含む(雑音・ブレーキ・展開は除く)。</summary>
        public static double LegEv(RaceRunner r, int si, RaceKind race, Env env, Instruction instr)
        {
            IReadOnlyList<SectionDef> secs = RaceTables.Course(race);
            SectionDef sec = secs[si];
            int longest = 0;
            for (int i = 1; i < secs.Count; i++) if (secs[i].Distance > secs[longest].Distance) longest = i;
            double p = RaceMath.SectionRating(r, sec.Type, si == longest, si == secs.Count - 1);
            double bt = RaceMath.BaseTime(sec.Distance, sec.Terrain, p);
            int ii = (int)instr;
            double q = Math.Min(0.95, RaceMath.PaceQ(r, sec.Distance, sec.Type, env.Temp) * RaceConstants.InstrStall[ii]);
            double c = (RaceConstants.StallLo + RaceConstants.StallHi) / 2;
            double con = r.Stats[(int)Stat.CON], men = r.Stats[(int)Stat.MEN];
            double pb = RaceConstants.BrakeP * (1.5 - con / 100.0) * Math.Max(0.5, Math.Min(1.5, 1.5 - men / 100.0)) * RaceTables.Rule(race).Weight * RaceConstants.InstrBrake[ii];
            double cb = (RaceConstants.BrakeLo + RaceConstants.BrakeHi) / 2;
            return bt * (1.0 + RaceMath.FormAdj(r) + RaceMath.EnvAdj(env, r) + RaceConstants.InstrAdj[ii] + q * c + pb * cb);
        }

        /// <summary>余裕の目安(◎=攻める / 〇=維持 / △=抑える)と、それに従う指示(04 §7.4)。</summary>
        public static (string mark, Instruction instr) Gauge(RaceRunner r, double d, SectionType typ, double temp)
        {
            double q = RaceMath.PaceQ(r, d, typ, temp);
            if (q < RaceConstants.GaugeLo) return ("◎", Instruction.Attack);
            return q < RaceConstants.GaugeHi ? ("〇", Instruction.Keep) : ("△", Instruction.Hold);
        }

        /// <summary>期待時間が最小になる指示(上級者の方針)。</summary>
        public static Instruction BestInstr(RaceRunner r, int si, RaceKind race, Env env)
        {
            Instruction best = Instruction.Attack;
            double bt = double.PositiveInfinity;
            foreach (Instruction k in All)
            {
                double v = LegEv(r, si, race, env, k);
                if (v < bt) { bt = v; best = k; }
            }
            return best;
        }

        private static double MinEv(RaceRunner r, int si, RaceKind race, Env env)
        {
            double m = double.PositiveInfinity;
            foreach (Instruction k in All) m = Math.Min(m, LegEv(r, si, race, env, k));
            return m;
        }

        /// <summary>区間 × 選手の所要時間の行列(基準タイム)。調子・環境は含まない。</summary>
        public static double[][] CostMatrix(IReadOnlyList<RaceRunner> runners, RaceKind race)
        {
            IReadOnlyList<SectionDef> secs = RaceTables.Course(race);
            int longest = 0;
            for (int i = 1; i < secs.Count; i++) if (secs[i].Distance > secs[longest].Distance) longest = i;
            var cost = new double[secs.Count][];
            for (int si = 0; si < secs.Count; si++)
            {
                cost[si] = new double[runners.Count];
                for (int j = 0; j < runners.Count; j++)
                {
                    double p = RaceMath.SectionRating(runners[j], secs[si].Type, si == longest, si == secs.Count - 1);
                    cost[si][j] = RaceMath.BaseTime(secs[si].Distance, secs[si].Terrain, p);
                }
            }
            return cost;
        }

        /// <summary>最適な区間配置(区間ごとの選手の添字)と合計コスト。留学生の出走が <c>intl_run_max</c> を超えるときは、1 人ずつ外して再計算し、総時間が最小になる案を採る(04 §5.2 O2)。</summary>
        public static (int[] assignment, double total) BestLineup(IReadOnlyList<RaceRunner> runners, RaceKind race)
        {
            double[][] cost = CostMatrix(runners, race);
            int[] asg = Hungarian.Solve(cost);
            int max = RaceTables.Rule(race).IntlRun;
            var banned = new HashSet<int>();
            while (IntlCount(runners, asg) > max && runners.Count - banned.Count - 1 >= asg.Length)
            {
                int bestJ = -1; double bestTotal = double.PositiveInfinity; int[] bestAsg = null;
                foreach (int j in asg)
                {
                    if (!runners[j].Overseas) continue;
                    var trial = new HashSet<int>(banned) { j };
                    int[] a = Hungarian.Solve(WithBanned(cost, trial));
                    double tot = 0;
                    for (int i = 0; i < a.Length; i++) tot += cost[i][a[i]];
                    if (tot < bestTotal) { bestTotal = tot; bestJ = j; bestAsg = a; }
                }
                if (bestJ < 0) break;
                banned.Add(bestJ);
                asg = bestAsg;
            }
            double total = 0;
            for (int i = 0; i < asg.Length; i++) total += cost[i][asg[i]];
            return (asg, total);
        }

        private static int IntlCount(IReadOnlyList<RaceRunner> runners, int[] asg)
        {
            int n = 0;
            foreach (int j in asg) if (runners[j].Overseas) n++;
            return n;
        }

        private static double[][] WithBanned(double[][] cost, ISet<int> banned)
        {
            var c = new double[cost.Length][];
            for (int i = 0; i < cost.Length; i++)
            {
                c[i] = (double[])cost[i].Clone();
                foreach (int j in banned) c[i][j] = 1e9;
            }
            return c;
        }

        /// <summary>当日変更の自動(04 §5.3): 配置された先発と控えを入れ替える。mode = "auto"(おまかせ: 不調以下の先発だけ、維持のときの期待時間が縮む控えと)/ "skilled"(最小の期待時間で比べる)。
        /// lineupIdx(区間 → entry の添字)を書き換え、入れ替えた回数(上限 limit)を返す。</summary>
        public static int ApplySwaps(IReadOnlyList<RaceRunner> entry, int[] lineupIdx, RaceKind race, Env[] envs, string swapMode, int limit)
        {
            int n = lineupIdx.Length;
            int swaps = 0;
            var bench = new List<int>();
            for (int k = 0; k < entry.Count; k++) if (Array.IndexOf(lineupIdx, k) < 0) bench.Add(k);
            for (int rep = 0; rep < limit; rep++)
            {
                bool found = false; double bestGain = 0; int bestSi = -1, bestB = -1;
                for (int si = 0; si < n; si++)
                {
                    Env env = RaceMath.DayEnv(race, envs, si);
                    RaceRunner cur = entry[lineupIdx[si]];
                    double curT = swapMode == "auto" ? LegEv(cur, si, race, env, Instruction.Keep) : MinEv(cur, si, race, env);
                    if (swapMode == "auto" && cur.Form > -1) continue;        // おまかせは不調以下だけ置き換える
                    foreach (int b in bench)
                    {
                        if (entry[b].Overseas && !cur.Overseas && IntlCount(entry, lineupIdx) + 1 > RaceTables.Rule(race).IntlRun) continue;     // 留学生の出走は上限まで(O2)
                        double newT = swapMode == "auto" ? LegEv(entry[b], si, race, env, Instruction.Keep) : MinEv(entry[b], si, race, env);
                        double gain = curT - newT;
                        if (gain > 0 && (!found || gain > bestGain)) { found = true; bestGain = gain; bestSi = si; bestB = b; }
                    }
                }
                if (!found) break;
                bench.Remove(bestB);
                bench.Add(lineupIdx[bestSi]);
                lineupIdx[bestSi] = bestB;
                swaps++;
            }
            return swaps;
        }

        /// <summary>登録メンバー(調子が決まった後)から、当日の配置・指示を決める(04 §5.3・§9.1)。</summary>
        public static (RaceRunner[] lineup, Instruction[] instr, int swaps) PlanDay(IReadOnlyList<RaceRunner> entry, RaceKind race, Env[] envs, PlanMode mode)
        {
            string swapMode;
            switch (mode)
            {
                case PlanMode.Auto: swapMode = "auto"; break;
                case PlanMode.Skilled: case PlanMode.SkilledSwap: swapMode = "skilled"; break;
                default: swapMode = "none"; break;
            }
            RaceRule rule = RaceTables.Rule(race);
            IReadOnlyList<SectionDef> secs = RaceTables.Course(race);
            int n = secs.Count;
            // オーダー提出時は調子が未確定(03 §6)
            var baseRunners = new List<RaceRunner>();
            foreach (RaceRunner e in entry) { RaceRunner b = e.Clone(); b.Form = 0; baseRunners.Add(b); }
            int[] asg0 = BestLineup(baseRunners, race).assignment;
            var lineupIdx = new int[n];
            for (int i = 0; i < n; i++) lineupIdx[i] = asg0[i];
            int swaps = 0;
            if (swapMode != "none" && rule.Swap > 0) swaps = ApplySwaps(entry, lineupIdx, race, envs, swapMode, rule.Swap);
            var lineup = new RaceRunner[n];
            for (int i = 0; i < n; i++) lineup[i] = entry[lineupIdx[i]];
            var instr = new Instruction[n];
            for (int si = 0; si < n; si++)
            {
                switch (mode)
                {
                    case PlanMode.AllAttack: instr[si] = Instruction.Attack; break;
                    case PlanMode.AllHold: instr[si] = Instruction.Hold; break;
                    case PlanMode.Gauge: instr[si] = Gauge(lineup[si], secs[si].Distance, secs[si].Type, RaceMath.DayEnv(race, envs, si).Temp).instr; break;
                    case PlanMode.Skilled: case PlanMode.SkilledInstr: instr[si] = BestInstr(lineup[si], si, race, RaceMath.DayEnv(race, envs, si)); break;
                    default: instr[si] = Instruction.Keep; break;
                }
            }
            return (lineup, instr, swaps);
        }
    }
}
