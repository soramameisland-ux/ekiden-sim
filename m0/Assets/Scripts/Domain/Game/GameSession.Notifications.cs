using System;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Team;
using Ekiden.Domain.Views;

namespace Ekiden.Domain.Game
{
    /// <summary>通知の履歴(08 §7.5・08a SC-38)の読み取りと操作。通知を足すのは各所の出来事(<c>GameSession</c> の週の処理・年度末)と、外(App 層: 自動セーブの失敗・購入状態の不明)。</summary>
    public sealed partial class GameSession
    {
        /// <summary>外から通知を足す(App 層。Kind は Entitlement / AutosaveFailed など)。</summary>
        public void Notify(NotifyKind kind, string code, int runnerId = 0, int n = 0) { State.Notify(kind, code, runnerId, n); }

        public NotificationsView Notifications()
        {
            TeamState s = State;
            var v = new NotificationsView();
            for (int i = 0; i < s.Notifications.Count; i++)
            {
                Notification n = s.Notifications[i];
                int abs = n.AbsWeek, yw = Calendar.FloorMod(abs, Calendar.WeeksPerYear);
                Runner r = n.RunnerId != 0 ? s.FindRunner(n.RunnerId) : null;
                var nv = new NotificationView
                {
                    Index = i, Kind = n.Kind, Year = Calendar.FloorDiv(abs, Calendar.WeeksPerYear) + 1, MonthIndex = yw / 4, WeekOfMonth = yw % 4 + 1,
                    RunnerId = n.RunnerId, RunnerName = r != null ? r.FamilyName + r.GivenName : "", N = n.N, Unread = n.Unread,
                };
                FillText(nv, n, r != null);
                if (n.Unread) v.Unread++;
                v.Items.Add(nv);
            }
            return v;
        }

        private static void FillText(NotificationView v, Notification n, bool runnerExists)
        {
            string[] parts = n.Code.Split('.');
            string tail = parts.Length > 1 ? parts[1] : "";
            switch (n.Kind)
            {
                case NotifyKind.Injury: v.TextKey = "nt." + n.Code; v.Target = runnerExists ? "SC-19" : ""; break;
                case NotifyKind.Warning: v.TextKey = "nt.warning"; v.Target = "SC-16"; break;
                case NotifyKind.Event: v.TextKey = "nt.event"; v.ArgKey = n.Code + ".title"; v.Target = runnerExists ? "SC-19" : ""; break;
                case NotifyKind.Ticket: v.TextKey = "nt.ticket"; v.Target = "SC-31"; break;
                case NotifyKind.Schedule:
                    v.TextKey = "nt." + parts[0];
                    v.ArgKey = parts[0] == "qualifier_week" ? "race.qualifier.name" : "race." + tail + ".name";
                    v.Target = parts[0] == "entry_open" || parts[0] == "qualifier_week" ? "SC-21" : "SC-22";
                    break;
                case NotifyKind.Entitlement: v.TextKey = "nt.entitlement." + n.Code; v.Target = ""; break;
                default: v.TextKey = "nt.autosave." + n.Code; v.Target = ""; break;
            }
        }

        /// <summary>通知 1 件を既読にする(添字は <see cref="NotificationView.Index"/>)。</summary>
        public OpResult MarkNotificationRead(int index)
        {
            if (index < 0 || index >= State.Notifications.Count) return OpResult.Fail(OpErr.NotifyNone);
            State.Notifications[index].Unread = false;
            return OpResult.Success;
        }

        /// <summary>『すべて既読』。</summary>
        public OpResult MarkAllNotificationsRead()
        {
            foreach (Notification n in State.Notifications) n.Unread = false;
            return OpResult.Success;
        }
    }
}
