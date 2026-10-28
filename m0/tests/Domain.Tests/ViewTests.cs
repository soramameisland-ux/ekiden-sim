using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>画面に出す値を組み立てる読み取りの API(<see cref="GameSession"/> の Home / RosterRows / RunnerDetail など。08b)。状態は変えず、規則(R-01 ほか)と食い違わない。</summary>
    public class ViewTests
    {
        private static GameSession Game(Difficulty d = Difficulty.Mid, ulong seed = 20261031UL) { return GameSession.New(seed, d); }

        // ------------------------------------------------------------ ホーム
        [Test]
        public void Home_AtTheStartOfTheFirstYear()
        {
            GameSession gs = Game();
            HomeView h = gs.Home();
            Assert.AreEqual("school." + gs.School.Id + ".name", h.SchoolNameKey);
            Assert.AreEqual(1, h.Year);
            Assert.AreEqual(0, h.YearWeek);
            Assert.AreEqual(4, h.CalendarMonth, "年度は 4 月始まり");
            Assert.AreEqual(1, h.WeekOfMonth);
            Assert.AreEqual(gs.School.Rank(), h.CoachRank);
            Assert.AreEqual(WorldK.RankCum[h.CoachRank], h.NextRankPoints, "次のランクの累計評価ポイント");
            Assert.That(h.PowerRank, Is.InRange(1, 36));
            Assert.IsFalse(h.Cleared);
            CollectionAssert.AreEqual(new[] { false, false, false }, h.WonEver);
            Assert.AreEqual(RaceKind.Fall, h.Next.Kind, "最初の大会は第一戦");
            Assert.AreEqual(Calendar.RaceWeeks[0], h.Next.WeeksUntil);
            Assert.AreEqual(TeamWeek.EntryDeadlineWeeks[0], h.Next.EntryDeadlineWeeksLeft, "締切まであと 21 週");
            Assert.IsFalse(h.Next.EntryOpen);
            Assert.IsFalse(h.Next.OrderOpen);
            Assert.AreEqual(Math.Max(1, gs.State.LastRank[2] - 1), h.ExpectedTargetRank);
            Assert.AreEqual(200, h.ExpectedBonusFunds, "期待目標の達成の予算ボーナスはイベント(ev.team.univ_bonus)の効果から");
            Assert.IsFalse(h.Declarable, "宣言できるのは 6〜2 月");
            Assert.AreEqual(gs.State.Funds, h.Funds);
            Assert.AreEqual(gs.State.ManagerName, h.ManagerName);
        }

        [Test]
        public void Home_FollowsTheCalendar_EntryWindow_AndNextYear()
        {
            GameSession gs = Game(Difficulty.Elite);
            bool sawOpen = false, sawNextYear = false;
            for (int w = 0; w < 48 * 2; w++)
            {
                HomeView h = gs.Home();
                int yw = gs.YearWeek;
                Assert.AreEqual((3 + yw / 4) % 12 + 1, h.CalendarMonth);
                if (h.QualifierWeeksUntil.HasValue) Assert.AreEqual(GameSession.QualifierWeek - yw, h.QualifierWeeksUntil.Value);
                if (yw > GameSession.QualifierWeek) Assert.IsFalse(h.QualifierWeeksUntil.HasValue, "予選会を過ぎたら出さない");
                int k = (int)h.Next.Kind;
                if (yw >= TeamWeek.EntryDeadlineWeeks[k] && yw <= Calendar.RaceWeeks[k] && h.Next.CanEnter == true)
                {
                    Assert.IsTrue(h.Next.EntryOpen, "締切の週から大会の週の初めまで、エントリーを出せる(週 " + yw + ")");
                    Assert.AreEqual(yw > TeamWeek.EntryDeadlineWeeks[k] ? -1 : 0, h.Next.EntryDeadlineWeeksLeft, "締切の週は 0・過ぎたら −1");
                    sawOpen = true;
                }
                if (yw > Calendar.RaceWeeks[2] && gs.Year >= 1) { Assert.IsNull(h.Next.CanEnter, "来年度の大会はまだ決まっていない"); sawNextYear = true; Assert.AreEqual(RaceKind.Fall, h.Next.Kind); }
                gs.StepWeek();
            }
            Assert.IsTrue(sawOpen && sawNextYear);
        }

        [Test]
        public void Home_QualifierCountdown_OnlyForSchoolsWithoutASeed()
        {
            foreach (Difficulty d in new[] { Difficulty.Elite, Difficulty.Mid, Difficulty.Low })
            {
                GameSession gs = Game(d, 31UL);
                bool seeded = gs.PrepareYear().Seeds.Contains(gs.School.Id);
                HomeView h = gs.Home();
                Assert.AreEqual(!seeded, h.QualifierWeeksUntil.HasValue, d + ": 予選会はシード外だけ");
                if (!seeded) Assert.AreEqual(GameSession.QualifierWeek, h.QualifierWeeksUntil.Value);
            }
        }

        [Test]
        public void Home_TracksProgress_Clear_Wins_Declarable()
        {
            GameSession gs = Game(Difficulty.Elite, 77UL);
            gs.State.Tickets.Earn(Ekiden.Domain.Tickets.TicketMilestones.WinFall, 0);
            int guard = 0;
            while (guard++ < 48 * 12 && !gs.Home().Cleared) gs.StepWeek();
            HomeView h = gs.Home();
            for (int k = 0; k < 3; k++) if (gs.Stats.Any(x => x.Wins[k])) Assert.IsTrue(h.WonEver[k], "年度の結果で優勝した大会は優勝経験にある");
            if (h.Cleared) { Assert.AreEqual(gs.State.Cleared.Year, h.ClearYear); Assert.IsTrue(h.WonEver.All(b => b)); }
            // 招聘の宣言の期間(6〜2 月)に入ると『宣言できます』
            GameSession g2 = Game(Difficulty.Mid, 5UL);
            g2.State.Tickets.Earn(Ekiden.Domain.Tickets.TicketMilestones.WinFall, 0);
            while (g2.YearWeek / 4 < 2) g2.StepWeek();
            Assert.IsTrue(g2.Home().Declarable);
            Assert.IsTrue(g2.Home().TicketStock >= 1);
            Assert.IsTrue(g2.DeclareTicket(Ekiden.Domain.Tickets.TicketKind.Rookie).Ok);
            Assert.IsFalse(g2.Home().Declarable);
            Assert.IsTrue(g2.Home().TicketPending);
        }

        // ------------------------------------------------------------ 部員の行
        [Test]
        public void RosterRows_MatchTheRunnersAndTheRules()
        {
            GameSession gs = Game();
            List<RunnerRowView> rows = gs.RosterRows();
            Assert.AreEqual(gs.State.Runners.Count, rows.Count);
            CollectionAssert.AreEqual(gs.State.Runners.Select(r => r.Id).OrderBy(x => x).ToArray(), rows.Select(r => r.Id).ToArray(), "ID の昇順");
            foreach (RunnerRowView v in rows)
            {
                Runner r = gs.State.FindRunner(v.Id);
                double p = TeamState.LongP(r);
                Assert.AreEqual(p, v.P);
                Assert.AreEqual(DisplayRules.RankLetter(p), v.Rank, "R-01");
                Assert.AreEqual(r.FamilyName, v.FamilyName);
                Assert.IsNotEmpty(v.FamilyName);
                Assert.AreEqual(r.Fatigue, v.Fatigue);
                Assert.AreEqual(r.Group, v.Group);
                Assert.AreEqual(r.Skills.Count, v.SkillCount);
                Assert.AreEqual(v.SkillCount, v.GoldSkills + v.BlueSkills + v.RedSkills);
                if (!r.Overseas) { Assert.AreEqual(Geo.ById(r.Pref).Name, v.PrefName); Assert.AreEqual("", v.CountryName); }
                else Assert.IsNotEmpty(v.CountryName);
                if (r.HighSchool > 0) Assert.AreEqual("hs." + r.HighSchool + ".name", v.HighSchoolNameKey);
                Assert.IsNotEmpty(v.StyleChips, "個性チップ(普通なら standard)");
                Assert.IsNotEmpty(v.Traits);
            }
            Assert.AreEqual(gs.State.Runners.Count(r => r.Regular), rows.Count(v => v.Regular));
        }

        [Test]
        public void RosterRows_MarkSameHometownAndSameHighSchoolAsTheSelectedRunner()
        {
            GameSession gs = Game();
            Runner sel = gs.State.Runners.First(r => gs.State.Runners.Count(o => o.Pref == r.Pref && !o.Overseas) > 1 && !r.Overseas);
            List<RunnerRowView> rows = gs.RosterRows(sel.Id);
            foreach (RunnerRowView v in rows)
            {
                Runner r = gs.State.FindRunner(v.Id);
                bool home = r.Id != sel.Id && r.Pref != 0 && r.Pref == sel.Pref;
                bool hs = r.Id != sel.Id && r.HighSchool != 0 && r.HighSchool == sel.HighSchool;
                Assert.AreEqual(home, v.SameHometownAsSelected, "同郷の印 ●");
                Assert.AreEqual(hs, v.SameHighSchoolAsSelected, "同じ高校の印 ◆");
            }
            Assert.IsTrue(rows.Any(v => v.SameHometownAsSelected));
            Assert.IsFalse(gs.RosterRows().Any(v => v.SameHometownAsSelected || v.SameHighSchoolAsSelected), "選んでいなければ印はない");
        }

        // ------------------------------------------------------------ 選手詳細
        [Test]
        public void RunnerDetail_StatsFitsPbAndHiddenGifts()
        {
            GameSession gs = Game(Difficulty.Elite);
            foreach (Runner r in gs.State.Runners.Take(12))
            {
                RunnerDetailView d = gs.RunnerDetail(r.Id);
                Assert.AreEqual(r.Id, d.Row.Id);
                Assert.AreEqual(StatInfo.Count, d.Stats.Count);
                for (int i = 0; i < d.Stats.Count; i++)
                {
                    Assert.AreEqual((Stat)i, d.Stats[i].Stat);
                    Assert.AreEqual(r.Stats[i], d.Stats[i].Value);
                    Assert.AreEqual(DisplayRules.RankLetter(r.Stats[i]), d.Stats[i].Rank);
                    Assert.AreEqual(DisplayRules.Hint(r.Stats[i], r.Potential[i]), d.Stats[i].Hint, "R-02。素質の数値は出さない");
                }
                var dv = r.Stats.Select(x => (double)x).ToArray();
                Assert.AreEqual(5, d.Fits.Count);
                foreach (FitView f in d.Fits)
                {
                    Assert.AreEqual(RaceTables.Rating(dv, f.Type), f.Value, 1e-12);
                    Assert.AreEqual(f.Value >= 70 ? '◎' : (f.Value >= 60 ? '〇' : '△'), f.Mark);
                }
                Assert.AreEqual(DisplayRules.DerivePb(r.Stats, DisplayRules.PbEvent.M5000), d.Pb5000);
                Assert.AreEqual(DisplayRules.DerivePb(r.Stats, DisplayRules.PbEvent.M10000), d.Pb10000);
                Assert.AreEqual(DisplayRules.DerivePb(r.Stats, DisplayRules.PbEvent.Half), d.PbHalf);
                Assert.Less(d.Pb5000, d.Pb10000, "距離が長いほど長いタイム");
                Assert.Less(d.Pb10000, d.PbHalf);
                Assert.AreEqual(r.GiftRevealed ? r.Gifts.Length : 0, d.Gifts.Count, "素養は判明するまで出さない(10 §8)");
                Assert.AreEqual(r.Style, d.Style);
                Assert.AreEqual(r.Skills.Count, d.Skills.Count);
            }
            Runner hidden = gs.State.Runners.FirstOrDefault(r => r.Gifts.Length > 0 && !r.GiftRevealed);
            if (hidden != null) Assert.IsEmpty(gs.RunnerDetail(hidden.Id).Gifts);
        }

        [Test]
        public void RunnerDetail_BondsAreSortedAndLimited_ThemeFitFollowsTheGroupTheme_IndividualMenuShows()
        {
            GameSession gs = Game();
            Runner r = gs.State.Runners.OrderBy(x => x.Id).First(x => !x.IsInjured);
            RunnerDetailView d = gs.RunnerDetail(r.Id);
            Assert.LessOrEqual(d.Bonds.Count, 8);
            for (int i = 1; i < d.Bonds.Count; i++) Assert.GreaterOrEqual(d.Bonds[i - 1].Value, d.Bonds[i].Value, "絆の大きい順");
            foreach (BondView b in d.Bonds) Assert.AreEqual(gs.State.Bond(r, gs.State.FindRunner(b.RunnerId)), b.Value);
            Assert.AreEqual(gs.State.MonthlyPlan[r.Group], d.ThemeId);
            double fit = r.Style.HasValue ? Presentation.FitView(gs.Master, r.Style.Value, r.Gifts, r.GiftRevealed, d.ThemeId) : Presentation.FitValue(gs.Master, null, d.ThemeId);
            Assert.AreEqual(Presentation.FitMark(fit), d.ThemeFitMark);
            Assert.IsNull(d.IndividualMenu);
            Assert.IsFalse(d.Talked);
            Assert.IsTrue(gs.Talk(r.Id).Ok);
            Assert.IsTrue(gs.RunnerDetail(r.Id).Talked, "面談した印");
            Assert.IsTrue(gs.GuideMenu(r.Id, new MenuSpec("ks.pace_run", "ks.build_up", "mil.std"), true).Ok);
            d = gs.RunnerDetail(r.Id);
            Assert.IsNotNull(d.IndividualMenu);
            Assert.AreEqual("ks.pace_run", d.IndividualMenu.Key1);
        }

        [Test]
        public void Views_AreReadOnly_AndUnknownRunnersAreRefused()
        {
            GameSession gs = Game();
            for (int i = 0; i < 20; i++) gs.StepWeek();
            string before = GameSessionTests.Digest(gs);
            gs.Home();
            gs.RosterRows(gs.State.Runners[0].Id);
            foreach (Runner r in gs.State.Runners) { gs.RunnerRow(r.Id); gs.RunnerDetail(r.Id); }
            Assert.AreEqual(before, GameSessionTests.Digest(gs), "読み取りは状態を変えない");
            Assert.Throws<ArgumentException>(() => gs.RunnerDetail(99999));
            Assert.Throws<ArgumentException>(() => gs.RunnerRow(99999));
        }

        [Test]
        public void SchoolAndHighSchoolNames_ExistInTheStringsSeed()
        {
            // 大学名 36・高校名 216 は文字列テーブルのキー(school.{id}.name / hs.{id}.name)。Views はキーを渡す。
            GameSession gs = Game();
            Assert.AreEqual(36, WorldSim.SchoolCount);
            Assert.AreEqual(216, Geo.HighSchoolTotal);
            Assert.That(gs.School.Id, Is.InRange(0, 35));
        }
    }
}
