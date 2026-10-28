using Ekiden.Domain.Entities;
using Ekiden.Domain.Numerics;

namespace Ekiden.Domain.Training
{
    /// <summary>XP の付与とレベルアップ(03 §6 手順3。06 の効果 xp が使う)。<c>E</c>・素質の補正は掛けない。</summary>
    public static class Progression
    {
        /// <summary>能力 i に XP(xu)を与え、たまっていれば能力を上げる(1 回に最大 K_LEVELUP_MAX)。上げた能力の数を返す。</summary>
        public static int ApplyXp(Runner r, int i, int xu)
        {
            int before = r.Stats[i];
            r.Xp[i] += xu;
            int n = 0;
            while (n < TrainingMaster.K_LEVELUP_MAX && r.Stats[i] < r.Potential[i])
            {
                int t = Rounding.HalfUp(10 * (TrainingMaster.K_T_BASE + TrainingMaster.K_T_SLOPE * r.Stats[i]));
                if (r.Xp[i] < t) break;
                r.Xp[i] -= t;
                r.Stats[i]++;
                n++;
            }
            if (r.Stats[i] >= r.Potential[i]) r.Xp[i] = 0;
            return r.Stats[i] - before;
        }
    }
}
