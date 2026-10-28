using System;
using System.Collections.Generic;

namespace Ekiden.Domain.World
{
    public enum Region { Hokkaido = 0, Tohoku, Kanto, Chubu, Kinki, Chugoku, Shikoku, Kyushu }

    public enum PopTier { A = 0, B, C, D }

    public sealed class Prefecture
    {
        public int Id { get; }
        public string Name { get; }
        public Region Region { get; }
        public PopTier Pop { get; }
        public bool Elite { get; }

        public Prefecture(int id, string name, Region region, PopTier pop, bool elite)
        {
            Id = id; Name = name; Region = region; Pop = pop; Elite = elite;
        }

        /// <summary>供給重み(候補の出身県の抽選。02 §3.2): { A:4.0, B:2.5, C:1.5, D:1.0 } × (強豪県 1.3)。</summary>
        public double SupplyWeight
        {
            get
            {
                double w = Pop == PopTier.A ? 4.0 : (Pop == PopTier.B ? 2.5 : (Pop == PopTier.C ? 1.5 : 1.0));
                return w * (Elite ? 1.3 : 1.0);
            }
        }

        /// <summary>出身高校の数(02 §3.3): A=8 / B=5 / C=3 / D=2、強豪県は +1。</summary>
        public int HighSchoolCount
        {
            get { return (Pop == PopTier.A ? 8 : (Pop == PopTier.B ? 5 : (Pop == PopTier.C ? 3 : 2))) + (Elite ? 1 : 0); }
        }
    }

    /// <summary>地方・県のマスタと距離帯(02 §3.1・§3.2)。</summary>
    public static class Geo
    {
        /// <summary>留学生の出身国(24。国による能力・外見・成長の補正は設けない。07 §6.3)。名称・並びは名称テーブルで差し替える(L-20)。</summary>
        public static readonly string[] Countries =
        {
            "ケニア", "エチオピア", "タンザニア", "ウガンダ", "モンゴル", "韓国", "中国", "ベトナム", "タイ", "インド", "インドネシア", "トルコ",
            "アメリカ", "カナダ", "ブラジル", "メキシコ", "オーストラリア", "ニュージーランド", "イギリス", "フランス", "ドイツ", "スペイン", "ポーランド", "スウェーデン",
        };

        private static readonly Region[][] Adjacent =
        {
            new[] { Region.Tohoku },                                         // Hokkaido
            new[] { Region.Hokkaido, Region.Kanto, Region.Chubu },           // Tohoku
            new[] { Region.Tohoku, Region.Chubu },                           // Kanto
            new[] { Region.Tohoku, Region.Kanto, Region.Kinki },             // Chubu
            new[] { Region.Chubu, Region.Chugoku, Region.Shikoku },          // Kinki
            new[] { Region.Kinki, Region.Shikoku, Region.Kyushu },           // Chugoku
            new[] { Region.Kinki, Region.Chugoku, Region.Kyushu },           // Shikoku
            new[] { Region.Chugoku, Region.Shikoku },                        // Kyushu
        };

        public const int Overseas = 3;

        /// <summary>距離帯: 同じ地方 0(地元)/ 隣接 1 / それ以外 2(遠方)。</summary>
        public static int Band(Region a, Region b)
        {
            if (a == b) return 0;
            return Array.IndexOf(Adjacent[(int)a], b) >= 0 ? 1 : 2;
        }

