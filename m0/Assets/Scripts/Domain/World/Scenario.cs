using System.Collections.Generic;
using Ekiden.Domain.Training;

namespace Ekiden.Domain.World
{
    /// <summary>実績の分類に使う本体の 4 つの難易度(05 §5・07 §10.1)。</summary>
    public enum Difficulty { Elite, Mid, Low, Club }

    /// <summary>開始状態(07 §10.1・02 §3.11)。開始状態だけを定める。世界の規則・他校・乱数は変えない。</summary>
    public sealed class ScenarioDef
    {
        public string Id, Name, Description;
        public int StrengthRank;                // 開始校を選ぶ戦力順位(先発 10 人の力の平均の順位。1〜36)
        public int? Prestige;                   // 名声の上書き(0〜1000)。null = その学校のまま
        public double? FameHist;                // 名声ポイントの履歴の上書き(0〜40)
        public FacilityLevels Facilities;       // 施設・スタッフのレベル(各 1〜5)
        public int Funds;                       // 資金(万円。0〜5000)
        public int? Members;                    // 部員数(5〜40)。null = その学校の名簿のまま。多いときは無作為に減らす
        public int? ObLevel;                    // 各地方の OB の人数の初期値(0〜25)
        public Difficulty? Tag;                 // 本体の 4 つだけ。シナリオパックは null
    }

    /// <summary>本体の難易度とシナリオ例(07 §10)。</summary>
    public static class Scenarios
    {
        private static FacilityLevels All(int n)
        {
            return new FacilityLevels { Coach = n, Trainer = n, Nutri = n, Dorm = n, Track = n, Hill = n, Gym = n, Medical = n, Scout = n };
        }

        public static ScenarioDef Elite()
        {
            return new ScenarioDef
            {
                Id = "sc.elite", Name = "強豪", StrengthRank = 3, Funds = 1500, Tag = Difficulty.Elite,
                Facilities = new FacilityLevels { Coach = 4, Trainer = 3, Nutri = 3, Dorm = 4, Track = 4, Hill = 3, Gym = 4, Medical = 3, Scout = 3 },
            };
        }

        public static ScenarioDef Mid() { return new ScenarioDef { Id = "sc.mid", Name = "中堅", StrengthRank = 7, Funds = 1500, Facilities = All(2), Tag = Difficulty.Mid }; }

        public static ScenarioDef Low() { return new ScenarioDef { Id = "sc.low", Name = "弱小", StrengthRank = 20, Funds = 1500, Facilities = All(1), Tag = Difficulty.Low }; }

        /// <summary>同好会: 戦力順位 33 位、施設なし、部員 5 人(07 §10.3。L-09)。</summary>
        public static ScenarioDef Club() { return new ScenarioDef { Id = "sc.club", Name = "同好会", StrengthRank = 33, Funds = 1500, Facilities = All(1), Members = 5, Tag = Difficulty.Club }; }

        /// <summary>古豪復活: 戦力は弱小と同じだが、名声は強豪並み(700)、OB は各地方に多い(Lv3 = 9 人)、施設は中堅(07 §10.2)。</summary>
        public static ScenarioDef Koki()
        {
            return new ScenarioDef { Id = "sc.koki", Name = "古豪復活", StrengthRank = 20, Prestige = 700, FameHist = 16.0, Facilities = All(2), Funds = 1500, ObLevel = 9 };
        }

        public static ScenarioDef For(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Elite: return Elite();
                case Difficulty.Mid: return Mid();
                case Difficulty.Low: return Low();
                default: return Club();
            }
        }

        public static List<ScenarioDef> BuiltIn() { return new List<ScenarioDef> { Elite(), Mid(), Low(), Club() }; }

        private static bool Lv(int v) { return v >= 1 && v <= 5; }       // 開始時のレベル(各 1〜5。Lv6〜8 はランクで解放。L-70)

        /// <summary>検証(07 §10.1 の範囲)。違反の文言のリスト(空なら適合)。</summary>
        public static List<string> Validate(IEnumerable<ScenarioDef> defs)
        {
            var errs = new List<string>();
            var ids = new HashSet<string>();
            foreach (ScenarioDef s in defs)
            {
                if (string.IsNullOrEmpty(s.Id) || !ids.Add(s.Id)) errs.Add("SC-1 " + s.Id + ": id が空か重複");
                if (s.StrengthRank < 1 || s.StrengthRank > 36) errs.Add("SC-2 " + s.Id + ": strength_rank は 1〜36");
                if (s.Prestige.HasValue && (s.Prestige.Value < 0 || s.Prestige.Value > 1000)) errs.Add("SC-3 " + s.Id + ": prestige は 0〜1000");
                if (s.FameHist.HasValue && (s.FameHist.Value < 0 || s.FameHist.Value > 40)) errs.Add("SC-3 " + s.Id + ": fame_hist は 0〜40");
                FacilityLevels f = s.Facilities;
                if (!(Lv(f.Coach) && Lv(f.Trainer) && Lv(f.Nutri) && Lv(f.Dorm) && Lv(f.Track) && Lv(f.Hill) && Lv(f.Gym) && Lv(f.Medical) && Lv(f.Scout))) errs.Add("SC-4 " + s.Id + ": 施設・スタッフは各 1〜5");
                if (s.Funds < 0 || s.Funds > 5000) errs.Add("SC-5 " + s.Id + ": funds は 0〜5000");
                if (s.Members.HasValue && (s.Members.Value < 5 || s.Members.Value > 40)) errs.Add("SC-6 " + s.Id + ": members は 5〜40");
                if (s.ObLevel.HasValue && (s.ObLevel.Value < 0 || s.ObLevel.Value > 25)) errs.Add("SC-7 " + s.Id + ": ob_level は 0〜25");
            }
            return errs;
        }
    }
}
