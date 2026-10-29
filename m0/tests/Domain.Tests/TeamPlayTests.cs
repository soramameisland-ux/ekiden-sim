using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Team;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.Training;
using Ekiden.Domain.World;
using NUnit.Framework;
using Calendar = Ekiden.Domain.Team.Calendar;

namespace Ekiden.Domain.Tests
{
    /// <summary>自校(実体の選手)の大会の側(<see cref="TeamRacing"/>)・週の処理(<see cref="TeamWeek"/>)・年度末(<see cref="TeamYear"/>)の規則(04 §4・§10、03 §5・§7、05 §4・§10)。</summary>
    public class TeamPlayTests
    {
        private const ulong Seed = 20260301UL;

        private static GameSession NewSession(Difficulty d = Difficulty.Mid, ulong seed = Seed) { return GameSession.New(seed, d); }

        private static TeamState Team(Difficulty d = Difficulty.Mid, ulong seed = Seed) { return NewSession(d, seed).State; }

        // ---------------------------------------------------------------- TeamRacing: 登録
        [Test]
        public void AutoRegister_FillsTheEntry_WithoutInjuredOrBenchedRunners()
        {
            TeamState s = Team();
            s.Runners.OrderBy(r => r.Id).First().InjurySeverity = InjurySeverity.Moderate;
            s.Bench.Add(s.Runners.OrderBy(r => r.Id).Skip(1).First().Id);
            foreach (RaceKind kind in WorldSim.AllRaces)
            {
                List<int> entry = TeamRacing.AutoRegister(s, kind);
                Assert.AreEqual(RaceTables.Rule(kind).Entry, entry.Count, kind.ToString());
                Assert.AreEqual(entry.Count, entry.Distinct().Count());
                foreach (int id in entry) Assert.IsTrue(TeamRacing.CanRun(s, s.FindRunner(id)), "出られない選手が登録された");
            }
        }

        [Test]
        public void AutoRegister_LimitsInternationalStudents()
        {
            TeamState s = Team();
            foreach (Runner r in s.Runners.OrderByDescending(x => TeamState.LongP(x)).Take(12)) r.Overseas = true;          // 上位 12 人が留学生
            foreach (RaceKind kind in WorldSim.AllRaces)
            {
                List<int> entry = TeamRacing.AutoRegister(s, kind);
                int intl = entry.Count(id => s.FindRunner(id).Overseas);
                Assert.LessOrEqual(intl, RaceTables.Rule(kind).IntlReg, kind.ToString());
                Assert.AreEqual(RaceTables.Rule(kind).Entry, entry.Count, kind.ToString());
            }
        }

        [Test]
        public void AutoRegister_ReturnsEveryAvailableRunner_WhenTooFew()
        {
            TeamState s = Team();
            int run = RaceTables.Rule(RaceKind.NewYear).Run;
            foreach (Runner r in s.Runners.OrderBy(x => x.Id).Skip(run - 1)) r.InjurySeverity = InjurySeverity.Severe;
            List<int> entry = TeamRacing.AutoRegister(s, RaceKind.NewYear);
            Assert.AreEqual(run - 1, entry.Count);
        }

        // ---------------------------------------------------------------- TeamRacing: 当日の準備
        [Test]
        public void PrepareRace_ReplacesInjuredRegisteredRunner_AndIsDeterministic()
        {
            GameSession gs = NewSession();
            TeamState s = gs.State;
            var rngs = new KeyedRngFactory(Seed);
            RaceKind kind = RaceKind.National;
            RaceRule rule = RaceTables.Rule(kind);
            s.Entries[(int)kind].AddRange(TeamRacing.AutoRegister(s, kind));
            int hurt = s.Entries[(int)kind][0];
            s.FindRunner(hurt).InjurySeverity = InjurySeverity.Moderate;
            Env[] envs = RaceMath.GenEnv(rngs.Stream(RngCtr.Race, 1, 9999), kind);
            RaceTeam a = TeamRacing.PrepareRace(s, gs.School.Id, kind, 1, envs, rngs);
            RaceTeam b = TeamRacing.PrepareRace(s, gs.School.Id, kind, 1, envs, rngs);
            Assert.IsNotNull(a);
            Assert.AreEqual(rule.Run, a.Lineup.Length);
            Assert.IsFalse(a.Lineup.Any(x => x.RunnerId == hurt), "故障した選手が出走した");
            CollectionAssert.AreEqual(a.Lineup.Select(x => x.RunnerId), b.Lineup.Select(x => x.RunnerId));
            Assert.AreEqual(gs.School.Id, a.Id);
        }

