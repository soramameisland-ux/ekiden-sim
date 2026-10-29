using System.Collections.Generic;
using Ekiden.Domain.Entities;

namespace Ekiden.Domain.Race
{
    public enum RaceKind { Fall = 0, National = 1, NewYear = 2 }

    /// <summary>区間タイプ(04 §5.1)。</summary>
    public enum SectionType { Fast, Mid, Long, Climb, Descent }

    /// <summary>ペース配分の指示(04 §9.1)。</summary>
    public enum Instruction { Attack = 0, Keep = 1, Hold = 2 }

    public readonly struct SectionDef
    {
        public double Distance { get; }
        public SectionType Type { get; }
        public double Terrain { get; }

        public SectionDef(double distance, SectionType type, double terrain)
        {
            Distance = distance; Type = type; Terrain = terrain;
        }
    }

    public sealed class RaceRule
    {
        public int Entry, Run, Swap, IntlReg, IntlRun, Geki;
        public double Kuriage, Weight;
    }

    /// <summary>04 §2 の定数表(初期値案)。</summary>
    public static class RaceConstants
    {
        public const double K_SLOPE = 0.4;
        public static readonly double[] FormAdj = { 0.030, 0.012, 0.0, -0.007, -0.015 };      // 調子 −2〜+2(添字 = 調子 + 2)
        public const double FatigueExtra = 0.01; public const int FatigueExtraFrom = 70;
        public const double NoiseSd = 0.008, BrakeP = 0.02, BrakeLo = 0.03, BrakeHi = 0.10, BoostP = 0.03, BoostAdj = -0.02;
        public const double G_PACK = 15.0, G_CHASE = 60.0, PackAdj = -0.003, SoloAdj = 0.002, ChaseSkillAdj = -0.005;
        public const double PackSkillMult = 2.0, SoloWeakExtra = 0.003;
        // 見どころ(§9.3。表示の規則で、結果には影響しない)
        public const int HighlightGapDs = 200, HighlightRankMove = 3, HighlightFocusTop = 3, HighlightPerSection = 2, HighlightResultMax = 5, HighlightPerKind = 2;
        // 個性(10 §6)
        public static readonly double[] K_RUN_SCALE = { 1.5, 1.0, 0.5 };                    // StyleRun: Pack / Free / Solo
        public static readonly double[] K_STYLE_CHASE_ADJ = { -0.002, 0.0, 0.002 };         // StyleFlow: Chase / Even / Lead
        public static readonly double[] K_LEAD_NOISE = { 1.15, 1.0, 0.85 };
        public static readonly double[] K_LEAD_BRAKE = { 1.2, 1.0, 0.8 };
        public static readonly double[] InstrAdj = { -0.008, 0.0, 0.006 };                  // Instruction: Attack / Keep / Hold
        public static readonly double[] InstrNoise = { 1.3, 1.0, 0.7 };
        public static readonly double[] InstrBrake = { 1.5, 1.0, 0.5 };
        public static readonly double[] InstrStall = { 2.5, 1.0, 0.3 };
        // ペース配分(失速。04 §7.4)
        public const double Need0 = -2.0, NeedPerKm = 0.5, Q0 = 0.18, QSlope = 0.015, QMin = 0.03, QMax = 0.60;
        public const double FormSta = 3.0, FatigueSta = 5.0, HeatSta = 0.4, HeatFrom = 20.0, StallLo = 0.02, StallHi = 0.04;
        public const double GaugeLo = 0.16, GaugeHi = 0.26;
        public const double EnvScale = 2.0;
        public const double GekiAdj = 0.0025, GekiBrakeMult = 0.8, GekiReduce = 0.2, GekiOverMult = 0.5; public const int GekiOverFrom = 3;
        public const double GekiF0 = 0.5, GekiFMot = 0.1, GekiFBond = 0.3, GekiFLo = 0.3, GekiFHi = 1.2;          // 走者ごとの檄の効き f_k(04 §9.2)
        public const double SkillMountainGod = 8.0, SkillDownhillMaster = 5.0, SkillAceSection = 3.0, SkillAnchor = 2.0;
        public const double AvailEntry = 0.95, AvailDay = 0.97;
        public const double QualKm = 20.0; public const int QualEntry = 12, QualCount = 10, QualPass = 10;
        public const double MultFloor = 0.85;
    }

