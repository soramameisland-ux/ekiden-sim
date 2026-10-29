using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;
using Ekiden.Domain.World;
using NUnit.Framework;
using Calendar = Ekiden.Domain.Team.Calendar;

namespace Ekiden.Domain.Tests
{
    /// <summary>予選会の前後の育成(04 §3.2・03 §5.3。L-53): 登録メンバーは予選会の週に調整(TAPER[4])・走った 12 名は大会と同じ疲労(20km × 1.6 = 32)。シード校には関係しない。</summary>
    public class QualifierTrainingTests
    {
        private const ulong Seed = 20260601UL;

        private static GameSession AtWeek(ulong seed, Difficulty d, int yearWeek)
        {
            GameSession gs = GameSession.New(seed, d);
            while (gs.YearWeek != yearWeek || gs.Year != 1) gs.StepWeek();
            return gs;
        }

        /// <summary>1 年度目の予選会の週(26)の初めで、自校がシード外のゲーム(Low 開始から探す)。</summary>
        private static GameSession NonSeedAtQualifierWeek()
        {
            for (ulong seed = Seed; seed < Seed + 12; seed++)
            {
                GameSession gs = AtWeek(seed, Difficulty.Low, Calendar.QualifierWeek);
                if (!gs.PrepareYear().Seeds.Contains(gs.School.Id)) return gs;
            }
            Assert.Fail("試験の前提: シード外の年度がある");
            return null;
        }

        private static void Heal(GameSession gs) { foreach (Runner r in gs.State.Runners) { r.InjurySeverity = InjurySeverity.None; r.InjuryWeeksLeft = 0; } }

        [Test]
        public void TheRosterRunnersTaperInTheQualifierWeek_AndTheOthersDoNot()
        {
            GameSession gs = NonSeedAtQualifierWeek();
            Heal(gs);
            TeamState s = gs.State;
            // 手動の登録: 12 名
            List<int> ids = s.Runners.OrderBy(r => r.Id).Take(RaceConstants.QualEntry).Select(r => r.Id).ToList();
            Assert.IsTrue(gs.SetQualifierEntries(ids).Ok);
            foreach (Runner r in s.Runners)
                Assert.AreEqual(ids.Contains(r.Id), TeamWeek.TaperIndex(s, r, Calendar.QualifierWeek) == 3, "登録メンバーだけが TAPER[4](添字 3)。登録メンバーでなくても第二戦の登録なら、その調整(第 26 週は T2)は変わらない 選手 " + r.Id);
            Assert.AreEqual(-1, TeamWeek.TaperIndex(s, s.FindRunner(ids[0]), Calendar.QualifierWeek - 1), "予選会の前の週は調整しない(Short は予選会の週だけ。第一戦の週が直前)");
            // 方針を『適用しない』にすると、だれも調整しない
            Assert.IsTrue(gs.SetAnnualPlan(s.Plan.Focus, s.Plan.TargetRace, new[] { PeakingMode.Standard, PeakingMode.Standard, PeakingMode.Standard, PeakingMode.None, PeakingMode.None }).Ok);
            foreach (Runner r in s.Runners) Assert.AreNotEqual(3, TeamWeek.TaperIndex(s, r, Calendar.QualifierWeek), "予選会のピーキング = なし");
        }

        [Test]
        public void WithoutAnEntry_TheTwelveBestForTheDistanceAreTheRoster_AnInjuredOneIsLeftOut()
        {
            GameSession gs = NonSeedAtQualifierWeek();
            Heal(gs);
            TeamState s = gs.State;
            Assert.IsNull(gs.QualifierEntries);
            List<Runner> best = s.Runners.OrderByDescending(r => RaceTables.Rating(RaceRunner.From(r).Stats, SectionType.Long)).ThenBy(r => r.Id).ToList();
            Func<Runner, bool> tapered = r => TeamWeek.TaperIndex(s, r, Calendar.QualifierWeek) == 3;
            Assert.AreEqual(RaceConstants.QualEntry, s.Runners.Count(tapered));
            foreach (Runner r in best.Take(RaceConstants.QualEntry)) Assert.IsTrue(tapered(r), "上位 12 名");
            Runner hurt = best[0];
            hurt.InjurySeverity = InjurySeverity.Moderate; hurt.InjuryWeeksLeft = 6;
            Assert.IsFalse(tapered(hurt), "出られない選手は対象でない");
            Assert.IsTrue(tapered(best[RaceConstants.QualEntry]), "繰り上がって 13 番目の選手が入る");
        }

        [Test]
        public void ASeedTeamDoesNotTaperForTheQualifier()
        {
            GameSession gs = null;
            for (ulong seed = Seed; seed < Seed + 12 && gs == null; seed++)
            {
                GameSession t = AtWeek(seed, Difficulty.Elite, Calendar.QualifierWeek);
                if (t.PrepareYear().Seeds.Contains(t.School.Id)) gs = t;
            }
            Assert.IsNotNull(gs, "試験の前提: シード校の年度がある");
            foreach (Runner r in gs.State.Runners) Assert.AreNotEqual(3, TeamWeek.TaperIndex(gs.State, r, Calendar.QualifierWeek), "シード校に予選会の調整はない(第二戦の登録なら、その調整は別)");
        }

        [Test]
        public void TheTwelveWhoRan_GetTheRaceFatigue_OthersKeepTheirs()
        {
            GameSession gs = NonSeedAtQualifierWeek();
            gs.ResolveTraining();
            TeamState s = gs.State;
            Dictionary<int, int> before = s.Runners.ToDictionary(r => r.Id, r => r.Fatigue);
            RaceInfo? own;
            gs.PlayWeekRaces(out own);
            QualifierResult q = gs.YearInProgress.QualifierResults.Single(x => x.Id == gs.School.Id);
            int[] ran = q.RunnerIds.Where(id => id != 0).ToArray();
            Assert.AreEqual(RaceConstants.QualEntry, ran.Length, "自校の 12 名が走った");
            int add = (int)Math.Round(RaceConstants.QualKm * 1.6, MidpointRounding.AwayFromZero);
            Assert.AreEqual(32, add);
            foreach (Runner r in s.Runners)
            {
                int want = ran.Contains(r.Id) ? Math.Min(100, before[r.Id] + add) : before[r.Id];
                Assert.AreEqual(want, r.Fatigue, "選手 " + r.Id + (ran.Contains(r.Id) ? "(走った)" : "(走っていない)"));
            }
        }

        [Test]
        public void SavingAndLoadingInTheQualifierWeek_KeepsTheRosterAndTheTaper()
        {
            GameSession gs = NonSeedAtQualifierWeek();
            Heal(gs);
            List<int> ids = gs.State.Runners.OrderBy(r => r.Id).Skip(3).Take(RaceConstants.QualEntry).Select(r => r.Id).ToList();
            Assert.IsTrue(gs.SetQualifierEntries(ids).Ok);
            int[] want = gs.State.Runners.Select(r => TeamWeek.TaperIndex(gs.State, r, Calendar.QualifierWeek)).ToArray();
            GameSessionExtras ex = gs.CaptureExtras();
            Assert.IsNotNull(ex.QualifierEntries);
            GameSession back = GameSession.Restore(gs.World, gs.State, ex);
            int[] got = back.State.Runners.Select(r => TeamWeek.TaperIndex(back.State, r, Calendar.QualifierWeek)).ToArray();
            CollectionAssert.AreEqual(want, got);
            Assert.AreEqual(RaceConstants.QualEntry, got.Count(i => i == 3));
        }
    }
}
