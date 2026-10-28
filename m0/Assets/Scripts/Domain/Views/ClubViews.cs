using System.Collections.Generic;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;
using Ekiden.Domain.World;

namespace Ekiden.Domain.Views
{
    // ------------------------------------------------------------ 運営(SC-32)
    /// <summary>施設・スタッフの効果 1 つ(03 §4.6)。<see cref="Value"/> は現在のレベルでの効果(割合の変化: +0.12 = +12%、−0.03 = −3%)、<see cref="NextValue"/> は 1 つ上げたとき。
    /// <c>scout_sd</c> だけは割合ではなく見積もりの誤差(σ)、<c>gift_kind</c> は素養の種類が見えるか(1 / 0)。文は文字列テーブルの <c>fx.{Code}</c>(<c>{value}</c> を埋める)。</summary>
    public sealed class FacilityEffectView
    {
        /// <summary>効果のコード(文字列テーブルの <c>fx.{コード}</c>)。</summary>
        public static readonly string[] Codes = { "xp_all", "injury_prob", "injury_dur", "recovery_r", "xp_rec", "xp_spd_kck", "xp_clb_dwn", "xp_dur", "scout_sd", "gift_kind" };

        public string Code = "";
        public double Value, NextValue;
    }

    public sealed class FacilityRowView
    {
        public string Key = "";                     // "dorm" など(FacilityPolicy.Order)
        public string NameKey = "";                 // "fac.{Key}.name"
        public bool IsStaff;                        // スタッフ(コーチ・トレーナー・栄養士・スカウト担当)/ false = 施設
        public int Level, Cap;                      // 現在のレベルと、監督ランクで決まる上限
        public double? NextCost;                    // 次のレベルの費用(万円)。上限なら null
        public bool CanUpgrade;
        public string BlockedBy;                    // 上げられない理由: "cap"(上限)/ "funds"(資金)/ null
        public int RecommendedOrder;                // 推奨の順位(1 = 寮)
        public int RecommendedLevel;                // 『推奨をすべて実行』のあとのレベル(いまと同じなら対象外)
        public List<FacilityEffectView> Effects = new List<FacilityEffectView>();
    }

    public sealed class FundsPointView
    {
        public int Year;
        public double Funds;                        // その年度の大会が終わった時点の資金
    }

    /// <summary>運営(08a SC-32)。</summary>
    public sealed class OpsView
    {
        public readonly List<FacilityRowView> Rows = new List<FacilityRowView>();      // 推奨の順(寮 → スカウト → コーチ → …)
        public int Funds;
        public int Reserve;                         // 推奨の強化が残す予備費(万円)
        public int CoachRank, Cap;
        public int Members;
        public double IncomeFormula, IncomeMod, Income;     // 年度の収入の見込み(いまの名声 + 前年度の評価ポイント + funds_year の補正)
        public double Staff, Upkeep, MembersExpense, Expense;
        public double Balance;                      // 見込みの収支(収入 − 支出)
        public int RecommendedCount;                // 『推奨をすべて実行』で上がるレベルの合計
        public readonly List<FundsPointView> History = new List<FundsPointView>();
    }

    // ------------------------------------------------------------ 全国の大学(SC-35)
    public sealed class WorldSchoolRowView
    {
        public int SchoolId;
        public string NameKey = "";
        public bool IsOwn;
        public int PowerRank;                       // 先発 10 人の力の平均の順位(1 = 最強)
        public double Power;
        public double Prestige;
        public Tier Tier;                           // 校格
        public Region Region;                       // 所在地(地方)
        public int PrevPoints;                      // 前年度の評価ポイント
        public int? LastNewYearPlace;               // 前年度の第三戦の順位(1 始まり。出なければ null)
    }

    /// <summary>大会ごとの出場権に対する自校の位置(04 §3.1)。</summary>
    public sealed class EligibilityView
    {
        public RaceKind Kind;
        public int Cutoff;                          // 第一戦 20 校 / 第二戦 30 校 / 第三戦のシード 10 校
        public int Position;                        // 自校の位置(1 始まり)。第三戦は前年度の順位(シードでなければ 0)
        public bool InRange;                        // 出場権の圏内(第三戦はシード)
        public int Short;                           // 圏外のとき、あと何位(Position − Cutoff)
        public bool NeedsQualifier;                 // 第三戦で、シードでなければ予選会(上位 10 校が通過)
    }

    public sealed class WorldView
    {
        public int Year;
        public readonly List<WorldSchoolRowView> Schools = new List<WorldSchoolRowView>();     // 戦力順位の順
        public readonly EligibilityView[] Eligibility = new EligibilityView[3];
    }

    // ------------------------------------------------------------ OB 名簿(SC-33)
    public sealed class ObRowView
    {
        public GraduateRowView Grad;                // 卒業生の行(名前・出身・最終の能力・特殊能力・成績・進路・殿堂・招聘)
        public int EnrollYear, GradYear;
        public ObRole Role;                         // 役割のある OB(コーチ / スカウト協力 / スポンサー)
        public int RoleWeeksLeft;                   // 役割の残り週数(役割がなければ 0)
    }

    /// <summary>県別 OB ネットワーク(05 §5。Lv = min(5, floor(√人数)))。</summary>
    public sealed class ObNetworkRowView
    {
        public int Pref;                            // 1〜47(0 = 海外)
        public string PrefName = "";
        public int Count, Level;
        public int ToNextLevel;                     // 次のレベルまであと何人(Lv5 なら 0)
    }

    public sealed class ObView
    {
        public int Total;
        public readonly List<ObRowView> Rows = new List<ObRowView>();         // 卒業年度の新しい順、同じなら ID の昇順
        public readonly List<ObRowView> Roles = new List<ObRowView>();        // 役割のある OB
        public readonly List<ObNetworkRowView> Network = new List<ObNetworkRowView>();    // OB のいる県(海外を最後)
    }

    // ------------------------------------------------------------ 歴史・殿堂(SC-34 の自校の部分)
    public sealed class YearRecordView
    {
        public int Year;
        public double P;                            // 第三戦の週の先発 10 人の力の平均
        public int PowerRank;
        public int[] Place = new int[3];            // 第一〜第三戦の順位(出なければ 99)
        public bool[] Won = new bool[3];
        public bool TripleCrown;
        public int EvalPoints;
        public double Prestige;
        public int CoachRank;
    }

    public sealed class PrefCountView
    {
        public int Pref;
        public string PrefName = "";
        public int Count;                           // 在籍 + OB
    }

    public sealed class HistoryView
    {
        public readonly List<YearRecordView> Years = new List<YearRecordView>();          // 年度の古い順
        public readonly int[] FirstWinYear = new int[3];       // 各大会の初優勝の年度(0 = まだ)
        public int Titles;                          // 優勝の通算
        public int TripleCrowns;
        public int? ClearYear;
        public bool NoInvite;
        public readonly List<ObRowView> HallOfFame = new List<ObRowView>();               // 殿堂入り(卒業年度の新しい順)
        public readonly List<PrefCountView> Prefectures = new List<PrefCountView>();      // 出身県ごとの人数(在籍 + OB。県の番号順)
        public int PrefecturesCovered;              // 『47 県の達成 n / 47』の n
    }
}
