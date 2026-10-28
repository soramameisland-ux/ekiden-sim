using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Training;

namespace Ekiden.Domain.Screens
{
    /// <summary>誤差関数(<c>math.erf</c> の代わり。非負の項だけの級数で、全域で相対誤差 1e-15 程度)。</summary>
    public static class MathX
    {
        public static double Erf(double x)
        {
            if (x == 0.0) return 0.0;
            double ax = Math.Abs(x);
            if (ax >= 6.0) return x > 0 ? 1.0 : -1.0;
            // erf(x) = 2/√π × e^(−x²) × Σ 2^n x^(2n+1) / (1·3·5···(2n+1))
            double term = ax, sum = ax;
            for (int n = 1; n < 400; n++)
            {
                term *= 2.0 * ax * ax / (2 * n + 1);
                sum += term;
                if (term < sum * 1e-17) break;
            }
            double v = 2.0 / Math.Sqrt(Math.PI) * Math.Exp(-ax * ax) * sum;
            if (v > 1.0) v = 1.0;
            return x > 0 ? v : -v;
        }

        public static double NormCdf(double x) { return 0.5 * (1.0 + Erf(x / Math.Sqrt(2.0))); }
    }

    /// <summary>乱数を使わない 1 週の予測の結果(08 R-05)。</summary>
    public sealed class ExpectedWeekReport
    {
        public int FEnd;
        public double Load;
        public int Levelups;
        public bool Safety;               // 安全網が働いた
        public double InjuryP;            // 故障の確率(すでに故障中なら 0)
        public double Mu, Sigma;          // 調子のスコアの中心と標準偏差
        public int Peaking;
        public bool InjuredNow;
    }

    public struct PlannedWeek
    {
        public Menu Menu;
        public int MonthIndex;
        public bool Camp, Individual;
    }

    public sealed class ForecastResult
    {
        public List<int> F = new List<int>();
        public int FMax;
        public double PInjury;
        public int[] Levelups = new int[StatInfo.Count];
        public int SafetyWeeks;
        public ExpectedWeekReport Last;
        public double[] Form;             // 調子の確率: [−2, −1, 0, +1, +2]
        public Runner End;
        public List<ExpectedWeekReport> Weeks = new List<ExpectedWeekReport>();
    }

