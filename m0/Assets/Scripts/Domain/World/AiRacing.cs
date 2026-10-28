using System;
using System.Collections.Generic;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;

namespace Ekiden.Domain.World
{
    /// <summary>他校の選手(抽象)を 10 能力へ復元し、登録・当日の配置までを決める(04 §4.2・§5・05 §9)。</summary>
    public static class AiRacing
    {
        // 型(プロフィール)ごとの能力の偏り(05 §6.2)。SPD STA PAC KCK CLB DWN
        private static readonly double[][] Offsets =
        {
            new double[] { 0, 0, 0, 0, 0, 0 },          // Balanced
            new double[] { -8, 6, 4, -6, -2, -3 },      // Endurance
            new double[] { 8, -6, -4, 6, -2, -2 },      // Speed
            new double[] { -4, 2, 0, -2, 10, 6 },       // Mountain
        };

        public static double[] ExpandStats(double power, Profile profile, double[] n10)
        {
            return ExpandStats(power, profile, n10, null, 1.0);
        }

        // 型に合わせた育成(03 §11 の『重点プラン − 標準プラン』)による能力の差(プレイヤーの標準方針。04 §11)。SPD STA PAC KCK CLB DWN CON MEN REC DUR
        private static readonly double[][] SpecShift =
        {
            null,                                                                       // Balanced
            new[] { -8.0, 2.1, 2.7, -4.4, -3.7, -4.6, 0, 0, 0, 0 },                     // Endurance
            new[] { 4.2, -0.9, -0.6, 3.2, -3.7, -4.6, 0, 0, 0, 0 },                     // Speed
            new[] { -7.9, -0.1, -0.8, -4.3, 5.8, 9.0, 0, 0, 0, 0 },                     // Mountain
        };

        public static double[] SpecShiftOf(Profile p) { return SpecShift[(int)p]; }

        /// <summary>力 P と型から 10 能力を復元する(05 §6.2)。shift があれば、型に合わせた育成の差を shiftScale 倍して足す(クランプの前)。</summary>
        public static double[] ExpandStats(double power, Profile profile, double[] n10, double[] shift, double shiftScale)
        {
            var o = Offsets[(int)profile];
            var stats = new double[10];
            for (int i = 0; i < 10; i++)
            {
                double v = power + (i < 6 ? o[i] : 0.0) + n10[i];
                if (shift != null) v += shift[i] * shiftScale;
                stats[i] = Math.Max(20.0, Math.Min(99.0, v));
            }
            return stats;
        }

        public static RaceRunner ToRaceRunner(WorldRunner r, double lv, IRngFactory rngs, double powerBonus = 0.0)
        {
            double p = WorldMath.SeasonPower(r, lv) + powerBonus;
            var rr = new RaceRunner { Stats = ExpandStats(p, r.Profile, r.N10(rngs)) };
            return rr;
        }

        /// <summary>自校の選手(検算用の抽象)をレース用にする。標準方針は『型に合わせた育成』の差を載せる(scale = 進捗の割合 ÷ 0.6 を 0〜1 に収めた値)。</summary>
        public static RaceRunner ToPlayerRaceRunner(WorldRunner r, double lv, IRngFactory rngs, double powerBonus = 0.0)
        {
            double p = WorldMath.SeasonPower(r, lv) + powerBonus;
            double scale = School.Clamp((p - r.A0) / Math.Max(1e-6, r.Gap) / 0.6, 0.0, 1.0);
            return new RaceRunner { Stats = ExpandStats(p, r.Profile, r.N10(rngs), SpecShiftOf(r.Profile), scale) };
        }

        public static RaceRunner Filler()
        {
            var rr = new RaceRunner();
            for (int i = 0; i < 10; i++) rr.Stats[i] = 30.0;
            return rr;
        }

        public static List<RaceRunner> Pad(List<RaceRunner> list, int n)
        {
            while (list.Count < n) list.Add(Filler());
            return list;
        }

        /// <summary>調子の抽選(他校・調整あり。04 §2): 絶好調 55% / 好調 31% / 普通 12% / 不調 2%。</summary>
        public static int DrawForm(Pcg32 g)
        {
            double u = g.Uniform();
            if (u < 0.55) return 2;
            if (u < 0.55 + 0.31) return 1;
            if (u < 0.55 + 0.31 + 0.12) return 0;
            if (u < 0.55 + 0.31 + 0.12 + 0.02) return -1;
            return 0;
        }