    /// <summary>04 のコースと大会の規則。</summary>
    public static class RaceTables
    {
        private static readonly SectionDef[] FallCourse =
        {
            new SectionDef(8.0, SectionType.Fast, 1.0), new SectionDef(5.8, SectionType.Fast, 1.0), new SectionDef(8.5, SectionType.Fast, 1.0),
            new SectionDef(6.2, SectionType.Fast, 1.0), new SectionDef(6.4, SectionType.Fast, 1.0), new SectionDef(10.2, SectionType.Long, 1.0),
        };
        private static readonly SectionDef[] NationalCourse =
        {
            new SectionDef(9.5, SectionType.Fast, 1.0), new SectionDef(11.1, SectionType.Fast, 1.0), new SectionDef(11.9, SectionType.Fast, 1.0),
            new SectionDef(11.8, SectionType.Fast, 1.0), new SectionDef(12.4, SectionType.Mid, 1.0), new SectionDef(12.8, SectionType.Mid, 1.0),
            new SectionDef(17.6, SectionType.Long, 1.0), new SectionDef(19.7, SectionType.Long, 1.0),
        };
        private static readonly SectionDef[] NewYearCourse =
        {
            new SectionDef(21.3, SectionType.Long, 1.0), new SectionDef(23.1, SectionType.Long, 1.0), new SectionDef(21.4, SectionType.Long, 1.0),
            new SectionDef(20.9, SectionType.Long, 1.0), new SectionDef(20.8, SectionType.Climb, 1.13), new SectionDef(20.8, SectionType.Descent, 0.93),
            new SectionDef(21.3, SectionType.Long, 1.0), new SectionDef(21.4, SectionType.Long, 1.0), new SectionDef(23.1, SectionType.Long, 1.0),
            new SectionDef(23.0, SectionType.Long, 1.0),
        };

        public static IReadOnlyList<SectionDef> Course(RaceKind k)
        {
            return k == RaceKind.Fall ? FallCourse : (k == RaceKind.National ? NationalCourse : NewYearCourse);
        }

        public static RaceRule Rule(RaceKind k)
        {
            switch (k)
            {
                case RaceKind.Fall: return new RaceRule { Entry = 10, Run = 6, Swap = 1, IntlReg = 2, IntlRun = 1, Kuriage = 240.0, Weight = 0.9, Geki = 2 };
                case RaceKind.National: return new RaceRule { Entry = 14, Run = 8, Swap = 2, IntlReg = 2, IntlRun = 1, Kuriage = 600.0, Weight = 1.0, Geki = 3 };
                default: return new RaceRule { Entry = 16, Run = 10, Swap = 4, IntlReg = 2, IntlRun = 1, Kuriage = 1200.0, Weight = 1.2, Geki = 4 };
            }
        }

        /// <summary>区間 i が走る日(1 日目 / 2 日目)。第三戦は 5 区までが 1 日目。</summary>
        public static int DayOf(RaceKind k, int sectionIndex)
        {
            return k == RaceKind.NewYear && sectionIndex >= 5 ? 2 : 1;
        }

        // 区間タイプ別の適性値の重み(04 §5.1)。足し合わせの順序は参照モデルの dict の順に合わせる(浮動小数の和の順序)
        private static readonly (Stat s, double w)[][] Weights =
        {
            new[] { (Stat.SPD, .40), (Stat.STA, .20), (Stat.PAC, .15), (Stat.KCK, .15), (Stat.MEN, .10) },     // Fast
            new[] { (Stat.SPD, .25), (Stat.STA, .30), (Stat.PAC, .25), (Stat.KCK, .10), (Stat.MEN, .10) },     // Mid
            new[] { (Stat.STA, .35), (Stat.PAC, .25), (Stat.SPD, .20), (Stat.MEN, .10), (Stat.KCK, .10) },     // Long
            new[] { (Stat.CLB, .45), (Stat.STA, .25), (Stat.PAC, .15), (Stat.MEN, .10), (Stat.SPD, .05) },     // Climb
            new[] { (Stat.DWN, .45), (Stat.SPD, .20), (Stat.STA, .15), (Stat.PAC, .10), (Stat.MEN, .10) },     // Descent
        };

        public static double Rating(IReadOnlyList<double> stats, SectionType t)
        {
            double sum = 0;
            foreach (var (s, w) in Weights[(int)t]) sum += w * stats[(int)s];
            return sum;
        }
    }
}