    /// <summary>月末の予測(R-05)。03 §6 の週の解決から乱数を除いたもの。疲労・能力・XP・安全網は『正確』(故障が起きなければ実際の結果と一致する)で、故障と調子は確率で出す。</summary>
    public static class Forecaster
    {
        public static ExpectedWeekReport ExpectedWeek(TrainingMaster m, Runner r, Menu menu, FacilityLevels fac, int monthIndex, bool camp = false, bool safety = true,
                                                      bool individual = false, double? bondAvg = null, WeekMods? mods = null)
        {
            WeekMods wm = mods ?? WeekMods.None;
            var rep = new ExpectedWeekReport();
            bool injured = r.IsInjured;
            int f0 = r.Fatigue;
            if (injured) menu = m.Injured;
            else if (safety && !individual && f0 >= TrainingMaster.K_SAFETY_F)
            {
                menu = m.Safe;
                rep.Safety = true;
            }
            KeySessionDef k1 = m.KeySessions[menu.Key1], k2 = m.KeySessions[menu.Key2];
            MileageDef mil = menu.Mileage != null ? m.Mileages[menu.Mileage] : null;
            double eBond = 1.0 + r.BondK * ((bondAvg ?? r.BondAvg) / 1000.0);
            double e = WeekResolver.EFatigue(f0) * TrainingMaster.MotivationE[r.Motivation]
                       * TrainingMaster.GrowthByGrade[(int)r.GrowthType][Math.Min(r.Grade, 4) - 1]
                       * (1.0 + TrainingMaster.FcCoach * (fac.Coach - 1)) * eBond;
            if (camp) e *= TrainingMaster.K_CAMP_XP * (r.Skills.Contains("skill.summer_boy") ? 1.1 : 1.0);
            e *= wm.EMult;
            if (individual) e *= r.IndMult;
            e = Math.Min(e, TrainingMaster.K_E_MAX);
            var gains = new double[StatInfo.Count];
            for (int i = 0; i < gains.Length; i++) gains[i] += k1.Gain[i];
            for (int i = 0; i < gains.Length; i++) gains[i] += k2.Gain[i];
            if (mil != null) for (int i = 0; i < gains.Length; i++) gains[i] += mil.Gain[i];
            double trackMult = 1 + TrainingMaster.FcTrack * (fac.Track - 1);
            double hillMult = 1 + TrainingMaster.FcHill * (fac.Hill - 1);
            double gymMult = 1 + TrainingMaster.FcGymXp * (fac.Gym - 1);
            double dormMult = 1 + TrainingMaster.FcDormXp * (fac.Dorm - 1);
            int lv = 0;
            for (int i = 0; i < StatInfo.Count; i++)
            {
                if (gains[i] == 0 || r.Stats[i] >= r.Potential[i]) continue;
                double room = Math.Pow(1.0 - (double)r.Stats[i] / r.Potential[i], TrainingMaster.K_ROOM_EXP);
                double statMult = 1.0;
                switch ((Stat)i)
                {
                    case Stat.SPD: case Stat.KCK: statMult = trackMult; break;
                    case Stat.CLB: case Stat.DWN: statMult = hillMult; break;
                    case Stat.DUR: statMult = gymMult; break;
                    case Stat.REC: statMult = dormMult; break;
                }
                double add0 = gains[i] * TrainingMaster.K_XP_SCALE * e * statMult * room;
                if (r.Gm != null) add0 *= r.Gm[i];
                r.Xp[i] += Rounding.HalfUp(add0);
                int n = 0;
                while (n < TrainingMaster.K_LEVELUP_MAX && r.Stats[i] < r.Potential[i])
                {
                    int t = Rounding.HalfUp(10 * (TrainingMaster.K_T_BASE + TrainingMaster.K_T_SLOPE * r.Stats[i]));
                    if (r.Xp[i] < t) break;
                    r.Xp[i] -= t;
                    r.Stats[i]++;
                    n++;
                    lv++;
                }
                if (r.Stats[i] >= r.Potential[i]) r.Xp[i] = 0;
            }
            rep.Levelups = lv;
            double care = TrainingMaster.FcDormR * (fac.Dorm - 1) + TrainingMaster.FcNutriR * (fac.Nutri - 1) + TrainingMaster.FcTrainerR * (fac.Trainer - 1);
            double rr = TrainingMaster.K_R_BASE + TrainingMaster.K_R_REC * r.Stats[(int)Stat.REC] / 100.0 + care + k1.RBonus + k2.RBonus
                        + (r.Skills.Contains("skill.recovery_plus") ? 0.05 : 0.0)
                        - (r.Skills.Contains("skill.summer_fade") && monthIndex >= 3 && monthIndex <= 5 ? 0.05 : 0.0);
            rr += wm.RAdd;
            rr = Math.Max(TrainingMaster.K_R_MIN, Math.Min(TrainingMaster.K_R_MAX, rr));
            var resolver = new WeekResolver(m);
            double load = resolver.WeekLoad(menu, camp);
            int f1 = Math.Max(0, Math.Min(100, Rounding.HalfUp(f0 * (1 - rr) + load)));
            rep.FEnd = f1;
            rep.Load = load;
            if (injured)
            {
                int left = r.InjuryWeeksLeft - 1;
                if (left <= 0) { r.InjurySeverity = InjurySeverity.None; r.InjuryWeeksLeft = 0; }
                else r.InjuryWeeksLeft = left;
                rep.InjuryP = 0.0;
            }
            else
            {
                double mLoad = Math.Max(0.5, 1.0 + k1.Risk + k2.Risk + (mil != null ? mil.Risk : 0.0));
                double mDur = 1.6 - r.Stats[(int)Stat.DUR] / 100.0;
                double mSk = (r.Skills.Contains("skill.iron_legs") ? 0.5 : 1.0) * (r.Skills.Contains("skill.injury_prone") ? 1.5 : 1.0);
                double mFac = (1.0 - TrainingMaster.FcGymInj * (fac.Gym - 1)) * (1.0 - TrainingMaster.FcTrainerInj * (fac.Trainer - 1));
                double p = TrainingMaster.K_P0 * WeekResolver.MFatigue(f0) * mLoad * mDur * mSk * mFac * (camp ? TrainingMaster.K_CAMP_INJ : 1.0) * wm.InjMult;
                rep.InjuryP = Math.Max(0.0, Math.Min(TrainingMaster.K_P_MAX, p));
            }
            bool sharpenedNow = menu.Key1 == "ks.sharpen" || menu.Key2 == "ks.sharpen";
            r.SharpenHistory = new[] { r.SharpenHistory[1], r.SharpenHistory[2], sharpenedNow };
            if (r.IsInjured || injured) r.TaperWeeks = 0;
            else if (load <= TrainingMaster.K_TAPER_L_MAX) r.TaperWeeks += 1;
            else r.TaperWeeks = 0;
            bool anySharpen = r.SharpenHistory[0] || r.SharpenHistory[1] || r.SharpenHistory[2];
            int pk = WeekResolver.PeakingBonus(r.TaperWeeks, anySharpen, r.Skills.Contains("skill.peaking_master"));
            rep.Mu = (50 - f1) * 0.5 + Math.Max(-8, Math.Min(12, pk + wm.FormAdd));
            rep.Sigma = 8.0 * (1.5 - r.Stats[(int)Stat.CON] / 100.0);
            rep.Peaking = pk;
            rep.InjuredNow = r.IsInjured || injured;
            r.Fatigue = f1;
            return rep;
        }

