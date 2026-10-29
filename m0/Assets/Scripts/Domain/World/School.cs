using System;
using System.Collections.Generic;
using Ekiden.Domain.Training;

namespace Ekiden.Domain.World
{
    /// <summary>大学(02 §4.3)。他校は名声・基準名声・指導体制で動く。自校(検算用の抽象)は施設レベルと資金を持つ。</summary>
    public sealed class School
    {
        public int Id;
        public Region Region;
        public Tier Tier;
        public double Prestige;
        public double Hist;                  // 名声ポイントの履歴(05 §3.2)
        public double Base;                  // 基準名声(他校のみ)
        public double CoachQ;                // 指導体制(他校のみ。±0.30)
        public bool IsPlayer;
        public readonly List<WorldRunner> Roster = new List<WorldRunner>();
        public int YearPoints;
        public double FamePoints;
        public int PrevPoints;
        public int? LastNewYearRank;
        public double CumPoints;
        // 自校(検算用の抽象。ゲーム本体の自校は Runner で育つ)
        public FacilityLevels Fac;
        public int ScoutLv = 2;
        public double Funds;
        public readonly Dictionary<int, int> Ob = new Dictionary<int, int>();

        public static double Clamp(double x, double lo, double hi) { return Math.Max(lo, Math.Min(hi, x)); }

        /// <summary>施設・スタッフの平均レベル(8 項目。05 §5)。</summary>
        public static double LvEff(FacilityLevels f)
        {
            return (TrainingMaster.EffLv(f.Coach) + TrainingMaster.EffLv(f.Trainer) + TrainingMaster.EffLv(f.Nutri) + TrainingMaster.EffLv(f.Dorm) + TrainingMaster.EffLv(f.Track) + TrainingMaster.EffLv(f.Hill) + TrainingMaster.EffLv(f.Gym) + TrainingMaster.EffLv(f.Medical)) / 8.0;
        }

        /// <summary>施設レベル(他校は名声と指導体制から。05 §9.2)。</summary>
        public double Lv()
        {
            if (IsPlayer) return LvEff(Fac);
            return Clamp(1.0 + WorldK.RivalFacCoef * Math.Pow(Prestige / 1000.0, WorldK.RivalFacExp) + WorldK.CoachLv * CoachQ, 0.5, 6.5);
        }

        public double Dorm() { return IsPlayer ? TrainingMaster.EffLv(Fac.Dorm) : Math.Min(5.0, Lv()); }

        /// <summary>監督ランク(累計評価ポイントから。05 §3.3)。</summary>
        public int Rank()
        {
            int r = 1;
            for (int i = 0; i < WorldK.RankCum.Length; i++) if (CumPoints >= WorldK.RankCum[i]) r = i + 1;
            return r;
        }

        /// <summary>奨学金枠(05 §2)。他校 clamp(round(1 + 名声/150), 1, 6)、自校 min(5, 寮Lv + 1)。</summary>
        public int ScholSlots()
        {
            if (IsPlayer) return Math.Min(5, Fac.Dorm + 1);
            return (int)Clamp(Math.Round(1 + Prestige / 150.0, MidpointRounding.ToEven), 1, 6);
        }

        public int Quota() { return (IsPlayer || Prestige >= WorldK.SmallPrestige) ? WorldK.QuotaNormal : WorldK.QuotaSmall; }
    }
}
