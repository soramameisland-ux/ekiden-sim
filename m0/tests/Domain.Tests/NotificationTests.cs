using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;
using Ekiden.Domain.Training;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>通知の履歴(08 §7.5・08a SC-38。08b G-07): 生成元(故障・警告・イベント・券・大会の予定)・上限・既読。</summary>
    public class NotificationTests
    {
        [Test]
        public void Notify_KeepsTheNewest30_NewestFirst()
        {
            var s = new TeamState();
            for (int i = 0; i < 45; i++) { s.AbsWeek = i; s.Notify(NotifyKind.Warning, "fatigue", 0, i); }
            Assert.AreEqual(30, s.Notifications.Count);
            Assert.AreEqual(44, s.Notifications[0].N);
            Assert.AreEqual(15, s.Notifications[29].N, "古い方から捨てる");
            Assert.AreEqual(44, s.Notifications[0].AbsWeek);
            Assert.IsTrue(s.Notifications.All(n => n.Unread));
        }

        /// <summary>1 週を段階ごとに進め、各段階で足された通知を独立に数え直す。</summary>
        [Test]
        public void Sources_MatchWhatHappenedEachWeek_ForSixYears()
        {
            int injuries = 0, events = 0, warnings = 0, schedule = 0, tickets = 0, notEntered = 0;
            foreach (ulong seed in new ulong[] { 61UL, 64UL, 65UL, 66UL, 67UL })
                RunSource(seed, Difficulty.Elite, ref injuries, ref events, ref warnings, ref schedule, ref tickets, ref notEntered);
            string counts = " injuries=" + injuries + " events=" + events + " warnings=" + warnings + " schedule=" + schedule + " tickets=" + tickets;
            Assert.Greater(injuries, 0, "試験の前提: 故障がある" + counts); Assert.Greater(events, 5, counts); Assert.Greater(warnings, 0, counts); Assert.Greater(schedule, 10, counts); Assert.Greater(tickets, 0, "試験の前提: 券を獲得する" + counts);
        }

        /// <summary>出場権のない大会(予選会を通らなかった・落ちた)では、エントリー開始も大会の週の知らせも出ない。出場権のない年が実際にあるシードで確かめる。</summary>
        [Test]
        public void ARaceTheSchoolIsNotIn_GivesNoScheduleNotice()
        {
            int injuries = 0, events = 0, warnings = 0, schedule = 0, tickets = 0, notEntered = 0;
            foreach (ulong seed in new ulong[] { 71UL, 72UL, 73UL })
                RunSource(seed, Difficulty.Low, ref injuries, ref events, ref warnings, ref schedule, ref tickets, ref notEntered);
            Assert.Greater(notEntered, 5, "試験の前提: 出場権のない大会の週が何度もある(schedule=" + schedule + ")");
            Assert.Greater(schedule, 0, "出場権のある大会の知らせはある");
        }

        private static void RunSource(ulong seed, Difficulty difficulty, ref int injuries, ref int events, ref int warnings, ref int schedule, ref int tickets, ref int notEntered)
        {
            GameSession gs = GameSession.New(seed, difficulty);
            gs.State.Settings.EventAuto = true;
            gs.State.Settings.AutoSafetyF = 0;                                                      // 安全網を切る(疲労が高いまま残り、警告の範囲に入る選手が出る = 試験の前提)
            for (int w = 0; w < 48 * 6; w++)
            {
                TeamState s = gs.State;
                if (Calendar.FloorMod(s.AbsWeek, Calendar.WeeksPerYear) % 4 == 2)
                {
                    int[] fat = { 75, 80, 85, 90, 95, 100 };                                                  // 解決のあとに警告の範囲(70 以上 85 未満)に入る選手が出るように(試験の前提を作る)
                    int j = 0;
                    foreach (Runner t in s.Runners.Where(x => !x.IsInjured).Take(fat.Length)) t.Fatigue = fat[j++];
                }
                int yw = Calendar.FloorMod(s.AbsWeek, Calendar.WeeksPerYear), abs = s.AbsWeek;
                int before = s.Notifications.Count;
                var firstSeen = new List<Notification>(s.Notifications.Take(Math.Min(s.Notifications.Count, 30)));
                // 週の最初の処理(前の週の AdvanceWeek)で足された予定の通知は、すでに入っている(この週の AbsWeek)。独立に数え直す
                var sched = s.Notifications.Where(n => n.Kind == NotifyKind.Schedule && n.AbsWeek == abs).Select(n => n.Code + ":" + n.N).OrderBy(c => c).ToList();
                var expectedSched = new List<string>();
                YearState ys = gs.PrepareYear();
                string[] names = { "fall", "national", "newyear" };
                for (int k = 0; k < 3; k++)
                {
                    bool deadline = yw == TeamWeek.EntryDeadlineWeeks[k], raceWeek = yw == Calendar.RaceWeeks[k];
                    if (!ys.Entrants((RaceKind)k).Contains(gs.School.Id)) { if (w > 0 && (deadline || raceWeek)) notEntered++; continue; }
                    if (deadline) expectedSched.Add("entry_open." + names[k] + ":4");                  // 締切は大会の 4 週前(08 §7.5・04 §4)
                    if (raceWeek) expectedSched.Add("race_week." + names[k] + ":0");
                }
                if (yw == GameSession.QualifierWeek && !ys.Seeds.Contains(gs.School.Id)) expectedSched.Add("qualifier_week:0");
                if (w > 0) CollectionAssert.AreEqual(expectedSched.OrderBy(c => c).ToList(), sched, "週 " + w + " の予定の通知");
                schedule += sched.Count;
                // 練習の解決: 故障と警告
                List<int> ids;
                List<WeekReport> reports = gs.ResolveTraining(out ids);
                var newN = s.Notifications.Take(s.Notifications.Count - before > 0 ? s.Notifications.Count - before : 0).ToList();
                var inj = s.Notifications.Where(n => n.Kind == NotifyKind.Injury && n.AbsWeek == abs).Select(n => n.RunnerId + ":" + n.Code).OrderBy(x => x).ToList();
                var expectedInj = new List<string>();
                for (int i = 0; i < reports.Count; i++) if (reports[i].NewInjury.HasValue) expectedInj.Add(ids[i] + ":injury." + reports[i].NewInjury.Value.ToString().ToLowerInvariant());
                CollectionAssert.AreEqual(expectedInj.OrderBy(x => x).ToList(), inj, "週 " + w + " の故障の通知");
                injuries += inj.Count;
                int thr = yw / 4 == 4 ? GameFlow.WarnFatigueCamp : GameFlow.WarnFatigue;
                int warned = yw % 4 == 2 ? s.Runners.Count(r => !r.IsInjured && r.Fatigue >= thr && r.Fatigue < GameFlow.WarnFatigueMax) : 0;
                var warn = s.Notifications.Where(n => n.Kind == NotifyKind.Warning && n.AbsWeek == abs).ToList();
                Assert.AreEqual(warned > 0 ? 1 : 0, warn.Count, "週 " + w + " の警告");
                if (warned > 0) Assert.AreEqual(warned, warn[0].N);
                warnings += warn.Count;
                // 大会とイベント
                RaceInfo? own;
                int ticketsBefore = s.Notifications.Count(n => n.Kind == NotifyKind.Ticket);
                gs.PlayWeekRaces(out own);
                EventSelection sel = gs.SelectWeekEvents(own);
                gs.ResolveWeekEvents(sel, own, new DefaultChoicePolicy());
                var ev = s.Notifications.Where(n => n.Kind == NotifyKind.Event && n.AbsWeek == abs).Select(n => n.Code + ":" + n.RunnerId).OrderBy(x => x).ToList();
                CollectionAssert.AreEqual(sel.Fired.Select(f => f.Def.Id + ":" + (f.R != null ? f.R.Id : 0)).OrderBy(x => x).ToList(), ev, "週 " + w + " のイベントの通知");
                events += ev.Count;
                tickets += s.Notifications.Count(n => n.Kind == NotifyKind.Ticket) - ticketsBefore;
                gs.FinishWeek();
                if (yw == Calendar.WeeksPerYear - 1)
                {
                    int tBefore = s.Notifications.Count(n => n.Kind == NotifyKind.Ticket);
                    gs.SettleYear(); gs.TurnoverYear();
                    tickets += s.Notifications.Count(n => n.Kind == NotifyKind.Ticket) - tBefore;
                }
                gs.AdvanceWeek();
            }
            Assert.LessOrEqual(gs.State.Notifications.Count, 30);
            for (int i = 1; i < gs.State.Notifications.Count; i++) Assert.GreaterOrEqual(gs.State.Notifications[i - 1].AbsWeek, gs.State.Notifications[i].AbsWeek, "新しい順");
        }

        [Test]
        public void View_FormatsDates_RunnersAndTargets_AndUnreadCounts()
        {
            GameSession gs = GameSession.New(62UL, Difficulty.Mid);
            Runner r = gs.State.Runners[0];
            gs.State.AbsWeek = 48 * 2 + 5 * 4 + 2;                                    // 第 3 年度 9 月(月 5)の第 3 週
            gs.State.Notify(NotifyKind.Injury, "injury.moderate", r.Id);
            gs.State.Notify(NotifyKind.Warning, "fatigue", 0, 3);
            gs.State.Notify(NotifyKind.Event, "ev.team.captain", r.Id);
            gs.State.Notify(NotifyKind.Ticket, "earned", 0, 2);
            gs.State.Notify(NotifyKind.Schedule, "entry_open.national", 0, 4);
            gs.State.Notify(NotifyKind.Schedule, "race_week.newyear");
            gs.State.Notify(NotifyKind.Schedule, "qualifier_week");
            gs.Notify(NotifyKind.Entitlement, "unknown");
            gs.Notify(NotifyKind.AutosaveFailed, "disk_full");
            gs.State.Notify(NotifyKind.Injury, "injury.severe", 999999);
            NotificationsView v = gs.Notifications();
            Assert.AreEqual(10, v.Items.Count);
            Assert.AreEqual(10, v.Unread);
            Assert.AreEqual(10, gs.Home().UnreadNotifications);
            NotificationView last = v.Items[0];                                         // 新しい順の先頭 = 最後に足したもの
            Assert.AreEqual("nt.injury.severe", last.TextKey); Assert.AreEqual("", last.RunnerName, "いない選手は名前なし"); Assert.AreEqual("", last.Target); Assert.AreEqual(999999, last.RunnerId);
            Assert.AreEqual(3, last.Year); Assert.AreEqual(5, last.MonthIndex); Assert.AreEqual(3, last.WeekOfMonth);
            NotificationView inj = v.Items[9];
            Assert.AreEqual("nt.injury.moderate", inj.TextKey); Assert.AreEqual(r.FamilyName + r.GivenName, inj.RunnerName); Assert.AreEqual("SC-19", inj.Target);
            Assert.AreEqual("nt.warning", v.Items[8].TextKey); Assert.AreEqual(3, v.Items[8].N); Assert.AreEqual("SC-16", v.Items[8].Target);
            Assert.AreEqual("nt.event", v.Items[7].TextKey); Assert.AreEqual("ev.team.captain.title", v.Items[7].ArgKey); Assert.AreEqual("SC-19", v.Items[7].Target);
            Assert.AreEqual("nt.ticket", v.Items[6].TextKey); Assert.AreEqual("SC-31", v.Items[6].Target);
            Assert.AreEqual("nt.entry_open", v.Items[5].TextKey); Assert.AreEqual("race.national.name", v.Items[5].ArgKey); Assert.AreEqual(4, v.Items[5].N); Assert.AreEqual("SC-21", v.Items[5].Target);
            Assert.AreEqual("nt.race_week", v.Items[4].TextKey); Assert.AreEqual("race.newyear.name", v.Items[4].ArgKey); Assert.AreEqual("SC-22", v.Items[4].Target);
            Assert.AreEqual("nt.qualifier_week", v.Items[3].TextKey); Assert.AreEqual("race.qualifier.name", v.Items[3].ArgKey); Assert.AreEqual("SC-21", v.Items[3].Target);
            Assert.AreEqual("nt.entitlement.unknown", v.Items[2].TextKey);
            Assert.AreEqual("nt.autosave.disk_full", v.Items[1].TextKey);
            Assert.IsTrue(Enumerable.Range(0, v.Items.Count).All(i => v.Items[i].Index == i));
        }

        [Test]
        public void MarkRead_OneOrAll_AndRejectsABadIndex()
        {
            GameSession gs = GameSession.New(63UL, Difficulty.Mid);
            for (int i = 0; i < 4; i++) gs.Notify(NotifyKind.Warning, "fatigue", 0, i);
            Assert.IsTrue(gs.MarkNotificationRead(1).Ok);
            NotificationsView v = gs.Notifications();
            Assert.AreEqual(3, v.Unread);
            Assert.IsFalse(v.Items[1].Unread);
            Assert.IsFalse(gs.MarkNotificationRead(4).Ok); Assert.IsFalse(gs.MarkNotificationRead(-1).Ok);
            Assert.AreEqual(3, gs.Notifications().Unread, "失敗では変わらない");
            Assert.IsTrue(gs.MarkAllNotificationsRead().Ok);
            Assert.AreEqual(0, gs.Notifications().Unread);
            Assert.AreEqual(0, gs.Home().UnreadNotifications);
            Assert.AreEqual(4, gs.Notifications().Items.Count, "既読にしても消えない");
            gs.Notify(NotifyKind.Warning, "fatigue", 0, 9);
            Assert.AreEqual(1, gs.Notifications().Unread, "新しい通知は未読");
        }
    }
}
