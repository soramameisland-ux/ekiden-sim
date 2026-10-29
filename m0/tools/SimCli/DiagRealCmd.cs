using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;
using Ekiden.Domain.World;

namespace Ekiden.SimCli
{
    /// <summary>
    /// 実体の自校が抽象の自校・他校より弱くなる原因(L-55)を探す診断。大会ごとに、自校の出走メンバーと他校の出走メンバーの
    /// 調子・疲労・能力(長距離の適性値)を比べる。使い方: diag-real Elite|Mid|Low [シード数 30] [年度 30]。
    /// 時間への効き: 調子(絶好調 −1.5% / 好調 −0.7% / 普通 0 / 不調 +1.2% / 絶不調 +3.0%。04 §7.2)・疲労 70 以上 +1%。
    /// </summary>
    internal static class DiagRealCmd
    {
        private sealed class Acc
        {
            public double N, Form, FormTime, Fatigue, Fat70, Rating, Rank, Win, Runners;
            public void Add(IEnumerable<RaceRunner> lineup)
            {
                foreach (RaceRunner r in lineup)
                {
                    N++; Form += r.Form; FormTime += FormMul(r.Form); Fatigue += r.Fatigue; if (r.Fatigue >= 70) Fat70++;
                    Rating += RaceTables.Rating(r.Stats, SectionType.Long);
                }
            }
        }

        private static double FormMul(int f)
        {
            switch (f) { case 2: return -0.015; case 1: return -0.007; case 0: return 0.0; case -1: return 0.012; default: return 0.03; }
        }

        public static int Run(string[] args)
        {
            var pos = Common.Positional(args);
            var start = (Difficulty)Enum.Parse(typeof(Difficulty), pos.Count > 0 ? pos[0] : "Mid");
            int n = Common.IntArg(pos, 1, 30), years = Common.IntArg(pos, 2, 30);
            var own = new Acc[3]; var top = new Acc[3]; var oth = new Acc[3];
            for (int k = 0; k < 3; k++) { own[k] = new Acc(); top[k] = new Acc(); oth[k] = new Acc(); }
            var counts = new int[3]; var absent = new int[3]; var ran = new int[3];
            object lk = new object();
            Parallel.For(0, n, i =>
            {
                var o = new Acc[3]; var t = new Acc[3]; var h = new Acc[3];
                for (int k = 0; k < 3; k++) { o[k] = new Acc(); t[k] = new Acc(); h[k] = new Acc(); }
                var rc = new int[3]; var ab = new int[3];
                GameSession gs = GameSession.New(Common.WorldSeed(i + 1), start);
                for (int w = 0; w < years * Calendar.WeeksPerYear; w++)
                {
                    int yw = TeamWeek.YearWeek(gs.State);
                    int k = Array.IndexOf(Calendar.RaceWeeks, yw);
                    if (k < 0) { gs.StepWeek(); continue; }
                    gs.ResolveTraining();
                    RaceInfo? info;
                    RaceOutcome race = gs.PlayWeekRaces(out info);
                    if (race != null)
                    {
                        RaceTeam mine = race.Teams.FirstOrDefault(x => x.Id == gs.School.Id);
                        rc[k]++;
                        if (mine == null) ab[k]++;
                        else
                        {
                            o[k].Add(mine.Lineup);
                            var ids = race.Results.Select(x => x.Id).ToList();
                            foreach (RaceTeam tm in race.Teams)
                            {
                                if (tm.Id == gs.School.Id) continue;
                                int rank = ids.IndexOf(tm.Id);
                                (rank >= 0 && rank < 5 ? t[k] : h[k]).Add(tm.Lineup);
                            }
                            int myRank = ids.IndexOf(gs.School.Id);
                            o[k].Rank += myRank + 1; o[k].Win += myRank == 0 ? 1 : 0; o[k].Runners++;
                        }
                    }
                    EventSelection sel = gs.SelectWeekEvents(info);
                    gs.ResolveWeekEvents(sel, info, new DefaultChoicePolicy());
                    gs.FinishWeek();
                    gs.AdvanceWeek();
                }
                lock (lk)
                    for (int k = 0; k < 3; k++)
                    {
                        Merge(own[k], o[k]); Merge(top[k], t[k]); Merge(oth[k], h[k]); counts[k] += rc[k]; absent[k] += ab[k];
                    }
            });
            string[] names = { "第一戦", "第二戦", "第三戦" };
            Console.WriteLine("== 実体の自校 " + start + "  シード " + n + "  " + years + " 年度  (出走メンバー 1 人あたりの平均)");
            Console.WriteLine("   大会   区分              調子   調子の時間への効き   疲労   疲労70以上  長距離P   順位  優勝率  棄権");
            for (int k = 0; k < 3; k++)
            {
                Line(names[k], "自校", own[k], own[k].Runners, counts[k], absent[k]);
                Line("", "他校(上位 5 校)", top[k], 0, 0, 0);
                Line("", "他校(それ以外)", oth[k], 0, 0, 0);
            }
            return 0;
        }

        private static void Merge(Acc a, Acc b)
        {
            a.N += b.N; a.Form += b.Form; a.FormTime += b.FormTime; a.Fatigue += b.Fatigue; a.Fat70 += b.Fat70; a.Rating += b.Rating; a.Rank += b.Rank; a.Win += b.Win; a.Runners += b.Runners;
        }

        private static void Line(string race, string label, Acc a, double ownRaces, int total, int absent)
        {
            string extra = ownRaces > 0 ? "  " + (a.Rank / ownRaces).ToString("0.00").PadLeft(5) + "  " + (a.Win / ownRaces).ToString("0.000") + "  " + absent + "/" + total : "";
            Console.WriteLine("   " + race.PadRight(4) + "  " + label.PadRight(14) + "  " + (a.Form / a.N).ToString("0.00").PadLeft(5) + "  " + (100.0 * a.FormTime / a.N).ToString("+0.000;-0.000").PadLeft(10) + " %  "
                + (a.Fatigue / a.N).ToString("0.0").PadLeft(5) + "  " + (100.0 * a.Fat70 / a.N).ToString("0.0").PadLeft(6) + " %  " + (a.Rating / a.N).ToString("0.0").PadLeft(6) + extra);
        }
    }
}
