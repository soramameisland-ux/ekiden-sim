using System;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Training;

namespace Ekiden.Domain.World
{
    /// <summary>世界の数式(05 §3〜§9)。乱数を使わない部分。</summary>
    public static class WorldMath
    {
        // 成長の進捗率(累積)の近似(05 §9.3): 成長タイプ別・学年別の (切片, Lv 傾き)
        private static readonly double[][][] Prog =
        {
            new[] { new[] { 0.24, 0.020 }, new[] { 0.40, 0.030 }, new[] { 0.50, 0.030 }, new[] { 0.56, 0.033 } },      // Early
            new[] { new[] { 0.19, 0.020 }, new[] { 0.37, 0.030 }, new[] { 0.49, 0.035 }, new[] { 0.58, 0.035 } },      // Normal
            new[] { new[] { 0.14, 0.010 }, new[] { 0.31, 0.020 }, new[] { 0.48, 0.025 }, new[] { 0.60, 0.028 } },      // Late
        };

        /// <summary>yearDone = 0〜4 の累積進捗率(ギャップに対する割合)。lv は施設・スタッフの平均レベル。</summary>
        public static double ProgBase(GrowthKind g, int yearDone, double lv)
        {
            if (yearDone <= 0) return 0.0;
            double[] ab = Prog[(int)g][yearDone - 1];
            return ab[0] + ab[1] * (lv - 1);
        }

        public static double GapAdj(double gap)
        {
            return Math.Max(0.7, Math.Min(1.3, 1.33 - 0.0125 * gap));
        }

        /// <summary>今年度のレース時点(年度の 75% 経過)の力。</summary>
        public static double SeasonPower(WorldRunner r, double lv, double frac = 0.75)
        {
            double inc = (ProgBase(r.Growth, r.Grade, lv) - ProgBase(r.Growth, r.Grade - 1, lv)) * GapAdj(r.Gap);
            return r.A0 + r.Gap * (r.Prog + frac * inc);
        }

        public static void SeasonEnd(WorldRunner r, double lv)
        {
            double inc = (ProgBase(r.Growth, r.Grade, lv) - ProgBase(r.Growth, r.Grade - 1, lv)) * GapAdj(r.Gap);
            r.Prog = Q6.Round(Math.Min(0.95, r.Prog + inc));
        }

        /// <summary>名声ポイント F(k)(k は 1 位から。05 §3.1)。</summary>
        public static double FamePoint(int k)
        {
            return WorldK.K_FAME_ENTRY + WorldK.K_FAME_TOP * Math.Exp(-(k - 1) / WorldK.K_FAME_DECAY);
        }

        /// <summary>名声の年度更新(05 §3.2): 履歴と名声を更新する。</summary>
        public static void UpdatePrestige(School s)
        {
            s.Hist = Q6.Round((1 - WorldK.K_PRESTIGE_ALPHA) * s.Hist + WorldK.K_PRESTIGE_ALPHA * s.FamePoints);
            double target = 1000.0 * (1.0 - Math.Exp(-s.Hist / WorldK.K_PRESTIGE_H));
            s.Prestige = Q6.Round(s.Prestige + WorldK.K_PRESTIGE_STEP * (target - s.Prestige));
        }

        /// <summary>提示スコア(乱数・好感度を除く。05 §8.1)。</summary>
        public static double OfferBase(School s, WorldRunner r, double p10)
        {
            int band = r.Overseas ? Geo.Overseas : Geo.Band(r.Region, s.Region);
            double score = WorldK.K_W_PRESTIGE * s.Prestige + WorldK.Local[band] + WorldK.FacBonus * (s.Dorm() - 1.0) + WorldK.CoachOffer * s.CoachQ;
            double play = Math.Max(-1.0, Math.Min(1.0, (r.Q - p10) / WorldK.PlaytimeScale)) * WorldK.PlaytimeRange;
            return score + play;
        }

        // ---- 資金(05 §4。万円)
        public static double Income(double prestige, int points)
        {
            return WorldK.IncBase + WorldK.IncPrestige * prestige + WorldK.IncPrestige2 * prestige * prestige + WorldK.IncPoint * points;
        }

        public static double StaffExpense(int coach, int trainer, int nutri, int scout)
        {
            return WorldK.ExpStaff[coach - 1] + WorldK.ExpStaff[trainer - 1] + WorldK.ExpStaff[nutri - 1] + WorldK.ExpStaff[scout - 1];
        }

        public static double FacilityUpkeep(int dorm, int track, int hill, int gym, int medical)
        {
            return WorldK.ExpFacUpkeep * ((dorm - 1) + (track - 1) + (hill - 1) + (gym - 1) + (medical - 1));
        }

        /// <summary>スタッフの給与。主務(L-70 ②。Lv0 = 不在 = 0)を足す。</summary>
        public static double StaffExpense(FacilityLevels f)
        {
            return StaffExpense(f.Coach, f.Trainer, f.Nutri, f.Scout) + (f.Manager >= 1 ? WorldK.ExpStaff[f.Manager - 1] : 0.0);
        }

        /// <summary>施設の維持費。新設の施設(L-70 ②)は <c>8 × Lv</c>(通常の施設は <c>8 × (Lv − 1)</c>。未設置は 0)。</summary>
        public static double FacilityUpkeep(FacilityLevels f)
        {
            return FacilityUpkeep(f.Dorm, f.Track, f.Hill, f.Gym, f.Medical) + WorldK.ExpFacUpkeep * (f.Altitude + f.Mental + f.Camp);
        }

        /// <summary>月の指導ポイント(02 §4.4。L-70 ②・L-25)。主務 Lv3 以上で +1・Lv6 以上でさらに +1。</summary>
        public const int GuidanceMax = 6;           // 月の指導ポイントの上限(主務 Lv6 以上。保存の検証が使う)

        public static int GuidancePoints(int managerLv) { return 4 + (managerLv >= 3 ? 1 : 0) + (managerLv >= 6 ? 1 : 0); }

        public static double RunnerExpense(int members) { return WorldK.ExpPerRunner * members; }

        public static double UpgradeCost(int currentLv) { return currentLv <= 0 ? WorldK.NewCost : currentLv >= 5 ? WorldK.UpCostHi[currentLv - 5] : WorldK.UpBase + WorldK.UpSlope * currentLv; }

        public static int FacilityCap(int rank)
        {
            return WorldK.FacCapBase + (rank >= WorldK.FacCapR4 ? 1 : 0) + (rank >= WorldK.FacCapR5 ? 1 : 0)
                   + (rank >= WorldK.FacCapR6 ? 1 : 0) + (rank >= WorldK.FacCapR7 ? 1 : 0) + (rank >= WorldK.FacCapR8 ? 1 : 0);
        }
    }
}
