using System;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Rng;

namespace Ekiden.Domain.Training
{
    /// <summary>06 §6 の補正のうち、週の解決が受け取るもの(合成・範囲の収めは呼ぶ側。既定 = 補正なし)。</summary>
    public struct WeekMods
    {
        public double EMult, RAdd, InjMult, FormAdd;

        public static WeekMods None { get { return new WeekMods { EMult = 1.0, RAdd = 0.0, InjMult = 1.0, FormAdd = 0.0 }; } }
    }

    /// <summary>週の解決(03 §6)。1 選手の 1 週を解決して、選手を更新し、レポートを返す。乱数は鍵つきストリーム(故障 = label 1・調子 = label 2。b = 通算週)。</summary>
    public sealed class WeekResolver
    {
        private readonly TrainingMaster _m;

        public WeekResolver(TrainingMaster master)
        {
            _m = master;
        }

        public static double EFatigue(int f)
        {
            if (f < 40) return 1.0;
            if (f < 70) return 1.0 - 0.15 * (f - 40) / 29.0;
            if (f < 85) return 0.7;
            return 0.3;
        }

        public static double MFatigue(int f)
        {
            if (f < 40) return 1.0;
            if (f < 70) return 1.0 + 0.5 * (f - 40) / 29.0;
            if (f < 85) return 2.0;
            return 4.0;
        }

        public static int PeakingBonus(int taperWeeks, bool sharpenRecent, bool peakingMaster)
        {
            int b;
            if (taperWeeks >= 4) b = -8;
            else if (taperWeeks == 0) b = 0;
            else
            {
                b = taperWeeks == 1 ? 3 : (taperWeeks == 2 ? 6 : 8);
                if (!sharpenRecent) b = Rounding.HalfUp(b * 0.5);
                if (peakingMaster) b += 4;
            }
            return Math.Max(-8, Math.Min(12, b));
        }

        public static int FormLevel(double score)
        {
            if (score >= 20) return 2;
            if (score >= 8) return 1;
            if (score > -8) return 0;
            if (score > -20) return -1;
            return -2;
        }

        public double WeekLoad(Menu menu, bool camp)
        {
            int load = _m.KeySessions[menu.Key1].Load + _m.KeySessions[menu.Key2].Load + (menu.Mileage != null ? _m.Mileages[menu.Mileage].Load : 0);
            return load * (camp ? TrainingMaster.K_CAMP_LOAD : 1.0);
        }

