using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.World;
using NUnit.Framework;
using Calendar = Ekiden.Domain.Team.Calendar;

namespace Ekiden.Domain.Tests
{
    /// <summary>実体の選手を持つ自校 + 世界(<see cref="GameSession"/>。方針『おまかせ』)の通し。</summary>
    public class GameSessionTests
    {
        private const ulong Seed = 1000020UL;

        /// <summary>状態の指紋(選手の能力・疲労・やる気・調子・学年・絆・資金・施設・OB・成績)。同じ入力 → 同じ指紋を確かめる。</summary>
        internal static string Digest(GameSession gs)
        {
            var sb = new StringBuilder();
            TeamState s = gs.State;
            sb.Append(s.AbsWeek).Append('|').Append(s.Funds).Append('|').Append(s.Prestige.ToString("R")).Append('|').Append(s.Morale).Append('|').Append(s.NextRunnerId).Append('\n');
            foreach (Runner r in s.Runners.OrderBy(x => x.Id))
            {
                sb.Append(r.Id).Append(':').Append(r.Grade).Append(',').Append(r.Fatigue).Append(',').Append(r.Motivation).Append(',').Append(r.Form).Append(',').Append((int)r.InjurySeverity)
                  .Append(',').Append(r.Races).Append(',').Append(r.SectionWins).Append(',').Append(string.Join(".", r.Stats)).Append(',').Append(string.Join(".", r.Xp)).Append(',').Append(string.Join(".", r.Potential)).Append('\n');
            }
            foreach (int[] b in s.Bonds.Items()) sb.Append(b[0]).Append('-').Append(b[1]).Append('=').Append(b[2]).Append(';');
            sb.Append('\n').Append(s.Obs.Count).Append('|').Append(s.Tickets.Stock).Append('|').Append(s.Tickets.InvitesDeclared).Append('|').Append(s.Fac.Dorm).Append(s.Fac.Scout).Append(s.Fac.Coach).Append('\n');
            foreach (PlayerYearStats st in gs.Stats) sb.Append(st.Year).Append(':').Append(st.P.ToString("R")).Append(',').Append(st.Place[0]).Append(st.Place[1]).Append(st.Place[2]).Append(';');
            foreach (School sc in gs.World.Schools) sb.Append(sc.Id).Append('=').Append(sc.Prestige.ToString("R")).Append(',').Append(sc.Roster.Count).Append(';');
            using (SHA256 h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "");
        }

        /// <summary>週の境目で守られているべきこと。</summary>
        internal static void AssertInvariants(GameSession gs)
        {
            TeamState s = gs.State;
            Assert.AreEqual(0, gs.School.Roster.Count, "抽象の名簿は空のまま");
            Assert.GreaterOrEqual(s.Runners.Count, TeamYear.MinRunners);
            Assert.AreEqual(s.Runners.Count, s.Runners.Select(r => r.Id).Distinct().Count(), "ID の重複");
            var ids = new HashSet<int>(s.Runners.Select(r => r.Id));
            foreach (Runner r in s.Runners)
            {
                for (int i = 0; i < StatInfo.Count; i++)
                {
                    Assert.GreaterOrEqual(r.Stats[i], 1);
                    Assert.LessOrEqual(r.Stats[i], r.Potential[i], "I-01 能力 ≤ 素質(選手 " + r.Id + ")");
                    Assert.LessOrEqual(r.Potential[i], 100);
                }
                Assert.That(r.Fatigue, Is.InRange(0, 100));
                Assert.That(r.Motivation, Is.InRange(1, 5));
                Assert.That(r.Form, Is.InRange(-2, 2));
                Assert.That(r.Grade, Is.InRange(1, 4));
            }
            foreach (int[] b in s.Bonds.Items())
            {
                Assert.IsTrue(ids.Contains(b[0]) && ids.Contains(b[1]), "絆が存在しない選手を指す");
                Assert.That(b[2], Is.InRange(1, 1000));
            }
            foreach (int id in s.Bench.Concat(s.Forced).Concat(s.Talked).Concat(s.IndividualMenus.Keys).Concat(s.Entries.SelectMany(e => e))) Assert.IsTrue(ids.Contains(id), "存在しない選手への参照");
            Assert.That(s.Prestige, Is.InRange(0.0, 1000.0));
            Assert.That(s.Morale, Is.InRange(0.0, 100.0));
            Assert.That(s.Tickets.Stock, Is.InRange(0, TicketK.K_TICKET_STOCK_MAX));
            Assert.GreaterOrEqual(s.Tickets.Bank, 0);
            if (s.Tickets.Stock < TicketK.K_TICKET_STOCK_MAX) Assert.AreEqual(0, s.Tickets.Bank, "stock < 2 ならば bank = 0");
        }