        /// <summary>調子スコア ~ N(mu, sigma) から 5 段階 [−2, −1, 0, +1, +2] の確率(03 §2 の境界: ≥ 20 / ≥ 8 / &gt; −8 / &gt; −20)。</summary>
        public static double[] FormProbs(double mu, double sigma)
        {
            double[] edges = { 20.0, 8.0, -8.0, -20.0 };
            var c = new double[4];
            for (int i = 0; i < 4; i++) c[i] = 1.0 - MathX.NormCdf((edges[i] - mu) / sigma);     // P(score ≥ e)
            return new[] { 1.0 - c[3], c[3] - c[2], c[2] - c[1], c[1] - c[0], c[0] };
        }

        /// <summary>r0 の状態から、週のリストを順に進めた予測。r0 は変えない。</summary>
        public static ForecastResult Run(TrainingMaster m, Runner r0, IReadOnlyList<PlannedWeek> weeks, FacilityLevels fac, bool safety = true, double? bondAvg = null, WeekMods? mods = null)
        {
            Runner r = r0.Clone();
            var res = new ForecastResult();
            double surv = 1.0;
            ExpectedWeekReport last = null;
            foreach (PlannedWeek w in weeks)
            {
                ExpectedWeekReport rep = ExpectedWeek(m, r, w.Menu, fac, w.MonthIndex, w.Camp, safety, w.Individual, bondAvg, mods);
                res.F.Add(rep.FEnd);
                res.Weeks.Add(rep);
                surv *= 1.0 - rep.InjuryP;
                if (rep.Safety) res.SafetyWeeks++;
                last = rep;
            }
            res.FMax = res.F.Count > 0 ? res.F.Max() : r0.Fatigue;
            res.PInjury = 1.0 - surv;
            for (int i = 0; i < StatInfo.Count; i++) res.Levelups[i] = r.Stats[i] - r0.Stats[i];
            res.Last = last;
            res.Form = last != null ? FormProbs(last.Mu, last.Sigma) : null;
            res.End = r;
            return res;
        }
    }

    public sealed class ThemeGain
    {
        public double[] DP = new double[5];       // 区間タイプ別の適性値 P の伸び: Fast / Mid / Long / Climb / Descent
        public int FEnd;                           // 月末の疲労
    }

    public static class ThemeTable
    {
        /// <summary>基準の選手(全能力 58・素質 +25・F = 40・2 年・標準・施設 Lv2)が、各テーマを 1 か月続けたときの適性値 P の伸び(能力 + XP の端数。08 I-10 の表示値・TV-10)。合宿は 8 月扱い(夏男なし)。</summary>
        public static Dictionary<string, ThemeGain> Gains(TrainingMaster m, int[] stats = null, int f0 = 40, int grade = 2, GrowthType gtype = GrowthType.Normal)
        {
            int[] st0 = stats ?? new[] { 58, 58, 58, 58, 58, 58, 58, 58, 58, 58 };
            var res = new Dictionary<string, ThemeGain>();
            foreach (string th in m.Themes.Keys)
            {
                var r = new Runner { Grade = grade, GrowthType = gtype, Fatigue = f0 };
                for (int i = 0; i < StatInfo.Count; i++) { r.Stats[i] = st0[i]; r.Potential[i] = st0[i] + 25; }
                for (int w = 0; w < 4; w++) Forecaster.ExpectedWeek(m, r, m.Themes[th][w], FacilityLevels.AllTwo, 3, camp: th == "th.camp", bondAvg: 0.0);
                var before = new double[StatInfo.Count];
                var after = new double[StatInfo.Count];
                for (int i = 0; i < StatInfo.Count; i++)
                {
                    before[i] = st0[i];
                    double frac = r.Stats[i] + r.Xp[i] / (10.0 * (20 + 0.6 * r.Stats[i])) - st0[i];
                    after[i] = st0[i] + frac;
                }
                var g = new ThemeGain { FEnd = r.Fatigue };
                for (int t = 0; t < 5; t++) g.DP[t] = Ekiden.Domain.Race.RaceTables.Rating(after, (Ekiden.Domain.Race.SectionType)t) - Ekiden.Domain.Race.RaceTables.Rating(before, (Ekiden.Domain.Race.SectionType)t);
                res[th] = g;
            }
            return res;
        }
    }
}
