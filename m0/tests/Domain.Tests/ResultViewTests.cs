using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>大会の結果(SC-26)の読み取りの API(08b)。総合・区間賞・自校の区間ごと・評価ポイントが、レースの結果と食い違わない。</summary>
    public class ResultViewTests
    {
        /// <summary>最初に自校が出た大会の結果を取る(週を進めながら)。</summary>
        private static bool FirstOwnRace(GameSession gs, out RaceOutcome race, out RaceInfo? own)
        {
            race = null;
            own = null;
            for (int w = 0; w < 48 * 3; w++)
            {
                int yw = gs.YearWeek;
                bool raceWeek = Array.IndexOf(Ekiden.Domain.Team.Calendar.RaceWeeks, yw) >= 0;
                if (raceWeek)
                {
                    gs.ResolveTraining();
                    RaceInfo? o;
                    RaceOutcome r = gs.PlayWeekRaces(out o);
                    var sel = gs.SelectWeekEvents(o);
                    gs.ResolveWeekEvents(sel, o, new DefaultChoicePolicy());
                    gs.FinishWeek();
                    if (r != null && o.HasValue) { race = r; own = o; return true; }
                    gs.AdvanceWeek();
                }
                else gs.StepWeek();
            }
            return false;
        }

        [Test]
        public void Result_OverallPrizesAndOwnLegs_MatchTheRace()
        {
            GameSession gs = GameSession.New(20261102UL, Difficulty.Elite);
            RaceOutcome race;
            RaceInfo? own;
            Assert.IsTrue(FirstOwnRace(gs, out race, out own), "自校が出る大会がある");
            RaceResultView v = gs.RaceResult(race, own);
            int n = RaceTables.Course(race.Kind).Count;
            Assert.AreEqual(race.Kind, v.Kind);
            Assert.AreEqual(n, v.Sections);
            Assert.AreEqual(race.Results.Count, v.Overall.Count);
            Assert.IsTrue(v.OwnEntered);
            for (int i = 0; i < v.Overall.Count; i++)
            {
                ResultRowView row = v.Overall[i];
                TeamResult t = race.Results[i];
                Assert.AreEqual(i + 1, row.Rank);
                Assert.AreEqual(t.Id, row.SchoolId);
                Assert.AreEqual("school." + t.Id + ".name", row.SchoolNameKey);
                Assert.AreEqual(t.TotalDs, row.TotalDs);
                Assert.AreEqual(t.TotalDs - race.Results[0].TotalDs, row.GapDs, "先頭との差");
                Assert.AreEqual(t.Kuriage, row.Kuriage);
                Assert.AreEqual(t.Id == gs.School.Id, row.IsOwn);
            }
            Assert.AreEqual(1, v.Overall.Count(r => r.IsOwn), "自校はちょうど 1 行");
            Assert.AreEqual(race.Results[0].Id, v.WinnerSchoolId);
            Assert.AreEqual(v.Overall.First(r => r.IsOwn).Rank, v.OwnRank);
            Assert.AreEqual(own.Value.TeamRank, v.OwnRank);
            Assert.AreEqual(own.Value.ExpectedRank, v.ExpectedRank);
            Assert.AreEqual(v.OwnRank <= 10 ? WorldK.YearPoints[v.OwnRank - 1] : 0, v.EvalPoints);
            // 区間賞
            Assert.AreEqual(n, v.Prizes.Count);
            for (int si = 0; si < n; si++)
            {
                int best = race.Results.Min(t => t.Sections[si]);
                Assert.AreEqual(best, v.Prizes[si].Ds, "区間賞のタイム");
                Assert.AreEqual(best, race.Results.First(t => t.Id == v.Prizes[si].SchoolId).Sections[si]);
                Assert.AreEqual(v.Prizes[si].SchoolId == gs.School.Id, v.Prizes[si].IsOwn);
                if (v.Prizes[si].IsOwn) Assert.AreEqual(race.Teams.First(t => t.Id == gs.School.Id).Lineup[si].RunnerId, v.Prizes[si].RunnerId);
                else Assert.AreEqual(0, v.Prizes[si].RunnerId);
            }
            // 自校の区間ごと
            TeamResult mine = race.Results.First(t => t.Id == gs.School.Id);
            RaceTeam team = race.Teams.First(t => t.Id == gs.School.Id);
            Assert.AreEqual(n, v.OwnLegs.Count);
            for (int si = 0; si < n; si++)
            {
                OwnLegView leg = v.OwnLegs[si];
                Runner r = gs.State.FindRunner(team.Lineup[si].RunnerId);
                Assert.AreEqual(r.Id, leg.RunnerId);
                Assert.AreEqual(r.FamilyName, leg.FamilyName);
                Assert.AreEqual(mine.Sections[si], leg.Ds);
                Assert.AreEqual(1 + race.Results.Count(t => t.Sections[si] < mine.Sections[si]), leg.SectionRank);
                Assert.AreEqual(mine.Cum[si], leg.CumDs);
                Assert.AreEqual(1 + race.Results.Count(t => t.Cum[si] < mine.Cum[si]), leg.CumRank);
                Assert.AreEqual(leg.SectionRank == 1, leg.Prize);
                Assert.AreEqual(team.Instr[si], leg.Instruction);
                Assert.AreEqual(mine.Events.Any(e => e.Section == si && e.Kind == "brake"), leg.Brake);
                Assert.AreEqual(mine.Events.Any(e => e.Section == si && e.Kind == "geki_brake"), leg.Geki);
            }
            Assert.AreEqual(v.OwnLegs.Count(l => l.Prize), v.Prizes.Count(p => p.IsOwn), "自校の区間賞の数");
            Assert.AreEqual(v.OwnRank, v.OwnLegs[n - 1].CumRank, "最終区間の累積順位 = 総合順位");
            Assert.AreEqual(v.OwnLegs[n - 1].CumDs, mine.TotalDs);
        }

        /// <summary>数年ぶんの、自校が出た大会の結果を集める。</summary>
        private static List<Tuple<RaceOutcome, RaceInfo?>> OwnRaces(GameSession gs, int years)
        {
            var list = new List<Tuple<RaceOutcome, RaceInfo?>>();
            for (int w = 0; w < 48 * years; w++)
            {
                if (Array.IndexOf(Ekiden.Domain.Team.Calendar.RaceWeeks, gs.YearWeek) >= 0)
                {
                    gs.ResolveTraining();
                    RaceInfo? o;
                    RaceOutcome r = gs.PlayWeekRaces(out o);
                    var sel = gs.SelectWeekEvents(o);
                    gs.ResolveWeekEvents(sel, o, new DefaultChoicePolicy());
                    gs.FinishWeek();
                    if (r != null && o.HasValue) list.Add(Tuple.Create(r, o));
                    gs.AdvanceWeek();
                }
                else gs.StepWeek();
            }
            return list;
        }

        [Test]
        public void Result_LegFlags_FollowTheRaceEvents_AcrossManyRaces()
        {
            bool sawBrake = false, sawGeki = false, sawStall = false, sawPrize = false;
            int races = 0;
            for (ulong seed = 1; seed <= 4; seed++)
            {
                GameSession gs = GameSession.New(seed, Difficulty.Mid);
                foreach (var rr in OwnRaces(gs, 4))
                {
                    RaceResultView v = gs.RaceResult(rr.Item1, rr.Item2);
                    TeamResult mine = rr.Item1.Results.First(t => t.Id == gs.School.Id);
                    foreach (OwnLegView leg in v.OwnLegs)
                    {
                        Assert.AreEqual(mine.Events.Any(e => e.Section == leg.Section && e.Kind == "brake"), leg.Brake);
                        Assert.AreEqual(mine.Events.Any(e => e.Section == leg.Section && e.Kind == "stall"), leg.Stall);
                        Assert.AreEqual(mine.Events.Any(e => e.Section == leg.Section && e.Kind == "geki_brake"), leg.Geki);
                        Assert.AreEqual(mine.Events.Any(e => e.Section == leg.Section && e.Kind == "kuriage"), leg.Kuriage);
                        sawBrake |= leg.Brake; sawStall |= leg.Stall; sawGeki |= leg.Geki; sawPrize |= leg.Prize;
                    }
                    Assert.AreEqual(mine.GekiLeft, RaceTables.Rule(rr.Item1.Kind).Geki - v.OwnLegs.Count(l => l.Geki), "檄の残り = 上限 − 使った回数");
                    races++;
                }
            }
            Assert.Greater(races, 12);
            Assert.IsTrue(sawBrake && sawGeki && sawPrize, "ブレーキ・檄・区間賞のある大会で確かめられた(検査が空振りしない): ブレーキ " + sawBrake + " 檄 " + sawGeki + " 区間賞 " + sawPrize + " 失速 " + sawStall);
        }

        [Test]
        public void Result_WhenOwnSchoolDidNotRun_HasNoOwnParts()
        {
            // 他校だけの結果(出場しない大会)でも落ちず、自校の項目は空
            GameSession gs = GameSession.New(20261102UL, Difficulty.Low);
            RaceOutcome race = null;
            for (int w = 0; w < 48 * 4 && race == null; w++)
            {
                int yw = gs.YearWeek;
                if (Array.IndexOf(Ekiden.Domain.Team.Calendar.RaceWeeks, yw) >= 0 && !gs.PrepareYear().Entrants((RaceKind)Array.IndexOf(Ekiden.Domain.Team.Calendar.RaceWeeks, yw)).Contains(gs.School.Id))
                {
                    gs.ResolveTraining();
                    RaceInfo? o;
                    race = gs.PlayWeekRaces(out o);
                    Assert.IsNull(o);
                    gs.FinishWeek();
                    gs.AdvanceWeek();
                }
                else gs.StepWeek();
            }
            if (race == null) Assert.Inconclusive("自校が出ない大会がなかった");
            RaceResultView v = gs.RaceResult(race, null);
            Assert.IsFalse(v.OwnEntered);
            Assert.AreEqual(0, v.OwnRank);
            Assert.AreEqual(0, v.EvalPoints);
            Assert.IsEmpty(v.OwnLegs);
            Assert.IsFalse(v.Overall.Any(r => r.IsOwn));
            Assert.AreEqual(RaceTables.Course(race.Kind).Count, v.Prizes.Count);
        }
    }
}
