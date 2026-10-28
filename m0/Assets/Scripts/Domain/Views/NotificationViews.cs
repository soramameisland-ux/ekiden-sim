using System.Collections.Generic;
using Ekiden.Domain.Team;

namespace Ekiden.Domain.Views
{
    /// <summary>通知 1 件(08 §7.5・08a SC-38)。文は <see cref="TextKey"/> に <c>{runner}</c>(<see cref="RunnerName"/>)・<c>{n}</c>(<see cref="N"/>)・<c>{arg}</c>(<see cref="ArgKey"/> の文字列)を当てはめる。</summary>
    public sealed class NotificationView
    {
        public int Index;                           // 新しい順の添字(MarkNotificationRead の引数)
        public NotifyKind Kind;
        public int Year, MonthIndex, WeekOfMonth;   // 起きた日(第 N 年度・0 = 4 月・1〜4 週)
        public string TextKey = "";                 // 文字列表のキー(nt.*)
        public string ArgKey;                       // {arg} の文字列のキー(イベントの題 / 大会の名前)。なければ null
        public int RunnerId;                        // 関係する選手(なければ 0)。いなくなっていても ID は残る
        public string RunnerName = "";              // 在籍の選手のとき、名前(OB・退部は空)
        public int N;
        public bool Unread;
        public string Target = "";                  // 選んだとき開く画面: "SC-19" 選手 / "SC-16" 月間計画 / "SC-21" エントリー / "SC-22" オーダー / "SC-31" 特別招聘 / "SC-37" 確認 / ""(なし)
    }

    /// <summary>通知(SC-38 と、ホームのベルの未読数)。</summary>
    public sealed class NotificationsView
    {
        public readonly List<NotificationView> Items = new List<NotificationView>();     // 新しい順
        public int Unread;
    }
}