        [Test]
        public void PrepareRace_ReturnsNull_WhenTooFewCanRun()
        {
            GameSession gs = NewSession();
            TeamState s = gs.State;
            var rngs = new KeyedRngFactory(Seed);
            RaceRule rule = RaceTables.Rule(RaceKind.Fall);
            foreach (Runner r in s.Runners.OrderBy(x => x.Id).Skip(rule.Run - 1)) r.InjurySeverity = InjurySeverity.Moderate;
            Env[] envs = RaceMath.GenEnv(rngs.Stream(RngCtr.Race, 1, 9999), RaceKind.Fall);
            Assert.IsNull(TeamRacing.PrepareRace(s, gs.School.Id, RaceKind.Fall, 1, envs, rngs));
        }

        [Test]
        public void PrepareRace_ThirdRace_AddsForcedRunners()
        {
            GameSession gs = NewSession();
            TeamState s = gs.State;
            var rngs = new KeyedRngFactory(Seed);
            RaceKind kind = RaceKind.NewYear;
            s.Entries[(int)kind].AddRange(TeamRacing.AutoRegister(s, kind));
            Runner weakest = s.Runners.Where(r => !s.Entries[(int)kind].Contains(r.Id)).OrderBy(r => TeamState.LongP(r)).First();
            s.Forced.Add(weakest.Id);
            Env[] envs = RaceMath.GenEnv(rngs.Stream(RngCtr.Race, 1, 9999), kind);
            RaceTeam t = TeamRacing.PrepareRace(s, gs.School.Id, kind, 1, envs, rngs);
            Assert.IsNotNull(t);
            TeamRacing.CloseRace(s, kind);
            Assert.AreEqual(0, s.Entries[(int)kind].Count);
            Assert.AreEqual(0, s.Forced.Count, "第三戦のあと、登録に追加した指定は消える");
            Assert.AreEqual(0, s.Bench.Count);
        }

