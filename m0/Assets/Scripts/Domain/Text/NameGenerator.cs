using System;
using Ekiden.Domain.Rng;

namespace Ekiden.Domain.Text
{
    /// <summary>生成した選手の名前(姓・名・かな)。</summary>
    public struct PersonName
    {
        public string Family, Given, FamilyKana, GivenKana;

        /// <summary>姓 + 名(区切りなし)。重複の判定と OB の記録の名前に使う。</summary>
        public string Full { get { return Family + Given; } }
    }

    /// <summary>
    /// 選手の名前の生成(02 §4.1・09 §8.2 label 8。<c>look_seed</c> から決まる。候補のときも入学後も同じ)。
    /// 引く順(固定): 姓の添字 = (label 8, a = look_seed, b = 0)の uniform ×1 / 名の添字 = (b = 1)の uniform ×1。かなは表の組(b = 2・3 は予約)。
    /// 重複(<paramref name="taken"/> が真)なら、再抽選 k = 0..9 回目に (b = 30 + k) のストリームから姓・名の順に uniform ×2 で引き直す。10 回でも重複なら、そのまま使う。
    /// 留学生は国別の表(L-20)が決まるまで、国の印象と結びつけない架空の音の組み合わせ(<see cref="NameTableData.IntlFamilyParts"/>)を列ごとに 1 つずつ引いてつなぐ。
    /// </summary>
    public static class NameGenerator
    {
        public const int FamilyKey = 0, GivenKey = 1, RedrawKey = 30, MaxRedraw = 10;

        private static int Pick(double u, int n) { return Math.Min(n - 1, (int)(u * n)); }

        public static PersonName Generate(IRngFactory rngs, uint lookSeed, bool overseas = false, Func<string, bool> taken = null)
        {
            Pcg32 gf = rngs.Stream(RngRegistry.LabelLook, lookSeed, FamilyKey), gg = rngs.Stream(RngRegistry.LabelLook, lookSeed, GivenKey);
            PersonName n = Draw(gf, gg, overseas);
            for (int k = 0; k < MaxRedraw && taken != null && taken(n.Full); k++)
            {
                Pcg32 g = rngs.Stream(RngRegistry.LabelLook, lookSeed, RedrawKey + k);
                n = Draw(g, g, overseas);
            }
            return n;
        }

        private static PersonName Draw(Pcg32 gf, Pcg32 gg, bool overseas)
        {
            if (overseas)
            {
                string f = Parts(gf, NameTableData.IntlFamilyParts), g = Parts(gg, NameTableData.IntlGivenParts);
                return new PersonName { Family = f, Given = g, FamilyKana = ToHiragana(f), GivenKana = ToHiragana(g) };
            }
            int i = Pick(gf.Uniform(), NameTableData.FamilyText.Length), j = Pick(gg.Uniform(), NameTableData.GivenText.Length);
            return new PersonName { Family = NameTableData.FamilyText[i], Given = NameTableData.GivenText[j], FamilyKana = NameTableData.FamilyKana[i], GivenKana = NameTableData.GivenKana[j] };
        }

        private static string Parts(Pcg32 g, string[][] columns)
        {
            string s = "";
            foreach (string[] col in columns) s += col[Pick(g.Uniform(), col.Length)];
            return s;
        }

        /// <summary>カタカナ → ひらがな(長音はそのまま)。</summary>
        public static string ToHiragana(string katakana)
        {
            var c = katakana.ToCharArray();
            for (int i = 0; i < c.Length; i++) if (c[i] >= 'ァ' && c[i] <= 'ヶ') c[i] = (char)(c[i] - 0x60);
            return new string(c);
        }

        /// <summary>選手に名前を付ける(生成済みなら何もしない)。</summary>
        public static void Assign(Entities.Runner r, IRngFactory rngs, Func<string, bool> taken = null)
        {
            if (r.FamilyName.Length > 0) return;
            PersonName n = Generate(rngs, r.LookSeed, r.Overseas, taken);
            r.FamilyName = n.Family;
            r.GivenName = n.Given;
            r.FamilyKana = n.FamilyKana;
            r.GivenKana = n.GivenKana;
        }
    }
}
