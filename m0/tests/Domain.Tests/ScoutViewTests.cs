using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Game;
using Ekiden.Domain.Recruit;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Text;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>スカウトの読み取り API(SC-27〜30。08 R-08・08b)。『見えているもの = 入学したときの実物』と、段階ごとの見え方。</summary>
    public class ScoutViewTests
    {
        private static GameSession Game(ulong seed = 20260403UL, Difficulty d = Difficulty.Mid) { return GameSession.New(seed, d); }

        private static void ToMonth(GameSession gs, int monthIndex)
        {
            while (gs.YearWeek / 4 != monthIndex) gs.StepWeek();
        }

        // ------------------------------------------------------------ 一覧と見え方
        [Test]
        public void Candidates_AreListedByValue_WithTheFixedFields()
        {
            GameSession gs = Game();
            List<ScoutCandidateView> list = gs.ScoutCandidates();
            Assert.AreEqual(WorldK.K_POOL_N, list.Count);
            for (int i = 1; i < list.Count; i++)
            {
                Assert.GreaterOrEqual(list[i - 1].V, list[i].V, "V の高い順(確定の順番)");
                if (list[i - 1].V == list[i].V) Assert.Less(list[i - 1].Index, list[i].Index);
            }
            Assert.AreEqual(list.Count, list.Select(c => c.Index).Distinct().Count());
            foreach (ScoutCandidateView c in list)
            {
                WorldRunner w = gs.ScoutPool[c.Index];
                Assert.AreEqual(w.V, c.V);
                Assert.AreEqual(w.V < 42 ? 1 : (w.V < 48 ? 2 : (w.V < 54 ? 3 : (w.V < 60 ? 4 : 5))), c.Stars);
                Assert.AreEqual(w.V >= 51.5, c.TopTier);
                Assert.AreEqual(w.Overseas, c.Overseas);
                PersonName pn = Ekiden.Domain.Text.NameGenerator.Generate(gs.Rngs, w.LookSeed, w.Overseas);
                Assert.AreEqual(pn.Family + pn.Given, c.FamilyName + c.GivenName, "留学生は仮の名前の組み合わせ");
                Assert.IsNotEmpty(c.FamilyName);
                Assert.IsNotEmpty(c.GivenName);
                Assert.IsNotEmpty(c.FamilyKana);
                Assert.AreEqual(ScoutStage.None, c.Stage);
                Assert.IsFalse(c.QObs.HasValue);
                Assert.IsEmpty(c.StyleChips, "視察するまで個性は見えない");
                Assert.IsFalse(c.Growth.HasValue);
                Assert.IsFalse(c.HasGift.HasValue);
                Assert.IsEmpty(c.GiftKinds);
                if (c.Overseas) { Assert.AreEqual(3, c.Band); Assert.IsNull(c.HighSchoolNameKey); Assert.IsNotEmpty(c.CountryName); Assert.AreEqual(0, c.Pref); }
                else
                {
                    Assert.AreEqual(Geo.ById(w.PrefectureId).Name, c.PrefName);
                    Assert.AreEqual(Geo.Band(Geo.ById(w.PrefectureId).Region, gs.School.Region), c.Band);
                    StringAssert.StartsWith("hs.", c.HighSchoolNameKey);
                }
                Assert.Greater(c.Pb5000, 0);
            }
            Assert.IsTrue(list.Any(c => c.Overseas) && list.Any(c => !c.Overseas));
            Assert.IsTrue(Enumerable.Range(0, 4).All(b => list.Any(c => c.Band == b)), "距離帯 0〜3 がそろう");
        }

        [Test]
        public void Candidate_ShowsMoreAsTheStagesAdvance_AndOnlyWhatSpecSays()
        {
            foreach (int scoutLv in new[] { 2, 3 })
            {
                GameSession gs = Game(31UL);
                var f = gs.State.Fac; f.Scout = scoutLv; gs.State.Fac = f;
                gs.AutoUpgrade = false;
                ToMonth(gs, 5);                                             // 視察の時期(6〜11 月)
                gs.State.ScoutPts = 50;
                int idx = gs.ScoutCandidates().First(c => !c.Overseas && c.V < 50 && Individuality.GenerateGifts(gs.Rngs, gs.ScoutPool[c.Index].LookSeed).Length > 0).Index;       // 素養を持つ候補
                ScoutCandidateView none = gs.ScoutCandidate(idx);
                Assert.IsTrue(none.CanObserve);
                Assert.IsFalse(none.CanTalk);
                Assert.IsTrue(gs.ScoutObserve(idx).Ok);
                ScoutCandidateView obs = gs.ScoutCandidate(idx);
                Assert.AreEqual(ScoutStage.Observed, obs.Stage);
                Assert.IsTrue(obs.QObs.HasValue);
                Assert.AreEqual(Scouting.ObserveQ(gs.ScoutPool[idx], gs.School.ScoutLv, gs.Rngs), obs.QObs.Value, 0.0);
                Assert.AreEqual(Scouting.InfoSd(gs.School.ScoutLv), obs.Sigma);
                Assert.IsNotEmpty(obs.StyleChips);
                Assert.IsFalse(obs.Growth.HasValue);
                Assert.IsFalse(obs.HasGift.HasValue, "素養の有無は練習会のあと");
                Assert.IsFalse(obs.CanObserve);
                Assert.IsFalse(obs.CanTalk, "声かけは 12〜2 月");

                ToMonth(gs, 8);                                             // 声かけ・練習会の時期
                gs.State.ScoutPts = 50;
                Assert.IsTrue(gs.ScoutCandidate(idx).CanTalk);
                Assert.IsFalse(gs.ScoutCandidate(idx).CanInvite, "練習会は声かけのあと");
                Assert.IsTrue(gs.ScoutTalk(idx).Ok);
                ScoutCandidateView talked = gs.ScoutCandidate(idx);
                Assert.IsFalse(talked.Growth.HasValue);
                Assert.IsTrue(talked.CanInvite);
                Assert.IsTrue(gs.ScoutInvite(idx).Ok);
                ScoutCandidateView inv = gs.ScoutCandidate(idx);
                Assert.AreEqual(gs.ScoutPool[idx].Growth, inv.Growth.Value);
                Gift[] gifts = Individuality.GenerateGifts(gs.Rngs, gs.ScoutPool[idx].LookSeed);
                Assert.AreEqual(gifts.Length > 0, inv.HasGift.Value);
                if (scoutLv >= 3) CollectionAssert.AreEqual(gifts, inv.GiftKinds, "種類はスカウト担当 Lv3 以上のとき");
                else Assert.IsEmpty(inv.GiftKinds, "Lv2 以下は入部後");
            }
        }

        [Test]
        public void WhatIsSeenOfACandidate_IsWhatEnrolls()
        {
            GameSession gs = Game(33UL, Difficulty.Mid);
            gs.AutoUpgrade = false;
            var f = gs.State.Fac; f.Scout = 3; gs.State.Fac = f;
            ToMonth(gs, 3);
            gs.State.ScoutPts = 200;
            List<ScoutCandidateView> top = gs.ScoutCandidates().Where(c => !c.TopTier).Take(8).ToList();
            foreach (ScoutCandidateView c in top) Assert.IsTrue(gs.ScoutObserve(c.Index).Ok);
            ToMonth(gs, 9);
            gs.State.ScoutPts = 200;
            foreach (ScoutCandidateView c in top) { Assert.IsTrue(gs.ScoutTalk(c.Index).Ok); Assert.IsTrue(gs.ScoutInvite(c.Index).Ok); }
            var seen = top.Select(c => gs.ScoutCandidate(c.Index)).ToList();                // 練習会まで進めた候補の見え方
            var worldPool = gs.ScoutPool.ToDictionary(w => w.LookSeed, w => w.Index);
            while (gs.YearWeek != 47) gs.StepWeek();
            gs.ResolveTraining();
            Ekiden.Domain.Events.RaceInfo? o;
            gs.PlayWeekRaces(out o);
            gs.ResolveWeekEvents(gs.SelectWeekEvents(o), o, new Ekiden.Domain.Events.DefaultChoicePolicy());
            gs.FinishWeek();
            gs.SettleYear();
            gs.TurnoverYear();
            int matched = 0;
            foreach (ScoutCandidateView c in seen)
            {
                Runner r = gs.State.Runners.FirstOrDefault(x => x.LookSeed == gs.ScoutPool[c.Index].LookSeed && x.Grade == 1);
                if (r == null) continue;                                                     // 定員・競り負けで入らなかった候補
                matched++;
                Assert.AreEqual(c.FamilyName + c.GivenName, r.FamilyName + r.GivenName, "候補のときの名前 = 入学後の名前");
                Assert.AreEqual(c.FamilyKana, r.FamilyKana);
                Assert.AreEqual(c.Pref, r.Pref);
                Assert.AreEqual(c.HighSchoolNameKey, "hs." + r.HighSchool + ".name");
                Assert.AreEqual(c.Pb5000, DisplayRules.DerivePb(r.Stats, DisplayRules.PbEvent.M5000), "持ちタイム = 入学時の能力から");
                CollectionAssert.AreEqual(c.Traits, r.Traits.Select(t => t.ToLowerInvariant()).ToList());
                CollectionAssert.AreEqual(c.StyleChips, Presentation.StyleChips(r.Style.Value), "個性チップ");
                Assert.AreEqual((GrowthType)(int)c.Growth.Value, r.GrowthType);
                Assert.AreEqual(c.HasGift.Value, r.Gifts.Length > 0);
                CollectionAssert.AreEqual(c.GiftKinds, r.Gifts, "見えていた素養の種類 = 実物");
                Assert.IsTrue(r.GiftRevealed, "種類まで見えていたので入部と同時に判明");
            }
            Assert.GreaterOrEqual(matched, 4, "狙った候補の多くが入学する(試験の前提)");
        }

        [Test]
        public void ActionFlags_AgreeWithTheCommands()
        {
            GameSession gs = Game(35UL);
            foreach (int month in new[] { 1, 4, 9 })
            {
                ToMonth(gs, month);
                gs.State.ScoutPts = month == 9 ? 6 : 3;
                foreach (ScoutCandidateView c in gs.ScoutCandidates().Take(8))
                {
                    ScoutCandidateView before = gs.ScoutCandidate(c.Index);
                    ScoutStage st = before.Stage;
                    var tries = new[]
                    {
                        new KeyValuePair<bool, Func<OpResult>>(before.CanObserve, () => gs.ScoutObserve(c.Index)),
                        new KeyValuePair<bool, Func<OpResult>>(before.CanTalk, () => gs.ScoutTalk(c.Index)),
                        new KeyValuePair<bool, Func<OpResult>>(before.CanInvite, () => gs.ScoutInvite(c.Index)),
                    };
                    foreach (var t in tries)
                    {
                        ScoutCandidateView fresh = gs.ScoutCandidate(c.Index);
                        if (fresh.Stage != st) break;                                          // 先の試行で段階が進んだら、この候補は終わり
                        OpResult res = t.Value();
                        Assert.AreEqual(t.Key, res.Ok, "月 " + month + " 候補 " + c.Index + " 段階 " + st + ": " + res.Reason);
                        if (res.Ok) break;
                    }
                }
            }
        }

        // ------------------------------------------------------------ 地図
        [Test]
        public void Map_CountsCandidatesByRegionAndPrefecture_AndTheTimeOfYear()
        {
            GameSession gs = Game(37UL, Difficulty.Elite);
            var phases = new Dictionary<int, ScoutPhase>();
            for (int m = 0; m < 12; m++)
            {
                ToMonth(gs, m);
                phases[m] = gs.ScoutMap().Phase;
                Assert.AreEqual(m, gs.ScoutMap().MonthIndex);
            }
            Assert.AreEqual(new[] { ScoutPhase.Closed, ScoutPhase.Closed, ScoutPhase.Observe, ScoutPhase.Observe, ScoutPhase.Observe, ScoutPhase.Observe, ScoutPhase.Observe, ScoutPhase.Observe,
                                    ScoutPhase.Recruit, ScoutPhase.Recruit, ScoutPhase.Recruit, ScoutPhase.Confirm }, Enumerable.Range(0, 12).Select(m => phases[m]).ToArray());

            gs = Game(37UL, Difficulty.Elite);
            ToMonth(gs, 4);
            gs.State.ScoutPts = 100;
            foreach (ScoutCandidateView c in gs.ScoutCandidates().Take(30)) gs.ScoutObserve(c.Index);
            ToMonth(gs, 8);
            gs.State.ScoutPts = 100;
            foreach (ScoutCandidateView c in gs.ScoutCandidates().Take(5)) { gs.ScoutTalk(c.Index); if (c.Index % 2 == 0) gs.ScoutInvite(c.Index); }
            ScoutMapView m2 = gs.ScoutMap();
            var pool = gs.ScoutPool;
            Assert.AreEqual(pool.Count, m2.Regions.Sum(r => r.Candidates) + m2.OverseasCandidates);
            Assert.AreEqual(pool.Count(c => c.Overseas), m2.OverseasCandidates);
            Assert.AreEqual(pool.Count(c => !c.Overseas), m2.Prefs.Sum(p => p.Candidates));
            Assert.AreEqual(47, m2.Prefs.Count);
            Assert.AreEqual(8, m2.Regions.Count);
            foreach (ScoutPrefView p in m2.Prefs)
            {
                var inPref = pool.Where(c => !c.Overseas && c.PrefectureId == p.Pref).ToList();
                Assert.AreEqual(inPref.Count, p.Candidates);
                Assert.AreEqual(inPref.Count(c => c.V >= 60), p.Stars5);
                Assert.AreEqual(inPref.Count(c => gs.StageOf(c.Index) >= ScoutStage.Observed), p.Observed);
                Assert.AreEqual(inPref.Count(c => gs.StageOf(c.Index) >= ScoutStage.Talked), p.Targets);
                Assert.AreEqual(Geo.Band(Geo.ById(p.Pref).Region, gs.School.Region), p.Band);
            }
            foreach (ScoutRegionView r in m2.Regions)
            {
                var inRegion = pool.Where(c => !c.Overseas && c.Region == r.Region).ToList();
                Assert.AreEqual(inRegion.Count, r.Candidates);
                Assert.AreEqual(inRegion.Sum(c => gs.StageOf(c.Index) >= ScoutStage.Observed ? 1 : 0), r.Observed);
            }
            Assert.AreEqual(_scoutCount(gs, ScoutStage.Observed), m2.Observed);
            Assert.AreEqual(_scoutCount(gs, ScoutStage.Talked), m2.Talked);
            Assert.AreEqual(_scoutCount(gs, ScoutStage.Invited), m2.Invited);
            Assert.AreEqual(36 + 4 * (gs.State.CoachRank / 3), m2.PointsYearTotal);
            gs.State.CoachRank = 6;
            Assert.AreEqual(44, gs.ScoutMap().PointsYearTotal, "ランクの追加分 4 × floor(6 / 3)");
            Assert.AreEqual(gs.ScoutPointsAvailable, m2.PointsAvailable);
        }

        private static int _scoutCount(GameSession gs, ScoutStage atLeast)
        {
            return atLeast == ScoutStage.Invited ? gs.ScoutStages.Count(kv => kv.Value == ScoutStage.Invited) : gs.ScoutStages.Count(kv => kv.Value >= atLeast);
        }

        [Test]
        public void Map_ObNetworkLevelPerPrefecture()
        {
            GameSession gs = Game();
            gs.State.Obs.Clear();
            for (int i = 0; i < 9; i++) gs.State.Obs.Add(new OBRecord { Pref = 13, RunnerId = i + 1, Name = "x" });
            for (int i = 0; i < 3; i++) gs.State.Obs.Add(new OBRecord { Pref = 14, RunnerId = 100 + i, Name = "y" });
            ScoutMapView m = gs.ScoutMap();
            Assert.AreEqual(3, m.Prefs.Single(p => p.Pref == 13).ObLevel);
            Assert.AreEqual(9, m.Prefs.Single(p => p.Pref == 13).ObCount);
            Assert.AreEqual(1, m.Prefs.Single(p => p.Pref == 14).ObLevel);
            Assert.AreEqual(0, m.Prefs.Single(p => p.Pref == 15).ObLevel);
        }

        // ------------------------------------------------------------ 目標とポイント
        [Test]
        public void Targets_AreOrderedByValue_WithTheSlotMeter()
        {
            GameSession gs = Game(39UL, Difficulty.Mid);
            ToMonth(gs, 4);
            gs.State.ScoutPts = 300;
            List<ScoutCandidateView> picks = gs.ScoutCandidates().Skip(20).Take(10).ToList();
            foreach (ScoutCandidateView c in picks) gs.ScoutObserve(c.Index);
            ToMonth(gs, 8);
            gs.State.ScoutPts = 300;
            for (int n = 1; n <= 10; n++)
            {
                ScoutCandidateView c = picks[picks.Count - n];                      // V の低い方から狙っても、順番は V 順
                Assert.IsTrue(gs.ScoutTalk(c.Index).Ok);
                if (n % 2 == 0) Assert.IsTrue(gs.ScoutInvite(c.Index).Ok);
                ScoutTargetsView t = gs.ScoutTargets();
                Assert.AreEqual(n, t.Targets.Count);
                Assert.AreEqual(n, t.Meter.TargetCount);
                for (int i = 0; i < t.Targets.Count; i++)
                {
                    Assert.AreEqual(i + 1, t.Targets[i].ConfirmOrder);
                    Assert.AreEqual(i + 1 >= 9, t.Targets[i].Unlikely, "9 番目以降は『ほぼ入れません』");
                    if (i > 0) Assert.GreaterOrEqual(t.Targets[i - 1].Candidate.V, t.Targets[i].Candidate.V);
                }
                Assert.AreEqual(10, t.Meter.Capacity);
                Assert.AreEqual(10, t.Meter.Usable);
                Assert.AreEqual(3, t.Meter.NaturalIntake);
                Assert.AreEqual(8, t.Meter.Recommended);
                Assert.AreEqual(n > 8, t.Meter.OverRecommended, "推奨の目標 8 人を超えたら");
                Assert.AreEqual(n > 7, t.Meter.OverCapacity, "使える枠 − 自然に入る目安 = 7 を超えたら");
                Assert.AreEqual(t.Targets.Count(x => x.Candidate.TopTier), t.Meter.TopTierTargets);
                Assert.AreEqual(gs.School.ScholSlots(), t.Meter.ScholarshipSlots);
                Assert.AreEqual(gs.ScoutPointsAvailable, t.PointsAvailable);
                Assert.AreEqual(n, t.Talked);
                Assert.AreEqual(n / 2, t.Invited);
            }
            gs.State.Tickets.Pending = new PendingInvite { Kind = TicketKind.Rookie, EnrollYear = gs.Year + 1 };
            ScoutSlotMeterView withTicket = gs.ScoutTargets().Meter;
            Assert.AreEqual(1, withTicket.TicketReserved);
            Assert.AreEqual(9, withTicket.Usable, "特別招聘の予約ぶん、使える枠が減る");
            Assert.IsTrue(withTicket.OverCapacity);
            gs.State.Tickets.Pending = null;
        }

        [Test]
        public void Targets_AreEmpty_WithoutAnyAction()
        {
            GameSession gs = Game();
            ScoutTargetsView t = gs.ScoutTargets();
            Assert.IsEmpty(t.Targets);
            Assert.AreEqual(0, t.Meter.TargetCount);
            Assert.IsFalse(t.Meter.OverRecommended);
            Assert.IsFalse(t.Meter.OverCapacity);
            Assert.AreEqual(0, t.Observed);
            Assert.IsNull(gs.ScoutCandidate(-1));
            Assert.IsNull(gs.ScoutCandidate(WorldK.K_POOL_N));
        }
    }
}
