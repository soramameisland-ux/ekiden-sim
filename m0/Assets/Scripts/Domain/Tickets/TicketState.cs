using System;
using System.Collections.Generic;

namespace Ekiden.Domain.Tickets
{
    /// <summary>07 の定数表(§2。<c>K_TICKET_</c> / <c>K_PLAN_</c> 接頭辞)。初期値案。</summary>
    public static class TicketK
    {
        public const int K_TICKET_STOCK_MAX = 2;            // 手元の券の上限。超える入手は繰り延べる
        public const int K_TICKET_PER_YEAR = 1;             // 1年度に宣言できる回数
        public const int K_TICKET_ACTIVE_MAX = 2;           // 在籍する招聘選手と宣言済みの招聘の合計の上限
        public const int K_TICKET_DECLARE_FROM = 2;         // 宣言できる期間(month_index。6月)
        public const int K_TICKET_DECLARE_TO = 10;          // 同(2月)
        public const int K_PLAN_CYCLE_YEARS = 4;            // 黄金世代プランの付与の周期(年度)
        public const int K_PLAN_COUNT = 2;                  // 1回の付与の枚数
        public const int K_OFFLINE_GRACE_DAYS = 30;         // 購入状態を確認できないときに、前回の確認を信じる日数
        public const int K_SPECIAL_MAX_STAT = 80;           // 招聘選手の入学時の能力の上限(各能力)
        public const int K_SPECIAL_MAX_MEAN = 68;           // 同(平均)
        public const double K_SPECIAL_SKILL_P = 0.30;       // 入学時に特殊能力を 1 つ持つ確率
    }

    public enum TicketKind { Intl = 0, Rookie = 1 }

    /// <summary>宣言済みで、まだ入学していない招聘。</summary>
    public sealed class PendingInvite
    {
        public TicketKind Kind;
        public int Pref;                // スーパールーキーの出身県の指定(0 = おまかせ)
        public int EnrollYear;
    }

    public struct TicketLogEntry
    {
        public int Year;
        public int Delta;
        public string Source;           // "Earned:tm.win.fall" / "PlanGrant" / "Declare:Rookie"
    }

    /// <summary>特別招聘券の状態(07 §3・§5。<c>SaveData.player_team.tickets</c>)。券は失効しない: 入手した分は手元(stock)か繰り延べ(bank)に残る。</summary>
    public sealed class TicketState
    {
        public int Stock;                                       // 0〜K_TICKET_STOCK_MAX
        public int Bank;                                        // 繰り延べた券。stock < 上限なら 0
        public int LastUsedYear = -99;
        public PendingInvite Pending;
        public int? PlanAnchorYear;
        public readonly SortedSet<int> PlanGrantedYears = new SortedSet<int>();
        public readonly SortedSet<string> Milestones = new SortedSet<string>(StringComparer.Ordinal);
        public readonly List<TicketLogEntry> Log = new List<TicketLogEntry>();
        public int InvitesDeclared;                             // run.invites_declared(『無招聘』の実績の判定。02 §3.10)

        /// <summary>入手は常に繰り延べに入れてから、手元に空きがあるだけ移す(§3.2)。</summary>
        public void Add(int n, string source, int year)
        {
            Bank += n;
            Log.Add(new TicketLogEntry { Year = year, Delta = n, Source = source });
            Release();
        }

        /// <summary>繰り延べを手元へ(空きがあるだけ)。</summary>
        public void Release()
        {
            int move = Math.Min(Bank, TicketK.K_TICKET_STOCK_MAX - Stock);
            Stock += move;
            Bank -= move;
        }

        /// <summary>節目を初めて満たしたら 1 枚入手する。すでに獲得済みなら false。</summary>
        public bool Earn(string milestone, int year)
        {
            if (!Milestones.Add(milestone)) return false;
            Add(1, "Earned:" + milestone, year);
            return true;
        }

        /// <summary>黄金世代プランの付与(§5.2)。owned: true = 購入済み / false = 返金・未購入 / null = 未確認。付与した枚数を返す。同じ年度に何度呼んでも付与は 1 回。</summary>
        public int TryPlanGrant(int year, bool? owned)
        {
            if (owned != true) return 0;
            if (!PlanAnchorYear.HasValue) PlanAnchorYear = year;        // 初めて確認した年度。ここで最初の付与
            if (Mod(year - PlanAnchorYear.Value, TicketK.K_PLAN_CYCLE_YEARS) == 0 && !PlanGrantedYears.Contains(year))
            {
                PlanGrantedYears.Add(year);
                Add(TicketK.K_PLAN_COUNT, "PlanGrant", year);
                return TicketK.K_PLAN_COUNT;
            }
            return 0;
        }

