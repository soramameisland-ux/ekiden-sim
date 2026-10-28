using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Recruit;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>07 §13 のテストベクタ(TV-1〜TV-4)・09 TV-10 と、券・開始状態の性質。</summary>
    public class SpecialVectorTests
    {
        private static readonly KeyedRngFactory Rngs = new KeyedRngFactory(20260101UL);

        [Test]
        public void TV1_Rookie_Pref13()
        {
            SpecialRecruit r = SpecialRecruitGenerator.Generate(Rngs, 6, TicketKind.Rookie, 13);
            Assert.AreEqual(61.661, r.A0, 5e-4);
            Assert.AreEqual(27.206, r.Gap, 5e-4);
            Assert.AreEqual(GrowthType.Late, r.Growth);
            Assert.AreEqual(Profile.Balanced, r.Profile);
            CollectionAssert.AreEqual(new[] { 57, 58, 62, 54, 64, 63, 63, 56, 64, 62 }, r.Stats);
            CollectionAssert.AreEqual(new[] { 84, 85, 89, 81, 89, 92, 94, 83, 90, 92 }, r.PotentialBase);
            Assert.AreEqual(13, r.Pref);
            Assert.AreEqual(77.98, r.Q, 5e-3);
            Assert.AreEqual(69.82, r.V, 5e-3);
            Assert.IsNull(r.Skill, "u = 0.5464 は確率 30% に届かない");
            CollectionAssert.IsEmpty(SpecialRecruitGenerator.Validate(r));
        }

        [Test]
        public void TV1_Intl()
        {
            SpecialRecruit r = SpecialRecruitGenerator.Generate(Rngs, 6, TicketKind.Intl);
            Assert.AreEqual(66.661, r.A0, 5e-4);
            Assert.AreEqual(9.471, r.Gap, 5e-4);
            Assert.AreEqual(GrowthType.Late, r.Growth);
            Assert.AreEqual(Profile.Balanced, r.Profile);
            CollectionAssert.AreEqual(new[] { 62, 63, 67, 59, 69, 68, 68, 61, 69, 67 }, r.Stats);
            CollectionAssert.AreEqual(new[] { 72, 72, 77, 68, 76, 79, 81, 71, 78, 79 }, r.PotentialBase);
            Assert.AreEqual("アメリカ", r.Country);
            Assert.AreEqual(72.34, r.Q, 5e-3);
            Assert.AreEqual(69.50, r.V, 5e-3);
            CollectionAssert.IsEmpty(SpecialRecruitGenerator.Validate(r));
        }

        [Test]
        public void TV1_CapAdjustment_Example()
        {
            SpecialRecruit r = SpecialRecruitGenerator.Generate(Rngs, 5, TicketKind.Intl);
            Assert.AreEqual(68.0, r.A0, 1e-9);
            Assert.AreEqual(Profile.Speed, r.Profile);
            Assert.AreEqual(2, r.Shift);
            CollectionAssert.AreEqual(new[] { 76, 65, 57, 71, 66, 67, 63, 68, 71, 74 }, r.Stats);
            Assert.AreEqual(67.8, r.Stats.Sum() / 10.0, 1e-9);
        }

        [Test]
        public void Generation_IsIndependentOfPrefSpecification_ForEverythingButTheOrigin()
        {
            SpecialRecruit a = SpecialRecruitGenerator.Generate(Rngs, 9, TicketKind.Rookie, 0);
            SpecialRecruit b = SpecialRecruitGenerator.Generate(Rngs, 9, TicketKind.Rookie, 47);
            CollectionAssert.AreEqual(a.Stats, b.Stats, "指定しても能力は変わらない(§6.3)");
            CollectionAssert.AreEqual(a.Potential, b.Potential);
            Assert.AreEqual(a.LookSeed, b.LookSeed);
            Assert.AreEqual(47, b.Pref);
            Assert.AreEqual(a.Style, b.Style);
        }

        [Test]
        public void ToRunner_AppliesInitialValues()
        {
            SpecialRecruit r = SpecialRecruitGenerator.Generate(Rngs, 6, TicketKind.Intl);
            Runner run = SpecialRecruitGenerator.ToRunner(r, 77);
            Assert.AreEqual(20, run.Fatigue);
            Assert.AreEqual(3, run.Motivation);
            Assert.AreEqual(0, run.Form);
            Assert.AreEqual(RecruitSource.Ticket, run.RecruitSource);
            Assert.AreEqual(TicketKind.Intl, run.TicketKind);
            Assert.IsTrue(run.Overseas);
            Assert.IsTrue(run.Far);
            Assert.AreEqual(0, run.Pref);
            CollectionAssert.AreEqual(r.Potential, run.Potential);
            Assert.AreEqual(r.Style, run.Style.Value);
        }

        [Test]
        public void Countries_Are24_Unique()
        {
            Assert.AreEqual(24, Geo.Countries.Length);
            Assert.AreEqual(24, Geo.Countries.Distinct().Count());
        }

        // ---- TV-2: 券の状態の推移 ----

        [Test]
        public void TV2_TicketTimeline_WithPlan()
        {
            var t = new TicketState();
            var active = new List<int>();
            var earned = new Dictionary<int, string> { { 3, "win_Fall" }, { 5, "top3" }, { 8, "seed" } };
            // 年度 | 付与 | 獲得 | 使用 | 所持 | 繰り延べ | 在籍
            int[][] want =
            {
                new[] { 2, 0, 1, 1, 0, 1 }, new[] { 0, 0, 1, 0, 0, 2 }, new[] { 0, 1, 0, 1, 0, 2 }, new[] { 0, 0, 0, 1, 0, 2 },
                new[] { 2, 1, 0, 2, 2, 2 }, new[] { 0, 0, 1, 2, 1, 2 }, new[] { 0, 0, 1, 2, 0, 2 }, new[] { 0, 1, 0, 2, 1, 2 },
                new[] { 2, 0, 0, 2, 3, 2 }, new[] { 0, 0, 0, 2, 3, 2 }, new[] { 0, 0, 1, 2, 2, 2 }, new[] { 0, 0, 1, 2, 1, 2 },
            };
            int kindToggle = 0;
            for (int year = 1; year <= 12; year++)
            {
                int g = t.TryPlanGrant(year, true);
                int e = earned.ContainsKey(year) && t.Earn(earned[year], year) ? 1 : 0;
                active.RemoveAll(y => year - y >= 4);
                int used = 0;
                if (t.CanDeclare(year, 5, active.Count))
                {
                    t.Declare(year, (kindToggle++ & 1) == 0 ? TicketKind.Intl : TicketKind.Rookie);
                    used = 1;
                    active.Add(year + 1);
                }
                t.ConfirmEnrollment();
                int[] w = want[year - 1];
                CollectionAssert.AreEqual(w, new[] { g, e, used, t.Stock, t.Bank, active.Count }, "年度 " + year);
            }
            Assert.AreEqual(6, t.InvitesDeclared, "12 年度までに使った券は 6 枚");
            int received = t.Log.Where(x => x.Delta > 0).Sum(x => x.Delta);
            int usedSum = t.Log.Where(x => x.Delta < 0).Sum(x => -x.Delta);
            CollectionAssert.IsEmpty(t.CheckInvariants(active.Count, received, usedSum), "失効は 0 枚");
        }

        // ---- TV-3: 購入状態の判定と付与 ----

        [Test]
        public void TV3_PlanGrant_Rules()
        {
            var t = new TicketState();
            Assert.AreEqual(0, t.TryPlanGrant(1, null));
            Assert.AreEqual(2, t.TryPlanGrant(1, true), "4 月にオフライン → 同じ年度のうちに確認できたら付与");
            Assert.AreEqual(0, t.TryPlanGrant(1, true), "同じ年度に 2 回確認しても付与は 1 回");
            t = new TicketState();
            t.TryPlanGrant(1, true);
            Assert.AreEqual(0, t.TryPlanGrant(2, null));
            Assert.AreEqual(2, t.TryPlanGrant(5, true), "周期の年度 1, 5, 9…");
            t = new TicketState();
            t.TryPlanGrant(1, true);
            Assert.AreEqual(0, t.TryPlanGrant(5, null));
            Assert.AreEqual(0, t.TryPlanGrant(6, true), "年度をまたいだ取り戻しはしない");
            Assert.AreEqual(2, t.TryPlanGrant(9, true));
            t = new TicketState();
            Assert.AreEqual(2, t.TryPlanGrant(7, true), "途中購入: 確認した年度を起点に、すぐ最初の付与");
            Assert.AreEqual(0, t.TryPlanGrant(8, true));
            Assert.AreEqual(2, t.TryPlanGrant(11, true));
            Assert.AreEqual(0, t.TryPlanGrant(12, false), "返金のあとは付与しない");
            Assert.AreEqual(4, t.Stock + t.Bank - 0, "返金しても所持済みの券は残る");
        }

        // ---- 節目(07 §4) ----

        [Test]
        public void Milestones_FortyFiveYears_YieldFourteenTickets()
        {
            var t = new TicketState();
            int total = 0;
            for (int year = 1; year <= 45; year++)
            {
                int wins = 0;
                if (year == 1)                                        // 1 年目に 3 大会すべて優勝
                {
                    for (int kind = 1; kind <= 3; kind++)
                    {
                        wins |= 1 << (kind - 1);
                        total += TicketMilestones.EarnAfterRace(t, year, kind, 1, wins);
                    }
                }
                int rank = year >= 9 ? 9 : year >= 6 ? 6 : year >= 3 ? 3 : 0;
                total += TicketMilestones.EarnAtYearEnd(t, year, rank, year);
            }
            Assert.AreEqual(14, total, "大会の節目 7 + ランク 3 + 継続の記念 4");
            Assert.AreEqual(14, t.Milestones.Count);
            CollectionAssert.Contains(t.Milestones.ToList(), "tm.anniv.40");
            Assert.AreEqual(2, t.Stock);
            Assert.AreEqual(12, t.Bank);
        }

        [Test]
        public void Milestones_RaceRules()
        {
            var t = new TicketState();
            Assert.AreEqual(0, TicketMilestones.EarnAfterRace(t, 1, 1, 12, 0));
            Assert.AreEqual(2, TicketMilestones.EarnAfterRace(t, 1, 2, 1, 2), "第二戦の初優勝 + 初の表彰台");
            Assert.AreEqual(0, TicketMilestones.EarnAfterRace(t, 1, 2, 1, 2), "同じ節目は 1 回だけ");
            Assert.AreEqual(2, TicketMilestones.EarnAfterRace(t, 2, 3, 8, 0) + TicketMilestones.EarnAfterRace(t, 2, 1, 1, 1), "第三戦の初のシード + 第一戦の初優勝");
            Assert.AreEqual(3, TicketMilestones.EarnAfterRace(t, 3, 3, 1, 7), "第三戦の初優勝・三冠・クリア");
            CollectionAssert.Contains(t.Milestones.ToList(), TicketMilestones.Clear);
        }

        // ---- 開始状態(07 §10) ----

        [Test]
        public void Scenarios_AreValid_AndMatchThe05Definitions()
        {
            var all = Scenarios.BuiltIn();
            all.Add(Scenarios.Koki());
            CollectionAssert.IsEmpty(Scenarios.Validate(all));
            Assert.AreEqual(new[] { 3, 7, 20, 33 }, Scenarios.BuiltIn().Select(s => s.StrengthRank).ToArray());
            Assert.AreEqual(5, Scenarios.Club().Members);
            Assert.AreEqual(700, Scenarios.Koki().Prestige);
            Assert.AreEqual(9, Scenarios.Koki().ObLevel);
            Assert.IsNull(Scenarios.Koki().Tag);
            Assert.AreEqual(4, Scenarios.Elite().Facilities.Dorm);
        }

        [Test]
        public void Scenarios_Validation_Rejects_OutOfRange()
        {
            var bad = Scenarios.Low();
            bad.StrengthRank = 37;
            bad.Funds = 6000;
            bad.Members = 3;
            bad.Facilities.Gym = 0;
            var dup = Scenarios.Low();
            string[] heads = Scenarios.Validate(new[] { bad, dup, Scenarios.Low() }).Select(e => e.Substring(0, 4)).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(new[] { "SC-1", "SC-2", "SC-4", "SC-5", "SC-6" }, heads);
        }
    }
}
