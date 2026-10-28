using System.Collections.Generic;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Race;

namespace Ekiden.Domain.Views
{
    /// <summary>月末レポートの注目の項目 1 件(R-10)。<c>Kind</c> = injury_severe / injury_moderate / awaken / rank_up / big_growth / return / notice / form_up(gift_reveal は素養の判明が未実装 = L-64)。</summary>
    public sealed class ReportItemView
    {
        public string Kind;
        public int RunnerId;                        // 選手に関する項目(0 = なし)
        public string RunnerName = "";
        public string EventId;                      // awaken・notice の元のイベント('ev.…'。本文は文字列テーブル)
        public string SkillId;                      // awaken: 覚醒した特殊能力('skill.…')
        public SkillRank? SkillRank;                // awaken: 金・青・赤の枠(I-53)
        public Stat? Stat;                          // rank_up: 上がった能力
        public char Letter;                         // rank_up: 新しいランク文字
        public int Value;                           // big_growth: 能力の増加の合計 / form_up: 調子の差
    }

    /// <summary>月に開かれた大会のまとめ(自校が出たときは順位つき)。</summary>
    public sealed class MonthRaceView
    {
        public RaceKind Kind;
        public int WinnerSchoolId;
        public string WinnerNameKey = "";
        public int OwnRank;                         // 0 = 出なかった
        public bool OwnWon;
    }

    /// <summary>次の月への注意(R-10 ③)。</summary>
    public sealed class MonthCautionView
    {
        public List<int> HighFatigue = new List<int>();         // 疲労が警告の値(70)以上の選手
        public List<int> Injured = new List<int>();             // 故障中の選手(来月に残る)
    }

    /// <summary>月末レポート(08a SC-11)。</summary>
    public sealed class MonthReportView
    {
        public int MonthIndex;                      // 0 = 4 月 … 11 = 3 月
        public int CalendarMonth;                   // 1〜12
        public double AvgFatigue, AvgForm, Morale;
        public double AvgFatigueChange, AvgFormChange, MoraleChange;       // 月の初めからの変化(月の途中から復元したときは 0)
        public bool ChangeKnown;
        public string[] ThemeIds = new string[3];   // 今月のテーマ(グループ A / B / C)
        public List<ReportItemView> Items = new List<ReportItemView>();   // 最大 7 件(優先度の順)
        public int More;                            // 『ほか n 件』
        public List<string> Notices = new List<string>();                  // 止まらずにここへ回ったお知らせ(イベントの ID)
        public List<MonthRaceView> Races = new List<MonthRaceView>();
        public MonthCautionView Caution = new MonthCautionView();
        public NextRaceView Next;                   // 来月の予定(次の大会)
    }
}
