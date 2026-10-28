using System;
using System.Collections.Generic;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Rng;

namespace Ekiden.Domain.Race
{
    /// <summary>1 人・1 区間の乱数の結果(04 §7.1・§12。引く順序は固定で 8 回)。</summary>
    public struct LegDraw
    {
        public double Noise, Brake, BrakePos, Boost, Stall;
    }

    /// <summary>環境(日ごと。04 §6)。</summary>
    public struct Env
    {
        public double Temp, Wind;
        public bool Rain;
    }

    /// <summary>区間タイムの式(04 §5〜§7)。</summary>
    public static class RaceMath
    {
        public static double BaseTime(double d, double terr, double p)
        {
            return d * (Pace50.Of(d) * terr - RaceConstants.K_SLOPE * (p - 50.0));
        }

        /// <summary>区間適性値 P(特殊能力を含む)。</summary>
        public static double SectionRating(RaceRunner r, SectionType typ, bool longest = false, bool isLast = false)
        {
            double p = RaceTables.Rating(r.Stats, typ);
            if (typ == SectionType.Climb && r.Skills.Contains("skill.mountain_god")) p += RaceConstants.SkillMountainGod;
            if (typ == SectionType.Descent && r.Skills.Contains("skill.downhill_master")) p += RaceConstants.SkillDownhillMaster;
            if (longest && r.Skills.Contains("skill.ace_section")) p += RaceConstants.SkillAceSection;
            if (isLast && r.Skills.Contains("skill.anchor")) p += RaceConstants.SkillAnchor;
            return p;
        }

        public static double FormAdj(RaceRunner r)
        {
            int f = r.Form;
            if (r.Skills.Contains("skill.ekiden_man")) f = Math.Min(2, f + 1);
            double a = RaceConstants.FormAdj[f + 2];
            if (r.Fatigue >= RaceConstants.FatigueExtraFrom) a += RaceConstants.FatigueExtra;
            return a;
        }

        public static double EnvAdj(Env env, RaceRunner r)
        {
            double heat = 0.0004 * Math.Max(0.0, env.Temp - 15.0) * (1.4 - r.Stats[(int)Stat.STA] / 100.0);
            if (r.Skills.Contains("skill.summer_boy")) heat = 0.0;
            double cold = 0.0002 * Math.Max(0.0, 3.0 - env.Temp);
            double wind = 0.0015 * Math.Max(0.0, env.Wind) * (1.2 - r.Stats[(int)Stat.PAC] / 100.0);
            double rain = env.Rain ? 0.003 * (1.2 - r.Stats[(int)Stat.CON] / 100.0) / 0.7 : 0.0;
            return (heat + cold + wind + rain) * RaceConstants.EnvScale;
        }

        /// <summary>ペース配分の危険度 q(維持のときに失速が起きる確率。04 §7.4)。temp が null のときは気温を見ない。</summary>
        public static double PaceQ(RaceRunner r, double d, SectionType typ, double? temp = null)
        {
            int f = r.Form;
            if (r.Skills.Contains("skill.ekiden_man")) f = Math.Min(2, f + 1);
            double eff = r.Stats[(int)Stat.STA] + RaceConstants.FormSta * f;
            if (r.Fatigue >= RaceConstants.FatigueExtraFrom) eff -= RaceConstants.FatigueSta;
            if (temp != null) eff -= RaceConstants.HeatSta * Math.Max(0.0, temp.Value - RaceConstants.HeatFrom);
            double m = eff - RaceTables.Rating(r.Stats, typ);
            double need = RaceConstants.Need0 + RaceConstants.NeedPerKm * d;
            return Math.Max(RaceConstants.QMin, Math.Min(RaceConstants.QMax, RaceConstants.Q0 + RaceConstants.QSlope * (need - m)));
        }

