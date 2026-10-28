using Ekiden.Domain.Numerics;
using Ekiden.Domain.Training;

namespace Ekiden.Domain.World
{
    /// <summary>年度末の施設・スタッフの強化の方針(05 §4.2。検算用の『おまかせ』。08 R-11 の推奨)。抽象の自校と実体の選手を持つ自校が同じ方針を使う。</summary>
    public static class FacilityPolicy
    {
        /// <summary>強化の順番(寮 → スカウト → コーチ → トラック → 坂道 → トレーニング室 → トレーナー → 栄養士 → 医療室)。</summary>
        public static readonly string[] Order = { "dorm", "scout", "coach", "track", "hill", "gym", "trainer", "nutri", "medical" };

        public static int Level(FacilityLevels f, string k)
        {
            switch (k)
            {
                case "dorm": return f.Dorm;
                case "scout": return f.Scout;
                case "coach": return f.Coach;
                case "track": return f.Track;
                case "hill": return f.Hill;
                case "gym": return f.Gym;
                case "trainer": return f.Trainer;
                case "nutri": return f.Nutri;
                default: return f.Medical;
            }
        }

        public static void SetLevel(ref FacilityLevels f, string k, int v)
        {
            switch (k)
            {
                case "dorm": f.Dorm = v; break;
                case "scout": f.Scout = v; break;
                case "coach": f.Coach = v; break;
                case "track": f.Track = v; break;
                case "hill": f.Hill = v; break;
                case "gym": f.Gym = v; break;
                case "trainer": f.Trainer = v; break;
                case "nutri": f.Nutri = v; break;
                default: f.Medical = v; break;
            }
        }

        /// <summary>上限(監督ランクで決まる)まで、順番に 1 つずつ上げる。予備費(<see cref="WorldK.Reserve"/>)を残す。資金は <c>Q6</c> に丸める。</summary>
        public static void Upgrade(ref FacilityLevels f, ref double funds, int rank)
        {
            int cap = WorldMath.FacilityCap(rank);
            while (true)
            {
                string pick = null;
                foreach (string k in Order) if (Level(f, k) < cap) { pick = k; break; }
                if (pick == null) break;
                double cost = WorldMath.UpgradeCost(Level(f, pick));
                if (funds - cost < WorldK.Reserve) break;
                funds = Q6.Round(funds - cost);
                SetLevel(ref f, pick, Level(f, pick) + 1);
            }
        }
    }
}
