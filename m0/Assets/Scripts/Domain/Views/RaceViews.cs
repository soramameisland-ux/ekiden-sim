using System.Collections.Generic;
using Ekiden.Domain.Race;

namespace Ekiden.Domain.Views
{
    /// <summary>登録・配置の検証の指摘(04 §4.1 E1〜E4・§5.2 O1〜O3)。本文は文字列テーブルの <c>problem.{Code}</c>(<see cref="Value"/> は本文の差し込み <c>{max}</c> など)。</summary>
    public sealed class ViewProblem
    {
        public string Code;                          // "entry.short" / "entry.intl" / "entry.injured" / "order.count" / "order.dup" / "order.intl" / "order.injured" / "order.not_entered"
        public int RunnerId;                         // 該当の選手(なければ 0)
        public int Value;                            // 上限など(なければ 0)

        public static readonly string[] Codes = { "entry.short", "entry.intl", "entry.injured", "order.count", "order.dup", "order.intl", "order.injured", "order.not_entered" };
    }

    /// <summary>エントリー画面(SC-21)の 1 行。</summary>
    public sealed class EntryRowView
    {
        public RunnerRowView Row;
        public SectionType BestType;                 // 区間適性の最も高いタイプ
        public double BestFit;
        public char BestMark;                        // ◎〇△
        public bool Entered;
        public bool CanEnter;                        // 登録できる(故障していない)
        public string BlockedReason;                 // できない理由(キー: "injured")。できるなら null
    }

    /// <summary>エントリー(SC-21)。登録 11 / 16・出走に必要 10・留学生の枠・締切。</summary>
    public sealed class EntryView
    {
        public RaceKind Kind;
        public int Registered;                       // 登録済みの人数
        public int Capacity;                         // 登録の上限(第一戦 10 / 第二戦 14 / 第三戦 16)
        public int Required;                         // 出走に必要な人数(6 / 8 / 10)。足りないと棄権
        public int IntlRegistered, IntlRegisterMax, IntlRunMax;
        public int DeadlineWeeksLeft;                // 締切まであと何週(0 = 今週。過ぎていれば −1)
        public bool Open;                            // いま登録を直せる
        public bool Qualified;                       // 自校に出場権がある
        public List<EntryRowView> Roster = new List<EntryRowView>();   // 在籍の全員(ID の昇順)
        public List<int> Entered = new List<int>();                    // 登録メンバーの ID(力の高い順)
        public List<ViewProblem> Problems = new List<ViewProblem>();   // 検証(E1〜E4)の指摘
    }

    /// <summary>区間オーダー(SC-22)の区間のカード。</summary>
    public sealed class OrderSectionView
    {
        public int Section;
        public double Distance;
        public SectionType Type;
        public bool Mountain;                        // 山登り / 山下り(山の区間)
        public int? RunnerId;                        // 配置された選手(未配置は null)
        public RunnerRowView Runner;
        public double Fit;                           // その選手のこの区間の適性値(特殊能力の加算を含む)
        public char Mark;
        public double ExpectedSec;                   // 予想タイム(基準タイム = 調子 0・環境なし。04 §7)
    }

    /// <summary>区間への配置の候補(その区間の適性値の高い順)。</summary>
    public sealed class OrderCandidateView
    {
        public RunnerRowView Row;
        public double Fit;
        public char Mark;
        public double ExpectedSec;
        public int? AssignedSection;                 // いま別の区間に配置されている(その区間の番号)
    }

    /// <summary>区間オーダー(SC-22)。</summary>
    public sealed class OrderView
    {
        public RaceKind Kind;
        public bool Submitted;                       // 提出済み(false なら、おまかせ = 最適な配置)
        public bool Open;                            // いま出せる/直せる
        public List<OrderSectionView> Sections = new List<OrderSectionView>();
        public double TotalExpectedSec;              // 総合の予想タイム(基準タイムの合計)
        public List<RunnerRowView> Bench = new List<RunnerRowView>();   // 補欠(登録のうち出走しない)
        public List<Env> Forecast = new List<Env>();                    // 環境の予報(SC-23 と同じ)
        public List<ViewProblem> Problems = new List<ViewProblem>();    // 登録・配置の検証の指摘(O1〜O3)
    }
}

namespace Ekiden.Domain.Views
{
    /// <summary>大会の結果(SC-26)の総合の 1 行。</summary>
    public sealed class ResultRowView
    {
        public int Rank;
        public int SchoolId;
        public string SchoolNameKey = "";            // "school.{id}.name"
        public int TotalDs;                          // 総合タイム(ds。Fmt.Total で整形)
        public int GapDs;                            // 先頭との差(ds。Fmt.Diff)
        public bool Kuriage;                         // 繰り上げスタートがあった
        public bool IsOwn;
    }

    /// <summary>区間賞(区間タイムが最小)。</summary>
    public sealed class SectionPrizeView
    {
        public int Section;
        public int SchoolId;
        public string SchoolNameKey = "";
        public int Ds;
        public bool IsOwn;
        public int RunnerId;                         // 自校の選手が取ったときだけ(他校は 0)
    }

    /// <summary>自校の区間ごとの結果。</summary>
    public sealed class OwnLegView
    {
        public int Section;
        public int RunnerId;
        public string FamilyName = "", GivenName = "";
        public int Ds;                               // 区間タイム
        public int SectionRank;                      // 区間順位
        public int CumDs;                            // 累積(繰り上げ後の公式タイム)
        public int CumRank;                          // 累積順位
        public Instruction Instruction;
        public bool Prize;                           // 区間賞
        public bool Brake, Stall, Geki, Kuriage;     // 出来事(ブレーキ・失速・檄を使った・繰り上げスタート)
    }

    /// <summary>大会の結果(SC-26)。総合・区間賞・自校の区間ごと・評価ポイント。記録の更新と大会の見どころは未実装(08b・L-60)。</summary>
    public sealed class RaceResultView
    {
        public RaceKind Kind;
        public int RaceId;
        public int Sections;
        public bool OwnEntered;                      // 自校が出場した
        public int OwnRank;                          // 自校の順位(出場しなければ 0)
        public int ExpectedRank;                     // 戦力から見た予想順位(やる気の変化の目安。04 §10)
        public int EvalPoints;                       // この大会で得た評価ポイント(10 位以内)
        public int WinnerSchoolId;
        public List<ResultRowView> Overall = new List<ResultRowView>();
        public List<SectionPrizeView> Prizes = new List<SectionPrizeView>();
        public List<OwnLegView> OwnLegs = new List<OwnLegView>();
    }
}