        // ---------------------------------------------------------------- TeamRacing: 大会後の処理(04 §10)
        [Test]
        public void ApplyResult_UpdatesLineupRunnersOnly_AccordingToSpec()
        {
            GameSession gs = NewSession();
            TeamState s = gs.State;
            s.Refresh();
            YearState ys = gs.World.BeginYear(1, gs);
            Assert.That(ys.FallIds, Contains.Item(gs.School.Id), "前提: 自校は第一戦に出る");
            RaceKind kind = RaceKind.Fall;
            s.Entries[(int)kind].AddRange(TeamRacing.AutoRegister(s, kind));
            var before = s.Runners.ToDictionary(r => r.Id, r => r.Clone());
            RaceOutcome o = gs.World.PlayRace(ys, kind);
            RaceInfo? info = TeamRacing.ApplyResult(s, o, gs.School.Id);
            Assert.IsTrue(info.HasValue);
            int idx = o.Results.FindIndex(t => t.Id == gs.School.Id);
            int rank = idx + 1;
            Assert.AreEqual(rank, info.Value.TeamRank);
            Assert.AreEqual(1, info.Value.Kind);
            Assert.That(info.Value.ExpectedRank, Is.InRange(1, o.Teams.Count));
            Assert.AreEqual(rank, s.Rank[0]);
            Assert.AreEqual(rank <= 10 ? WorldK.YearPoints[rank - 1] : 0, s.YearPoints);

            RaceTeam team = o.Teams.First(t => t.Id == gs.School.Id);
            TeamResult tr = o.Results[idx];
            IReadOnlyList<SectionDef> course = RaceTables.Course(kind);
            var inLineup = new HashSet<int>(team.Lineup.Select(x => x.RunnerId));
            for (int si = 0; si < team.Lineup.Length; si++)
            {
                Runner r = s.FindRunner(team.Lineup[si].RunnerId);
                Runner b = before[r.Id];
                Assert.AreEqual(b.Races + 1, r.Races);
                Assert.AreEqual(Math.Min(100, b.Fatigue + Rounding.HalfUp(course[si].Distance * 1.6)), r.Fatigue, "疲労 = 区間距離 × 1.6");
                int secRank = 1 + o.Results.Count(t => t.Sections[si] < tr.Sections[si]);
                Assert.AreEqual(secRank, r.RaceFlags.SectionRank);
                Assert.AreEqual(secRank == 1 ? b.SectionWins + 1 : b.SectionWins, r.SectionWins);
                Assert.AreEqual(rank == 1 ? b.RelayWins + 1 : b.RelayWins, r.RelayWins);
                Assert.AreEqual(1, r.RaceFlags.InLineup);
                Assert.That(r.Motivation, Is.InRange(1, 5));
                Assert.That(Math.Abs(r.Motivation - b.Motivation), Is.LessThanOrEqualTo(3));
                Assert.GreaterOrEqual(r.Xp.Sum(), b.Xp.Sum(), "MEN の経験値");
            }
            foreach (Runner r in s.Runners.Where(x => !inLineup.Contains(x.Id)))
            {
                Runner b = before[r.Id];
                Assert.AreEqual(b.Races, r.Races);
                Assert.AreEqual(b.Fatigue, r.Fatigue);
                Assert.AreEqual(b.Motivation, r.Motivation);
                Assert.AreEqual(0, r.RaceFlags.InLineup);
            }
        }

        [Test]
        public void ApplyResult_ReturnsNull_WhenTheTeamDidNotRun()
        {
            GameSession gs = NewSession();
            TeamState s = gs.State;
            YearState ys = gs.World.BeginYear(1, gs);
            foreach (Runner r in s.Runners) r.InjurySeverity = InjurySeverity.Moderate;                     // 全員故障 → 棄権
            RaceOutcome o = gs.World.PlayRace(ys, RaceKind.Fall);
            Assert.IsFalse(o.Teams.Any(t => t.Id == gs.School.Id));
            int before = s.YearPoints;
            Assert.IsNull(TeamRacing.ApplyResult(s, o, gs.School.Id));
            Assert.AreEqual(before, s.YearPoints);
            Assert.AreEqual(99, s.Rank[0]);
        }

        // ---------------------------------------------------------------- TeamWeek: メニューの優先順位・ピーキング
        [Test]
        public void TaperIndex_StandardShortNone_AndOverlap()
        {
            TeamState s = Team();
            s.InQualifier = _ => false;                                    // 予選会の登録メンバーかどうかは世界(シードかどうか)で変わる。ここは第一戦だけを見る
            Runner r = s.Runners[0];
            s.Entries[0].Add(r.Id);
            int[] std = Enumerable.Range(20, 8).Select(yw => TeamWeek.TaperIndex(s, r, yw)).ToArray();                 // yw 20..27
            CollectionAssert.AreEqual(new[] { -1, -1, 0, 1, 2, 3, -1, -1 }, std, "Standard: 大会週(25)の 3 週前から TAPER[1..4]");
            s.Plan.Peaking[0] = PeakingMode.Short;
            CollectionAssert.AreEqual(new[] { -1, -1, -1, -1, 2, 3, -1, -1 }, Enumerable.Range(20, 8).Select(yw => TeamWeek.TaperIndex(s, r, yw)).ToArray(), "Short: 大会週とその前の週だけ");
            s.Plan.Peaking[0] = PeakingMode.None;
            Assert.IsTrue(Enumerable.Range(0, 48).All(yw => TeamWeek.TaperIndex(s, r, yw) == -1));
            s.Plan.Peaking[0] = PeakingMode.Standard;
            s.Entries[1].Add(r.Id);                                                                                    // 第二戦(28)も登録: 前の大会(25)と重ならない 26 から詰める
            CollectionAssert.AreEqual(new[] { 3, 1, 2, 3 }, new[] { 25, 26, 27, 28 }.Select(yw => TeamWeek.TaperIndex(s, r, yw)).ToArray());
            Assert.AreEqual(-1, TeamWeek.TaperIndex(s, s.Runners[1], 23), "登録されていない選手には適用しない");
        }

