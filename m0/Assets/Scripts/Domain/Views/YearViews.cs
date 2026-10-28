using System.Collections.Generic;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.World;

namespace Ekiden.Domain.Views
{
    /// <summary>年度の結果(SC-12)の、大会 1 つぶん。</summary>
    public sealed class YearRaceView
    {
        public RaceKind Kind;
        public int Place;                           // 順位(出場しなければ 99)
        public bool Won;
        public int EvalPoints;                      // この大会の評価ポイント(10 位まで。出場しなければ 0)
    }

    /// <summary>収支(SC-12 の funds。万円)。精算のあとの内訳は保存しないので、復元した直後は <see cref="YearSummaryView.Finance"/> が null。</summary>
    public sealed class YearFinanceView
    {
        public int FundsBefore;
        public double IncomeFormula, IncomeMod, Income;     // 名声・評価ポイントの式 / 補正 / 計
        public double Staff, Upkeep, MembersExpense, Expense;
        public int Members;
        public int FundsAfterOps;                   // 収支のあと・強化の前
        public int FundsNow;                        // いまの資金(強化のあと)
        public int[] FacBefore = new int[0];        // 施設・スタッフのレベル。順は FacilityOrder
        public int[] FacAfter = new int[0];
    }

    /// <summary>特別招聘券の獲得の節目(07 §4)。名前は文字列テーブル(<see cref="NameKey"/>。継続の記念だけ <c>{years}</c> を <see cref="Years"/> で埋める)。</summary>
    public sealed class TicketMilestoneView
    {
        public string Id = "";                      // 'tm.win.fall' / 'tm.anniv.10' など
        public string NameKey = "";                 // 'tm.win.fall.name' / 'tm.anniv.name'
        public int Years;                           // 継続の記念のとき N(それ以外は 0)

        public static TicketMilestoneView Of(string id)
        {
            const string anniv = "tm.anniv.";
            if (id.StartsWith(anniv) && int.TryParse(id.Substring(anniv.Length), out int n)) return new TicketMilestoneView { Id = id, NameKey = "tm.anniv.name", Years = n };
            return new TicketMilestoneView { Id = id, NameKey = id + ".name" };
        }
    }

    /// <summary>年度の結果と精算(08a SC-12。05 §12 の手順 1〜3 の結果)。</summary>
    public sealed class YearSummaryView
    {
        public int Year;
        public readonly YearRaceView[] Races = new YearRaceView[3];
        public int EvalPoints;                      // 年間の評価ポイント(三冠の加点を含む)
        public bool TripleCrown;
        public int TripleCrownBonus;                // 三冠の加点(評価ポイント。名声ポイントにも同じだけ)
        public double FamePoints;                   // 年間の名声ポイント
        public double PrestigeBefore, PrestigeAfter;
        public int ExpectedTargetRank;              // 期待目標(前年度の第三戦の順位 − 1。最上位は 1)
        public bool TargetMet;
        public int TargetBonusFunds;                // 達成したときの予算ボーナス(万円。達成しなければ 0)
        public int CoachRankBefore, CoachRankAfter;
        public bool RankUp;
        public int FacilityCapBefore, FacilityCapAfter;     // 強化の上限(ランクの上昇で解放される)
        public double CumPoints;                    // 累計評価ポイント
        public int? NextRankPoints;
        public bool TurnoverDone;                   // 卒業・入学まで済んでいる(false = 精算のあと・卒業の前 = 強化の画面)
        public List<TicketMilestoneView> TicketsEarned = new List<TicketMilestoneView>();         // 今年度に入手した券の節目(年度末の入手は卒業・入学のあとに入る)
        public List<TicketMilestoneView> TicketsAtYearEnd = new List<TicketMilestoneView>();      // このあと年度末の処理で入手する節目(精算のあと・卒業の前)
        public YearFinanceView Finance;
        public static readonly string[] FacilityOrder = { "dorm", "scout", "coach", "track", "hill", "gym", "trainer", "nutri", "medical" };
    }

    /// <summary>卒業生の行(SC-13)。</summary>
    public sealed class GraduateRowView
    {
        public int RunnerId;
        public string Name = "", Kana = "";
        public int Pref;                            // 出身県(留学生は 0)
        public string PrefName = "";
        public string CountryName = "";             // 留学生の出身国(OB に国が残っているときだけ。ないセーブは空)
        public string HighSchoolNameKey;            // "hs.{id}.name"。なければ null
        public bool Overseas;
        public double Power;                        // 卒業時の力
        public char Rank;
        public char[] StatRanks = new char[0];      // 10 能力のランク(Stat の順)
        public List<SkillView> Skills = new List<SkillView>();
        public int SectionWins, RelayWins;
        public int Pb5000, Pb10000, PbHalf;
        public CareerPath Career;
        public bool HallOfFame;
        public bool Ticket;
    }

    /// <summary>卒業・進路(08a SC-13)。殿堂入りを先頭に、残りは ID の昇順。</summary>
    public sealed class GraduationView
    {
        public int Year;                            // 卒業した年度
        public readonly List<GraduateRowView> Graduates = new List<GraduateRowView>();
        public readonly List<RunnerRowView> Quit = new List<RunnerRowView>();      // やる気 1 で退部した選手(別枠)
    }

    /// <summary>新入生の行(SC-14)。素質・体質・成長タイプは出さない(I-01)。素養は判明しているものだけ。</summary>
    public sealed class EnrolleeRowView
    {
        public RunnerRowView Row;
        public int Stars;                           // 評価★(候補のときの市場評価)
        public bool Targeted;                       // 声かけ・練習会で狙っていた
        public bool Far, Near;                      // 距離帯(遠方・近郊)
        public int[] Stats = new int[0];            // 10 能力(Stat の順)
        public char[] StatRanks = new char[0];
    }

    /// <summary>狙ったが入れなかった候補(08a SC-14『惜しかった候補』)。<see cref="Reason"/> は文字列表のキー(<c>miss.quota</c> / <c>miss.scholarship</c> / <c>miss.outbid</c>)。</summary>
    public sealed class MissedCandidateView
    {
        public ScoutCandidateView Candidate;        // 名前・出身・評価★・市場評価・上位級か(Stage は声かけまで済ませたものとして Invited)
        public MissReason Reason;
        public string ReasonKey = "";
        public int WinnerSchoolId = -1;             // 入った学校(どこにも入らなければ −1)
        public string WinnerNameKey = "";
        public double Margin;                       // 競り負けた差(提示スコア。小さいほど惜しい。ほかの理由は 0)
    }

    /// <summary>入学者発表(08a SC-14)。狙った選手を先に、続けて自然に入った選手(それぞれ入部の順)。</summary>
    public sealed class EnrollmentView
    {
        public int Year;                            // 入学の年度(終えた年度 + 1)
        public readonly List<EnrolleeRowView> Targeted = new List<EnrolleeRowView>();
        public readonly List<EnrolleeRowView> Natural = new List<EnrolleeRowView>();
        public readonly List<MissedCandidateView> Missed = new List<MissedCandidateView>();      // 惜しかった候補(競り負けを差の小さい順、続けて奨学金枠・定員の順。それぞれ評価の高い順)
    }
}