        /// <param name="bondAvg">null のとき選手の BondAvg を使う。</param>
        public WeekReport Resolve(Runner r, Menu menu, FacilityLevels fac, int monthIndex, IRngFactory rngs, int runnerId, int absWeek,
                                  bool camp = false, bool safety = true, bool individual = false, double? bondAvg = null, WeekMods? mods = null, int safetyF = TrainingMaster.K_SAFETY_F)
        {
            WeekMods wm = mods ?? WeekMods.None;
            var rep = new WeekReport();
            bool injured = r.IsInjured;
            int f0 = r.Fatigue;

            // 1. メニュー決定: 個別 > 故障 > 安全網 > 与えられたメニュー
            if (injured) menu = _m.Injured;
            else if (safety && !individual && f0 >= safetyF) menu = _m.Safe;
            KeySessionDef k1 = _m.KeySessions[menu.Key1], k2 = _m.KeySessions[menu.Key2];
            MileageDef mil = menu.Mileage != null ? _m.Mileages[menu.Mileage] : null;
            rep.Menu = menu;

            // 2. 効率 E
            double eBond = 1.0 + r.BondK * ((bondAvg ?? r.BondAvg) / 1000.0);
            double e = EFatigue(f0) * TrainingMaster.MotivationE[r.Motivation]
                       * TrainingMaster.GrowthByGrade[(int)r.GrowthType][Math.Min(r.Grade, 4) - 1]
                       * (1.0 + TrainingMaster.FcCoach * (TrainingMaster.EffLv(fac.Coach) - 1)) * eBond;
            if (camp) e *= TrainingMaster.K_CAMP_XP * (r.Skills.Contains("skill.summer_boy") ? 1.1 : 1.0);
            e *= wm.EMult;                                         // 06 §6: 練習の効率(E_MAX の前)
            if (individual) e *= r.IndMult;
            e = Math.Min(e, TrainingMaster.K_E_MAX);
            rep.E = e;

            // 3. XP 付与・レベルアップ
            var gains = new double[StatInfo.Count];
            for (int i = 0; i < gains.Length; i++)
            {
                gains[i] += k1.Gain[i];
            }
            for (int i = 0; i < gains.Length; i++) gains[i] += k2.Gain[i];
            if (mil != null) for (int i = 0; i < gains.Length; i++) gains[i] += mil.Gain[i];
            double trackMult = 1 + TrainingMaster.FcTrack * (TrainingMaster.EffLv(fac.Track) - 1);
            double hillMult = 1 + TrainingMaster.FcHill * (TrainingMaster.EffLv(fac.Hill) - 1);
            double gymMult = 1 + TrainingMaster.FcGymXp * (TrainingMaster.EffLv(fac.Gym) - 1);
            double dormMult = 1 + TrainingMaster.FcDormXp * (TrainingMaster.EffLv(fac.Dorm) - 1);
            int lvUps = 0;
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
                int add = Rounding.HalfUp(add0);
                rep.XpAdd[i] = add;
                r.Xp[i] += add;
                int n = 0;
                while (n < TrainingMaster.K_LEVELUP_MAX && r.Stats[i] < r.Potential[i])
                {
                    int t = Rounding.HalfUp(10 * (TrainingMaster.K_T_BASE + TrainingMaster.K_T_SLOPE * r.Stats[i]));
                    if (r.Xp[i] < t) break;
                    r.Xp[i] -= t;
                    r.Stats[i]++;
                    n++;
                    lvUps++;
                }
                if (r.Stats[i] >= r.Potential[i]) r.Xp[i] = 0;
            }
            rep.Levelups = lvUps;
            r.WeekLevelups = lvUps;

            // 4. 疲労
            double care = TrainingMaster.FcDormR * (TrainingMaster.EffLv(fac.Dorm) - 1) + TrainingMaster.FcNutriR * (TrainingMaster.EffLv(fac.Nutri) - 1) + TrainingMaster.FcTrainerR * (TrainingMaster.EffLv(fac.Trainer) - 1);
            double rb = k1.RBonus + k2.RBonus;
            double rr = TrainingMaster.K_R_BASE + TrainingMaster.K_R_REC * r.Stats[(int)Stat.REC] / 100.0 + care + rb
                        + (r.Skills.Contains("skill.recovery_plus") ? 0.05 : 0.0)
                        - (r.Skills.Contains("skill.summer_fade") && monthIndex >= 3 && monthIndex <= 5 ? 0.05 : 0.0);
            rr += wm.RAdd;                                         // 06 §6: 疲労の回復率(R_MIN〜R_MAX の前)
            rr = Math.Max(TrainingMaster.K_R_MIN, Math.Min(TrainingMaster.K_R_MAX, rr));
            double load = WeekLoad(menu, camp);
            int f1 = Math.Max(0, Math.Min(100, Rounding.HalfUp(f0 * (1 - rr) + load)));
            rep.R = rr; rep.L = load; rep.FEnd = f1;

