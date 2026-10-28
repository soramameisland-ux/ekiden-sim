using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>区間記録・大会記録の効果(04 §8.3・§10。05 §10.2。06 race.record)と、SC-26・SC-34 の View(08b G-03・G-04)。</summary>
    public class RecordTests
    {
        [Test]
        public void HallOfFame_AlsoTakesARecordHolder()
        {
            var r = new Runner { SectionWins = 0, Records = 0 };
            Assert.IsFalse(TeamYear.IsHallOfFame(r, 60.0));
            r.Records = 1;
            Assert.IsTrue(TeamYear.IsHallOfFame(r, 60.0), "区間記録を更新した");
            r.Records = 0; r.SectionWins = TeamYear.HallOfFameSectionWins - 1;
            Assert.IsFalse(TeamYear.IsHallOfFame(r, TeamYear.HallOfFamePower - 0.01));
            r.SectionWins = TeamYear.HallOfFameSectionWins;
            Assert.IsTrue(TeamYear.IsHallOfFame(r, 60.0));
            r.SectionWins = 0;
            Assert.IsTrue(TeamYear.IsHallOfFame(r, TeamYear.HallOfFamePower));
        }

        /// <summary>自校の走者が区間記録を出す大会を探して、その場の状態を返す(大会の直後、イベントの選択の前)。</summary>
        private static bool FindOwnRecordRace(out GameSession gs, out RaceOutcome race, out RaceInfo? info, out EventSelection sel)
        {
            gs = null; race = null; info = null; sel = null;
            for (ulong seed = 1; seed <= 24; seed++)
            {
                GameSession g = GameSession.New(seed, seed % 2 == 0 ? Difficulty.Elite : Difficulty.Mid);
                for (int w = 0; w < 48 * 6; w++)
                {
                    if (Array.IndexOf(Calendar.RaceWeeks, g.YearWeek) >= 0)
                    {
                        g.ResolveTraining();
                        RaceInfo? o;
                        RaceOutcome r = g.PlayWeekRaces(out o);
                        EventSelection s = g.SelectWeekEvents(o);
                        if (r != null && o.HasValue && r.Records.Any(u => u.SchoolId == g.School.Id && u.Section >= 0))
                        {
                            gs = g; race = r; info = o; sel = s;
                            return true;
                        }
                        g.ResolveWeekEvents(s, o, new DefaultChoicePolicy());
                        g.FinishWeek();
                        g.AdvanceWeek();
                    }
                    else g.StepWeek();
                }
            }
            return false;
        }

        [Test]
        public void OwnRecord_SetsTheRaceFlag_CountsOnTheRunner_AndFiresTheEvent()
        {
            GameSession gs; RaceOutcome race; RaceInfo? info; EventSelection sel;
            Assert.IsTrue(FindOwnRecordRace(out gs, out race, out info, out sel), "試験の前提: 自校が区間記録を出す大会がある");
            RaceTeam team = race.Teams.First(t => t.Id == gs.School.Id);
            var own = race.Records.Where(u => u.SchoolId == gs.School.Id && u.Section >= 0).ToList();
            var holders = new HashSet<int>();
            foreach (RecordUpdate u in own)
            {
                Assert.AreEqual(team.Lineup[u.Section].RunnerId, u.RunnerId, "記録の保持者は、その区間を走った自校の選手");
                holders.Add(u.RunnerId);
            }
            foreach (Runner r in gs.State.Runners)
            {
                bool inLineup = team.Lineup.Any(x => x.RunnerId == r.Id);
                int mine = own.Count(u => u.RunnerId == r.Id);
                Assert.AreEqual(mine > 0 ? 1 : 0, r.RaceFlags.Record, "race.record は記録を出した選手だけ");
                Assert.IsTrue(mine == 0 || inLineup);
                Assert.GreaterOrEqual(r.Records, mine);
            }
            // 同じ週の EventPhase で『区間記録の更新』が、記録を出した選手の数だけ起きる(出た記録の保持者ごとに 1 回)
            var fired = sel.Fired.Where(f => f.Def.Id == "ev.race.record").Select(f => f.R.Id).ToList();
            CollectionAssert.AreEquivalent(holders.ToList(), fired);
            // 結果の View
            RaceResultView v = gs.RaceResult(race, info);
            Assert.AreEqual(race.Records.Count, v.Records.Count);
            for (int i = 0; i < race.Records.Count; i++)
            {
                RecordUpdate u = race.Records[i];
                RecordUpdateView rv = v.Records[i];
                Assert.AreEqual(u.Section, rv.Section); Assert.AreEqual(u.SchoolId, rv.SchoolId); Assert.AreEqual(u.TimeDs, rv.TimeDs); Assert.AreEqual(u.PrevTimeDs, rv.PrevTimeDs);
                Assert.AreEqual(u.PrevSchoolId, rv.PrevSchoolId); Assert.AreEqual(u.PrevYear, rv.PrevYear); Assert.AreEqual(u.Year, rv.Year);
                Assert.AreEqual("school." + u.SchoolId + ".name", rv.SchoolNameKey);
                Assert.AreEqual(u.SchoolId == gs.School.Id, rv.IsOwn);
                Assert.AreEqual(rv.IsOwn ? u.RunnerId : 0, rv.RunnerId, "自校の走者だけ出す");
                Assert.Less(u.TimeDs, u.PrevTimeDs);
            }
            for (int si = 0; si < v.OwnLegs.Count; si++)
                Assert.AreEqual(own.Any(u => u.Section == si), v.OwnLegs[si].Record);
            // 見どころにも出る(自校の記録は自校の重要度で先に)
            Assert.IsTrue(v.Highlights.Any(h => h.Kind == HighlightKind.CourseRecord && h.IsOwn) || v.Highlights.Count == 5, "上位 5 件に入らなくても、5 件が埋まっている");
        }

        [Test]
        public void RecordHolder_BecomesHallOfFame_WhenGraduating()
        {
            GameSession gs; RaceOutcome race; RaceInfo? info; EventSelection sel;
            Assert.IsTrue(FindOwnRecordRace(out gs, out race, out info, out sel));
            int holder = race.Records.First(u => u.SchoolId == gs.School.Id && u.Section >= 0).RunnerId;
            Runner r = gs.State.FindRunner(holder);
            Assert.GreaterOrEqual(r.Records, 1);
            // 卒業(OB になる)ときの殿堂入りの判定と、OB の career.records
            r.Grade = 4;
            OBRecord ob = OBRecord.FromGraduate(r, gs.Year, 1, CareerPath.Employment, "", TeamYear.IsHallOfFame(r, 50.0));
            Assert.IsTrue(ob.HallOfFame);
            Assert.AreEqual(r.Records, ob.Records);
        }

        [Test]
        public void PeekedRecords_OnRaceDay_EqualTheRecordsOfTheRaceThenPlayed_AndShowInTheLiveHighlights()
        {
            int peeked = 0;
            for (ulong seed = 75; seed <= 96; seed++)
            {
                GameSession gs = GameSession.New(seed, Difficulty.Elite);
                while (gs.YearWeek != Calendar.RaceWeeks[(int)RaceKind.Fall]) gs.StepWeek();
                if (!gs.RaceThisWeek.HasValue) continue;
                gs.ResolveTraining();
                RaceDay rd = gs.OpenRaceDay(RaceKind.Fall);
                int races = gs.World.History.Races.Count;
                List<TeamResult> preview = rd.Preview();
                List<RecordUpdate> peek = gs.World.History.Peek(gs.Year, RaceKind.Fall, preview, null);
                // レース進行: 最後まで進めたときの見どころは、見込みの記録を含めた判定のダイジェスト(区間記録は区間賞の代わりに出る)
                Assert.IsTrue(rd.Start().Ok);
                rd.Complete();
                var expected = RaceHighlights.Digest(RaceHighlights.Detect(rd.Preview(), gs.School.Id, rd.Sections, peek)).Select(h => h.Section + ":" + h.Kind + ":" + h.TeamId).ToList();
                var live = gs.RaceLive(rd).Highlights.Select(h => h.Section + ":" + h.Kind + ":" + h.SchoolId).ToList();
                CollectionAssert.AreEqual(expected, live);
                Assert.AreEqual(races, gs.World.History.Races.Count, "見込みと表示は履歴を書かない");
                RaceInfo? o;
                RaceOutcome race = gs.PlayWeekRaces(out o);
                Assert.AreEqual(races + 1, gs.World.History.Races.Count);
                CollectionAssert.AreEqual(peek.Select(u => u.Section + ":" + u.SchoolId + ":" + u.TimeDs + ":" + u.PrevTimeDs).ToArray(),
                    race.Records.Select(u => u.Section + ":" + u.SchoolId + ":" + u.TimeDs + ":" + u.PrevTimeDs).ToArray());
                peeked += peek.Count;
            }
            Assert.Greater(peeked, 0, "試験の前提: 見込みに記録の更新がある大会がある");
        }

        [Test]
        public void HistoryView_ListsEveryRaceOfTheWorld_AndTheRecordsInTheTable()
        {
            GameSession gs = null;
            HistoryView v = null;
            for (ulong seed = 2026; seed < 2040; seed++)          // 自校が優勝した年度がある世界を探す(自校の印の試験に要る)
            {
                gs = GameSession.New(seed, Difficulty.Elite);
                gs.PlayYears(3);
                v = gs.History();
                if (v.Champions.Any(c => c.IsOwn.Any(x => x))) break;
            }
            Assert.IsTrue(v.Champions.Any(c => c.IsOwn.Any(x => x)), "試験の前提: 自校が優勝した大会がある");
            RaceHistory h = gs.World.History;
            Assert.AreEqual(h.Races.Select(e => e.Year).Distinct().Count(), v.Champions.Count);
            CollectionAssert.AreEqual(v.Champions.Select(c => c.Year).OrderBy(y => y).ToList(), v.Champions.Select(c => c.Year).ToList(), "年度の古い順");
            Assert.IsTrue(v.Champions.Where(c => c.Year <= 0).All(c => c.PreGame));
            Assert.IsTrue(v.Champions.Where(c => c.Year >= 1).All(c => !c.PreGame));
            Assert.AreEqual(18, v.Champions.Count, "予熱 15 年度(−14〜0)+ 開始から 3 年度(1〜3)");
            foreach (ChampionRowView row in v.Champions)
                for (int k = 0; k < 3; k++)
                {
                    RaceEntry e = h.Races.Single(x => x.Year == row.Year && (int)x.Kind == k);
                    Assert.AreEqual(e.WinnerId, row.WinnerId[k]);
                    Assert.AreEqual("school." + e.WinnerId + ".name", row.WinnerNameKey[k]);
                    Assert.AreEqual(e.WinnerId == gs.School.Id, row.IsOwn[k]);
                }
            int expectedCourse = Enumerable.Range(0, 3).Sum(k => h.Course[k] == null ? 0 : h.Course[k].Count(c => c != null));
            Assert.AreEqual(expectedCourse, v.CourseRecords.Count);
            Assert.Greater(v.CourseRecords.Count, 0);
            foreach (CourseRecordView c in v.CourseRecords)
            {
                CourseRecord src = h.Course[(int)c.Kind][c.Section];
                Assert.AreEqual(src.TimeDs, c.TimeDs); Assert.AreEqual(src.Year, c.Year); Assert.AreEqual(src.SchoolId, c.SchoolId);
                Assert.AreEqual(src.SchoolId == gs.School.Id, c.IsOwn);
                if (!c.IsOwn) { Assert.AreEqual(0, c.RunnerId); Assert.AreEqual("", c.RunnerName); }
            }
            Assert.AreEqual(3, v.TeamRecords.Count);
            foreach (TeamRecordView t in v.TeamRecords) { Assert.AreEqual(h.Team[(int)t.Kind].TotalDs, t.TotalDs); Assert.AreEqual(h.Team[(int)t.Kind].Year, t.Year); }
        }
    }
}
