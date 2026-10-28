using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// <summary>運営(SC-32)・全国の大学(SC-35)・OB 名簿(SC-33)・歴史(SC-34)の読み取り API(08b)。</summary>
    public class ClubViewTests
    {
        private static GameSession Game(ulong seed = 20260403UL, Difficulty d = Difficulty.Mid) { return GameSession.New(seed, d); }

        private static void SetAll(GameSession gs, int lv)
        {
            gs.State.Fac = new FacilityLevels { Coach = lv, Trainer = lv, Nutri = lv, Dorm = lv, Track = lv, Hill = lv, Gym = lv, Medical = lv, Scout = lv };
        }

        // ------------------------------------------------------------ 運営
        [Test]
        public void Ops_EffectsFollowTheTableOfSpec03_AtLevel3AndLevel1()
        {
            GameSession gs = Game();
            SetAll(gs, 3);
            OpsView v = gs.Ops();
            Func<string, FacilityRowView> row = k => v.Rows.Single(r => r.Key == k);
            Func<string, string, FacilityEffectView> fx = (k, c) => row(k).Effects.Single(e => e.Code == c);
            Assert.AreEqual(0.12, fx("coach", "xp_all").Value, 1e-12);
            Assert.AreEqual(-0.06, fx("trainer", "injury_prob").Value, 1e-12);
            Assert.AreEqual(-0.06, fx("trainer", "injury_dur").Value, 1e-12);
            Assert.AreEqual(0.04, fx("trainer", "recovery_r").Value, 1e-12);
            Assert.AreEqual(0.04, fx("nutri", "recovery_r").Value, 1e-12);
            Assert.AreEqual(0.04, fx("dorm", "recovery_r").Value, 1e-12);
            Assert.AreEqual(0.24, fx("dorm", "xp_rec").Value, 1e-12);
            Assert.AreEqual(0.24, fx("track", "xp_spd_kck").Value, 1e-12);
            Assert.AreEqual(0.30, fx("hill", "xp_clb_dwn").Value, 1e-12);
            Assert.AreEqual(0.30, fx("gym", "xp_dur").Value, 1e-12);
            Assert.AreEqual(-0.08, fx("gym", "injury_prob").Value, 1e-12);
            Assert.AreEqual(-0.10, fx("medical", "injury_dur").Value, 1e-12);
            Assert.AreEqual(5.6, fx("scout", "scout_sd").Value, 1e-12, "σ = 8 − 1.2 × (Lv − 1)");
            Assert.AreEqual(1.0, fx("scout", "gift_kind").Value, "Lv3 から素養の種類が見える");
            Assert.AreEqual(0.18, fx("coach", "xp_all").NextValue, 1e-12, "次のレベルの効果");
            Assert.AreEqual(4.4, fx("scout", "scout_sd").NextValue, 1e-12);
            foreach (FacilityRowView r in v.Rows) foreach (FacilityEffectView e in r.Effects) CollectionAssert.Contains(FacilityEffectView.Codes, e.Code);

            SetAll(gs, 1);
            foreach (FacilityRowView r in gs.Ops().Rows)
                foreach (FacilityEffectView e in r.Effects.Where(e => e.Code != "scout_sd" && e.Code != "gift_kind")) Assert.AreEqual(0.0, e.Value, 1e-12, "Lv1 は効果なし: " + r.Key + " " + e.Code);
            Assert.AreEqual(8.0, gs.Ops().Rows.Single(r => r.Key == "scout").Effects.Single(e => e.Code == "scout_sd").Value, 1e-12);
            SetAll(gs, 5);
            Assert.AreEqual(gs.Ops().Rows.Single(r => r.Key == "coach").Effects[0].Value, gs.Ops().Rows.Single(r => r.Key == "coach").Effects[0].NextValue, 1e-12, "Lv5 は次がない(同じ値)");
        }

        [Test]
        public void Ops_RowsAgreeWithTheUpgradeCommand_CostCapAndFunds()
        {
            foreach (int funds in new[] { 250, 900, 4000 })
            {
                GameSession gs = Game();
                var f = gs.State.Fac; f.Dorm = 1; f.Track = gs.Ops().Cap; gs.State.Fac = f;                 // 上限のものを 1 つ作る
                gs.State.Funds = funds;
                OpsView v = gs.Ops();
                Assert.AreEqual(FacilityPolicy.Order.Length, v.Rows.Count);
                for (int i = 0; i < v.Rows.Count; i++) Assert.AreEqual(FacilityPolicy.Order[i], v.Rows[i].Key, "推奨の順");
                foreach (FacilityRowView r in v.Rows)
                {
                    GameSession g = Game();
                    var ff = g.State.Fac; ff.Dorm = 1; ff.Track = g.Ops().Cap; g.State.Fac = ff;
                    g.State.Funds = funds;
                    Assert.AreEqual(r.Level, FacilityPolicy.Level(g.State.Fac, r.Key));
                    Assert.AreEqual(WorldMath.FacilityCap(g.School.Rank()), r.Cap);
                    OpResult res = g.UpgradeFacility(r.Key);
                    Assert.AreEqual(r.CanUpgrade, res.Ok, r.Key + " 資金 " + funds + ": " + res.Reason);
                    if (r.CanUpgrade)
                    {
                        Assert.AreEqual(300 + 250 * r.Level, r.NextCost.Value, 1e-9);
                        Assert.AreEqual(funds - (int)r.NextCost.Value, g.State.Funds, "費用のとおり減る");
                        Assert.AreEqual(r.Level + 1, FacilityPolicy.Level(g.State.Fac, r.Key));
                        Assert.IsNull(r.BlockedBy);
                    }
                    else
                    {
                        Assert.AreEqual(r.Level >= r.Cap ? "cap" : "funds", r.BlockedBy);
                        StringAssert.Contains(r.BlockedBy == "cap" ? "上限" : "資金", res.Reason);
                        Assert.AreEqual(funds, g.State.Funds, "拒否されたら資金は変わらない");
                    }
                    Assert.AreEqual(r.Level >= r.Cap, r.NextCost == null);
                    Assert.AreEqual(r.Key == "coach" || r.Key == "trainer" || r.Key == "nutri" || r.Key == "scout", r.IsStaff);
                    Assert.AreEqual("fac." + r.Key + ".name", r.NameKey);
                }
                Assert.IsTrue(v.Rows.Any(r => r.BlockedBy == "cap"));
            }
        }

        [Test]
        public void Ops_RecommendedLevels_AreWhatUpgradeRecommendedDoes()
        {
            foreach (int funds in new[] { 100, 1500, 6000 })
            {
                GameSession gs = Game(7UL, Difficulty.Elite);
                SetAll(gs, 1);                                                      // 上限(3 以上)に届かないところから
                gs.State.Funds = funds;
                OpsView v = gs.Ops();
                Dictionary<string, int> expected = v.Rows.ToDictionary(r => r.Key, r => r.RecommendedLevel);
                int sumBefore = v.Rows.Sum(r => r.Level);
                int done = gs.UpgradeRecommended();
                Assert.AreEqual(v.RecommendedCount, done);
                if (funds >= 1500) Assert.Greater(done, 0, "資金があれば推奨の強化が起きる(試験の前提)");
                foreach (string key in FacilityPolicy.Order) Assert.AreEqual(expected[key], FacilityPolicy.Level(gs.State.Fac, key), key);
                Assert.AreEqual(sumBefore + done, FacilityPolicy.Order.Sum(k => FacilityPolicy.Level(gs.State.Fac, k)));
                Assert.GreaterOrEqual(gs.State.Funds, Math.Min(funds, (int)WorldK.Reserve), "予備費を残す");
            }
        }

        [Test]
        public void Ops_BalanceUsesTheFormulas_AndEqualsTheSettlementAfterTheYear()
        {
            GameSession gs = Game(11UL, Difficulty.Mid);
            gs.State.Mods.Add(new TimedModifier { Kind = ModKind.FundsYear, To = TimedModifier.TeamTarget, Value = 30, Until = int.MaxValue, Src = "test" });
            OpsView v = gs.Ops();
            FacilityLevels f = gs.State.Fac;
            Assert.AreEqual(WorldMath.Income(gs.School.Prestige, gs.School.PrevPoints), v.IncomeFormula, 1e-9);
            Assert.AreEqual(30.0, v.IncomeMod, 1e-9);
            Assert.AreEqual(v.IncomeFormula + 30.0, v.Income, 1e-9);
            Assert.AreEqual(WorldMath.StaffExpense(f.Coach, f.Trainer, f.Nutri, f.Scout), v.Staff, 1e-9);
            Assert.AreEqual(WorldMath.FacilityUpkeep(f.Dorm, f.Track, f.Hill, f.Gym, f.Medical), v.Upkeep, 1e-9);
            Assert.AreEqual(15.0 * gs.State.Runners.Count, v.MembersExpense, 1e-9);
            Assert.AreEqual(v.Income - v.Expense, v.Balance, 1e-9);
            Assert.AreEqual(gs.State.Funds, v.Funds);
            Assert.AreEqual((int)WorldK.Reserve, v.Reserve);
            Assert.IsEmpty(v.History);

            gs.AutoUpgrade = false;
            for (int w = 0; w < 47; w++) gs.StepWeek();
            gs.ResolveTraining();
            RaceInfo? o;
            gs.PlayWeekRaces(out o);
            gs.ResolveWeekEvents(gs.SelectWeekEvents(o), o, new DefaultChoicePolicy());
            gs.FinishWeek();
            gs.SettleYear();
            OpsView after = gs.Ops();
            Assert.AreEqual(gs.LastSettlement.IncomeFormula, after.IncomeFormula, 1e-9, "精算のあとの見込みは、精算に使った式と同じ名声・評価ポイント");
            Assert.AreEqual(1, after.History.Count);
            Assert.AreEqual(gs.Stats[0].Year, after.History[0].Year);
            Assert.AreEqual(gs.Stats[0].Funds, after.History[0].Funds, 0.0);
        }

        // ------------------------------------------------------------ 全国の大学
        [Test]
        public void World_ListsAllSchoolsByPower_AndEligibilityAgreesWithTheRealEntrants()
        {
            for (ulong seed = 1; seed <= 3; seed++)
            {
                GameSession gs = Game(seed, seed == 2 ? Difficulty.Low : Difficulty.Mid);
                for (int year = 0; year < 3; year++)
                {
                    WorldView v = gs.WorldOverview();
                    Assert.AreEqual(WorldSim.SchoolCount, v.Schools.Count);
                    Assert.AreEqual(1, v.Schools.Count(r => r.IsOwn));
                    for (int i = 1; i < v.Schools.Count; i++) Assert.LessOrEqual(v.Schools[i - 1].PowerRank, v.Schools[i].PowerRank, "戦力順位の順");
                    WorldSchoolRowView own = v.Schools.Single(r => r.IsOwn);
                    Assert.AreEqual(gs.Home().PowerRank, own.PowerRank, "ホームの戦力順位と同じ");
                    Assert.AreEqual(gs.OwnPower(), own.Power, 0.0);
                    foreach (WorldSchoolRowView r in v.Schools)
                    {
                        School s = gs.World.Schools.Single(x => x.Id == r.SchoolId);
                        Assert.AreEqual(s.IsPlayer ? gs.OwnPower() : gs.World.TeamPower(s), r.Power, 0.0);
                        Assert.AreEqual(1 + gs.World.Schools.Count(o => (o.IsPlayer ? gs.OwnPower() : gs.World.TeamPower(o)) > r.Power), r.PowerRank);
                        Assert.AreEqual(s.Prestige, r.Prestige);
                        Assert.AreEqual(s.Tier, r.Tier);
                        Assert.AreEqual(s.Region, r.Region);
                        Assert.AreEqual(s.PrevPoints, r.PrevPoints);
                        Assert.AreEqual(s.LastNewYearRank.HasValue ? s.LastNewYearRank.Value + 1 : (int?)null, r.LastNewYearPlace);
                        Assert.AreEqual("school." + s.Id + ".name", r.NameKey);
                    }
                    // 独立の整列(安定)で出場権の位置を求め、実際の出場校の集合と突き合わせる
                    int me = gs.School.Id;
                    var byPoints = gs.World.Schools.OrderByDescending(s => s.PrevPoints).ThenByDescending(s => s.Prestige).ToList();
                    var byPrestige = gs.World.Schools.OrderByDescending(s => s.Prestige).ToList();
                    Assert.AreEqual(byPoints.FindIndex(s => s.Id == me) + 1, v.Eligibility[0].Position);
                    Assert.AreEqual(byPrestige.FindIndex(s => s.Id == me) + 1, v.Eligibility[1].Position);
                    YearState ys = gs.PrepareYear();
                    Assert.AreEqual(ys.Entrants(RaceKind.Fall).Contains(me), v.Eligibility[0].InRange);
                    Assert.AreEqual(ys.Entrants(RaceKind.National).Contains(me), v.Eligibility[1].InRange);
                    Assert.AreEqual(ys.Seeds.Contains(me), v.Eligibility[2].InRange);
                    Assert.AreEqual(!ys.Seeds.Contains(me), v.Eligibility[2].NeedsQualifier);
                    Assert.AreEqual(v.Eligibility[0].InRange ? 0 : v.Eligibility[0].Position - 20, v.Eligibility[0].Short);
                    Assert.AreEqual(v.Eligibility[1].InRange ? 0 : v.Eligibility[1].Position - 30, v.Eligibility[1].Short);
                    Assert.AreEqual(new[] { 20, 30, 10 }, v.Eligibility.Select(e => e.Cutoff).ToArray());
                    gs.PlayYears(1);
                }
            }
        }

        [Test]
        public void World_EligibilityBoundaries_AreExactlyTwentyThirtyAndTen()
        {
            GameSession gs = Game(21UL, Difficulty.Mid);
            School me = gs.World.Schools.Single(s => s.IsPlayer);
            foreach (int pos in new[] { 19, 20, 21, 30, 31 })
            {
                // 自校を pos 番目にする: 他校には 100 − 順番(自校を除いた並び)を与え、自校には pos 番目に当たる値を与える
                var others = gs.World.Schools.Where(s => !s.IsPlayer).OrderBy(s => s.Id).ToList();
                for (int i = 0; i < others.Count; i++)
                {
                    int rank = i + 1 >= pos ? i + 2 : i + 1;                         // 自校が pos 番目になるように 1 つずらす
                    others[i].PrevPoints = 1000 - rank * 10;
                    others[i].Prestige = 1000 - rank * 10;
                }
                me.PrevPoints = 1000 - pos * 10;
                me.Prestige = 1000 - pos * 10;
                WorldView v = gs.WorldOverview();
                Assert.AreEqual(pos, v.Eligibility[0].Position, "第一戦の位置 " + pos);
                Assert.AreEqual(pos <= 20, v.Eligibility[0].InRange);
                Assert.AreEqual(Math.Max(0, pos - 20), v.Eligibility[0].Short);
                Assert.AreEqual(pos, v.Eligibility[1].Position);
                Assert.AreEqual(pos <= 30, v.Eligibility[1].InRange);
                Assert.AreEqual(Math.Max(0, pos - 30), v.Eligibility[1].Short);
            }
            foreach (School s in gs.World.Schools) s.LastNewYearRank = null;
            var rivals = gs.World.Schools.Where(s => !s.IsPlayer).OrderBy(s => s.Id).Take(10).ToList();
            for (int i = 0; i < 9; i++) rivals[i].LastNewYearRank = i;
            me.LastNewYearRank = 9;                                                  // 前年度の第三戦 10 位 = シード
            WorldView seed = gs.WorldOverview();
            Assert.IsTrue(seed.Eligibility[2].InRange, "前年度の第三戦 10 位はシード");
            Assert.IsFalse(seed.Eligibility[2].NeedsQualifier);
            Assert.AreEqual(10, seed.Eligibility[2].Position);
            Assert.AreEqual(10, gs.World.SeedIds().Count);
            me.LastNewYearRank = 10;                                                 // 11 位はシードでない(予選会)
            rivals[9].LastNewYearRank = 9;
            WorldView out10 = gs.WorldOverview();
            Assert.IsFalse(out10.Eligibility[2].InRange);
            Assert.IsTrue(out10.Eligibility[2].NeedsQualifier);
            Assert.AreEqual(0, out10.Eligibility[2].Position);
        }

        // ------------------------------------------------------------ OB 名簿
        [Test]
        public void Obs_ListsGraduatesNewestFirst_WithRolesAndTheNetworkLevels()
        {
            GameSession gs = Game(5UL, Difficulty.Elite);
            gs.PlayYears(5);
            Assert.GreaterOrEqual(gs.State.Obs.Count, 10);
            OBRecord roleOb = gs.State.Obs[2];
            roleOb.Role = ObRole.Coach;
            roleOb.Until = gs.State.AbsWeek + 10;
            ObView v = gs.Obs();
            Assert.AreEqual(gs.State.Obs.Count, v.Total);
            Assert.AreEqual(v.Total, v.Rows.Count);
            for (int i = 1; i < v.Rows.Count; i++)
            {
                Assert.GreaterOrEqual(v.Rows[i - 1].GradYear, v.Rows[i].GradYear, "卒業年度の新しい順");
                if (v.Rows[i - 1].GradYear == v.Rows[i].GradYear) Assert.Less(v.Rows[i - 1].Grad.RunnerId, v.Rows[i].Grad.RunnerId);
            }
            foreach (ObRowView r in v.Rows)
            {
                OBRecord ob = gs.State.Obs.Single(o => o.RunnerId == r.Grad.RunnerId && o.GradYear == r.GradYear);
                Assert.AreEqual(ob.Name, r.Grad.Name);
                Assert.AreEqual(ob.EnrollYear, r.EnrollYear);
                Assert.AreEqual(ob.CareerPath, r.Grad.Career);
                Assert.AreEqual(ob.HallOfFame, r.Grad.HallOfFame);
                Assert.AreEqual(ob.Role, r.Role);
            }
            Assert.AreEqual(gs.State.Obs.Count(o => o.Role != ObRole.None), v.Roles.Count, "役割のある OB だけ");
            ObRowView mine = v.Roles.Single(r => r.Grad.RunnerId == roleOb.RunnerId && r.GradYear == roleOb.GradYear);
            Assert.AreEqual(ObRole.Coach, mine.Role);
            Assert.AreEqual(10, mine.RoleWeeksLeft);
            Assert.IsTrue(v.Rows.Where(r => r.Role == ObRole.None).All(r => r.RoleWeeksLeft == 0));
            Assert.IsTrue(v.Roles.All(r => r.RoleWeeksLeft == Math.Max(0, gs.State.Obs.First(o => o.RunnerId == r.Grad.RunnerId && o.GradYear == r.GradYear).Until - gs.State.AbsWeek)));

            Assert.AreEqual(v.Total, v.Network.Sum(n => n.Count));
            foreach (ObNetworkRowView n in v.Network)
            {
                Assert.AreEqual(gs.State.Obs.Count(o => o.Pref == n.Pref), n.Count);
                int lv = Math.Min(5, (int)Math.Floor(Math.Sqrt(n.Count)));
                Assert.AreEqual(lv, n.Level);
                Assert.AreEqual(lv >= 5 ? 0 : (lv + 1) * (lv + 1) - n.Count, n.ToNextLevel);
                Assert.AreEqual(n.Pref >= 1 ? Geo.ById(n.Pref).Name : "", n.PrefName);
            }
            for (int i = 1; i < v.Network.Count; i++) Assert.Less(v.Network[i - 1].Pref == 0 ? 1000 : v.Network[i - 1].Pref, v.Network[i].Pref == 0 ? 1000 : v.Network[i].Pref, "県の番号順(海外は最後)");
        }

        [Test]
        public void Obs_NetworkLevelThresholds()
        {
            GameSession gs = Game();
            gs.State.Obs.Clear();
            Action<int, int> add = (pref, n) => { for (int i = 0; i < n; i++) gs.State.Obs.Add(new OBRecord { Pref = pref, RunnerId = 1000 * pref + i, Name = "x" }); };
            add(13, 1); add(14, 3); add(15, 4); add(16, 8); add(17, 9); add(18, 24); add(19, 25); add(20, 26); add(0, 2);
            Dictionary<int, ObNetworkRowView> net = gs.Obs().Network.ToDictionary(n => n.Pref);
            Assert.AreEqual(new[] { 1, 1, 2, 2, 3, 4, 5, 5 }, new[] { 13, 14, 15, 16, 17, 18, 19, 20 }.Select(p => net[p].Level).ToArray(), "Lv = min(5, floor(√人数))");
            Assert.AreEqual(new[] { 3, 1, 5, 1, 7, 1, 0, 0 }, new[] { 13, 14, 15, 16, 17, 18, 19, 20 }.Select(p => net[p].ToNextLevel).ToArray());
            Assert.AreEqual(1, net[0].Level, "海外もまとめて数える");
            Assert.AreEqual(0, gs.Obs().Network.Last().Pref, "海外は最後");
        }

        // ------------------------------------------------------------ 歴史
        [Test]
        public void History_FollowsTheYearlyStats_TitlesAndTheHallOfFame()
        {
            GameSession gs = Game(9UL, Difficulty.Elite);
            gs.PlayYears(4);
            gs.Stats[0].Wins = new[] { true, false, false };
            gs.Stats[1].Wins = new[] { true, true, true };
            gs.Stats[2].Wins = new[] { false, true, false };
            gs.Stats[3].Wins = new[] { false, false, false };
            HistoryView v = gs.History();
            Assert.AreEqual(gs.Stats.Count, v.Years.Count);
            for (int i = 0; i < gs.Stats.Count; i++)
            {
                PlayerYearStats st = gs.Stats[i];
                YearRecordView y = v.Years[i];
                Assert.AreEqual(st.Year, y.Year);
                Assert.AreEqual(st.P, y.P);
                Assert.AreEqual(st.PowerRank, y.PowerRank);
                CollectionAssert.AreEqual(st.Place, y.Place);
                CollectionAssert.AreEqual(st.Wins, y.Won);
                Assert.AreEqual(st.Points, y.EvalPoints);
                Assert.AreEqual(st.Prestige, y.Prestige);
                Assert.AreEqual(st.CoachRank, y.CoachRank);
            }
            Assert.IsFalse(v.Years[0].TripleCrown);
            Assert.IsTrue(v.Years[1].TripleCrown);
            Assert.AreEqual(1, v.TripleCrowns);
            Assert.AreEqual(1 + 3 + 1, v.Titles);
            Assert.AreEqual(new[] { gs.Stats[0].Year, gs.Stats[1].Year, gs.Stats[1].Year }, v.FirstWinYear, "各大会の初優勝の年度");
            Assert.IsNull(v.ClearYear);
            gs.State.Cleared = new ClearInfo { Year = 7, NoInvite = true };
            HistoryView c = gs.History();
            Assert.AreEqual(7, c.ClearYear);
            Assert.IsTrue(c.NoInvite);

            int hof = gs.State.Obs.Count(o => o.HallOfFame);
            gs.State.Obs[0].HallOfFame = true;
            gs.State.Obs[1].HallOfFame = true;
            HistoryView h = gs.History();
            Assert.AreEqual(gs.State.Obs.Count(o => o.HallOfFame), h.HallOfFame.Count);
            Assert.IsTrue(h.HallOfFame.All(r => r.Grad.HallOfFame));
            Assert.GreaterOrEqual(h.HallOfFame.Count, Math.Min(2, gs.State.Obs.Count));
            Assert.GreaterOrEqual(hof, 0);
        }

        [Test]
        public void History_CountsThisYearsTitlesBeforeTheYearIsSettled()
        {
            GameSession gs = Game(13UL, Difficulty.Mid);
            gs.PlayYears(1);
            typeof(GameSession).GetField("_yearWins", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(gs, 0b110);
            HistoryView v = gs.History();
            Assert.AreEqual(2, v.Titles, "今年度の優勝(第二戦・第三戦)は戦績に入る前でも数える");
            Assert.AreEqual(0, v.FirstWinYear[0]);
            Assert.AreEqual(gs.Year, v.FirstWinYear[1]);
            Assert.AreEqual(gs.Year, v.FirstWinYear[2]);
        }

        [Test]
        public void History_PrefecturesCountMembersAndOb_Together()
        {
            GameSession gs = Game(15UL, Difficulty.Elite);
            gs.PlayYears(5);
            HistoryView v = gs.History();
            var expected = new SortedDictionary<int, int>();
            foreach (Runner r in gs.State.Runners) if (!r.Overseas && r.Pref >= 1) { expected.TryGetValue(r.Pref, out int n); expected[r.Pref] = n + 1; }
            foreach (OBRecord o in gs.State.Obs) if (o.Pref >= 1) { expected.TryGetValue(o.Pref, out int n); expected[o.Pref] = n + 1; }
            CollectionAssert.AreEqual(expected.Keys, v.Prefectures.Select(p => p.Pref).ToList());
            CollectionAssert.AreEqual(expected.Values, v.Prefectures.Select(p => p.Count).ToList());
            Assert.AreEqual(expected.Count, v.PrefecturesCovered);
            Assert.IsTrue(v.Prefectures.All(p => p.PrefName == Geo.ById(p.Pref).Name));
            Assert.LessOrEqual(v.PrefecturesCovered, 47);
            Assert.Greater(v.PrefecturesCovered, 5);
        }
    }
}
