using System;
using System.Collections.Generic;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Rng;

namespace Ekiden.Domain.World
{
    /// <summary>入学候補(プール)の生成(05 §6.1)。候補 1 人 = 1 ストリーム(label 3・a = 入学年度・b = 1000 + 候補の番号)。</summary>
    public static class PoolGenerator
    {
        private static readonly double[] PrefCum = BuildPrefCum();
        private static readonly double EliteShare = BuildEliteShare();

        private static double[] BuildPrefCum()
        {
            var cum = new double[Geo.Prefectures.Count];
            double acc = 0;
            for (int i = 0; i < cum.Length; i++) { acc += Geo.Prefectures[i].SupplyWeight; cum[i] = acc; }
            return cum;
        }

        private static double BuildEliteShare()
        {
            double e = 0;
            foreach (Prefecture p in Geo.Prefectures) if (p.Elite) e += p.SupplyWeight;
            return e / Geo.TotalSupplyWeight;
        }

        /// <summary>強豪県の候補は能力の基準値を +3 する(02 §6)。プール全体の A0 の平均が 37 のままになるように、基準の平均をその分(3 × 強豪県の供給の割合)だけ下げる。</summary>
        public static double BaseMean(bool elitePref, double? bonus = null)
        {
            double b = bonus ?? WorldK.ElitePrefBonus;
            return WorldK.PoolAMean - b * EliteShare + (elitePref ? b : 0.0);
        }

        private static double Pick(double[] cum, double u)
        {
            double x = u * cum[cum.Length - 1];
            for (int i = 0; i < cum.Length; i++) if (x < cum[i]) return i;
            return cum.Length - 1;
        }

        /// <summary>1 人の候補。引く順序(固定): ① 留学生か(uniform)② 県(uniform)③ 出身高校(uniform)④ 出身国(uniform)⑤ A0(normal)⑥ gap(normal)⑦ 成長タイプ(uniform)⑧ 型(uniform)⑨ look_seed(u32)。</summary>
        public static WorldRunner Candidate(IRngFactory rngs, int entryYear, int index, double? elitePrefBonus = null)
        {
            Pcg32 g = rngs.Stream(RngRegistry.LabelScout, entryYear, 1000 + index);
            var r = new WorldRunner { Index = index };
            double uOverseas = g.Uniform(), uPref = g.Uniform(), uHs = g.Uniform(), uCountry = g.Uniform();
            r.Overseas = uOverseas < WorldK.OverseasShare;
            bool elite = false;
            if (!r.Overseas)
            {
                int pi = (int)Pick(PrefCum, uPref);
                Prefecture pref = Geo.Prefectures[pi];
                r.PrefectureId = pref.Id; r.Region = pref.Region; elite = pref.Elite;
                r.HighSchoolU = Q6.Round(uHs);
            }
            else
            {
                r.CountryIndex = Math.Min(23, (int)(uCountry * 24));
            }
            r.A0 = Q6.Round(School.Clamp(g.Normal(BaseMean(elite, elitePrefBonus), WorldK.PoolASd), WorldK.PoolAMin, WorldK.PoolAMax));
            r.Gap = Q6.Round(School.Clamp(g.Normal(WorldK.PoolGapMean, WorldK.PoolGapSd) + WorldK.PoolGapSlope * (r.A0 - WorldK.PoolAMean), WorldK.PoolGapMin, WorldK.PoolGapMax));
            double uG = g.Uniform(), uP = g.Uniform();
            r.Growth = PickIndex(WorldK.GrowthShare, uG) == 0 ? GrowthKind.Early : (PickIndex(WorldK.GrowthShare, uG) == 1 ? GrowthKind.Normal : GrowthKind.Late);
            r.Profile = (Profile)PickIndex(WorldK.ProfileShare, uP);
            r.LookSeed = g.U32();
            r.SetValues();
            return r;
        }

        private static int PickIndex(double[] share, double u)
        {
            double acc = 0;
            for (int i = 0; i < share.Length; i++) { acc += share[i]; if (u < acc) return i; }
            return share.Length - 1;
        }

        public static List<WorldRunner> Generate(IRngFactory rngs, int entryYear, int n = WorldK.K_POOL_N, double? elitePrefBonus = null)
        {
            var pool = new List<WorldRunner>(n);
            for (int i = 0; i < n; i++) pool.Add(Candidate(rngs, entryYear, i, elitePrefBonus));
            return pool;
        }
    }
}
