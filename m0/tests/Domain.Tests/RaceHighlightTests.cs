using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
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
        public void OnlyFocusTeams_AreReportedForGapsIncidentsAndRanks_ButEveryoneGetsAPrize()
        {
            // 6 校。1 区の累積は T1..T6 の順(1100〜1600)。2 区で 5 位の T5 だけ 800(他は 1000)で 20 秒詰める(順位は 5 → 4。上位 3 校の外のまま)。6 位の T6 はブレーキ
            var teams = new List<TeamResult>();
            for (int i = 1; i <= 6; i++)
                teams.Add(Team(i, new[] { 1000 + 100 * i, i == 5 ? 800 : 1000 }, i == 6 ? new[] { new RaceEvent { Kind = "brake", Section = 1, Value = 0.06 } } : new RaceEvent[0]));
            var all = RaceHighlights.Detect(teams, -1);
            Assert.IsEmpty(Of(all, HighlightKind.Brake), "上位 3 校でも自校でもない 6 位のブレーキは出さない");
            Assert.IsEmpty(Of(all, HighlightKind.GapMove).Where(h => h.Section == 1).ToList(), "T5 の差の変化(注目校でない)は出さない");
            // 自校にすれば、同じ出来事が出る
            Assert.AreEqual(6, Of(RaceHighlights.Detect(teams, 6), HighlightKind.Brake).Single().TeamId);
            Assert.IsTrue(Of(RaceHighlights.Detect(teams, 5), HighlightKind.GapMove).Any(h => h.Section == 1 && h.TeamId == 5 && h.Delta == -200));
            // 区間賞は注目校でなくても出る(2 区の最速は T5)
            Assert.AreEqual(5, Of(all, HighlightKind.SectionPrize).Single(h => h.Section == 1).TeamId);
            // 前の中継所で上位 3 校だった校は、その区間で順位を落としても注目校のまま(T3 が 2 区で大きく遅れた)
            var drop = new List<TeamResult> { Team(1, new[] { 1000, 1000 }), Team(2, new[] { 1010, 1010 }), Team(3, new[] { 1020, 1400 }), Team(4, new[] { 1030, 1030 }), Team(5, new[] { 1040, 1040 }) };
            Highlight lost = Of(RaceHighlights.Detect(drop, -1), HighlightKind.GapMove).Single(h => h.Section == 1);
            Assert.AreEqual(3, lost.TeamId);
        }

        [Test]
        public void GapMove_OfTheOthers_IsOnlyTheBiggestOne_AndTheOwnTeamAlwaysCounts()
        {
            // 4 校が全員上位 3 校の内外にかかわらず注目(前後どちらかで上位 3)。2 区で T2 は +250、T3 は +400、自校 T4 は +200
            var teams = new List<TeamResult> { Team(1, new[] { 1000, 1000 }), Team(2, new[] { 1000, 1250 }), Team(3, new[] { 1000, 1400 }), Team(4, new[] { 1000, 1200 }) };
            var gaps = Of(RaceHighlights.Detect(teams, 4), HighlightKind.GapMove).Where(h => h.Section == 1).ToList();
            CollectionAssert.AreEquivalent(new[] { 3, 4 }, gaps.Select(h => h.TeamId).ToArray(), "自校のほかは動きの最も大きい T3 だけ");
            var noOwn = Of(RaceHighlights.Detect(teams, -1), HighlightKind.GapMove).Where(h => h.Section == 1).ToList();
            CollectionAssert.AreEqual(new[] { 3 }, noOwn.Select(h => h.TeamId).ToArray());
            // 同じ大きさなら ID の小さい方
            var tie = new List<TeamResult> { Team(1, new[] { 1000, 1000 }), Team(2, new[] { 1000, 1300 }), Team(3, new[] { 1000, 1300 }) };
            CollectionAssert.AreEqual(new[] { 2 }, Of(RaceHighlights.Detect(tie, -1), HighlightKind.GapMove).Where(h => h.Section == 1).Select(h => h.TeamId).ToArray());
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

        private static RecordUpdate Rec(int section, int school, int time, int prevTime, int prevSchool)
        {
            return new RecordUpdate { Kind = RaceKind.Fall, Section = section, SchoolId = school, TimeDs = time, PrevTimeDs = prevTime, PrevSchoolId = prevSchool, Year = 3, PrevYear = 1 };
        }

        [Test]
        public void Records_ReplaceTheSectionPrizeOfTheirHolder_AndTheTeamRecordComesAtTheFinish()
        {
            var results = new[] { Team(1, new[] { 1000, 900 }), Team(2, new[] { 1010, 900 }), Team(3, new[] { 1100, 950 }) };
            var records = new[] { Rec(1, 1, 900, 905, 2), Rec(-1, 1, 1900, 1950, 2) };
            var all = RaceHighlights.Detect(results, 1, -1, records);
            Highlight cr = Of(all, HighlightKind.CourseRecord).Single();
            Assert.AreEqual(1, cr.Section); Assert.AreEqual(1, cr.TeamId); Assert.AreEqual(5, cr.Magnitude); Assert.AreEqual(2, cr.OtherTeamId); Assert.IsTrue(cr.IsOwn);
            Highlight tr = Of(all, HighlightKind.TeamRecord).Single();
            Assert.AreEqual(1, tr.Section, "大会記録は最終区間(ゴール)に出す"); Assert.AreEqual(50, tr.Magnitude); Assert.AreEqual(1, tr.Tier);
            var prizes = Of(all, HighlightKind.SectionPrize).Select(h => h.Section + ":" + h.TeamId).OrderBy(x => x).ToArray();
            CollectionAssert.AreEqual(new[] { "0:1", "1:2" }, prizes, "区間 1 は記録の保持者(T1)の区間賞を出さない。同タイムのもう 1 校(T2)は区間賞のまま");
            // 途中まで(1 区の終わりまで)では、2 区の記録も大会記録も出ない
            var early = RaceHighlights.Detect(results, 1, 1, records);
            Assert.IsEmpty(Of(early, HighlightKind.CourseRecord)); Assert.IsEmpty(Of(early, HighlightKind.TeamRecord));
            // 記録を渡さなければ、従来どおり区間賞
            Assert.AreEqual(3, Of(RaceHighlights.Detect(results, 1), HighlightKind.SectionPrize).Count);
            // 同じ重要度(区間賞)の中では自校が先。記録は重要度が上
            var mix = new List<Highlight> { H(1, HighlightKind.SectionPrize, 2, false, 99), H(1, HighlightKind.CourseRecord, 3, false, 1), H(1, HighlightKind.SectionPrize, 1, true, 1), H(1, HighlightKind.TeamRecord, 4, false, 2) };
            // 記録は首位交代と同じ重要度。その中では 自校 → 記録が先 → 大きさ
            var top = new List<Highlight> { H(1, HighlightKind.LeadChange, 5, false, 500), H(1, HighlightKind.CourseRecord, 6, false, 1), H(1, HighlightKind.LeadChange, 7, true, 1) };
            RaceSimulator.StableSort(top, RaceHighlights.Compare);
            CollectionAssert.AreEqual(new[] { 7, 6, 5 }, top.Select(h => h.TeamId).ToArray(), "自校の首位交代 → 記録 → 他校の首位交代");
            RaceSimulator.StableSort(mix, RaceHighlights.Compare);
            CollectionAssert.AreEqual(new[] { 4, 3, 1, 2 }, mix.Select(h => h.TeamId).ToArray(), "記録(大きさの順。最も重要)→ 自校の区間賞 → 他校の区間賞");
        }

        [Test]
        public void Records_OnRealRaces_AreOneForOneWithTheUpdates()
        {
            WorldSim w = WorldSim.Create(new KeyedRngFactory(31UL), 6);
            int courseSeen = 0, teamSeen = 0;
            for (int year = 1; year <= 8; year++)
            {
                YearState st = w.BeginYear(year, null);
                w.PlayQualifier(st);
                foreach (RaceKind kind in WorldSim.AllRaces)
                {
                    RaceOutcome o = w.PlayRace(st, kind);
                    int n = o.Results[0].Cum.Length;
                    var plain = RaceHighlights.Detect(o.Results, -1);
                    var withRec = RaceHighlights.Detect(o.Results, -1, -1, o.Records);
                    CollectionAssert.AreEqual(o.Records.Where(u => u.Section >= 0).Select(u => u.Section + ":" + u.SchoolId).OrderBy(x => x).ToArray(),
                        Of(withRec, HighlightKind.CourseRecord).Select(h => h.Section + ":" + h.TeamId).OrderBy(x => x).ToArray());
                    Assert.AreEqual(o.Records.Count(u => u.Section < 0), Of(withRec, HighlightKind.TeamRecord).Count);
                    foreach (Highlight h in Of(withRec, HighlightKind.TeamRecord)) Assert.AreEqual(n - 1, h.Section);
                    // 記録のほかは変わらない: 区間賞だけが、記録の保持者の分だけ減る
                    var expected = plain.Where(h => !(h.Kind == HighlightKind.SectionPrize && o.Records.Any(u => u.Section == h.Section && u.SchoolId == h.TeamId))).ToList();
                    CollectionAssert.AreEqual(Flat(expected), Flat(withRec.Where(h => h.Kind != HighlightKind.CourseRecord && h.Kind != HighlightKind.TeamRecord)));
                    foreach (RecordUpdate u in o.Records.Where(u => u.Section >= 0))
                    {
                        Highlight cr = Of(withRec, HighlightKind.CourseRecord).Single(h => h.Section == u.Section);
                        Assert.AreEqual(u.PrevTimeDs - u.TimeDs, cr.Magnitude);
                        Assert.AreEqual(u.PrevSchoolId, cr.OtherTeamId);
                        courseSeen++;
                    }
                    teamSeen += o.Records.Count(u => u.Section < 0);
                }
                w.EndYear(st);
            }
            Assert.Greater(courseSeen, 0, "試験の前提: 区間記録の更新がある");
            Assert.Greater(teamSeen, 0, "試験の前提: 大会記録の更新がある");
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
            // 同じ種類は 2 件まで: 首位交代が 4 件あっても、見どころは首位交代 2 件 + ほかの種類
            var many = new List<Highlight>
            {
                H(0, HighlightKind.LeadChange, 1, false, 50), H(1, HighlightKind.LeadChange, 2, false, 40), H(2, HighlightKind.LeadChange, 3, false, 30), H(3, HighlightKind.LeadChange, 4, false, 20),
                H(4, HighlightKind.SectionPrize, 5, false, 3), H(5, HighlightKind.RankMove, 6, false, 3),
            };
            CollectionAssert.AreEqual(new[] { 1, 2, 5, 6 }, RaceHighlights.Top(many, 5).Select(h => h.TeamId).ToArray(), "首位交代は大きい順に 2 件(1・2)、区間賞、順位の変動");
            Assert.AreEqual(detected.GroupBy(h => h.Kind).Sum(g => Math.Min(2, g.Count())), RaceHighlights.Top(detected, 100).Count, "上限がなくても、同じ種類は 2 件まで");
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

        /// <summary>独立の数え直し: 順位は『自分より速い校の数 + 1』(同着は ID)、首位・差・賞は全チームを直接見る。注目校 = 自校 と 前後どちらかの中継所で上位 3 校。</summary>
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
                var otherGaps = new List<(int mag, int id, int move)>();
                for (int i = 0; i < res.Count; i++)
                {
                    TeamResult t = res[i];
                    if (t.Sections[s] == best) o.Add(string.Join("|", s, HighlightKind.SectionPrize, t.Id, -1, worse.Count == 0 ? 0 : worse.Min() - best, 0));
                    bool focus = t.Id == own || rankAt(i, s) <= 3 || (s > 0 && rankAt(i, s - 1) <= 3);
                    if (!focus) continue;
                    int gap = t.Cum[s] - minAt(s), prev = s > 0 ? t.Cum[s - 1] - minAt(s - 1) : 0;
                    if (rankAt(i, s) != 1 && Math.Abs(gap - prev) >= 200)
                    {
                        if (t.Id == own) o.Add(string.Join("|", s, HighlightKind.GapMove, t.Id, -1, Math.Abs(gap - prev), gap - prev));
                        else otherGaps.Add((Math.Abs(gap - prev), t.Id, gap - prev));
                    }
                    foreach (RaceEvent e in t.Events.Where(e => e.Section == s && (e.Kind == "brake" || e.Kind == "stall")))
                        o.Add(string.Join("|", s, e.Kind == "brake" ? HighlightKind.Brake : HighlightKind.Stall, t.Id, -1, e.Value.ToString("R"), 0));
                    if (t.Boost[s]) o.Add(string.Join("|", s, HighlightKind.Boost, t.Id, -1, (-RaceConstants.BoostAdj).ToString("R"), 0));
                    if (s > 0)
                    {
                        int up = rankAt(i, s - 1) - rankAt(i, s);
                        if (Math.Abs(up) >= 3) o.Add(string.Join("|", s, HighlightKind.RankMove, t.Id, -1, Math.Abs(up), up));
                    }
                }
                if (otherGaps.Count > 0)
                {
                    var pick = otherGaps.OrderByDescending(x => x.mag).ThenBy(x => x.id).First();
                    o.Add(string.Join("|", s, HighlightKind.GapMove, pick.id, -1, pick.mag, pick.move));
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
                    // 独立の選び方: 重要度の順に並べ、同じ種類は 2 件までで 5 件
                    var sortedAll = all.OrderBy(h => h, Comparer<Highlight>.Create(RaceHighlights.Compare)).ToList();
                    var expectedTop = new List<Highlight>();
                    foreach (Highlight h in sortedAll) if (expectedTop.Count < 5 && expectedTop.Count(x => x.Kind == h.Kind) < 2) expectedTop.Add(h);
                    CollectionAssert.AreEquivalent(expectedTop, top);
                    Assert.IsTrue(top.GroupBy(h => h.Kind).All(g => g.Count() <= 2));
                    for (int i = 1; i < top.Count; i++) Assert.LessOrEqual(top[i - 1].Section, top[i].Section, "時間順");
                }
            }
            foreach (HighlightKind k in Enum.GetValues(typeof(HighlightKind)))
                if (k != HighlightKind.CourseRecord && k != HighlightKind.TeamRecord)        // 記録は結果だけからは決まらない(別の試験)
                    Assert.IsTrue(seenKinds.Contains(k), k + " が一度も出ない(試験の前提)");
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
                var expected = RaceHighlights.Top(RaceHighlights.Detect(race.Results, gs.School.Id, -1, race.Records));
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
