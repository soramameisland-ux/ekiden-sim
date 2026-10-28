using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>見どころの判定(04 §9.3。08b G-02)と、SC-25 のダイジェスト・SC-26 の『大会の見どころ』。</summary>
    public class RaceHighlightTests
    {
        /// <summary>区間タイムから結果を組む(累積は素直な和。繰り上げなし)。</summary>
        private static TeamResult Team(int id, int[] sections, params RaceEvent[] events)
        {
            var t = new TeamResult { Id = id, Sections = sections, Cum = new int[sections.Length], Boost = new bool[sections.Length] };
            int sum = 0;
            for (int i = 0; i < sections.Length; i++) { sum += sections[i]; t.Cum[i] = sum; }
            t.TotalDs = sum;
            t.Events.AddRange(events);
            return t;
        }

        private static List<Highlight> Of(List<Highlight> all, HighlightKind kind) { return all.Where(h => h.Kind == kind).ToList(); }

        // ------------------------------------------------------------ 個々の判定(手で作った結果)
        [Test]
        public void LeadChange_OnlyWhenTheLeaderOfTheStationChanges_TiesGoToTheSmallerId()
        {
            // 1 区は T1 が首位、2 区で T2 が逆転(差 50)。1 区の時点では『前の首位』がないので首位交代にならない
            var a = RaceHighlights.Detect(new[] { Team(1, new[] { 1000, 1100 }), Team(2, new[] { 1050, 1000 }) }, -1);
            Highlight lc = Of(a, HighlightKind.LeadChange).Single();
            Assert.AreEqual(1, lc.Section);
            Assert.AreEqual(2, lc.TeamId);
            Assert.AreEqual(1, lc.OtherTeamId);
            Assert.AreEqual(50, lc.Magnitude);
            // 同着ならチーム ID の小さい方が首位: T5 が 1 区の首位、2 区で T2 と同着 → 首位は T2
            var b = RaceHighlights.Detect(new[] { Team(5, new[] { 1000, 1000 }), Team(2, new[] { 1010, 990 }) }, -1);
            Highlight tie = Of(b, HighlightKind.LeadChange).Single();
            Assert.AreEqual(2, tie.TeamId);
            Assert.AreEqual(5, tie.OtherTeamId);
            Assert.AreEqual(0, tie.Magnitude);
            // 同じ首位が続くなら出ない
            var c = RaceHighlights.Detect(new[] { Team(1, new[] { 1000, 1000 }), Team(2, new[] { 1010, 1000 }) }, -1);
            Assert.IsEmpty(Of(c, HighlightKind.LeadChange));
        }

        [Test]
        public void GapMove_Needs20Seconds_AndCountsFromZeroAtTheFirstStation()
        {
            // 1 区で T2 が 199 ds(19.9 秒)遅れ → 出ない。200 ds ちょうど → 出る
            Assert.IsEmpty(Of(RaceHighlights.Detect(new[] { Team(1, new[] { 1000, 1000 }), Team(2, new[] { 1199, 1000 }) }, -1), HighlightKind.GapMove));
            Highlight g = Of(RaceHighlights.Detect(new[] { Team(1, new[] { 1000, 1000 }), Team(2, new[] { 1200, 1000 }) }, -1), HighlightKind.GapMove).Single();
            Assert.AreEqual(0, g.Section);
            Assert.AreEqual(2, g.TeamId);
            Assert.AreEqual(200, g.Delta, "正 = 先頭との差が開いた");
            Assert.AreEqual(200, g.Magnitude);
            // 2 区で 200 ds 縮めた(縮める側は負)。先頭との差は 400 → 200
            var r = RaceHighlights.Detect(new[] { Team(1, new[] { 1000, 1000 }), Team(2, new[] { 1400, 800 }) }, -1);
            Highlight gain = Of(r, HighlightKind.GapMove).Single(h => h.Section == 1);
            Assert.AreEqual(2, gain.TeamId);
            Assert.AreEqual(-200, gain.Delta);
            Assert.AreEqual("hl.gap_gain", HighlightView.KeyOf(gain.Kind, gain.Delta));
            Assert.AreEqual("hl.gap_lose", HighlightView.KeyOf(HighlightKind.GapMove, 200));
        }

        [Test]
        public void RankMove_NeedsThreePlaces_AndNotAtTheFirstStation()
        {
            // 1 区の終わりは T1..T5 の順(累積 1000, 1010, 1020, 1030, 1040)。2 区は T2〜T4 が 1000 で、T1 と T5 の区間タイムだけ変える
            Func<int, int, List<Highlight>> run = (t1Second, t5Second) =>
            {
                var teams = new List<TeamResult>
                {
                    Team(1, new[] { 1000, t1Second }), Team(2, new[] { 1010, 1000 }), Team(3, new[] { 1020, 1000 }), Team(4, new[] { 1030, 1000 }), Team(5, new[] { 1040, t5Second }),
                };
                return Of(RaceHighlights.Detect(teams, -1), HighlightKind.RankMove);
            };
            Assert.IsEmpty(run(1000, 975), "T5 は累積 2015 で 5 位 → 3 位(2 つ上がっただけ)");
            Assert.IsEmpty(run(1000, 970), "T5 は累積 2010 で T2 と同着。ID が大きいので 3 位(2 つ)");
            Highlight up = run(1000, 965).Single();
            Assert.AreEqual(1, up.Section);
            Assert.AreEqual(5, up.TeamId);
            Assert.AreEqual(3, up.Delta, "5 位 → 2 位(正 = 上がった)");
            Assert.AreEqual(3, up.Magnitude);
            Assert.AreEqual("hl.rank_up", HighlightView.KeyOf(up.Kind, up.Delta));
            Assert.IsEmpty(run(1030, 1000), "T1 は累積 2030 で T4 と同着。ID が小さいので 4 位とは同着で T1 が先 → 3 位(2 つ)");
            Highlight down = run(1031, 1000).Single();
            Assert.AreEqual(1, down.TeamId);
            Assert.AreEqual(-3, down.Delta, "1 位 → 4 位");
            Assert.AreEqual("hl.rank_down", HighlightView.KeyOf(down.Kind, down.Delta));
            // 1 区の終わりは『前の順位』がない(全員同時にスタート)ので、順位の変動は出ない
            Assert.IsEmpty(Of(RaceHighlights.Detect(new[] { Team(1, new[] { 5000 }), Team(2, new[] { 100 }), Team(3, new[] { 200 }), Team(4, new[] { 300 }) }, -1), HighlightKind.RankMove));
        }

        [Test]
        public void Incidents_BrakeStallAndBoost_AreReportedPerSection_GekiAndKuriageAreNot()
        {
            var t1 = Team(1, new[] { 1000, 1000 },
                new RaceEvent { Kind = "brake", Section = 0, Value = 0.05 }, new RaceEvent { Kind = "stall", Section = 1, Value = 0.03 },
                new RaceEvent { Kind = "geki_brake", Section = 0 }, new RaceEvent { Kind = "kuriage", Section = 1 });
            t1.Boost[1] = true;
            var all = RaceHighlights.Detect(new[] { t1, Team(2, new[] { 1000, 1000 }) }, 1);
            Highlight br = Of(all, HighlightKind.Brake).Single();
            Assert.AreEqual(0, br.Section); Assert.AreEqual(0.05, br.Magnitude); Assert.AreEqual(1, br.TeamId); Assert.IsTrue(br.IsOwn);
            Highlight st = Of(all, HighlightKind.Stall).Single();
            Assert.AreEqual(1, st.Section); Assert.AreEqual(0.03, st.Magnitude);
            Highlight bo = Of(all, HighlightKind.Boost).Single();
            Assert.AreEqual(1, bo.Section); Assert.AreEqual(0.02, bo.Magnitude, 1e-12);
            Assert.AreEqual(3, br.Tier); Assert.AreEqual(3, st.Tier); Assert.AreEqual(3, bo.Tier);
            Assert.IsTrue(Of(all, HighlightKind.Brake).All(h => !h.IsOwn || h.TeamId == 1));
            Assert.IsFalse(Of(RaceHighlights.Detect(new[] { t1, Team(2, new[] { 1000, 1000 }) }, 2), HighlightKind.Brake).Single().IsOwn, "自校でなければ印は付かない");
        }

        [Test]
        public void SectionPrize_TiedTeamsAllGetIt_AndTheMarginIsToTheNextBest()
        {
            var all = RaceHighlights.Detect(new[] { Team(1, new[] { 1000 }), Team(2, new[] { 1000 }), Team(3, new[] { 1030 }) }, -1);
            var prizes = Of(all, HighlightKind.SectionPrize);
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, prizes.Select(h => h.TeamId).ToArray());
            Assert.IsTrue(prizes.All(h => h.Magnitude == 30), "2 位(別のタイム)との差");
            var solo = Of(RaceHighlights.Detect(new[] { Team(1, new[] { 990 }), Team(2, new[] { 1000 }) }, -1), HighlightKind.SectionPrize).Single();
            Assert.AreEqual(10, solo.Magnitude);
            var allSame = Of(RaceHighlights.Detect(new[] { Team(1, new[] { 1000 }), Team(2, new[] { 1000 }) }, -1), HighlightKind.SectionPrize);
            Assert.AreEqual(2, allSame.Count);
            Assert.IsTrue(allSame.All(h => h.Magnitude == 0));
        }

        [Test]
        public void EveryKindMapsToAKnownKey_AndEveryKeyIsUsed()
        {
            var used = new HashSet<string>();
            foreach (HighlightKind k in Enum.GetValues(typeof(HighlightKind)))
                foreach (int delta in new[] { -3, 0, 3 }) used.Add(HighlightView.KeyOf(k, delta));
            CollectionAssert.AreEquivalent(HighlightView.AllKeys, used);
        }

        // ------------------------------------------------------------ 並びと件数
        private static Highlight H(int section, HighlightKind kind, int team, bool own, double mag)
        {
            return new Highlight { Section = section, Kind = kind, TeamId = team, IsOwn = own, Magnitude = mag };
        }

        [Test]
        public void Priority_TierThenOwnThenMagnitudeThenKindThenSectionThenTeam()
        {
            var list = new List<Highlight>
            {
                H(2, HighlightKind.RankMove, 1, true, 5),
                H(2, HighlightKind.SectionPrize, 9, false, 100),
                H(2, HighlightKind.Boost, 3, false, 0.02),
                H(2, HighlightKind.Brake, 4, false, 0.08),
                H(2, HighlightKind.Brake, 5, true, 0.03),
                H(2, HighlightKind.GapMove, 6, false, 300),
                H(2, HighlightKind.LeadChange, 7, false, 10),
                H(2, HighlightKind.Brake, 2, false, 0.08),
            };
            var sorted = new List<Highlight>(list);
            RaceSimulator.StableSort(sorted, RaceHighlights.Compare);
            CollectionAssert.AreEqual(new[] { 7, 6, 5, 2, 4, 3, 9, 1 }, sorted.Select(h => h.TeamId).ToArray(),
                "首位交代 → 差 → ブレーキ(自校が先。同じ大きさは ID の小さい方)→ 好走 → 区間賞 → 順位");
            // 逆順に並べても同じ(全順序)
            var rev = new List<Highlight>(list); rev.Reverse();
            RaceSimulator.StableSort(rev, RaceHighlights.Compare);
            CollectionAssert.AreEqual(sorted.Select(h => h.TeamId).ToArray(), rev.Select(h => h.TeamId).ToArray());
        }

        [Test]
        public void Digest_TwoPerSection_AndTop_FiveInTimeOrder()
        {
            var detected = new List<Highlight>
            {
                H(0, HighlightKind.LeadChange, 1, false, 1), H(0, HighlightKind.GapMove, 2, false, 300), H(0, HighlightKind.SectionPrize, 3, false, 5),
                H(1, HighlightKind.Brake, 4, true, 0.04),
                H(2, HighlightKind.SectionPrize, 5, false, 9), H(2, HighlightKind.RankMove, 6, false, 3), H(2, HighlightKind.RankMove, 7, false, 4),
                H(3, HighlightKind.Boost, 8, false, 0.02), H(3, HighlightKind.SectionPrize, 9, false, 0),
            };
            var d = RaceHighlights.Digest(detected);
            CollectionAssert.AreEqual(new[] { 1, 2, 4, 5, 6, 8, 9 }, d.Select(h => h.TeamId).ToArray(), "区間ごとに先頭から 2 件");
            var top = RaceHighlights.Top(detected, 5);
            // 全体の重要度の上位 5 件: 首位交代(1)・差(2)・ブレーキ(4)・好走(8)・区間賞(5: 大きさ 9)→ 区間の順
            CollectionAssert.AreEqual(new[] { 1, 2, 4, 5, 8 }, top.Select(h => h.TeamId).ToArray());
            Assert.AreEqual(3, RaceHighlights.Top(detected, 3).Count);
            Assert.AreEqual(detected.Count, RaceHighlights.Top(detected, 100).Count);
            Assert.IsEmpty(RaceHighlights.Detect(new List<TeamResult>(), 1));
            Assert.IsEmpty(RaceHighlights.Detect(null, 1));
        }

        // ------------------------------------------------------------ 本物の結果での突き合わせ(別の書き方で数え直す)
        private static List<RaceOutcome> Races()
        {
            var list = new List<RaceOutcome>();
            foreach (ulong seed in new ulong[] { 20261102UL, 7UL, 11UL, 42UL })
            {
                GameSession gs = GameSession.New(seed, seed % 2 == 0 ? Difficulty.Mid : Difficulty.Elite);
                for (int w = 0; w < 48 * 2; w++)
                {
                    if (Array.IndexOf(Ekiden.Domain.Team.Calendar.RaceWeeks, gs.YearWeek) >= 0)
                    {
                        gs.ResolveTraining();
                        RaceInfo? o;
                        RaceOutcome r = gs.PlayWeekRaces(out o);
                        var sel = gs.SelectWeekEvents(o);
                        gs.ResolveWeekEvents(sel, o, new DefaultChoicePolicy());
                        gs.FinishWeek();
                        if (r != null) list.Add(r);
                        gs.AdvanceWeek();
                    }
                    else gs.StepWeek();
                }
            }
            return list;
        }

        /// <summary>独立の数え直し: 順位は『自分より速い校の数 + 1』(同着は ID)、首位・差・賞は全チームを直接見る。</summary>
        private static List<string> Brute(IReadOnlyList<TeamResult> res, int own, int through)
        {
            var o = new List<string>();
            Func<int, int, int> rankAt = (idx, s) => 1 + res.Count(u => u.Cum[s] < res[idx].Cum[s] || (u.Cum[s] == res[idx].Cum[s] && u.Id < res[idx].Id));
            Func<int, TeamResult> leaderAt = s => res.Single(t => rankAt(res.ToList().IndexOf(t), s) == 1);
            Func<int, int> minAt = s => res.Min(t => t.Cum[s]);
            for (int s = 0; s < through; s++)
            {
                if (s > 0 && leaderAt(s).Id != leaderAt(s - 1).Id)
                    o.Add(string.Join("|", s, HighlightKind.LeadChange, leaderAt(s).Id, leaderAt(s - 1).Id, leaderAt(s - 1).Cum[s] - leaderAt(s).Cum[s], 0));
                int best = res.Min(t => t.Sections[s]);
                var worse = res.Where(t => t.Sections[s] > best).Select(t => t.Sections[s]).ToList();
                for (int i = 0; i < res.Count; i++)
                {
                    TeamResult t = res[i];
                    int gap = t.Cum[s] - minAt(s), prev = s > 0 ? t.Cum[s - 1] - minAt(s - 1) : 0;
                    if (Math.Abs(gap - prev) >= 200) o.Add(string.Join("|", s, HighlightKind.GapMove, t.Id, -1, Math.Abs(gap - prev), gap - prev));
                    foreach (RaceEvent e in t.Events.Where(e => e.Section == s && (e.Kind == "brake" || e.Kind == "stall")))
                        o.Add(string.Join("|", s, e.Kind == "brake" ? HighlightKind.Brake : HighlightKind.Stall, t.Id, -1, e.Value.ToString("R"), 0));
                    if (t.Boost[s]) o.Add(string.Join("|", s, HighlightKind.Boost, t.Id, -1, (-RaceConstants.BoostAdj).ToString("R"), 0));
                    if (t.Sections[s] == best) o.Add(string.Join("|", s, HighlightKind.SectionPrize, t.Id, -1, worse.Count == 0 ? 0 : worse.Min() - best, 0));
                    if (s > 0)
                    {
                        int up = rankAt(i, s - 1) - rankAt(i, s);
                        if (Math.Abs(up) >= 3) o.Add(string.Join("|", s, HighlightKind.RankMove, t.Id, -1, Math.Abs(up), up));
                    }
                }
            }
            o.Sort(StringComparer.Ordinal);
            return o;
        }

        private static List<string> Flat(IEnumerable<Highlight> hs)
        {
            var l = hs.Select(h => string.Join("|", h.Section, h.Kind, h.TeamId, h.OtherTeamId, h.Magnitude.ToString("R"), h.Delta)).ToList();
            l.Sort(StringComparer.Ordinal);
            return l;
        }

        [Test]
        public void Detect_MatchesAnIndependentRecount_OnRealRaces()
        {
            List<RaceOutcome> races = Races();
            Assert.GreaterOrEqual(races.Count, 6, "試験の前提: 本物の大会を十分に集める");
            var seenKinds = new HashSet<HighlightKind>();
            foreach (RaceOutcome race in races)
            {
                int n = race.Results[0].Cum.Length;
                foreach (int own in new[] { -1, race.Results[3].Id })
                {
                    var all = RaceHighlights.Detect(race.Results, own);
                    foreach (int through in new[] { n, n / 2, 1 })
                    {
                        var part = RaceHighlights.Detect(race.Results, own, through);
                        CollectionAssert.AreEqual(Brute(race.Results, own, through), Flat(part), race.Kind + " 区間 " + through + " まで");
                        Assert.IsTrue(part.All(h => h.Section < through));
                    }
                    foreach (Highlight h in all) { seenKinds.Add(h.Kind); Assert.AreEqual(h.TeamId == own, h.IsOwn); }
                    // 区間の順 → 重要度の順
                    for (int i = 1; i < all.Count; i++)
                        Assert.IsTrue(all[i - 1].Section < all[i].Section || (all[i - 1].Section == all[i].Section && RaceHighlights.Compare(all[i - 1], all[i]) <= 0));
                    // ダイジェスト: 区間ごとに先頭から 2 件。見どころの上位: 選ばれなかったものは、選ばれたどれよりも重要でない
                    var digest = RaceHighlights.Digest(all);
                    foreach (var g in all.GroupBy(h => h.Section))
                        CollectionAssert.AreEqual(g.Take(2).ToList(), digest.Where(h => h.Section == g.Key).ToList());
                    var top = RaceHighlights.Top(all);
                    Assert.AreEqual(Math.Min(5, all.Count), top.Count);
                    foreach (Highlight rest in all.Where(h => !top.Contains(h)))
                        foreach (Highlight chosen in top) Assert.Greater(RaceHighlights.Compare(rest, chosen), 0);
                    for (int i = 1; i < top.Count; i++) Assert.LessOrEqual(top[i - 1].Section, top[i].Section, "時間順");
                }
            }
            foreach (HighlightKind k in Enum.GetValues(typeof(HighlightKind))) Assert.IsTrue(seenKinds.Contains(k), k + " が一度も出ない(試験の前提)");
        }

        [Test]
        public void BoostFlag_IsNeverWithABrake_AndIsAboutThreePercent()
        {
            int sections = 0, boosts = 0, brakes = 0;
            foreach (RaceOutcome race in Races())
                foreach (TeamResult t in race.Results)
                    for (int s = 0; s < t.Sections.Length; s++)
                    {
                        sections++;
                        bool brake = t.Events.Any(e => e.Section == s && e.Kind == "brake");
                        if (brake) brakes++;
                        if (t.Boost[s]) { boosts++; Assert.IsFalse(brake, "好走はブレーキがないときだけ(04 §7)"); }
                    }
            double rate = boosts / (double)(sections - brakes);
            Assert.That(rate, Is.InRange(0.015, 0.05), "好走は 3%(boosts " + boosts + " / " + (sections - brakes) + ")");
        }

        // ------------------------------------------------------------ 画面
        private static string Sig(HighlightView h) { return string.Join("|", h.Section, h.Kind, h.Key, h.SchoolId, h.IsOwn, h.RunnerId, h.OtherSchoolId, h.Ds, h.Percent, h.RankDelta); }

        [Test]
        public void ResultView_CarriesTheTopFive_WithOwnRunnerAndKeys()
        {
            foreach (ulong seed in new ulong[] { 20261102UL, 7UL })
            {
                GameSession gs = GameSession.New(seed, Difficulty.Elite);
                RaceOutcome race = null;
                RaceInfo? own = null;
                for (int w = 0; w < 48 * 3 && race == null; w++)
                {
                    if (Array.IndexOf(Ekiden.Domain.Team.Calendar.RaceWeeks, gs.YearWeek) >= 0)
                    {
                        gs.ResolveTraining();
                        RaceInfo? o;
                        RaceOutcome r = gs.PlayWeekRaces(out o);
                        if (r != null && o.HasValue) { race = r; own = o; break; }
                        gs.FinishWeek(); gs.AdvanceWeek();
                    }
                    else gs.StepWeek();
                }
                Assert.IsNotNull(race);
                RaceResultView v = gs.RaceResult(race, own);
                var expected = RaceHighlights.Top(RaceHighlights.Detect(race.Results, gs.School.Id));
                Assert.AreEqual(expected.Count, v.Highlights.Count);
                Assert.LessOrEqual(v.Highlights.Count, 5);
                RaceTeam team = race.Teams.First(t => t.Id == gs.School.Id);
                for (int i = 0; i < expected.Count; i++)
                {
                    Highlight h = expected[i];
                    HighlightView hv = v.Highlights[i];
                    Assert.AreEqual(h.Section, hv.Section);
                    Assert.AreEqual(h.Kind, hv.Kind);
                    Assert.AreEqual(h.TeamId, hv.SchoolId);
                    Assert.AreEqual("school." + h.TeamId + ".name", hv.SchoolNameKey);
                    Assert.AreEqual(h.IsOwn, hv.IsOwn);
                    Assert.AreEqual(h.IsOwn ? team.Lineup[h.Section].RunnerId : 0, hv.RunnerId, "自校の走者だけ出す");
                    Assert.Contains(hv.Key, HighlightView.AllKeys);
                    Assert.AreEqual(HighlightView.KeyOf(h.Kind, h.Delta), hv.Key);
                    if (h.Kind == HighlightKind.LeadChange) { Assert.AreEqual(h.OtherTeamId, hv.OtherSchoolId); Assert.AreEqual("school." + h.OtherTeamId + ".name", hv.OtherSchoolNameKey); }
                    if (h.Kind == HighlightKind.Brake || h.Kind == HighlightKind.Stall || h.Kind == HighlightKind.Boost) Assert.AreEqual(h.Magnitude * 100.0, hv.Percent);
                    if (h.Kind == HighlightKind.RankMove) Assert.AreEqual(h.Delta, hv.RankDelta);
                    if (h.Kind == HighlightKind.GapMove || h.Kind == HighlightKind.SectionPrize || h.Kind == HighlightKind.LeadChange) Assert.AreEqual((int)h.Magnitude, hv.Ds);
                }
            }
        }

        private static RaceDay OpenDay(ulong seed, RaceKind kind, out GameSession gs)
        {
            gs = GameSession.New(seed, Difficulty.Elite);
            while (gs.YearWeek != Ekiden.Domain.Team.Calendar.RaceWeeks[(int)kind]) gs.StepWeek();
            Assert.IsTrue(gs.RaceThisWeek.HasValue);
            gs.ResolveTraining();
            return gs.OpenRaceDay(kind);
        }

        [Test]
        public void LiveView_ShowsOnlyWhatHasHappened_AndFutureDecisionsDoNotChangeIt()
        {
            GameSession gs;
            RaceDay rd = OpenDay(75UL, RaceKind.National, out gs);
            Assert.IsEmpty(gs.RaceLive(rd).Highlights, "まだ中継所を通過していない");
            Assert.IsTrue(rd.Start().Ok);
            Assert.IsEmpty(gs.RaceLive(rd).Highlights, "始まったばかりでも、通過した区間がなければ空");
            for (int s = 0; s < rd.Sections; s++)
            {
                rd.Advance();
                RaceLiveView v = gs.RaceLive(rd);
                var expected = RaceHighlights.Digest(RaceHighlights.Detect(rd.Preview(), gs.School.Id, rd.Resolved));
                Assert.AreEqual(expected.Count, v.Highlights.Count);
                Assert.IsTrue(v.Highlights.All(h => h.Section < rd.Resolved), "未来の区間は出さない");
                foreach (var g in v.Highlights.GroupBy(h => h.Section)) Assert.LessOrEqual(g.Count(), 2);
                // 先の区間の指示を変えても、通過した区間の見どころは変わらない
                if (rd.Resolved < rd.Sections)
                {
                    var before = v.Highlights.Select(Sig).ToList();
                    Assert.IsTrue(rd.SetInstruction(rd.Resolved, Instruction.Attack).Ok);
                    CollectionAssert.AreEqual(before, gs.RaceLive(rd).Highlights.Select(Sig).ToList());
                    Assert.IsTrue(rd.SetInstruction(rd.Resolved, Instruction.Keep).Ok);
                }
            }
        }
    }
}