            // 5. 故障
            Pcg32 rngI = rngs.Stream(RngRegistry.LabelInjury, runnerId, absWeek);
            InjurySeverity sev0 = r.InjurySeverity;
            int left = r.InjuryWeeksLeft;
            if (injured)
            {
                left -= 1;
                if (left <= 0) { sev0 = InjurySeverity.None; left = 0; }
                rep.InjuryP = null;
            }
            else
            {
                double mLoad = 1.0 + k1.Risk + k2.Risk + (mil != null ? mil.Risk : 0.0);
                mLoad = Math.Max(0.5, mLoad);
                double mDur = 1.6 - r.Stats[(int)Stat.DUR] / 100.0;
                double mSk = (r.Skills.Contains("skill.iron_legs") ? 0.5 : 1.0) * (r.Skills.Contains("skill.injury_prone") ? 1.5 : 1.0);
                double mFac = (1.0 - TrainingMaster.FcGymInj * (TrainingMaster.EffLv(fac.Gym) - 1)) * (1.0 - TrainingMaster.FcTrainerInj * (TrainingMaster.EffLv(fac.Trainer) - 1));
                double p = TrainingMaster.K_P0 * MFatigue(f0) * mLoad * mDur * mSk * mFac * (camp ? TrainingMaster.K_CAMP_INJ : 1.0) * wm.InjMult;
                p = Math.Max(0.0, Math.Min(TrainingMaster.K_P_MAX, p));
                rep.InjuryP = p;
                double u = rngI.Uniform();
                if (u < p)
                {
                    double w0 = f0 >= 85 ? 0.4 : 0.6, w1 = f0 >= 85 ? 0.8 : 0.9;
                    double v = rngI.Uniform();
                    InjurySeverity sev = v < w0 ? InjurySeverity.Minor : (v < w1 ? InjurySeverity.Moderate : InjurySeverity.Severe);
                    int lo = sev == InjurySeverity.Minor ? 1 : (sev == InjurySeverity.Moderate ? 4 : 12);
                    int hi = sev == InjurySeverity.Minor ? 2 : (sev == InjurySeverity.Moderate ? 8 : 24);
                    int baseWeeks = rngI.RandInt(lo, hi);
                    double mult = Math.Max(0.5, 1.0 - TrainingMaster.FcMedicalDur * (TrainingMaster.EffLv(fac.Medical) - 1) - TrainingMaster.FcTrainerDur * (TrainingMaster.EffLv(fac.Trainer) - 1));
                    sev0 = sev;
                    left = Math.Max(1, Rounding.HalfUp(baseWeeks * mult));
                    rep.NewInjury = sev;
                    rep.NewInjuryWeeks = left;
                }
            }
            r.InjurySeverity = sev0;
            r.InjuryWeeksLeft = left;

            // 6. 調子
            bool sharpenedNow = menu.Key1 == "ks.sharpen" || menu.Key2 == "ks.sharpen";
            r.SharpenHistory = new[] { r.SharpenHistory[1], r.SharpenHistory[2], sharpenedNow };
            if (r.IsInjured || injured) r.TaperWeeks = 0;
            else if (load <= TrainingMaster.K_TAPER_L_MAX) r.TaperWeeks += 1;
            else r.TaperWeeks = 0;
            Pcg32 rngF = rngs.Stream(RngRegistry.LabelForm, runnerId, absWeek);
            double sigma = 8.0 * (1.5 - r.Stats[(int)Stat.CON] / 100.0);
            double noise = rngF.Normal(0.0, sigma);
            bool anySharpen = r.SharpenHistory[0] || r.SharpenHistory[1] || r.SharpenHistory[2];
            int pk = PeakingBonus(r.TaperWeeks, anySharpen, r.Skills.Contains("skill.peaking_master"));
            double pkm = Math.Max(-8, Math.Min(12, pk + wm.FormAdd));   // 06 §6: form_add を足して −8〜+12 に収める
            double score = (50 - f1) * 0.5 + pkm + noise;
            r.Form = r.IsInjured ? -2 : FormLevel(score);
            rep.Peaking = pk; rep.Noise = noise; rep.FormScore = score; rep.Form = r.Form;
            r.Fatigue = f1;
            return rep;
        }
    }
}
