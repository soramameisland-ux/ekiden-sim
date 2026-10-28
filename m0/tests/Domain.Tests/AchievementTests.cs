using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Achievements;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>実績(01 §3.3・02 §3.10。08b G-08)の定義と判定。</summary>
    public class AchievementTests
    {
        private static AchievementFacts Facts(params YearFacts[] years)
        {
            var f = new AchievementFacts { YearsPlayed = years.Length > 0 ? years.Max(y => y.Year) : 1 };
            f.Years.AddRange(years);
            return f;
        }

        private static YearFacts Y(int year, int fall = 99, int national = 99, int newYear = 99) { return new YearFacts { Year = year, Place = new[] { fall, national, newYear } }; }

        private static bool Has(AchievementFacts f, string id) { return AchievementRules.Satisfied(f).Contains(id); }

        [Test]
        public void Master_HasUniqueIds_And23Entries_AndHiddenOnesAreMarked()
        {
            var ids = AchievementMaster.All.Select(d => d.Id).ToList();
            Assert.AreEqual(23, ids.Count);
            Assert.AreEqual(ids.Count, ids.Distinct().Count());
            CollectionAssert.AreEqual(new[] { "ach.clear.club.no_invite" }, AchievementMaster.All.Where(d => d.Hidden).Select(d => d.Id).ToArray(), "隠しは同好会・無招聘クリアだけ(01 §3.3)");
            Assert.IsEmpty(AchievementRules.Satisfied(Facts()), "何もなければ何も満たさない");
        }

        [Test]
        public void RaceRank_Boundaries_AndWhichRaceCounts()
        {
            Assert.IsTrue(Has(Facts(Y(1, newYear: 10)), "ach.race.seed"));
            Assert.IsFalse(Has(Facts(Y(1, newYear: 11)), "ach.race.seed"));
            Assert.IsFalse(Has(Facts(Y(1, fall: 3, national: 1)), "ach.race.seed"), "初シードは第三戦だけ");
            Assert.IsTrue(Has(Facts(Y(1, national: 3)), "ach.race.podium"));
            Assert.IsFalse(Has(Facts(Y(1, fall: 4, national: 5, newYear: 6)), "ach.race.podium"));
            AchievementFacts w = Facts(Y(2, national: 1));
            Assert.IsTrue(Has(w, "ach.race.win")); Assert.IsTrue(Has(w, "ach.race.win.national"));
            Assert.IsFalse(Has(w, "ach.race.win.fall")); Assert.IsFalse(Has(w, "ach.race.win.newyear"));
            Assert.IsTrue(Has(Facts(Y(1, fall: 1)), "ach.race.win.fall")); Assert.IsTrue(Has(Facts(Y(1, newYear: 1)), "ach.race.win.newyear"));
            Assert.IsFalse(Has(Facts(Y(1, fall: 2)), "ach.race.win"));
        }

        [Test]
        public void Streaks_NeedTheSameRaceInConsecutiveYears()
        {
            Assert.IsTrue(Has(Facts(Y(3, fall: 1), Y(4, fall: 1)), "ach.race.streak2"));
            Assert.IsFalse(Has(Facts(Y(3, fall: 1), Y(5, fall: 1)), "ach.race.streak2"), "年度が空いたら連覇ではない");
            Assert.IsFalse(Has(Facts(Y(3, fall: 1), Y(4, national: 1)), "ach.race.streak2"), "別の大会の優勝は連覇ではない");
            Assert.IsFalse(Has(Facts(Y(3, fall: 1), Y(4, fall: 2)), "ach.race.streak2"));
            AchievementFacts five = Facts(Enumerable.Range(10, 5).Select(y => Y(y, newYear: 1)).ToArray());
            Assert.IsTrue(Has(five, "ach.race.streak5"));
            AchievementFacts four = Facts(Enumerable.Range(10, 4).Select(y => Y(y, newYear: 1)).ToArray());
            Assert.IsFalse(Has(four, "ach.race.streak5")); Assert.IsTrue(Has(four, "ach.race.streak2"));
            AchievementFacts broken = Facts(Y(10, newYear: 1), Y(11, newYear: 1), Y(12, newYear: 2), Y(13, newYear: 1), Y(14, newYear: 1), Y(15, newYear: 1));
            Assert.IsFalse(Has(broken, "ach.race.streak5"), "途切れたら数え直し");
            Assert.AreEqual(3, AchievementRules.LongestStreak(broken, 2));
            Assert.AreEqual(3, AchievementRules.LongestStreak(broken, -1));
            Assert.AreEqual(0, AchievementRules.LongestStreak(broken, 0));
        }

        [Test]
        public void Sweeps_NeedAllThreeInOneYear_AndConsecutiveYearsForTheSecond()
        {
            Assert.IsTrue(Has(Facts(Y(4, 1, 1, 1)), "ach.sweep"));
            Assert.IsFalse(Has(Facts(Y(4, 1, 1, 2)), "ach.sweep"));
            Assert.IsFalse(Has(Facts(Y(4, 1, 1, 1)), "ach.sweep.2"));
            Assert.IsTrue(Has(Facts(Y(4, 1, 1, 1), Y(5, 1, 1, 1)), "ach.sweep.2"));
            Assert.IsFalse(Has(Facts(Y(4, 1, 1, 1), Y(6, 1, 1, 1)), "ach.sweep.2"), "三冠が連続していない");
            Assert.IsFalse(Has(Facts(Y(4, 1, 1, 1), Y(5, 1, 1, 2)), "ach.sweep.2"));
        }

        [Test]
        public void Clears_FollowTheStartingSchool_AndTheNoInviteFlag()
        {
            var f = new AchievementFacts { Difficulty = "Low", Cleared = true, ClearNoInvite = true };
            CollectionAssert.AreEqual(new[] { "ach.clear", "ach.clear.low", "ach.clear.low.no_invite" }, AchievementRules.Satisfied(f).ToArray());
            f.ClearNoInvite = false;
            CollectionAssert.AreEqual(new[] { "ach.clear", "ach.clear.low" }, AchievementRules.Satisfied(f).ToArray());
            f.Difficulty = "Club"; f.ClearNoInvite = true;
            CollectionAssert.AreEqual(new[] { "ach.clear", "ach.clear.club", "ach.clear.club.no_invite" }, AchievementRules.Satisfied(f).ToArray());
            f.Difficulty = "Elite";
            CollectionAssert.AreEqual(new[] { "ach.clear", "ach.clear.elite" }, AchievementRules.Satisfied(f).ToArray(), "強豪の無招聘クリアという実績はない");
            f.Difficulty = null;
            CollectionAssert.AreEqual(new[] { "ach.clear" }, AchievementRules.Satisfied(f).ToArray(), "シナリオパックは開始校の実績を満たさない");
            f.Cleared = false;
            Assert.IsEmpty(AchievementRules.Satisfied(f));
        }

        [Test]
        public void Counts_AtTheirBoundaries()
        {
            Assert.IsFalse(Has(new AchievementFacts { PrefecturesCovered = 46 }, "ach.dev.prefectures")); Assert.IsTrue(Has(new AchievementFacts { PrefecturesCovered = 47 }, "ach.dev.prefectures"));
            Assert.IsFalse(Has(new AchievementFacts { HallOfFameCount = 9 }, "ach.dev.hall")); Assert.IsTrue(Has(new AchievementFacts { HallOfFameCount = 10 }, "ach.dev.hall"));
            Assert.IsFalse(Has(new AchievementFacts { SectionWinsTotal = 99 }, "ach.dev.section_wins")); Assert.IsTrue(Has(new AchievementFacts { SectionWinsTotal = 100 }, "ach.dev.section_wins"));
            Assert.IsFalse(Has(new AchievementFacts { YearsPlayed = 24 }, "ach.years.25")); Assert.IsTrue(Has(new AchievementFacts { YearsPlayed = 25 }, "ach.years.25"));
            Assert.IsFalse(Has(new AchievementFacts { YearsPlayed = 49 }, "ach.years.50")); Assert.IsTrue(Has(new AchievementFacts { YearsPlayed = 50 }, "ach.years.50"));
            Assert.IsTrue(Has(new AchievementFacts { YearsPlayed = 50 }, "ach.years.25"), "50 年なら 25 年も満たす");
            Assert.IsTrue(Has(new AchievementFacts { MountainGodWin = true }, "ach.dev.mountain_god"));
            Assert.IsFalse(Has(new AchievementFacts(), "ach.dev.mountain_god"));
        }

        // ------------------------------------------------------------ 状態から集める
        [Test]
        public void Facts_FromARealSession_MatchAnIndependentRecount()
        {
            GameSession gs = GameSession.New(41UL, Difficulty.Elite);
            gs.PlayYears(12);
            AchievementFacts f = AchievementFacts.From(gs);
            Assert.AreEqual("Elite", f.Difficulty);
            Assert.AreEqual(gs.Year, f.YearsPlayed);
            Assert.AreEqual(gs.State.Cleared != null, f.Cleared);
            Assert.AreEqual(gs.State.Obs.Count(o => o.HallOfFame), f.HallOfFameCount);
            Assert.AreEqual(gs.State.Obs.Select(o => o.Pref).Where(p => p >= 1).Distinct().Count(), f.PrefecturesCovered);
            Assert.AreEqual(gs.State.Runners.Sum(r => r.SectionWins) + gs.State.Obs.Sum(o => o.SectionWins), f.SectionWinsTotal);
            // 年度ごとの順位: 精算ずみの年度は戦績から、今年度はここまでの順位
            foreach (PlayerYearStats st in gs.Stats)
                CollectionAssert.AreEqual(st.Place, f.Years.Single(y => y.Year == st.Year).Place);
            YearFacts cur = f.Years.Single(y => y.Year == gs.Year);
            if (gs.Stats.All(s => s.Year != gs.Year)) CollectionAssert.AreEqual(gs.State.Rank, cur.Place);
            Assert.AreEqual(f.Years.Select(y => y.Year).OrderBy(x => x).ToList(), f.Years.Select(y => y.Year).ToList(), "年度の順");
            // 判定は事実だけで決まる(同じ事実なら同じ結果)
            CollectionAssert.AreEqual(AchievementRules.Satisfied(f), AchievementRules.Satisfied(AchievementFacts.From(gs)));
        }

        [Test]
        public void MountainGod_NeedsTheSkill_TheFifthSection_AndTheSectionPrize_OfTheThirdRace()
        {
            GameSession gs = GameSession.New(42UL, Difficulty.Elite);
            Runner[] pool = gs.State.Runners.Take(10).ToArray();
            int own = gs.School.Id, other = own == 0 ? 1 : 0;
            Func<int, int, RaceOutcome> race = (ownFifth, otherFifth) =>
            {
                var lineup = pool.Select(r => new RaceRunner { RunnerId = r.Id }).ToArray();
                var ownSec = Enumerable.Repeat(1000, 10).ToArray(); ownSec[4] = ownFifth;
                var otherSec = Enumerable.Repeat(1000, 10).ToArray(); otherSec[4] = otherFifth;
                return new RaceOutcome
                {
                    Kind = RaceKind.NewYear,
                    Teams = new List<RaceTeam> { new RaceTeam { Id = own, Lineup = lineup }, new RaceTeam { Id = other, Lineup = lineup } },
                    Results = new List<TeamResult> { new TeamResult { Id = own, Sections = ownSec, Cum = new int[10] }, new TeamResult { Id = other, Sections = otherSec, Cum = new int[10] } },
                };
            };
            Runner fifth = pool[4];
            fifth.Skills.Remove("skill.mountain_god");
            Assert.IsFalse(AchievementFacts.From(gs, race(900, 1000)).MountainGodWin, "特殊能力がなければ満たさない");
            fifth.Skills.Add("skill.mountain_god");
            Assert.IsTrue(AchievementFacts.From(gs, race(900, 1000)).MountainGodWin);
            Assert.IsTrue(AchievementFacts.From(gs, race(1000, 1000)).MountainGodWin, "同タイムは区間賞(04 §8.3 は全員)");
            Assert.IsFalse(AchievementFacts.From(gs, race(1001, 1000)).MountainGodWin, "区間賞でなければ満たさない");
            RaceOutcome fall = race(900, 1000); fall.Kind = RaceKind.Fall;
            Assert.IsFalse(AchievementFacts.From(gs, fall).MountainGodWin, "第三戦だけ");
            Assert.IsFalse(AchievementFacts.From(gs, null).MountainGodWin);
            // 別の区間の走者が持っていても満たさない
            fifth.Skills.Remove("skill.mountain_god"); pool[3].Skills.Add("skill.mountain_god");
            Assert.IsFalse(AchievementFacts.From(gs, race(900, 1000)).MountainGodWin, "5 区を走った選手の特殊能力で見る");
        }
    }
}
