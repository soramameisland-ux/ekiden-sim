using Ekiden.Domain.Race;

namespace Ekiden.Domain.Views
{
    /// <summary>次の節目(大会)。</summary>
    public sealed class NextRaceView
    {
        public RaceKind Kind;
        public int WeeksUntil;                      // 0 = 今週
        public bool? CanEnter;                      // 今年度の大会で、自校に出場権があるか(来年度の大会はまだ決まっていないので null)
        public int EntryDeadlineWeeksLeft;          // エントリー締切まであと何週(0 = 今週が締切。締切を過ぎていれば −1)
        public bool EntryOpen;                      // いまエントリーを出せる/直せる(締切の週から大会の週の初めまで)
        public bool OrderOpen;                      // いまオーダーを出せる(同上。登録が済んでいること)
    }

    /// <summary>ホーム(SC-08)の status と next。</summary>
    public sealed class HomeView
    {
        // status
        public int SchoolId;
        public string SchoolNameKey;                // "school.{id}.name"
        public string ManagerName = "";
        public int Year;                            // 第 N 年度
        public int YearWeek;                        // 0〜47
        public int CalendarMonth;                   // 1〜12(年度は 4 月始まり)
        public int WeekOfMonth;                     // 1〜4
        public int CoachRank;                       // 監督ランク 1〜10
        public double CumPoints;                    // 累計評価ポイント
        public int? NextRankPoints;                 // 次のランクに必要な累計評価ポイント(最高ランクなら null)
        public double Prestige;
        public int Funds;                           // 万円
        public double Morale;
        public int PowerRank;                       // 戦力順位(36 校中。1 = 最強)
        public bool Cleared;
        public int? ClearYear;
        public bool[] WonEver = new bool[3];        // 第一〜第三戦の優勝経験(クリアの条件)
        // next
        public NextRaceView Next;
        public int? QualifierWeeksUntil;            // 予選会(シード外の年度のみ。今年度の予選会がまだなら)
        public int ExpectedTargetRank;              // 期待目標: 第三戦でこの順位以内
        public int ExpectedBonusFunds;              // その達成の予算ボーナス(万円)
        public bool Declarable;                     // 特別招聘『宣言できます』
        public int TicketStock;
        public bool TicketPending;                  // 宣言済み(年度末に入学)
        public int GuidanceLeft;                    // 今月に残る指導ポイント
        public bool YearSettled;                    // 年度末の精算まで済み(強化の画面)
    }
}