        [Test]
        public void MenuFor_Priority_Injured_Individual_Taper_Monthly()
        {
            GameSession gs = NewSession();
            TeamState s = gs.State;
            var week = new TeamWeek(TrainingMaster.CreateDefault());
            Runner r = s.Runners[0];
            s.Refresh();
            s.AbsWeek = 23;                                                                                           // yw 23 = 第一戦の 2 週前
            bool camp, ind;
            string theme = s.MonthlyPlan[r.Group];
            Menu monthly = week.MenuFor(s, r, out camp, out ind);
            Assert.IsFalse(ind);
            s.Entries[0].Add(r.Id);
            Menu taper = week.MenuFor(s, r, out camp, out ind);
            Assert.AreNotEqual(monthly.Key1 + monthly.Key2, taper.Key1 + taper.Key2, "ピーキングが月間計画に優先する");
            s.IndividualMenus[r.Id] = new IndividualMenu { UntilAbsWeek = s.AbsWeek + 2, Menu = new MenuSpec("ks.sharpen", "ks.pace_run", "mil.low") };
            Menu indiv = week.MenuFor(s, r, out camp, out ind);
            Assert.IsTrue(ind);
            Assert.AreEqual("ks.sharpen", indiv.Key1);
            Assert.AreEqual("th.base_build", theme);
        }

        // ---------------------------------------------------------------- TeamWeek: 週の最初
        [Test]
        public void BeginWeek_ScoutPointsGuidanceAndRegistration()
        {
            GameSession gs = NewSession();
            TeamState s = gs.State;
            var week = new TeamWeek(TrainingMaster.CreateDefault());
            s.CoachRank = 6;
            s.GuidanceLeft = 0;
            s.Talked.Add(5);
            s.AbsWeek = 8;                                                                                            // 6 月の最初の週
            week.BeginWeek(s);
            Assert.AreEqual(4 + 4 * 2, s.ScoutPts, "6 月: 月 4 ポイント + ランクによる追加分 4 × floor(6 / 3)");
            Assert.AreEqual(4, s.GuidanceLeft);
            Assert.AreEqual(0, s.Talked.Count);
            s.AbsWeek = 12;                                                                                           // 7 月: 月 4 ポイントだけ
            week.BeginWeek(s);
            Assert.AreEqual(16, s.ScoutPts);
            s.AbsWeek = 9;                                                                                            // 月の途中では増えない
            week.BeginWeek(s);
            Assert.AreEqual(16, s.ScoutPts);
            s.AbsWeek = 21;
            week.BeginWeek(s);
            Assert.AreEqual(RaceTables.Rule(RaceKind.Fall).Entry, s.Entries[0].Count, "エントリー締切(大会の 4 週前)で登録される");
            s.AbsWeek = 44 + Calendar.WeeksPerYear;                                                                    // 3 月: ポイントなし(6〜2 月だけ)
            double pts = s.ScoutPts;
            week.BeginWeek(s);
            Assert.AreEqual(pts, s.ScoutPts);
        }