        private static int Mod(int a, int b)
        {
            int m = a % b;
            return m < 0 ? m + b : m;
        }

        /// <summary>宣言できるか(§3.3)。active = 在籍する招聘選手の数(pending は含まない。ここで数える)。</summary>
        public bool CanDeclare(int year, int month, int active)
        {
            return Stock > 0 && Pending == null && LastUsedYear != year && active + 1 <= TicketK.K_TICKET_ACTIVE_MAX
                   && TicketK.K_TICKET_DECLARE_FROM <= month && month <= TicketK.K_TICKET_DECLARE_TO;
        }

        /// <summary>宣言する(取り消せない)。入学は翌年度(§3.3)。呼ぶ側が <see cref="CanDeclare"/> を確かめる。</summary>
        public void Declare(int year, TicketKind kind, int pref = 0)
        {
            Stock -= 1;
            LastUsedYear = year;
            Pending = new PendingInvite { Kind = kind, Pref = pref, EnrollYear = year + 1 };
            InvitesDeclared += 1;
            Log.Add(new TicketLogEntry { Year = year, Delta = -1, Source = "Declare:" + kind });
            Release();
        }

        /// <summary>入学の確定のあと(年度末)に呼ぶ。</summary>
        public void ConfirmEnrollment()
        {
            Pending = null;
        }

        /// <summary>不変条件(§3.1)の違反。active = 在籍する招聘選手の数。totalReceived / totalUsed は入手・使用の合計(ログは刈り込まれるので、呼ぶ側が数える)。</summary>
        public List<string> CheckInvariants(int active, int totalReceived, int totalUsed)
        {
            var errs = new List<string>();
            if (Stock < 0 || Stock > TicketK.K_TICKET_STOCK_MAX) errs.Add("0 ≤ stock ≤ 2");
            if (Stock < TicketK.K_TICKET_STOCK_MAX && Bank != 0) errs.Add("stock < 2 ならば bank = 0");
            if (Bank < 0) errs.Add("bank ≥ 0");
            if (active + (Pending != null ? 1 : 0) > TicketK.K_TICKET_ACTIVE_MAX) errs.Add("active ≤ 2");
            if (totalReceived != totalUsed + Stock + Bank) errs.Add("入手の合計 = 使用の合計 + stock + bank");
            return errs;
        }
    }

    /// <summary>ゲーム内の獲得の節目(07 §4)。大会の結果と年度末の状態から、その時点で満たした節目を券にする。</summary>
    public static class TicketMilestones
    {
        public const string WinFall = "tm.win.fall", WinNational = "tm.win.national", WinNewYear = "tm.win.newyear", Podium = "tm.podium", Seed = "tm.seed",
            Sweep = "tm.sweep", Clear = "tm.clear", Rank3 = "tm.rank.3", Rank6 = "tm.rank.6", Rank9 = "tm.rank.9";

        public static string Anniversary(int years) { return "tm.anniv." + years; }

        private static readonly string[] WinIds = { WinFall, WinNational, WinNewYear };

        /// <summary>大会の終了直後。kind = 1〜3、rank = 自校の順位、yearWins = 今年度に優勝した大会の集合(ビット k − 1。この大会を含めて呼ぶ側が更新したもの)。入手した券の枚数を返す。</summary>
        public static int EarnAfterRace(TicketState t, int year, int kind, int rank, int yearWins)
        {
            int n = 0;
            if (rank == 1 && t.Earn(WinIds[kind - 1], year)) n++;
            if (rank <= 3 && t.Earn(Podium, year)) n++;
            if (kind == 3 && rank <= 10 && t.Earn(Seed, year)) n++;
            if (kind == 3 && yearWins == 7 && t.Earn(Sweep, year)) n++;
            if (t.Milestones.Contains(WinFall) && t.Milestones.Contains(WinNational) && t.Milestones.Contains(WinNewYear) && t.Earn(Clear, year)) n++;
            return n;
        }

        /// <summary>年度末の精算後(05 §12 の手順3)。managerRank = 監督ランク、yearsCompleted = 終えた年度数(1 始まり)。使えるのは次の年度から(宣言期間のあとなので)。</summary>
        public static int EarnAtYearEnd(TicketState t, int year, int managerRank, int yearsCompleted)
        {
            int n = 0;
            if (managerRank >= 3 && t.Earn(Rank3, year)) n++;
            if (managerRank >= 6 && t.Earn(Rank6, year)) n++;
            if (managerRank >= 9 && t.Earn(Rank9, year)) n++;
            if (yearsCompleted > 0 && yearsCompleted % 10 == 0 && t.Earn(Anniversary(yearsCompleted), year)) n++;
            return n;
        }
    }
}