        /// <summary>1 人・1 区間ぶんの乱数を、固定の順序で引く(正規 1 回 = 2 draw + 一様 6 回 = 8 回)。</summary>
        public static LegDraw DrawLeg(Pcg32 rng, RaceRunner r, double raceWeight, Instruction instr = Instruction.Keep, double brakeMultSkill = 1.0,
                                      double? dist = null, double? temp = null, SectionType typ = SectionType.Long)
        {
            int ii = (int)instr;
            double con = r.Stats[(int)Stat.CON], men = r.Stats[(int)Stat.MEN];
            double sd = RaceConstants.NoiseSd * (1.5 - con / 100.0) * RaceConstants.InstrNoise[ii];
            if (r.Skills.Contains("skill.hold_lead") && r.Leading) sd *= 0.8;
            bool leadSty = r.Style != null && r.Leading;
            if (leadSty) sd *= RaceConstants.K_LEAD_NOISE[(int)r.Style.Value.Flow];
            double noise = rng.Normal(0.0, sd);
            double uB = rng.Uniform(), uM = rng.Uniform(), uP = rng.Uniform(), uO = rng.Uniform();
            double uS = rng.Uniform(), uZ = rng.Uniform();
            double pressure = Math.Max(0.5, Math.Min(1.5, 1.5 - men / 100.0)) * raceWeight;
            if (r.Skills.Contains("skill.big_stage")) pressure *= 0.5;
            if (r.Skills.Contains("skill.stage_fright")) pressure *= 1.5;
            double pB = RaceConstants.BrakeP * (1.5 - con / 100.0) * pressure * RaceConstants.InstrBrake[ii]
                        * (r.Skills.Contains("skill.brake_prone") ? 1.5 : 1.0) * brakeMultSkill
                        * (leadSty ? RaceConstants.K_LEAD_BRAKE[(int)r.Style.Value.Flow] : 1.0);
            double brake = uB < pB ? RaceConstants.BrakeLo + (RaceConstants.BrakeHi - RaceConstants.BrakeLo) * uM : 0.0;
            double pos = uP < 0.5 ? 1.0 / 3.0 : 2.0 / 3.0;
            double boost = (brake == 0.0 && uO < RaceConstants.BoostP) ? RaceConstants.BoostAdj : 0.0;
            double stall = 0.0;
            if (dist != null)
            {
                double q = Math.Min(0.95, PaceQ(r, dist.Value, typ, temp) * RaceConstants.InstrStall[ii]);
                if (uS < q) stall = RaceConstants.StallLo + (RaceConstants.StallHi - RaceConstants.StallLo) * uZ;
            }
            return new LegDraw { Noise = noise, Brake = brake, BrakePos = pos, Boost = boost, Stall = stall };
        }

        /// <summary>環境の抽選(04 §6)。日ごとに 正規 2 回 + 一様 1 回。</summary>
        public static Env[] GenEnv(Pcg32 rng, RaceKind race)
        {
            (double muT, double sdT, double muW, double sdW, double pRain)[] spec;
            switch (race)
            {
                case RaceKind.Fall: spec = new[] { (18.0, 3.0, 1.0, 1.5, 0.20) }; break;
                case RaceKind.National: spec = new[] { (11.0, 3.0, 1.0, 2.0, 0.15) }; break;
                default: spec = new[] { (4.0, 2.5, 1.5, 2.5, 0.10), (5.0, 2.5, 1.0, 2.5, 0.10) }; break;
            }
            var res = new Env[spec.Length];
            for (int i = 0; i < spec.Length; i++)
            {
                double t = rng.Normal(spec[i].muT, spec[i].sdT);
                double w = rng.Normal(spec[i].muW, spec[i].sdW);
                res[i] = new Env { Temp = t, Wind = w, Rain = rng.Uniform() < spec[i].pRain };
            }
            return res;
        }

        /// <summary>環境の予報(オーダー提出時に出る。04 §6): 実際の値に誤差(気温 N(0, 1.5)・風 N(0, 1.0))を加えたもの。雨は 85% の的中率。
        /// 日ごとに 正規 2 回 + 一様 1 回(鍵は label 4・race_id・9998。引く順序はこの文書で定めた)。</summary>
        public static Env[] Forecast(Pcg32 rng, Env[] actual)
        {
            var res = new Env[actual.Length];
            for (int i = 0; i < actual.Length; i++)
            {
                double t = actual[i].Temp + rng.Normal(0.0, 1.5);
                double w = actual[i].Wind + rng.Normal(0.0, 1.0);
                bool hit = rng.Uniform() < 0.85;
                res[i] = new Env { Temp = t, Wind = w, Rain = hit ? actual[i].Rain : !actual[i].Rain };
            }
            return res;
        }

        public static Env DayEnv(RaceKind race, Env[] envs, int sectionIndex)
        {
            int day = RaceTables.DayOf(race, sectionIndex);
            return envs[Math.Min(day, envs.Length) - 1];
        }
    }
}