        [Test]
        public void BeginWeek_FirstWeekOfYear_RollsRanks()
        {
            TeamState s = Team();
            var week = new TeamWeek(TrainingMaster.CreateDefault());
            s.Rank[0] = 3; s.Rank[1] = 99; s.Rank[2] = 7;
            s.YearPoints = 12;
            s.AbsWeek = Calendar.WeeksPerYear;
            week.BeginWeek(s);
            CollectionAssert.AreEqual(new[] { 3, 8, 7 }, s.LastRank);
            CollectionAssert.AreEqual(new[] { 99, 99, 99 }, s.Rank);
            Assert.AreEqual(0, s.YearPoints);
        }

        // ---------------------------------------------------------------- TeamWeek: 練習と絆
        [Test]
        public void TrainAll_RaisesBondsOfHealthyPairsInTheSameGroup()
        {
            GameSession gs = NewSession();
            TeamState s = gs.State;
            var week = new TeamWeek(TrainingMaster.CreateDefault());
            s.Refresh();
            var before = new Dictionary<long, int>();
            foreach (int[] b in s.Bonds.Items()) before[BondTable.Key(b[0], b[1])] = b[2];
            Runner a = s.Runners.First(r => r.Group == 0), c = s.Runners.Where(r => r.Group == 0 && r.Id != a.Id).First();
            Runner far = s.Runners.First(r => r.Group == 2);
            c.InjurySeverity = InjurySeverity.Minor; c.InjuryWeeksLeft = 5;
            Runner d = s.Runners.Where(r => r.Group == 0 && r.Id != a.Id && r.Id != c.Id).First();
            int ad = s.Bond(a, d), af = s.Bond(a, far), ac = s.Bond(a, c);
            int dBase = 10 + (a.Pref != 0 && a.Pref == d.Pref ? 1 : 0) + (a.HighSchool != 0 && a.HighSchool == d.HighSchool ? 1 : 0);
            week.TrainAll(s, new KeyedRngFactory(Seed));
            Assert.AreEqual(Math.Min(1000, ad + dBase), s.Bond(a, d), "同じグループ・どちらも健康: +10(同じ県 +1、同じ高校 +1)");
            Assert.AreEqual(af, s.Bond(a, far), "別グループは週では増えない");
            Assert.AreEqual(ac, s.Bond(a, c), "故障中の組は増えない");
        }

        // ---------------------------------------------------------------- TeamWeek: 月次
        [Test]
        public void ResultBonus_Cases()
        {
            TeamState s = Team();
            Array.Copy(new[] { 99, 99, 99 }, s.Rank, 3);
            Assert.AreEqual(0, TeamWeek.ResultBonus(s), "大会なし");
            s.Rank[0] = 1; Assert.AreEqual(10, TeamWeek.ResultBonus(s));
            s.Rank[0] = 3; Assert.AreEqual(5, TeamWeek.ResultBonus(s));
            s.Rank[0] = 4; Array.Copy(new[] { 3, 8, 8 }, s.LastRank, 3); Assert.AreEqual(-5, TeamWeek.ResultBonus(s), "出た全大会で前年度より下がった");
            s.Rank[1] = 2; Assert.AreEqual(5, TeamWeek.ResultBonus(s), "2 位がある");
            Array.Copy(new[] { 2, 8, 8 }, s.LastRank, 3); s.Rank[1] = 99; s.Rank[0] = 8; Assert.AreEqual(-5, TeamWeek.ResultBonus(s));
            Array.Copy(new[] { 9, 8, 8 }, s.LastRank, 3); Assert.AreEqual(0, TeamWeek.ResultBonus(s), "下がっていなければ 0");
        }

