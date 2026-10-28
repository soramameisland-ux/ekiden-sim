using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Team;
using Ekiden.Domain.Training;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>素養の判明(10 §8。L-64): 入部から K_GIFT_REVEAL_WEEKS 週で判明、スカウトで種類を見た選手は入部と同時に判明。表示だけが変わり、月末レポートの『素養が判明した』に出る。</summary>
    public class GiftRevealTests
    {
        private static void Give(Runner r, params Gift[] gifts) { r.SetIndividuality(r.Style, gifts); }

        private static WeekReport ReportOf(WeekOutcome o, Runner r) { return o.Reports[o.RunnerIds.IndexOf(r.Id)]; }

        [Test]
        public void Reveal_HappensExactlyTwelveWeeksAfterEnrollment_AndOnlyGiftHoldersAreAnnounced()
        {
            Assert.AreEqual(12, Individuality.K_GIFT_REVEAL_WEEKS);
            GameSession gs = GameSession.New(41UL, Difficulty.Mid);
            Runner withGift = gs.State.Runners[0], without = gs.State.Runners[1];
            Give(withGift, Gift.Climber);
            Give(without);
            foreach (Runner r in new[] { withGift, without }) { r.GiftRevealed = false; r.EnrollWeek = gs.State.AbsWeek - 10; }       // 週 0 の解決では 10 週、次の週に 11、その次が 12
            var seen = new List<string>();
            for (int w = 0; w < 4; w++)
            {
                int elapsed = gs.State.AbsWeek - withGift.EnrollWeek;
                WeekOutcome o = gs.StepWeek();
                bool should = elapsed >= 12;
                Assert.AreEqual(should, withGift.GiftRevealed, "経過 " + elapsed + " 週");
                Assert.AreEqual(should, without.GiftRevealed, "素養のない選手も、判明の扱いは同じ(『なし』が確定する)");
                bool flagGift = ReportOf(o, withGift).GiftRevealed, flagNone = ReportOf(o, without).GiftRevealed;
                seen.Add(elapsed + ":" + flagGift + "/" + flagNone);
                Assert.AreEqual(elapsed == 12, flagGift, "判明した週にだけ印が付く: " + string.Join(" ", seen));
                Assert.IsFalse(flagNone, "素養のない選手は『判明した』を知らせない");
            }
        }

        [Test]
        public void TheStartingRoster_IsRevealedInTheFirstWeek_AndEveryAnnouncementHappensOnce()
        {
            GameSession gs = GameSession.New(42UL, Difficulty.Elite);
            var announced = new Dictionary<int, int>();
            var holders = gs.State.Runners.Where(r => r.Gifts.Length > 0).Select(r => r.Id).ToList();
            Assert.Greater(holders.Count, 0, "素養を持つ選手がいる");
            Assert.IsTrue(gs.State.Runners.All(r => !r.GiftRevealed), "新規ゲームの直後はまだ誰も判明していない(隠し)");
            for (int w = 0; w < 14; w++)
            {
                WeekOutcome o = gs.StepWeek();
                for (int i = 0; i < o.Reports.Count; i++)
                    if (o.Reports[i].GiftRevealed) { announced.TryGetValue(o.RunnerIds[i], out int n); announced[o.RunnerIds[i]] = n + 1; }
                foreach (Runner r in gs.State.Runners)
                    Assert.AreEqual(gs.State.AbsWeek - 1 - r.EnrollWeek >= 12, r.GiftRevealed, "週 " + w + " の解決のあとは、経過 12 週以上の選手だけが判明している");
            }
            foreach (int id in holders.Where(id => gs.State.FindRunner(id) != null && gs.State.FindRunner(id).GiftRevealed))
                Assert.AreEqual(1, announced[id], "知らせは 1 人 1 回");
            CollectionAssert.IsSubsetOf(announced.Keys, holders);
            Assert.Greater(announced.Count, 0);
        }

        [Test]
        public void Intake_ScoutedTargets_AreRevealedAtEnrollment_OnlyWhenTheScoutStaffCanSeeTheKind()
        {
            foreach (int scoutLv in new[] { 2, 3 })
            {
                GameSession gs = GameSession.New(43UL, Difficulty.Mid);
                gs.AutoUpgrade = false;
                var f = gs.State.Fac;
                f.Scout = scoutLv;
                gs.State.Fac = f;
                for (int w = 0; w < 48; w++)
                {
                    int month = gs.YearWeek / 4;
                    if (gs.YearWeek % 4 == 0 && month >= 2 && month <= 7) gs.AutoScoutObserve();
                    if (gs.YearWeek % 4 == 0 && month >= 8 && month <= 10) gs.AutoScoutRecruit();
                    if (w == 47) break;
                    gs.StepWeek();
                }
                gs.ResolveTraining();
                RaceInfo? o;
                var race = gs.PlayWeekRaces(out o);
                var sel = gs.SelectWeekEvents(o);
                gs.ResolveWeekEvents(sel, o, new DefaultChoicePolicy());
                gs.FinishWeek();
                gs.SettleYear();
                gs.TurnoverYear();
                Assert.AreEqual(scoutLv, gs.School.ScoutLv);
                EnrollmentView e = gs.Enrollment();
                Assert.Greater(e.Targeted.Count, 0, "狙った新入生がいる");
                Assert.Greater(e.Natural.Count, 0, "自然に入った新入生もいる");
                foreach (EnrolleeRowView x in e.Targeted) Assert.AreEqual(scoutLv >= 3, gs.State.FindRunner(x.Row.Id).GiftRevealed, "スカウト担当 Lv" + scoutLv);
                foreach (EnrolleeRowView x in e.Natural) Assert.IsFalse(gs.State.FindRunner(x.Row.Id).GiftRevealed, "自然に入った選手は入部から 12 週後");
            }
        }

        [Test]
        public void MonthReport_AnnouncesTheRevealWithTheRunnerAndTheGift()
        {
            GameSession gs = GameSession.New(44UL, Difficulty.Mid);
            Runner r = gs.State.Runners[0];
            Give(r, Gift.Descender, Gift.Ace);
            foreach (Runner x in gs.State.Runners) x.GiftRevealed = true;              // ほかの選手の判明で月末レポートが埋まらないように
            r.GiftRevealed = false;
            r.EnrollWeek = gs.State.AbsWeek - 5;                                       // 週 7(月 1 の最後の週)の解決で経過 12 週
            var flow = new GameFlow(gs);
            Stop s = flow.Advance();
            MonthReportView first = null, second = null;
            int month = 0;
            while (month < 2)
            {
                while (s.Kind != StopKind.MonthReport) s = flow.Advance();
                if (month == 0) first = gs.MonthReportView(s.Report); else second = gs.MonthReportView(s.Report);
                month++;
                if (month < 2) s = flow.Advance();
            }
            Assert.IsFalse(first.Items.Any(i => i.Kind == "gift_reveal"), "月 0 にはまだ判明しない");
            ReportItemView it = second.Items.Single(i => i.Kind == "gift_reveal");
            Assert.AreEqual(r.Id, it.RunnerId);
            Assert.AreEqual(r.FamilyName + r.GivenName, it.RunnerName);
            CollectionAssert.AreEquivalent(new[] { Gift.Descender, Gift.Ace }, it.Gifts);
            Assert.IsTrue(r.GiftRevealed);
        }

        [Test]
        public void MountainAdvice_OnlyForRevealedClimbersAndDescenders()
        {
            GameSession gs = GameSession.New(45UL, Difficulty.Mid);
            Runner a = gs.State.Runners[0], b = gs.State.Runners[1], c = gs.State.Runners[2], d = gs.State.Runners[3];
            Give(a, Gift.Climber); a.GiftRevealed = true;
            Give(b, Gift.Descender); b.GiftRevealed = true;
            Give(c, Gift.Ace); c.GiftRevealed = true;
            Give(d, Gift.Climber); d.GiftRevealed = false;
            Assert.IsTrue(gs.RunnerRow(a.Id).MountainAdvice);
            Assert.IsTrue(gs.RunnerRow(b.Id).MountainAdvice);
            Assert.IsFalse(gs.RunnerRow(c.Id).MountainAdvice, "エースの素養は山対策の案内ではない");
            Assert.IsFalse(gs.RunnerRow(d.Id).MountainAdvice, "判明するまで案内しない(隠し)");
        }

        [Test]
        public void Reveal_ChangesNothingButTheDisplay()
        {
            GameSession a = GameSession.New(46UL, Difficulty.Mid), b = GameSession.New(46UL, Difficulty.Mid);
            for (int w = 0; w < 30; w++)
            {
                foreach (Runner r in b.State.Runners) r.GiftRevealed = false;          // b は毎週『未判明』に戻す。a は自然に判明していく。違いは表示だけのはず
                a.StepWeek();
                b.StepWeek();
            }
            Assert.IsTrue(a.State.Runners.Any(r => r.GiftRevealed), "a では判明している");
            for (int i = 0; i < a.State.Runners.Count; i++)
            {
                Runner x = a.State.Runners[i], y = b.State.Runners[i];
                CollectionAssert.AreEqual(x.Stats, y.Stats, "判明の時期で能力の伸びは変わらない");
                Assert.AreEqual(x.Fatigue, y.Fatigue);
                Assert.AreEqual(x.Form, y.Form);
            }
        }
    }
}
