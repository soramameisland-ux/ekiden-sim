using System.Collections.Generic;

namespace Ekiden.Domain.Rng
{
    /// <summary>鍵 (label, a, b) の登録表(09 §8.2)。新しい用途は、この表に足してから使う。</summary>
    public readonly struct RngRange
    {
        public int Label { get; }
        public string AMeaning { get; }
        public long Lo { get; }
        public long Hi { get; }
        public string Purpose { get; }
        public string Doc { get; }

        public RngRange(int label, string aMeaning, long lo, long hi, string purpose, string doc)
        {
            Label = label; AMeaning = aMeaning; Lo = lo; Hi = hi; Purpose = purpose; Doc = doc;
        }
    }

    public static class RngRegistry
    {
        public const int LabelInjury = 1, LabelForm = 2, LabelScout = 3, LabelRace = 4, LabelEvent = 5, LabelWorldUpdate = 6, LabelWorldGen = 7, LabelLook = 8, LabelTeamYear = 9;

        public static readonly IReadOnlyList<RngRange> Ranges = new[]
        {
            new RngRange(1, "runner_id", 0, 1L << 31, "週ごとの故障(b = 通算週)", "03 §3.2"),
            new RngRange(2, "runner_id", 0, 1L << 31, "週ごとの調子(b = 通算週)", "03 §3.2"),
            new RngRange(3, "入学年度", 0, 99, "入学候補の年ごとの共通の抽選(0: 地方・型の割合 / 1: プールの出身県の抽選)", "09 §8.2"),
            new RngRange(3, "入学年度", 100, 199, "他校の狙い撃ち(100 + 学校ID)", "09 §8.2"),
            new RngRange(3, "入学年度", 1000, 1999, "候補1人の生成(1000 + 候補の番号)", "09 §8.2"),
            new RngRange(3, "入学年度", 9000, 9999, "特別招聘の選手(9000 + index)", "07 §6.2"),
            new RngRange(3, "入学年度", 10000, 59999, "割り当ての提示スコアの乱数(10000 + 候補の番号 × 64 + 学校ID。候補 ≤ 780)", "09 §8.2"),
            new RngRange(4, "race_id(第一〜第三戦)", 0, 3599, "区間(チームID × 100 + 区間番号)", "04 §12"),
            new RngRange(4, "race_id(第一〜第三戦)", 8000, 8035, "欠場の抽選(8000 + チームID)", "04 §12"),
            new RngRange(4, "race_id(第一〜第三戦)", 9000, 9035, "他校の調子の抽選(9000 + チームID)", "04 §12"),
            new RngRange(4, "race_id(第一〜第三戦)", 9998, 9998, "予報の誤差", "04 §12"),
            new RngRange(4, "race_id(第一〜第三戦)", 9999, 9999, "環境", "04 §12"),
            new RngRange(4, "race_id(予選会)", 0, 3599, "予選会の選手(チームID × 100 + 選手の番号)", "04 §12"),
            new RngRange(5, "通算週", 0, 0, "イベントの発生の判定", "06 §7.5"),
            new RngRange(5, "通算週", 1000, 1099, "k 番目に発生したイベントの効果(1000 + k)", "06 §7.5"),
            new RngRange(6, "年度", 0, 0, "世界の更新の共通の抽選", "09 §8.2"),
            new RngRange(6, "年度", 100, 199, "学校ごとの更新(基準名声のゆらぎ・指導体制。100 + 学校ID)", "09 §8.2"),
            new RngRange(6, "年度", 1000, 1099, "学校ごとの入学・名簿の調整(1000 + 学校ID)", "09 §8.2"),
            new RngRange(7, "段階(0 学校 / 1 高校 / 2 初期の名簿)", 0, 999, "新規ゲームの世界の生成(0: b = 学校ID / 1: b = 高校ID / 2: b = 学校ID × 4 + 学年 − 1)", "09 §8.2"),
            new RngRange(8, "look_seed", 0, 3, "名前(姓・名・姓のかな・名のかな)", "09 §8.2"),
            new RngRange(8, "look_seed", 4, 8, "外見(髪型・髪色・顔・体型・肌)", "09 §8.2"),
            new RngRange(8, "look_seed", 10, 19, "10能力の展開の偏り n10(05 §6.2。b = 10)・視察の誤差(b = 11。Q_obs の雑音。05 §7.2)・入学時の素質の雑音(b = 12。10 個の N(0,3)。05 §6.2)", "09 §8.2"),
            new RngRange(8, "look_seed", 20, 29, "性格タグ(20: 個数 / 21〜: 選択。08 R-16)", "09 §8.2"),
            new RngRange(8, "look_seed", 30, 39, "名前の重複を避ける再抽選(30 + 回数)", "09 §8.2"),
            new RngRange(8, "look_seed", 40, 49, "個性と素養(40: 型・走法・展開 / 41: 素養の個数・種類)", "10 §4"),
            new RngRange(9, "年度", 0, 0, "自校(実体の選手)の年度末の抽選: 卒業生を ID の昇順に 2 回ずつ(実業団か・その他の進路の分け)、続けて やる気 1 の選手を ID の昇順に 1 回ずつ(退部)。年度 0 = 新規ゲームで部員数を指定したとき、名簿から残す人を選ぶ入れ替え", "05 §10.2・§10.4"),
        };

        /// <summary>同じ (label, a の意味) で b の範囲が重なるものを返す(空であること)。</summary>
        public static List<string> FindOverlaps()
        {
            var errs = new List<string>();
            var groups = new Dictionary<string, List<RngRange>>();
            foreach (RngRange r in Ranges)
            {
                string key = r.Label + "|" + r.AMeaning;
                if (!groups.TryGetValue(key, out List<RngRange> list)) groups[key] = list = new List<RngRange>();
                list.Add(r);
            }
            foreach (KeyValuePair<string, List<RngRange>> kv in groups)
            {
                List<RngRange> rs = kv.Value;
                rs.Sort((x, y) => x.Lo.CompareTo(y.Lo));
                for (int i = 1; i < rs.Count; i++)
                {
                    if (rs[i].Lo <= rs[i - 1].Hi) errs.Add(kv.Key + ": " + rs[i - 1].Lo + "-" + rs[i - 1].Hi + " と " + rs[i].Lo + "-" + rs[i].Hi);
                }
            }
            return errs;
        }

        /// <summary>鍵が登録表のどれかの範囲に入っているか(a の意味は問わない。label と b だけ)。</summary>
        public static bool IsRegistered(int label, long b)
        {
            foreach (RngRange r in Ranges)
            {
                if (r.Label == label && b >= r.Lo && b <= r.Hi) return true;
            }
            return false;
        }
    }
}
