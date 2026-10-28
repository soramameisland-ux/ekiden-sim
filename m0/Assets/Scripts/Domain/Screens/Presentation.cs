using System;
using System.Collections.Generic;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Training;

namespace Ekiden.Domain.Screens
{
    public enum Trait { Serious, Competitive, MyPace, Passionate, Careful, Cheerful, Quiet, Caring }

    /// <summary>候補・選手の性格タグ(R-16)・個性チップ・練習の相性の印(R-17)。見せるだけで、規則には作用しない。</summary>
    public static class Presentation
    {
        public const double K_FIT_GOOD = 1.05, K_FIT_OK = 0.99;

        /// <summary>R-16: 性格タグ(1〜2 個)。look_seed から決める(候補のときも入学後も同じ値)。固定 3 回: ① 個数(u &lt; 0.5 なら 2 個)② 1 つ目 ③ 2 つ目。残りの種類から一様に選ぶ(重複なし)。鍵は label 8・a = look_seed・b = 20。</summary>
        public static Trait[] Traits(IRngFactory rngs, long lookSeed)
        {
            Pcg32 g = rngs.Stream(RngRegistry.LabelLook, lookSeed, 20);
            double un = g.Uniform(), u1 = g.Uniform(), u2 = g.Uniform();
            var pool = new List<Trait>((Trait[])Enum.GetValues(typeof(Trait)));
            var res = new List<Trait>();
            int i = Math.Min(pool.Count - 1, (int)(u1 * pool.Count));
            res.Add(pool[i]);
            pool.RemoveAt(i);
            if (un < 0.5)
            {
                int j = Math.Min(pool.Count - 1, (int)(u2 * pool.Count));
                res.Add(pool[j]);
            }
            return res.ToArray();
        }

        /// <summary>R-17: 個性チップのキー。普通でない軸だけを出す(型 = speed / long、走法 = pack / solo、展開 = chase / lead)。三つとも普通なら 'standard'。表示の文言は名称テーブルで引く(09 §12.3)。</summary>
        public static List<string> StyleChips(Style s)
        {
            var chips = new List<string>();
            if (s.Form == StyleForm.Speed) chips.Add("form.speed");
            if (s.Form == StyleForm.Long) chips.Add("form.long");
            if (s.Run == StyleRun.Pack) chips.Add("run.pack");
            if (s.Run == StyleRun.Solo) chips.Add("run.solo");
            if (s.Flow == StyleFlow.Chase) chips.Add("flow.chase");
            if (s.Flow == StyleFlow.Lead) chips.Add("flow.lead");
            if (chips.Count == 0) chips.Add("standard");
            return chips;
        }

        /// <summary>テーマ(4 週)の能力別の XP(xu)の合計(走行量を含む)。</summary>
        public static double[] ThemeGains(TrainingMaster m, string themeId)
        {
            var tot = new double[StatInfo.Count];
            foreach (Menu menu in m.Themes[themeId])
            {
                for (int i = 0; i < tot.Length; i++) tot[i] += m.KeySessions[menu.Key1].Gain[i];
                for (int i = 0; i < tot.Length; i++) tot[i] += m.KeySessions[menu.Key2].Gain[i];
                if (menu.Mileage != null) for (int i = 0; i < tot.Length; i++) tot[i] += m.Mileages[menu.Mileage].Gain[i];
            }
            return tot;
        }

        /// <summary>そのテーマの XP の配分を、選手の乗数 gm(null = 乗数なし)で重み付けした平均の乗数。1.00 = 標準。</summary>
        public static double FitValue(TrainingMaster m, double[] gm, string themeId)
        {
            double[] g = ThemeGains(m, themeId);
            double tot = 0.0;
            foreach (double x in g) tot += x;
            double acc = 0.0;
            for (int i = 0; i < g.Length; i++) acc += g[i] * (gm != null ? gm[i] : 1.0);
            return tot != 0.0 ? acc / tot : 1.0;
        }

        public static char FitMark(double v) { return v >= K_FIT_GOOD ? '◎' : (v >= K_FIT_OK ? '〇' : '△'); }

        /// <summary>画面に出す相性の値。素養は、判明するまで(revealed = false)入れない(隠しの情報を印から漏らさない。10 §8)。</summary>
        public static double FitView(TrainingMaster m, Style style, IReadOnlyList<Gift> gifts, bool revealed, string themeId)
        {
            return FitValue(m, Individuality.GrowthMult(style, revealed ? gifts : new Gift[0]), themeId);
        }
    }
}