        [Test]
        public void MonthEnd_MoraleBondsAndMotivationRules()
        {
            GameSession gs = NewSession();
            TeamState s = gs.State;
            var week = new TeamWeek(TrainingMaster.CreateDefault());
            s.Refresh();
            foreach (Runner r in s.Runners) { r.Motivation = 3; r.MonthGain = 0; r.OverloadPending = false; }
            Array.Copy(new[] { 99, 99, 99 }, s.Rank, 3);
            Runner a = s.Runners.First(r => r.Group == 0), x = s.Runners.First(r => r.Group == 2);
            s.Bonds.Set(a.Id, x.Id, 100);
            s.AbsWeek = 3;                                                                                            // 4 月の第 4 週
            week.MonthEnd(s);
            Assert.AreEqual(50.0, s.Morale, "平均やる気 3 → round(25 × 2) = 50(結果加点 0)");
            Assert.AreEqual(90, s.Bonds.Get(a.Id, x.Id), "異なるグループの絆は −10");
            Assert.IsTrue(s.Runners.All(r => r.Motivation == 3));

            s.Rank[0] = 1;                                                                                            // 優勝 → +10 → 士気 60
            Runner low = s.Runners[1], gain = s.Runners[2], over = s.Runners[3];
            low.Motivation = 1; gain.Motivation = 3; gain.MonthGain = 2; over.OverloadPending = true;
            week.MonthEnd(s);
            Assert.AreEqual(over.Motivation, 2, "M1: 疲労の高止まり → やる気 −1");
            Assert.AreEqual(4, gain.Motivation, "M6: 月に 2 回以上の能力アップ → やる気 +1");
            Assert.AreEqual(0, gain.MonthGain);

            foreach (Runner r in s.Runners) r.Motivation = 5;                                                         // 士気が高く、やる気 2 以下の選手は +1(M4)
            low.Motivation = 2;
            week.MonthEnd(s);
            Assert.GreaterOrEqual(s.Morale, 70);
            Assert.AreEqual(3, low.Motivation, "M4: 士気 70 以上 → やる気 ≤ 2 の選手は +1");

            foreach (Runner r in s.Runners) r.Motivation = 1;                                                         // 士気が低く、やる気 4 以上の選手は −1(M5)
            low.Motivation = 4;
            Array.Copy(new[] { 99, 99, 99 }, s.Rank, 3);
            week.MonthEnd(s);
            Assert.LessOrEqual(s.Morale, 30);
            Assert.AreEqual(3, low.Motivation, "M5: 士気 30 以下 → やる気 ≥ 4 の選手は −1");
        }

        [Test]
        public void MonthEnd_AugustCampCounters()
        {
            GameSession gs = NewSession();
            TeamState s = gs.State;
            var week = new TeamWeek(TrainingMaster.CreateDefault());
            foreach (Runner r in s.Runners) r.CampMaxFatigue = 0;
            s.Runners[0].CampMaxFatigue = 90;
            s.Runners[1].CampMaxFatigue = 60;
            s.AbsWeek = 4 * 4 + 3;                                                                                    // 8 月(month 4)の第 4 週
            week.MonthEnd(s);
            Assert.AreEqual(1, s.Runners[0].Ctr(Counters.Camps));
            Assert.AreEqual(1, s.Runners[0].Ctr(Counters.CampOverload));
            Assert.AreEqual(1, s.Runners[1].Ctr(Counters.Camps));
            Assert.AreEqual(0, s.Runners[1].Ctr(Counters.CampOverload));
            Assert.AreEqual(0, s.Runners[0].CampMaxFatigue);
        }

        // ---------------------------------------------------------------- TeamYear
        [Test]
        public void CareerPath_Probabilities()
        {
            Assert.AreEqual(0.0, TeamYear.CorpProbability(55));
            Assert.AreEqual(0.0, TeamYear.CorpProbability(62));
            Assert.AreEqual(0.5, TeamYear.CorpProbability(69), 1e-12);
            Assert.AreEqual(0.9, TeamYear.CorpProbability(90));
            Assert.AreEqual(CareerPath.Corporate, TeamYear.ChoosePath(69, 0.49, 0.99));
            Assert.AreEqual(CareerPath.Teacher, TeamYear.ChoosePath(69, 0.5, 0.24));
            Assert.AreEqual(CareerPath.Employment, TeamYear.ChoosePath(69, 0.5, 0.25));
            Assert.AreEqual(CareerPath.Employment, TeamYear.ChoosePath(69, 0.5, 0.79));
            Assert.AreEqual(CareerPath.GradSchool, TeamYear.ChoosePath(69, 0.5, 0.80));
            Assert.AreEqual(CareerPath.Teacher, TeamYear.ChoosePath(50, 0.0, 0.0), "力が低ければ実業団には進まない");
        }