        // ---------------------------------------------------------------- 新規ゲーム
        [TestCase(Difficulty.Elite)]
        [TestCase(Difficulty.Mid)]
        [TestCase(Difficulty.Low)]
        public void New_BuildsRealRunnersFromTheClaimedSchool(Difficulty d)
        {
            GameSession gs = GameSession.New(Seed, d);
            ScenarioDef sc = Scenarios.For(d);
            Assert.IsTrue(gs.School.IsPlayer);
            Assert.AreEqual(0, gs.School.Roster.Count);
            Assert.AreEqual(sc.Funds, gs.State.Funds);
            Assert.AreEqual(50.0, gs.State.Morale);
            Assert.AreEqual(d.ToString(), gs.State.Scenario.Difficulty);
            Assert.AreEqual(gs.School.Id, gs.State.Scenario.PlayerSchoolId);
            Assert.AreEqual(Geo.HomePrefecture(gs.School.Region), gs.State.SchoolPref);
            Assert.That(gs.State.Runners.Count, Is.InRange(30, 40));
            CollectionAssert.AreEqual(Enumerable.Range(1, gs.State.Runners.Count), gs.State.Runners.Select(r => r.Id));
            Assert.AreEqual(gs.State.Runners.Count + 1, gs.State.NextRunnerId);
            Assert.IsTrue(Enumerable.Range(1, 4).All(g => gs.State.Runners.Any(r => r.Grade == g)), "1〜4 年生がいる");
            Assert.Greater(gs.State.Bonds.Count, 0, "同じ県の部員には初期の絆");
            Assert.IsTrue(gs.State.Settings.PlanAuto && gs.State.Settings.EventAuto && gs.State.Settings.ScoutAuto);
            AssertInvariants(gs);
        }

        [Test]
        public void New_Club_KeepsFiveRunners_Deterministically()
        {
            GameSession a = GameSession.New(Seed, Difficulty.Club), b = GameSession.New(Seed, Difficulty.Club), c = GameSession.New(Seed + 1, Difficulty.Club);
            Assert.AreEqual(5, a.State.Runners.Count);
            Assert.AreEqual(Digest(a), Digest(b));
            Assert.AreNotEqual(Digest(a), Digest(c));
            AssertInvariants(a);
        }

        [Test]
        public void New_SameSeed_SameWorld_AsTheAbstractAgent()
        {
            // 世界の生成と開始校の選び方は抽象の自校と同じ(同じシード → 同じ学校が自校になる)
            var w = WorldSim.Create(new Rng.KeyedRngFactory(Seed), 15);
            PlayerAgent agent = w.StartPlayer(Difficulty.Mid);
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            Assert.AreEqual(agent.School.Id, gs.School.Id);
            Assert.AreEqual(agent.School.Roster.Count, gs.State.Runners.Count);
        }

        // ---------------------------------------------------------------- 週
        [Test]
        public void StepWeek_RunsTheYearInOrder()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            var races = new List<int>();
            WeekOutcome end = null;
            for (int w = 0; w < Calendar.WeeksPerYear; w++)
            {
                int yw = gs.YearWeek, nRunners = gs.State.Runners.Count;
                WeekOutcome o = gs.StepWeek();
                Assert.AreEqual(yw, o.AbsWeek);
                Assert.AreEqual(nRunners, o.Reports.Count, "全選手の週の解決");
                if (o.Race != null) races.Add(yw);
                if (yw == GameSession.QualifierWeek) Assert.IsTrue(gs.YearInProgress.QualifierDone, "予選会は 10 月第 3 週");
                if (yw < GameSession.QualifierWeek) Assert.IsFalse(gs.YearInProgress.QualifierDone);
                if (o.YearEnd != null) end = o;
                if (yw % 4 == 3 && yw != 47) AssertInvariants(gs);
            }
            CollectionAssert.AreEqual(new[] { 25, 28, 36 }, races, "大会の週");
            Assert.IsNotNull(end);
            Assert.AreEqual(1, end.YearEnd.Year);
            Assert.AreEqual(Calendar.WeeksPerYear, gs.State.AbsWeek);
            Assert.AreEqual(2, gs.Year);
            Assert.IsNull(gs.YearInProgress, "年度末で閉じる");
            Assert.AreEqual(1, gs.Stats.Count);
            foreach (RaceKind k in WorldSim.AllRaces) Assert.IsTrue(end.YearEnd.Order.ContainsKey(k));
            AssertInvariants(gs);
        }

