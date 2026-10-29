using System.Collections.Generic;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;
using Ekiden.Domain.World;

namespace Ekiden.Domain.Views
{
    // ------------------------------------------------------------ 設定(SC-04。ゲームのタブ)
    /// <summary>設定の現在値(08 R-13)。端末の設定(文字の倍率・音量など = <c>DeviceSettings</c>)は端末側で、ここには入れない。変えるのは <c>GameSession.Set*</c>。</summary>
    public sealed class SettingsView
    {
        public string SpectateMode = "Full";        // Full / Digest / ResultOnly
        public string[] SpectateModes;              // 選べる値(この順に並べる)
        public int AutoSafetyF;                     // 75 / 85 / 0(無効)
        public int[] AutoSafetyChoices;
        public bool EventAuto, PlanAuto, ScoutAuto;
        public int UniformPattern, CheerSong, BgmSet;       // 自校の見た目・応援歌・BGM(所持している範囲は App の層)
    }

    // ------------------------------------------------------------ 年間予定(SC-09)
    public enum CalendarItemKind { Meet, EntryDeadline, QualifierDeadline, Qualifier, Race }

    /// <summary>年間予定の 1 行事(08a SC-09)。</summary>
    public sealed class CalendarItemView
    {
        public CalendarItemKind Kind;
        public RaceKind? Race;                      // EntryDeadline / Race(第一〜第三戦)。ほかは null
        public int YearWeek;                        // 年度内の週(0〜47)
        public int WeekOfMonth;                     // 1〜4(『10 月 2 週』の 2)
        public bool Past;                           // 今週より前
        public bool Current;                        // 今週
        public bool? Participates;                  // 大会・締切: 自校に出場権があるか。該当しない行事は null(I-60)
        public string NoEntryCode;                  // 出場権がないときの理由: "rank"(順位が足りない)/ "qualifier"(第三戦のシード外 = 予選会へ)。あれば null
        public PeakingMode Peaking;                 // Race: 年間方針のピーキング(調整)
        public int PeakingFromWeek;                 // Race: 調整期間の最初の週(大会の 3 週前から大会の週まで = 4 週)
    }

    public sealed class CalendarMonthView
    {
        public int MonthIndex;                      // 0 = 4 月 … 11 = 3 月
        public int CalendarMonth;                   // 1〜12
        public bool IsCurrent, IsPast;
        public bool Camp;                           // 夏合宿(8 月)
        public bool ScoutSurvey, ScoutPursue;       // スカウトの視察(6〜11 月)/ 勧誘(12〜2 月)
        public bool TicketWindow;                   // 特別招聘の宣言期間(6〜2 月)
        public bool YearEnd;                        // 年度末(卒業・精算・入学)= 3 月
        public string[] Themes;                     // 月間計画のテーマ(A / B / C)。過ぎた月は null
        public bool ThemesAreRecommended;           // 今月より先の月は、年間方針の推奨プラン(まだ決めていない)
        public readonly List<CalendarItemView> Items = new List<CalendarItemView>();       // 週の順
    }

    public sealed class CalendarView
    {
        public int Year, YearWeek, CurrentMonthIndex;
        public readonly List<CalendarMonthView> Months = new List<CalendarMonthView>();   // 4 月〜3 月の 12 件
    }

    // ------------------------------------------------------------ クリア(SC-15)
    public sealed class ClearView
    {
        public bool Cleared;
        public int ClearYear;                       // クリアした年度(未クリアは 0)
        public bool NoInvite;                       // 『無招聘』(特別招聘を使わずにクリア)
        public string ScenarioId = "";              // 開始校(シナリオ)の ID('sc.mid' など)
        public string Difficulty;                   // Elite / Mid / Low / Club。シナリオパックなら null
        public readonly int[] FirstWinYear = new int[3];       // 各大会の初優勝の年度(0 = まだ)
        public int Titles;                          // 優勝の通算
        public int TripleCrowns;
    }
}