        [Test]
        public void HallOfFame_ByPowerOrSectionWins()
        {
            var r = new Runner();
            Assert.IsFalse(TeamYear.IsHallOfFame(r, 84.9));
            Assert.IsTrue(TeamYear.IsHallOfFame(r, 85.0));
            r.SectionWins = 3;
            Assert.IsTrue(TeamYear.IsHallOfFame(r, 60));
        }

        [Test]
        public void GraduateAndQuit_MakesObs_AdvancesGrades_AndCleansReferences()
        {
            TeamState s = Team();
            var rngs = new KeyedRngFactory(Seed);
            int n4 = s.Runners.Count(r => r.Grade == 4);
            var g4 = s.Runners.Where(r => r.Grade == 4).Select(r => r.Id).ToList();
            Runner leaver = s.Runners.First(r => r.Grade == 4);
            s.IndividualMenus[leaver.Id] = new IndividualMenu { UntilAbsWeek = 99, Menu = new MenuSpec("ks.sharpen", "ks.pace_run", "mil.low") };
            s.Talked.Add(leaver.Id); s.Bench.Add(leaver.Id); s.Forced.Add(leaver.Id); s.Entries[0].Add(leaver.Id);
            foreach (Runner r in s.Runners) r.Motivation = 3;
            var gradesBefore = s.Runners.Where(r => r.Grade < 4).ToDictionary(r => r.Id, r => r.Grade);
            YearTurnover t = TeamYear.GraduateAndQuit(s, 1, rngs);
            Assert.AreEqual(n4, t.Graduates.Count);
            Assert.AreEqual(n4, s.Obs.Count);
            CollectionAssert.AreEquivalent(g4, t.Graduates.Select(o => o.RunnerId));
            Assert.AreEqual(0, t.Quit.Count);
            foreach (Runner r in s.Runners) Assert.AreEqual(gradesBefore[r.Id] + 1, r.Grade);
            Assert.IsFalse(s.IndividualMenus.ContainsKey(leaver.Id));
            Assert.IsFalse(s.Talked.Contains(leaver.Id) || s.Bench.Contains(leaver.Id) || s.Forced.Contains(leaver.Id) || s.Entries[0].Contains(leaver.Id));
            var ids = new HashSet<int>(s.Runners.Select(r => r.Id));
            foreach (int[] b in s.Bonds.Items()) Assert.IsTrue(ids.Contains(b[0]) && ids.Contains(b[1]), "卒業生の絆が残っている");
            foreach (OBRecord ob in t.Graduates)
            {
                Assert.AreEqual(1, ob.GradYear);
                Assert.AreEqual(ob.GradYear - 3, ob.EnrollYear);
                Assert.AreEqual(TeamState.LongP(ob.FinalStats), ob.Power, 1e-9);
            }
            // 同じ入力 → 同じ結果(乱数は鍵つき)
            TeamState s2 = Team();
            foreach (Runner r in s2.Runners) r.Motivation = 3;
            YearTurnover t2 = TeamYear.GraduateAndQuit(s2, 1, new KeyedRngFactory(Seed));
            CollectionAssert.AreEqual(t.Graduates.Select(o => (int)o.CareerPath), t2.Graduates.Select(o => (int)o.CareerPath));
        }