        [Test]
        public void StepWeek_YearEnd_GraduatesAndEnrolls()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            int seniors = gs.State.Runners.Count(r => r.Grade == 4);
            int total = gs.State.Runners.Count;
            gs.PlayYears(1);
            TeamState s = gs.State;
            YearTurnover t = gs.LastTurnover;
            Assert.AreEqual(seniors, t.Graduates.Count);
            Assert.AreEqual(seniors, s.Obs.Count);
            int entered = s.Runners.Count(r => r.Grade == 1);
            Assert.That(entered, Is.InRange(1, 11), "新入生(定員 10 + 招聘 1)");
            Assert.AreEqual(total - seniors - t.Quit.Count + entered, s.Runners.Count);
            Assert.IsTrue(s.Runners.Where(r => r.Grade == 1).All(r => r.EnrollWeek == Calendar.WeeksPerYear), "新入生の入学は翌年度の最初の週");
            Assert.IsTrue(s.Runners.Where(r => r.Grade == 1).All(r => r.Fatigue == 20 && r.Motivation >= 3 && r.Form == 0));
            Assert.AreEqual(gs.Stats[0].Points, gs.School.PrevPoints, "年度の評価ポイントは世界と自校で一致する");
            Assert.AreEqual(gs.School.Prestige, s.Prestige);
            Assert.AreEqual(gs.School.Rank(), s.CoachRank);
            AssertInvariants(gs);
        }

        [Test]
        public void StepWeek_FiresEvents_AndAdvancesRunners()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            int fired = 0;
            int start = gs.State.Runners.Sum(r => r.Stats.Sum());
            for (int w = 0; w < Calendar.WeeksPerYear * 2; w++) fired += gs.StepWeek().Events.Count;
            Assert.Greater(fired, 5, "2 年度でイベントが起こる");
            Assert.Greater(gs.State.EventLog.Count, 0);
            Assert.Greater(gs.State.Runners.Where(r => r.Grade >= 2).Sum(r => r.Stats.Sum()) / (double)gs.State.Runners.Count(r => r.Grade >= 2), start / (double)40 * 0.95, "練習で力が伸びる");
        }

        // ---------------------------------------------------------------- 長期
        [TestCase(Difficulty.Mid, 3UL)]
        [TestCase(Difficulty.Low, 5UL)]
        [TestCase(Difficulty.Club, 7UL)]
        [TestCase(Difficulty.Elite, 11UL)]
        public void Play_TenYears_KeepsInvariants(Difficulty d, ulong seed)
        {
            GameSession gs = GameSession.New(seed * 1000003UL + 17UL, d);
            for (int y = 1; y <= 10; y++)
            {
                for (int w = 0; w < Calendar.WeeksPerYear; w++)
                {
                    gs.StepWeek();
                    if (w % 6 == 5) AssertInvariants(gs);
                }
                PlayerYearStats st = gs.Stats.Last();
                Assert.AreEqual(y, st.Year);
                Assert.That(st.PowerRank, Is.InRange(1, 36));
                Assert.AreEqual(gs.State.Obs.Count, gs.State.Obs.Select(o => o.RunnerId).Distinct().Count());
            }
            Assert.That(gs.State.Runners.Count, Is.InRange(TeamYear.MinRunners, 41));
            if (d != Difficulty.Club) Assert.Greater(gs.State.Runners.Count, 25);
        }

        [Test]
        public void Play_IsDeterministic_AndDifferentSeedsDiffer()
        {
            GameSession a = GameSession.New(Seed, Difficulty.Mid), b = GameSession.New(Seed, Difficulty.Mid), c = GameSession.New(Seed + 1, Difficulty.Mid);
            a.PlayYears(3); b.PlayYears(3); c.PlayYears(3);
            Assert.AreEqual(Digest(a), Digest(b));
            Assert.AreNotEqual(Digest(a), Digest(c));
        }

        // ---------------------------------------------------------------- 特別招聘
        [Test]
        public void Tickets_AreDeclaredInFebruary_AndEnrollAtYearEnd()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            gs.State.Tickets.Earn(TicketMilestones.WinFall, 0);
            gs.State.Tickets.Earn(TicketMilestones.WinNational, 0);
            for (int w = 0; w < GameSession.TicketDeclareWeek + 1; w++) gs.StepWeek();
            Assert.IsNotNull(gs.State.Tickets.Pending, "2 月の最初の週に宣言(おまかせ)");
            Assert.AreEqual(TicketKind.Rookie, gs.State.Tickets.Pending.Kind);
            Assert.AreEqual(1, gs.State.Tickets.InvitesDeclared);
            Assert.AreEqual(2, gs.State.Tickets.Pending.EnrollYear);
            for (int w = GameSession.TicketDeclareWeek + 1; w < Calendar.WeeksPerYear; w++) gs.StepWeek();
            Assert.IsNull(gs.State.Tickets.Pending, "年度末に入学が確定する");
            Runner t = gs.State.Runners.Single(r => r.RecruitSource == RecruitSource.Ticket);
            Assert.AreEqual(1, t.Grade);
            Assert.AreEqual(TicketKind.Rookie, t.TicketKind);
            Assert.AreEqual(Calendar.WeeksPerYear, t.EnrollWeek);
            Assert.That(t.Pref, Is.InRange(1, 47));
            Assert.Greater(t.HighSchool, 0);
            AssertInvariants(gs);
        }

        [Test]
        public void Tickets_SecondDeclaration_IsAnInternationalStudent()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            gs.State.Tickets.Earn(TicketMilestones.WinFall, 0);
            gs.State.Tickets.Earn(TicketMilestones.WinNational, 0);
            gs.PlayYears(2);
            Assert.AreEqual(2, gs.State.Tickets.InvitesDeclared);
            Runner intl = gs.State.Runners.SingleOrDefault(r => r.TicketKind == TicketKind.Intl && r.RecruitSource == RecruitSource.Ticket);
            Assert.IsNotNull(intl);
            Assert.IsTrue(intl.Overseas && intl.Far && intl.Pref == 0);
            Assert.That(intl.Country, Is.InRange(1, 24));
        }

        [Test]
        public void ClearInfo_IsSetOnce_WhenAllThreeRacesAreWon()
        {
            // 3 大会の優勝経験が揃ったら一度だけ記録される(Stats の優勝から導かれる)
            GameSession gs = GameSession.New(GameSessionTests.Seed, Difficulty.Elite);
            gs.PlayYears(30);
            bool all = Enumerable.Range(0, 3).All(i => gs.Stats.Any(x => x.Wins[i]));
            Assert.AreEqual(all, gs.State.Cleared != null);
            if (all)
            {
                int first = gs.Stats.First(x => Enumerable.Range(0, 3).All(i => gs.Stats.Where(y => y.Year <= x.Year).Any(y => y.Wins[i]))).Year;
                Assert.AreEqual(first, gs.State.Cleared.Year);
            }
        }

        // ---------------------------------------------------------------- 世界との整合
        [Test]
        public void World_StaysConsistent_WithAPlayerSchool()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            gs.PlayYears(8);
            foreach (School s in gs.World.Schools)
            {
                if (s.IsPlayer) { Assert.AreEqual(0, s.Roster.Count); continue; }
                Assert.That(s.Roster.Count, Is.InRange(5, WorldK.RosterMax), "学校 " + s.Id);
                Assert.That(s.Prestige, Is.InRange(0.0, 1000.0));
            }
            Assert.AreEqual(1, gs.World.Schools.Count(s => s.IsPlayer));
        }
    }
}
