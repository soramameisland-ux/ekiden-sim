using System.Collections.Generic;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Screens;
using Ekiden.Domain.World;

namespace Ekiden.Domain.Views
{
    /// <summary>スカウトの時期(08 R-08)。</summary>
    public enum ScoutPhase { Closed, Observe, Recruit, Confirm }       // 4・5 月 / 視察 6〜11 月 / 声かけ・練習会 12〜2 月 / 3 月(確定)

    /// <summary>候補 1 人(SC-28 の行・SC-29 の詳細)。見える段階に達していない項目は空のまま(隠す情報は型に入れない)。</summary>
    public sealed class ScoutCandidateView
    {
        public int Index;                           // 候補の番号(行動の引数)
        public string FamilyName = "", GivenName = "", FamilyKana = "", GivenKana = "";
        public int Stars;                           // 評価★(V から。05 §6.1)
        public double V;                            // 市場評価(最初から見える)
        public bool TopTier;                        // 上位級(奨学金枠が要る)
        public DisplayRules.RivalryLevel Rivalry;
        public double RivalryWin;                   // 練習会まで進めて枠があれば競り勝つ割合の実測
        public int Pref;                            // 1〜47(留学生は 0)
        public string PrefName = "";
        public Region Region;
        public int Band;                            // 距離帯: 0 地元 / 1 隣接 / 2 遠方 / 3 海外
        public string HighSchoolNameKey;            // "hs.{id}.name"。留学生は null
        public bool Overseas;
        public string CountryName = "";
        public int Pb5000;                          // 5000m 持ちタイム(入学時の能力から。R-03。ds)
        public List<string> Traits = new List<string>();      // 性格タグ(R-16)
        public ScoutStage Stage;
        public bool CanObserve, CanTalk, CanInvite;           // いま行動できるか(時期・段階・ポイント)
        public bool StyleVisible, GiftPresenceVisible, GiftKindVisible, GrowthTypeVisible;
        public double? QObs;                        // 視察後: 卒業時の見込み(QObs ± Sigma)
        public double Sigma;
        public List<string> StyleChips = new List<string>();  // 視察後: 個性チップ
        public GrowthKind? Growth;                  // 練習会後
        public bool? HasGift;                       // 練習会後: 素養があるか
        public List<Gift> GiftKinds = new List<Gift>();       // 練習会後 + スカウト担当 Lv3 以上
    }

    public sealed class ScoutRegionView
    {
        public Region Region;
        public int Candidates, Observed, Targets, Stars5;
    }

    public sealed class ScoutPrefView
    {
        public int Pref;
        public string PrefName = "";
        public Region Region;
        public int Band;
        public int Candidates, Observed, Targets, Stars5;
        public int ObCount, ObLevel;                // 県別 OB ネットワーク(Lv = min(5, floor(√人数)))
    }

    /// <summary>スカウト(地図)(08a SC-27)。</summary>
    public sealed class ScoutMapView
    {
        public int MonthIndex;
        public ScoutPhase Phase;
        public int PointsAvailable;
        public int PointsYearTotal;                 // 今年度に付与される総数(36 + 4 × floor(ランク / 3))
        public int Observed, Talked, Invited;       // 行動した人数(視察済み以上 / 声かけ済み以上 / 練習会済み)
        public readonly List<ScoutRegionView> Regions = new List<ScoutRegionView>();     // 8 地方
        public readonly List<ScoutPrefView> Prefs = new List<ScoutPrefView>();           // 47 県
        public int OverseasCandidates, OverseasStars5;
    }

    public sealed class ScoutTargetRowView
    {
        public ScoutCandidateView Candidate;
        public int ConfirmOrder;                    // 確定の順番(目標の中で V の高い順に何番目か。1 始まり)
        public bool Unlikely;                       // 9 番目以降は『ほぼ入れません』
    }

    /// <summary>枠のメーター(08 R-08)。</summary>
    public sealed class ScoutSlotMeterView
    {
        public int Capacity;                        // 定員(10)
        public int TicketReserved;                  // 特別招聘の予約(あれば 1)
        public int Usable;                          // 使える枠
        public int NaturalIntake;                   // 自然に入る目安(3)
        public int Recommended;                     // 推奨の目標(8)
        public int TargetCount;
        public int TopTierTargets, ScholarshipSlots;
        public bool OverRecommended;                // 目標が推奨を超えた(黄色)
        public bool OverCapacity;                   // 目標が (使える枠 − 自然に入る目安) を超えた(注意)
    }

    /// <summary>目標とポイント(08a SC-30)。目標 = 声かけ済み以上の候補。</summary>
    public sealed class ScoutTargetsView
    {
        public int PointsAvailable, Observed, Talked, Invited;
        public readonly List<ScoutTargetRowView> Targets = new List<ScoutTargetRowView>();    // 確定の順番
        public ScoutSlotMeterView Meter = new ScoutSlotMeterView();
    }
}