        [Test]
        public void GraduateAndQuit_QuitOnlyAtMotivationOne_NeverBelowFive()
        {
            TeamState s = Team();
            foreach (Runner r in s.Runners) { r.Grade = 1; r.Motivation = 3; }
            var rngs = new KeyedRngFactory(Seed);
            // やる気 1 でない選手は退部しない
            YearTurnover none = TeamYear.GraduateAndQuit(s, 5, rngs);
            Assert.AreEqual(0, none.Quit.Count);
            // 全員やる気 1 → 平均 40% が退部(多くの年度で数える)。ただし 5 人は残る
            int quits = 0, trials = 0;
            for (int y = 1; y <= 60; y++)
            {
                TeamState t = Team();
                foreach (Runner r in t.Runners) { r.Grade = 1; r.Motivation = 1; }
                int n = t.Runners.Count;
                YearTurnover q = TeamYear.GraduateAndQuit(t, y, new KeyedRngFactory(Seed));
                quits += q.Quit.Count; trials += n;
                Assert.GreaterOrEqual(t.Runners.Count, TeamYear.MinRunners);
                Assert.AreEqual(n - q.Quit.Count, t.Runners.Count);
            }
            double rate = quits / (double)trials;
            Assert.That(rate, Is.InRange(0.30, 0.45), "退部率 " + rate);
            // 5 人のとき、全員やる気 1 でも誰も辞めない
            TeamState five = Team();
            five.Runners.RemoveRange(5, five.Runners.Count - 5);
            foreach (Runner r in five.Runners) { r.Grade = 1; r.Motivation = 1; }
            Assert.AreEqual(0, TeamYear.GraduateAndQuit(five, 3, rngs).Quit.Count);
            Assert.AreEqual(5, five.Runners.Count);
        }

        [Test]
        public void Settle_CashFlow_WhenNoUpgradeIsPossible()
        {
            TeamState s = Team();
            int cap = WorldMath.FacilityCap(1);
            s.Fac = new FacilityLevels { Coach = cap, Trainer = cap, Nutri = cap, Dorm = cap, Track = cap, Hill = cap, Gym = cap, Medical = cap, Scout = cap };
            s.Funds = 1500;
            FacilityLevels f = s.Fac;
            double expected = Q6.Round(1500 + (WorldMath.Income(400.0, 20) - WorldMath.StaffExpense(f.Coach, f.Trainer, f.Nutri, f.Scout) - WorldMath.FacilityUpkeep(f.Dorm, f.Track, f.Hill, f.Gym, f.Medical) - WorldMath.RunnerExpense(s.Runners.Count)));
            TeamYear.Settle(s, 400.0, 20, 1);
            Assert.AreEqual(Rounding.HalfUp(expected), s.Funds);
            Assert.AreEqual(f.Dorm, s.Fac.Dorm, "上限なら強化しない");
            // funds_year の補正(期間中の年度末に 1 回)
            TeamState s2 = Team();
            s2.Fac = f; s2.Funds = 1500;
            s2.Mods.Add(new TimedModifier { Kind = ModKind.FundsYear, To = TimedModifier.TeamTarget, Value = 200, Until = s2.AbsWeek + 10, Src = "x" });
            TeamYear.Settle(s2, 400.0, 20, 1);
            Assert.AreEqual(Rounding.HalfUp(expected + 200), s2.Funds);
        }

        [Test]
        public void Settle_UpgradesFacilitiesInOrder_AndKeepsTheReserve()
        {
            TeamState s = Team(Difficulty.Low);
            s.Funds = 5000;
            FacilityLevels f0 = s.Fac;
            TeamYear.Settle(s, 300.0, 5, 1);
            Assert.GreaterOrEqual(s.Fac.Dorm, f0.Dorm);
            Assert.GreaterOrEqual(s.Funds, WorldK.Reserve - 1e-9, "予備費を残す");
            Assert.LessOrEqual(s.Fac.Dorm, WorldMath.FacilityCap(1));
            Assert.Greater(s.Fac.Dorm + s.Fac.Scout + s.Fac.Coach, f0.Dorm + f0.Scout + f0.Coach, "寮・スカウト・コーチから上がる");
        }

        private static class RngCtr { public const int Race = RngRegistry.LabelRace; }
    }
}