        public static readonly IReadOnlyList<Prefecture> Prefectures = new[]
        {
            new Prefecture(1, "北海道", Region.Hokkaido, PopTier.A, false), new Prefecture(2, "青森", Region.Tohoku, PopTier.C, false),
            new Prefecture(3, "岩手", Region.Tohoku, PopTier.C, false), new Prefecture(4, "宮城", Region.Tohoku, PopTier.B, true),
            new Prefecture(5, "秋田", Region.Tohoku, PopTier.D, false), new Prefecture(6, "山形", Region.Tohoku, PopTier.C, false),
            new Prefecture(7, "福島", Region.Tohoku, PopTier.B, true), new Prefecture(8, "茨城", Region.Kanto, PopTier.B, false),
            new Prefecture(9, "栃木", Region.Kanto, PopTier.B, false), new Prefecture(10, "群馬", Region.Kanto, PopTier.B, false),
            new Prefecture(11, "埼玉", Region.Kanto, PopTier.A, true), new Prefecture(12, "千葉", Region.Kanto, PopTier.A, false),
            new Prefecture(13, "東京", Region.Kanto, PopTier.A, false), new Prefecture(14, "神奈川", Region.Kanto, PopTier.A, false),
            new Prefecture(15, "新潟", Region.Chubu, PopTier.B, false), new Prefecture(16, "富山", Region.Chubu, PopTier.C, false),
            new Prefecture(17, "石川", Region.Chubu, PopTier.C, false), new Prefecture(18, "福井", Region.Chubu, PopTier.D, false),
            new Prefecture(19, "山梨", Region.Chubu, PopTier.D, false), new Prefecture(20, "長野", Region.Chubu, PopTier.B, true),
            new Prefecture(21, "岐阜", Region.Chubu, PopTier.B, false), new Prefecture(22, "静岡", Region.Chubu, PopTier.B, false),
            new Prefecture(23, "愛知", Region.Chubu, PopTier.A, true), new Prefecture(24, "三重", Region.Kinki, PopTier.B, false),
            new Prefecture(25, "滋賀", Region.Kinki, PopTier.C, false), new Prefecture(26, "京都", Region.Kinki, PopTier.B, true),
            new Prefecture(27, "大阪", Region.Kinki, PopTier.A, false), new Prefecture(28, "兵庫", Region.Kinki, PopTier.A, true),
            new Prefecture(29, "奈良", Region.Kinki, PopTier.C, false), new Prefecture(30, "和歌山", Region.Kinki, PopTier.D, false),
            new Prefecture(31, "鳥取", Region.Chugoku, PopTier.D, false), new Prefecture(32, "島根", Region.Chugoku, PopTier.D, false),
            new Prefecture(33, "岡山", Region.Chugoku, PopTier.B, true), new Prefecture(34, "広島", Region.Chugoku, PopTier.B, true),
            new Prefecture(35, "山口", Region.Chugoku, PopTier.C, false), new Prefecture(36, "徳島", Region.Shikoku, PopTier.D, false),
            new Prefecture(37, "香川", Region.Shikoku, PopTier.D, false), new Prefecture(38, "愛媛", Region.Shikoku, PopTier.C, false),
            new Prefecture(39, "高知", Region.Shikoku, PopTier.D, false), new Prefecture(40, "福岡", Region.Kyushu, PopTier.A, true),
            new Prefecture(41, "佐賀", Region.Kyushu, PopTier.D, false), new Prefecture(42, "長崎", Region.Kyushu, PopTier.C, false),
            new Prefecture(43, "熊本", Region.Kyushu, PopTier.B, false), new Prefecture(44, "大分", Region.Kyushu, PopTier.C, false),
            new Prefecture(45, "宮崎", Region.Kyushu, PopTier.C, false), new Prefecture(46, "鹿児島", Region.Kyushu, PopTier.B, false),
            new Prefecture(47, "沖縄", Region.Kyushu, PopTier.C, false),
        };

        public static Prefecture ById(int id) { return Prefectures[id - 1]; }

        /// <summary>地方の代表の県(供給重みが最大の県。同じなら ID が小さい方)。世界の学校には所在県がまだないので、自校の所在県(遠方・近郊の判定)に使う(L-52)。</summary>
        public static int HomePrefecture(Region region)
        {
            int best = 0;
            double bw = -1;
            foreach (Prefecture p in Prefectures)
                if (p.Region == region && p.SupplyWeight > bw) { best = p.Id; bw = p.SupplyWeight; }
            return best;
        }

        public const int HighSchoolTotal = 216;

        /// <summary>出身高校の ID(1〜216)。県の順に高校を並べて番号を振る(02 §3.3: A=8 / B=5 / C=3 / D=2 校、強豪県は +1)。県内からの抽選 u(0〜1)は一様(高校ごとの強さ <c>strength</c> での重みつけは高校の表ができてから。L-52)。</summary>
        public static int HighSchoolId(int prefectureId, double u)
        {
            int first = 1;
            for (int i = 0; i < prefectureId - 1; i++) first += Prefectures[i].HighSchoolCount;
            int n = Prefectures[prefectureId - 1].HighSchoolCount;
            return first + Math.Min(n - 1, (int)(u * n));
        }

        /// <summary>供給重みの総和(02 §3.2 の検算: 112.3)。</summary>
        public static double TotalSupplyWeight
        {
            get { double t = 0; foreach (Prefecture p in Prefectures) t += p.SupplyWeight; return t; }
        }
    }
}
