using System;
using System.Collections.Generic;
using Ekiden.Domain.Rng;
using Ekiden.Domain.World;

namespace Ekiden.Domain.Entities
{
    /// <summary>個性・素養から決まる派生値(10 §2.3・§5。保存しない)と定数(10 §2.4)。</summary>
    public static class Individuality
    {
        public const double K_FORM_UP = 0.20, K_FORM_DOWN = 0.20;
        public const double K_FLOW_UP = 0.20, K_FLOW_DOWN = 0.20;
        public const double K_GIFT_XP = 0.15;
        public const double K_GIFT_ACE_OTHER = 0.05;
        public const int K_GIFT_POT = 6, K_GIFT_ACE_OTHER_POT = 2;
        public const int K_GIFT_REVEAL_WEEKS = 12;
        public const double K_FIT_GOOD = 1.05, K_FIT_OK = 0.99;

        /// <summary>絆の効き(K_BOND_K。集団走◎ / 普通 / 単独走◎)。</summary>
        public static double BondK(Style s)
        {
            switch (s.Run)
            {
                case StyleRun.Pack: return 0.30;
                case StyleRun.Solo: return 0.05;
                default: return 0.15;
            }
        }

        /// <summary>個別指導の週の効率(K_IND_MULT)。</summary>
        public static double IndMult(Style s)
        {
            return s.Run == StyleRun.Solo ? 1.25 : 1.0;
        }

        /// <summary>能力ごとの XP の乗数 gm[10]。型・展開・素養の積(10 §5.1)。</summary>
        public static double[] GrowthMult(Style style, IReadOnlyList<Gift> gifts)
        {
            var gm = new double[StatInfo.Count];
            for (int i = 0; i < gm.Length; i++) gm[i] = 1.0;
            switch (style.Form)
            {
                case StyleForm.Speed: gm[(int)Stat.SPD] *= 1.0 + K_FORM_UP; gm[(int)Stat.STA] *= 1.0 - K_FORM_DOWN; break;
                case StyleForm.Long: gm[(int)Stat.STA] *= 1.0 + K_FORM_UP; gm[(int)Stat.SPD] *= 1.0 - K_FORM_DOWN; break;
            }
            switch (style.Flow)
            {
                case StyleFlow.Chase: gm[(int)Stat.KCK] *= 1.0 + K_FLOW_UP; gm[(int)Stat.PAC] *= 1.0 - K_FLOW_DOWN; break;
                case StyleFlow.Lead: gm[(int)Stat.PAC] *= 1.0 + K_FLOW_UP; gm[(int)Stat.KCK] *= 1.0 - K_FLOW_DOWN; break;
            }
            if (gifts != null)
            {
                foreach (Gift g in gifts)
                {
                    switch (g)
                    {
                        case Gift.Climber: gm[(int)Stat.CLB] *= 1.0 + K_GIFT_XP * 1.0; break;
                        case Gift.Descender: gm[(int)Stat.DWN] *= 1.0 + K_GIFT_XP * 1.0; break;
                        case Gift.Ace:
                            for (int i = 0; i < gm.Length; i++)
                            {
                                double w = (i == (int)Stat.MEN || i == (int)Stat.CON) ? 1.0 : K_GIFT_ACE_OTHER / K_GIFT_XP;
                                gm[i] *= 1.0 + K_GIFT_XP * w;
                            }
                            break;
                    }
                }
            }
            return gm;
        }

        // ---- 生成(10 §4)。乱数は 09 §8.2 の鍵(label 8、a = look_seed、b = 40 個性 / 41 素養)。引く回数は固定
        private static readonly double[][] FormP =
        {
            new[] { 0.15, 0.70, 0.15 },     // Balanced: Speed / Balance / Long
            new[] { 0.05, 0.05, 0.90 },     // Endurance
            new[] { 0.90, 0.10, 0.00 },     // Speed
            new[] { 0.00, 0.40, 0.60 },     // Mountain
        };
        private static readonly double[] RunP = { 0.30, 0.40, 0.30 };       // Pack / Free / Solo
        private static readonly double[] FlowP = { 0.30, 0.40, 0.30 };      // Chase / Even / Lead
        public static readonly double[] GiftCount = { 0.80, 0.18, 0.02 };   // 素養の個数 0 / 1 / 2
        private static readonly double[] GiftKind = { 0.38, 0.31, 0.31 };   // Climber / Descender / Ace

        private static int Pick(double[] w, double u)
        {
            double acc = 0.0;
            for (int i = 0; i < w.Length; i++)
            {
                acc += w[i];
                if (u < acc) return i;
            }
            return w.Length - 1;
        }

        /// <summary>個性(型・走法・展開)を決める。型の分布は入学時の能力の型(05 §6.2)による。固定 3 回: ① 型 ② 走法 ③ 展開。</summary>
        public static Style GenerateStyle(IRngFactory rngs, long lookSeed, Profile profile)
        {
            Pcg32 g = rngs.Stream(RngRegistry.LabelLook, lookSeed, 40);
            double u1 = g.Uniform(), u2 = g.Uniform(), u3 = g.Uniform();
            return new Style((StyleForm)Pick(FormP[(int)profile], u1), (StyleRun)Pick(RunP, u2), (StyleFlow)Pick(FlowP, u3));
        }

        /// <summary>素養を決める(0〜2 個)。固定 3 回: ① 個数 ② 1 つ目の種類 ③ 2 つ目の種類(個数が 2 のときだけ使う。1 つ目と同じなら次の種類に送る)。</summary>
        public static Gift[] GenerateGifts(IRngFactory rngs, long lookSeed)
        {
            Pcg32 g = rngs.Stream(RngRegistry.LabelLook, lookSeed, 41);
            double un = g.Uniform(), u1 = g.Uniform(), u2 = g.Uniform();
            int n = un < GiftCount[0] ? 0 : (un < GiftCount[0] + GiftCount[1] ? 1 : 2);
            var list = new List<Gift>();
            if (n >= 1) list.Add((Gift)Pick(GiftKind, u1));
            if (n >= 2)
            {
                int second = Pick(GiftKind, u2);
                if (second == (int)list[0]) second = (second + 1) % GiftKind.Length;
                list.Add((Gift)second);
            }
            return list.ToArray();
        }

        /// <summary>素養による素質の上限の加算(入学時に potential へ足す。100 を超えない。10 §4)。</summary>
        public static void AddGiftPotential(int[] potential, IReadOnlyList<Gift> gifts)
        {
            if (gifts == null) return;
            foreach (Gift g in gifts)
            {
                switch (g)
                {
                    case Gift.Climber: Bump(potential, Stat.CLB, K_GIFT_POT); break;
                    case Gift.Descender: Bump(potential, Stat.DWN, K_GIFT_POT); break;
                    case Gift.Ace:
                        for (int i = 0; i < potential.Length; i++)
                            Bump(potential, (Stat)i, (i == (int)Stat.MEN || i == (int)Stat.CON) ? K_GIFT_POT : K_GIFT_ACE_OTHER_POT);
                        break;
                }
            }
        }

        private static void Bump(int[] pot, Stat s, int by)
        {
            pot[(int)s] = Math.Min(100, pot[(int)s] + by);
        }
    }
}