        /// <summary>他校の標準方針: 総合力(長距離の適性値)の上位を登録し、第三戦では登坂と下りの最適任者を各 1 名は必ず含める(04 §4.2)。</summary>
        public static List<RaceRunner> PickEntry(IReadOnlyList<RaceRunner> roster, RaceKind race)
        {
            int n = RaceTables.Rule(race).Entry;
            var sorted = new List<RaceRunner>(roster);
            RaceSimulator.StableSort(sorted, (a, b) => RaceTables.Rating(b.Stats, SectionType.Long).CompareTo(RaceTables.Rating(a.Stats, SectionType.Long)));
            var entry = sorted.GetRange(0, Math.Min(n, sorted.Count));
            if (race == RaceKind.NewYear)
            {
                foreach (SectionType typ in new[] { SectionType.Climb, SectionType.Descent })
                {
                    RaceRunner best = roster[0];
                    double bv = RaceTables.Rating(best.Stats, typ);
                    for (int i = 1; i < roster.Count; i++)
                    {
                        double v = RaceTables.Rating(roster[i].Stats, typ);
                        if (v > bv) { bv = v; best = roster[i]; }
                    }
                    if (!entry.Contains(best)) entry[entry.Count - 1 - (typ == SectionType.Descent ? 1 : 0)] = best;
                }
            }
            return entry;
        }

        /// <summary>自校の標準方針: 区間適性を見て最適な区間数ぶんを選び、残りの登録枠は総合力の高い順で埋める(04 §4.2)。</summary>
        public static List<RaceRunner> PickPlayerEntry(IReadOnlyList<RaceRunner> roster, RaceKind race)
        {
            RaceRule rule = RaceTables.Rule(race);
            var (asg, _) = DayPlanner.BestLineup(roster, race);
            var lineup = new List<RaceRunner>();
            var used = new HashSet<int>();
            foreach (int j in asg) { lineup.Add(roster[j]); used.Add(j); }
            var rest = new List<int>();
            for (int j = 0; j < roster.Count; j++) if (!used.Contains(j)) rest.Add(j);
            RaceSimulator.StableSort(rest, (a, b) => RaceTables.Rating(roster[b].Stats, SectionType.Long).CompareTo(RaceTables.Rating(roster[a].Stats, SectionType.Long)));
            var entry = new List<RaceRunner>(lineup);
            for (int i = 0; i < rest.Count && entry.Count < rule.Entry; i++) entry.Add(roster[rest[i]]);
            return entry;
        }

        /// <summary>自校の準備: 登録(標準方針)→ 当日の欠場 → 調子 → 当日変更・指示(おまかせ)。乱数の鍵は他校と同じ(label 4)。</summary>
        public static (RaceRunner[] lineup, Instruction[] instr) PreparePlayerLineup(List<RaceRunner> roster, RaceKind race, Env[] envs, IRngFactory rngs, int raceId, int teamId)
        {
            RaceRule rule = RaceTables.Rule(race);
            Pcg32 avail = rngs.Stream(RngRegistry.LabelRace, raceId, 8000 + teamId);
            Pcg32 forms = rngs.Stream(RngRegistry.LabelRace, raceId, 9000 + teamId);
            var pool = new List<RaceRunner>();
            foreach (RaceRunner d in roster) if (avail.Uniform() < RaceConstants.AvailEntry) pool.Add(d);
            Pad(pool, rule.Run);
            List<RaceRunner> entry = PickPlayerEntry(pool, race);
            var day = new List<RaceRunner>();
            foreach (RaceRunner d in entry) if (avail.Uniform() < RaceConstants.AvailDay) day.Add(d);
            Pad(day, rule.Run);
            for (int i = 0; i < day.Count; i++) { day[i] = day[i].Clone(); day[i].Form = DrawForm(forms); }
            var (lineup, instr, _) = DayPlanner.PlanDay(day, race, envs, PlanMode.Auto);
            return (lineup, instr);
        }

        /// <summary>登録 → 当日の欠場 → 調子の抽選 → 当日の配置・指示(他校は当日変更なし・指示は維持)。欠場は label 4 の b = 8000 + チーム ID、調子は b = 9000 + チーム ID(04 §12)。</summary>
        public static (RaceRunner[] lineup, Instruction[] instr) PrepareLineup(List<RaceRunner> roster, RaceKind race, Env[] envs, IRngFactory rngs, int raceId, int teamId)
        {
            RaceRule rule = RaceTables.Rule(race);
            Pcg32 avail = rngs.Stream(RngRegistry.LabelRace, raceId, 8000 + teamId);
            Pcg32 forms = rngs.Stream(RngRegistry.LabelRace, raceId, 9000 + teamId);
            var pool = new List<RaceRunner>();
            foreach (RaceRunner d in roster) if (avail.Uniform() < RaceConstants.AvailEntry) pool.Add(d);
            Pad(pool, rule.Run);
            List<RaceRunner> entry = PickEntry(pool, race);
            var day = new List<RaceRunner>();
            foreach (RaceRunner d in entry) if (avail.Uniform() < RaceConstants.AvailDay) day.Add(d);
            Pad(day, rule.Run);
            for (int i = 0; i < day.Count; i++) { day[i] = day[i].Clone(); day[i].Form = DrawForm(forms); }
            var (lineup, instr, _) = DayPlanner.PlanDay(day, race, envs, PlanMode.Plain);
            return (lineup, instr);
        }
    }
}
