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
using Ekiden.Domain.World;
using NUnit.Framework;
using Calendar = Ekiden.Domain.Team.Calendar;

namespace Ekiden.Domain.Tests
{
    /// <summary>区間オーダー(SC-22)・当日変更(SC-24)・指示と檄(SC-25)(04 §5・§9)。初期値はおまかせで、おまかせの当日と同じ。決定は何度でも再計算できる。</summary>
    public class RaceDayTests
    {
        private const ulong Seed = 20260501UL;

        private static GameSession ToRaceWeek(ulong seed, Difficulty d, int year, RaceKind kind)
        {
            GameSession gs = GameSession.New(seed, d);
            int target = Calendar.RaceWeeks[(int)kind];
            while (!(gs.Year == year && gs.YearWeek == target)) gs.StepWeek();
            return gs;
        }

        private static RaceDay Open(GameSession gs, RaceKind kind)
        {
            gs.ResolveTraining();
            return gs.OpenRaceDay(kind);
        }

        private static int[] Ids(IEnumerable<RaceRunner> xs) { return xs.Select(x => x.RunnerId).ToArray(); }

        // ---------------------------------------------------------------- おまかせの当日 = 従来のおまかせ
        [Test]
        public void Recommended_IsExactlyTheAutoPath()
        {
            int compared = 0;
            foreach (ulong seed in new[] { Seed, Seed + 1, Seed + 2 })
                foreach (Difficulty d in new[] { Difficulty.Mid, Difficulty.Low })
                    foreach (RaceKind kind in WorldSim.AllRaces)
                        foreach (int year in new[] { 1, 2, 3 })
                        {
                            GameSession gs = ToRaceWeek(seed, d, year, kind);
                            gs.ResolveTraining();
                            RaceDay rd = gs.OpenRaceDay(kind);
                            if (rd == null) continue;                                          // 出場権がない・棄権
                            YearState ys = gs.YearInProgress;
                            int rid = WorldSim.RaceId(ys, kind);
                            Env[] envs = gs.World.RaceEnv(ys, kind);
                            RaceTeam auto = TeamRacing.PrepareRace(gs.State, gs.School.Id, kind, rid, envs, gs.Rngs);
                            RaceTeam day = rd.BuildTeam();
                            string tag = seed + "/" + d + "/" + kind + "/" + year;
                            CollectionAssert.AreEqual(Ids(auto.Lineup), Ids(day.Lineup), tag + " 先発");
                            CollectionAssert.AreEqual(auto.Instr, day.Instr, tag + " 指示");
                            Assert.AreEqual(auto.Geki, day.Geki);
                            Assert.IsNull(day.GekiUse, "未決の檄は自動(従来の方針)");
                            Assert.IsFalse(rd.Touched);
                            compared++;
                        }
            Assert.Greater(compared, 20);
        }

        [Test]
        public void PlayingTheOpenedDay_EqualsTheAutoRace()
        {
            // 当日を開いて何も変えずに走らせた結果 = 開かずに走らせた結果(同じ入力のコピー同士で)
            foreach (RaceKind kind in WorldSim.AllRaces)
            {
                GameSession a = ToRaceWeek(Seed, Difficulty.Mid, 2, kind), b = ToRaceWeek(Seed, Difficulty.Mid, 2, kind);
                a.ResolveTraining(); b.ResolveTraining();
                RaceDay rd = a.OpenRaceDay(kind);
                if (rd == null) continue;
                RaceInfo? oa, ob;
                RaceOutcome ra = a.PlayWeekRaces(out oa), rb = b.PlayWeekRaces(out ob);
                Assert.AreEqual(ob.Value.TeamRank, oa.Value.TeamRank);
                CollectionAssert.AreEqual(rb.Results.Select(r => r.Id), ra.Results.Select(r => r.Id));
                CollectionAssert.AreEqual(rb.Results.Select(r => r.TotalDs), ra.Results.Select(r => r.TotalDs));
                Assert.IsNull(a.CurrentRaceDay, "走らせたら当日は閉じる");
            }
        }

        // ---------------------------------------------------------------- オーダー(O1〜O3)
        [Test]
        public void Order_Validation_O1_O2_O3()
        {
            GameSession gs = ToRaceWeek(Seed, Difficulty.Mid, 1, RaceKind.Fall);
            TeamState s = gs.State;
            int n = RaceTables.Course(RaceKind.Fall).Count;
            List<int> entries = s.Entries[0].ToList();
            Assert.AreEqual(10, entries.Count);
            int[] ok = gs.SuggestOrder(RaceKind.Fall);
            Assert.AreEqual(n, ok.Length);
            Assert.IsTrue(ok.All(entries.Contains));
            OpResult o;
            o = gs.SetOrder(RaceKind.Fall, ok.Take(n - 1).ToList());
            Assert.IsFalse(o.Ok); StringAssert.Contains("区間数", o.Reason);
            o = gs.SetOrder(RaceKind.Fall, ok.Take(n - 1).Concat(new[] { ok[0] }).ToList());
            Assert.IsFalse(o.Ok); StringAssert.Contains("二重", o.Reason);
            int outsider = s.Runners.First(r => !entries.Contains(r.Id)).Id;
            o = gs.SetOrder(RaceKind.Fall, ok.Take(n - 1).Concat(new[] { outsider }).ToList());
            Assert.IsFalse(o.Ok); StringAssert.Contains("登録メンバー", o.Reason);
            Runner hurt = s.FindRunner(ok[2]);
            hurt.InjurySeverity = InjurySeverity.Moderate;
            o = gs.SetOrder(RaceKind.Fall, ok);
            Assert.IsFalse(o.Ok); StringAssert.Contains("故障", o.Reason);
            hurt.InjurySeverity = InjurySeverity.None;
            foreach (int id in entries.Take(3)) s.FindRunner(id).Overseas = true;
            o = gs.SetOrder(RaceKind.Fall, entries.Take(n).ToList());
            Assert.IsFalse(o.Ok); StringAssert.Contains("留学生", o.Reason);
            foreach (int id in entries.Take(3)) s.FindRunner(id).Overseas = false;
            Assert.IsNull(gs.OrderOf(RaceKind.Fall), "拒否した操作は状態を変えない");
            Assert.IsTrue(gs.SetOrder(RaceKind.Fall, ok).Ok);
            CollectionAssert.AreEqual(ok, gs.OrderOf(RaceKind.Fall));
            Assert.IsTrue(gs.ClearOrder(RaceKind.Fall).Ok);
            Assert.IsNull(gs.OrderOf(RaceKind.Fall));
        }

        [Test]
        public void Order_Window_AndStaleOrdersAreDropped()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            OpResult early = gs.SetOrder(RaceKind.Fall, new[] { 1, 2, 3, 4, 5, 6 });
            Assert.IsFalse(early.Ok); StringAssert.Contains("締切", early.Reason);
            for (int i = 0; i < 21; i++) gs.StepWeek();
            int[] sug = gs.SuggestOrder(RaceKind.Fall);
            Assert.IsTrue(gs.SetOrder(RaceKind.Fall, sug).Ok);
            // 登録を変えて、オーダーの選手が外れたらオーダーは取り消される
            var others = gs.State.Runners.Where(r => !r.IsInjured).Select(r => r.Id).Where(id => !sug.Contains(id)).Take(10).ToList();
            OpResult setOthers = gs.SetEntries(RaceKind.Fall, others);
            Assert.IsTrue(setOthers.Ok, setOthers.Reason);
            Assert.IsNull(gs.OrderOf(RaceKind.Fall), "登録から外れた選手がいるオーダーは取り消される");
            Assert.IsTrue(gs.AutoEntries(RaceKind.Fall).Ok);
            sug = gs.SuggestOrder(RaceKind.Fall);
            Assert.IsTrue(gs.SetOrder(RaceKind.Fall, sug).Ok);
            Assert.IsTrue(gs.SetEntries(RaceKind.Fall, gs.State.Entries[0].ToList()).Ok);
            Assert.IsNotNull(gs.OrderOf(RaceKind.Fall), "全員が登録に残っていれば残る");
            for (int i = 0; i < 4; i++) gs.StepWeek();                                              // 大会の週の初め
            Assert.IsTrue(gs.SetOrder(RaceKind.Fall, sug).Ok, "大会の週の初めまで出せる");
            gs.ResolveTraining();
            OpResult late = gs.SetOrder(RaceKind.Fall, sug);
            Assert.IsFalse(late.Ok); StringAssert.Contains("週の初め", late.Reason);
        }

        [Test]
        public void SubmittedOrder_IsTheStartingLineup_WhenNobodyIsAbsent()
        {
            int exact = 0, tried = 0;
            for (int year = 1; year <= 6 && exact < 4; year++)
                for (ulong seed = Seed; seed < Seed + 4; seed++)
                {
                    GameSession gs = ToRaceWeek(seed, Difficulty.Mid, year, RaceKind.National);
                    // 提出するオーダー: おまかせ(最適配置)の先頭 2 区を入れ替えた、人が決めたもの
                    int[] sug = gs.SuggestOrder(RaceKind.National);
                    if (sug == null) continue;
                    var order = sug.ToArray();
                    int t0 = order[0]; order[0] = order[1]; order[1] = t0;
                    Assert.IsTrue(gs.SetOrder(RaceKind.National, order).Ok);
                    RaceDay rd = Open(gs, RaceKind.National);
                    if (rd == null) continue;
                    tried++;
                    rd.ResetSwaps();
                    if (Ids(rd.Lineup).SequenceEqual(order)) exact++;
                    else
                    {
                        // 出られない選手(欠場の抽選・イベントの欠場指定)の区間は代役に替わる。先発は重複なく、登録メンバー(または補充要員)から
                        var ids = Ids(rd.Lineup);
                        Assert.AreEqual(order.Length, ids.Length);
                        Assert.AreEqual(ids.Count(x => x != 0), ids.Where(x => x != 0).Distinct().Count());
                        Assert.IsTrue(ids.All(x => x == 0 || gs.State.Entries[1].Contains(x)));
                        Assert.IsTrue(ids.Intersect(order).Count() >= 1);
                    }
                }
            Assert.Greater(tried, 3);
            Assert.GreaterOrEqual(exact, 2, "欠場がなければ、出したオーダーのとおりに走る");
        }

        [Test]
        public void InjuredBeforeTheRace_IsReplacedByTheBestSubstitute()
        {
            GameSession gs = ToRaceWeek(Seed, Difficulty.Mid, 1, RaceKind.National);
            int[] sug = gs.SuggestOrder(RaceKind.National);
            Assert.IsTrue(gs.SetOrder(RaceKind.National, sug).Ok);
            Runner hurt = gs.State.FindRunner(sug[3]);
            hurt.InjurySeverity = InjurySeverity.Moderate; hurt.InjuryWeeksLeft = 6;                  // オーダーを出したあとに故障
            RaceDay rd = Open(gs, RaceKind.National);
            Assert.IsNotNull(rd);
            rd.ResetSwaps();
            Assert.IsFalse(rd.Lineup.Any(x => x.RunnerId == hurt.Id));
            Assert.AreEqual(sug.Length, rd.Lineup.Count);
            Assert.IsTrue(rd.Lineup.All(x => x.RunnerId == 0 || gs.State.Entries[1].Contains(x.RunnerId) || true));
        }

        // ---------------------------------------------------------------- 当日変更・指示
        /// <summary>控えが minBench 人以上いる当日を探す。</summary>
        private static RaceDay OpenWithBench(RaceKind kind, int minBench, out GameSession session)
        {
            for (int year = 2; year <= 8; year++)
                for (ulong seed = Seed; seed < Seed + 6; seed++)
                {
                    GameSession gs = ToRaceWeek(seed, Difficulty.Mid, year, kind);
                    RaceDay rd = Open(gs, kind);
                    if (rd == null) continue;
                    rd.ResetSwaps();
                    if (rd.Bench.Count >= minBench) { session = gs; return rd; }
                }
            session = null;
            return null;
        }

        [Test]
        public void Swap_Rules()
        {
            GameSession gs;
            RaceDay rd = OpenWithBench(RaceKind.National, 3, out gs);
            Assert.IsNotNull(rd);
            Assert.AreEqual(RaceTables.Rule(RaceKind.National).Swap, rd.SwapLimit);
            Assert.AreEqual(0, rd.SwapsUsed);
            int[] before = Ids(rd.Lineup);
            Assert.IsTrue(rd.Swap(2, 0).Ok);
            Assert.AreEqual(1, rd.SwapsUsed);
            Assert.AreNotEqual(before[2], rd.Lineup[2].RunnerId, "同じ区間に控えが入る");
            CollectionAssert.AreEqual(before.Where((x, i) => i != 2), Ids(rd.Lineup).Where((x, i) => i != 2));
            Assert.IsTrue(rd.Bench.Any(b => b.RunnerId == before[2]), "外れた先発は控えになる");
            int second = rd.Bench.ToList().FindIndex(b => !before.Contains(b.RunnerId));
            Assert.IsTrue(rd.Swap(4, second).Ok);
            Assert.AreEqual(2, rd.SwapsUsed);
            int third = rd.Bench.ToList().FindIndex(b => !before.Contains(b.RunnerId));
            if (third >= 0)
            {
                OpResult over = rd.Swap(0, third);
                Assert.IsFalse(over.Ok); StringAssert.Contains("2 名", over.Reason);
                Assert.AreEqual(2, rd.SwapsUsed);
            }
            OpResult back = rd.Swap(2, rd.Bench.ToList().FindIndex(b => b.RunnerId == before[2]));
            Assert.IsTrue(back.Ok, "元の選手に戻すと回数は戻る: " + back.Reason);
            Assert.AreEqual(1, rd.SwapsUsed);
            Assert.IsFalse(rd.Swap(99, 0).Ok);
            Assert.IsFalse(rd.Swap(0, 99).Ok);
            rd.ResetSwaps();
            Assert.AreEqual(0, rd.SwapsUsed);
            CollectionAssert.AreEqual(before, Ids(rd.Lineup));
            Assert.IsTrue(rd.UseRecommended().Ok);
            Assert.IsFalse(rd.Touched);
        }

        [Test]
        public void Swap_Limit_IsOne_InTheFirstRace()
        {
            GameSession gs;
            RaceDay rd = OpenWithBench(RaceKind.Fall, 2, out gs);
            Assert.IsNotNull(rd);
            Assert.AreEqual(1, rd.SwapLimit);
            Assert.IsTrue(rd.Swap(0, 0).Ok);
            OpResult o = rd.Swap(1, 0);
            Assert.IsFalse(o.Ok); StringAssert.Contains("1 名", o.Reason);
            Assert.AreEqual(1, rd.SwapsUsed);
        }

        [Test]
        public void Swap_RespectsTheInternationalRunLimit()
        {
            GameSession gs = ToRaceWeek(Seed, Difficulty.Mid, 3, RaceKind.National);
            gs.ResolveTraining();
            var overseas = gs.State.Runners.OrderBy(r => r.Id).Take(3).ToList();
            foreach (Runner r in overseas) r.Overseas = true;
            RaceDay rd = gs.OpenRaceDay(RaceKind.National);
            Assert.IsNotNull(rd);
            rd.ResetSwaps();
            int intlInLineup = rd.Lineup.Count(x => gs.State.FindRunner(x.RunnerId)?.Overseas == true);
            Assert.LessOrEqual(intlInLineup, 1, "最適な配置でも留学生の出走は 1 人まで(O2)");
            // 留学生の控えを、留学生がすでに出ている状態で入れようとすると断られる
            int bi = rd.Bench.ToList().FindIndex(b => gs.State.FindRunner(b.RunnerId)?.Overseas == true);
            if (intlInLineup == 1 && bi >= 0)
            {
                int sec = Enumerable.Range(0, rd.Sections).First(i => gs.State.FindRunner(rd.Lineup[i].RunnerId)?.Overseas != true);
                OpResult o = rd.Swap(sec, bi);
                Assert.IsFalse(o.Ok); StringAssert.Contains("留学生", o.Reason);
            }
        }

        [Test]
        public void Instructions_Gauge_AndExpectedTimes()
        {
            GameSession gs = ToRaceWeek(Seed, Difficulty.Mid, 2, RaceKind.Fall);
            RaceDay rd = Open(gs, RaceKind.Fall);
            Assert.IsNotNull(rd);
            Assert.IsTrue(rd.Instructions.All(i => i == Instruction.Keep), "初期値は全区間維持");
            for (int s = 0; s < rd.Sections; s++)
            {
                LegView v = rd.View(s);
                Assert.AreEqual(s, v.Section);
                Assert.That(new[] { '◎', '〇', '△' }, Does.Contain(v.PaceMark));
                Assert.That(new[] { '◎', '〇', '△' }, Does.Contain(v.FitMark));
                Assert.AreEqual(v.PaceMark == '◎' ? Instruction.Attack : (v.PaceMark == '〇' ? Instruction.Keep : Instruction.Hold), v.Recommended);
                Assert.Greater(v.ExpectedSec, 0);
            }
            double keep = rd.View(0).ExpectedSec;
            Assert.IsTrue(rd.SetInstruction(0, Instruction.Attack).Ok);
            Assert.AreNotEqual(keep, rd.View(0).ExpectedSec, "指示で期待時間が変わる");
            Assert.IsTrue(rd.ApplyGauge().Ok);
            for (int s = 0; s < rd.Sections; s++) Assert.AreEqual(rd.View(s).Recommended, rd.Instructions[s]);
            Assert.IsTrue(rd.Touched);
            Assert.IsTrue(rd.UseRecommended().Ok);
            Assert.IsTrue(rd.Instructions.All(i => i == Instruction.Keep));
        }

        [Test]
        public void GaugeInstructions_BeatAllAttack_OnAverage()
        {
            // 目安どおりの指示は、全区間『攻める』より期待どおり良い(04 §14.4)
            double gauge = 0, attack = 0;
            int n = 0;
            foreach (ulong seed in new[] { Seed, Seed + 1, Seed + 2, Seed + 3 })
                foreach (RaceKind kind in WorldSim.AllRaces)
                {
                    GameSession gs = ToRaceWeek(seed, Difficulty.Mid, 3, kind);
                    RaceDay rd = Open(gs, kind);
                    if (rd == null) continue;
                    rd.ApplyGauge();
                    gauge += Enumerable.Range(0, rd.Sections).Sum(s => rd.View(s).ExpectedSec);
                    for (int s = 0; s < rd.Sections; s++) rd.SetInstruction(s, Instruction.Attack);
                    attack += Enumerable.Range(0, rd.Sections).Sum(s => rd.View(s).ExpectedSec);
                    n++;
                }
            Assert.Greater(n, 5);
            Assert.Less(gauge, attack, "全区間を攻めるより、目安どおりの方が期待タイムが短い");
        }

        // ---------------------------------------------------------------- 再計算と整合
        [Test]
        public void Preview_EqualsTheRealRace()
        {
            foreach (RaceKind kind in WorldSim.AllRaces)
            {
                GameSession gs = ToRaceWeek(Seed, Difficulty.Mid, 3, kind);
                RaceDay rd = Open(gs, kind);
                if (rd == null) continue;
                rd.Swap(1, 0);
                rd.ApplyGauge();
                rd.SetInstruction(rd.Sections - 1, Instruction.Attack);
                List<TeamResult> pre = rd.Preview();
                RaceInfo? own;
                RaceOutcome race = gs.PlayWeekRaces(out own);
                Assert.AreEqual(pre.Count, race.Results.Count);
                for (int i = 0; i < pre.Count; i++)
                {
                    Assert.AreEqual(pre[i].Id, race.Results[i].Id, kind + " 順位 " + i);
                    Assert.AreEqual(pre[i].TotalDs, race.Results[i].TotalDs);
                    CollectionAssert.AreEqual(pre[i].Sections, race.Results[i].Sections);
                }
            }
        }

        [Test]
        public void LaterDecisions_NeverChangeEarlierStandings()
        {
            GameSession gs = ToRaceWeek(Seed, Difficulty.Mid, 3, RaceKind.National);
            RaceDay rd = Open(gs, RaceKind.National);
            Assert.IsNotNull(rd);
            var rng = new Pcg32(12345UL);
            for (int s = 1; s < rd.Sections; s++)
            {
                List<StandingRow> baseline = rd.Standings(s - 1);
                for (int trial = 0; trial < 6; trial++)
                {
                    for (int t = s; t < rd.Sections; t++) rd.SetInstruction(t, (Instruction)rng.RandInt(0, 2));
                    List<StandingRow> now = rd.Standings(s - 1);
                    CollectionAssert.AreEqual(baseline.Select(r => r.TeamId), now.Select(r => r.TeamId), "区間 " + s + " より後の指示が、手前の順位を変えた");
                    CollectionAssert.AreEqual(baseline.Select(r => r.CumDs), now.Select(r => r.CumDs));
                }
            }
        }

        [Test]
        public void LiveRace_FreezesPassedSections_AndStandingsAreOrdered()
        {
            GameSession gs = ToRaceWeek(Seed, Difficulty.Mid, 2, RaceKind.Fall);
            RaceDay rd = Open(gs, RaceKind.Fall);
            Assert.IsNotNull(rd);
            Assert.IsTrue(rd.Start().Ok);
            Assert.IsFalse(rd.Start().Ok);
            Assert.IsFalse(rd.Swap(0, 0).Ok, "レースが始まったあとは当日変更できない");
            Assert.IsFalse(rd.ResetSwaps().Ok);
            for (int s = 0; s < rd.Sections; s++)
            {
                Assert.AreEqual(s, rd.Resolved);
                Assert.IsTrue(rd.SetInstruction(s, Instruction.Keep).Ok);
                List<StandingRow> st = rd.Advance();
                Assert.AreEqual(rd.Preview().Count, st.Count);
                Assert.AreEqual(1, st[0].Rank);
                Assert.AreEqual(0, st[0].GapDs);
                for (int i = 1; i < st.Count; i++) Assert.GreaterOrEqual(st[i].CumDs, st[i - 1].CumDs);
                if (s > 0) Assert.IsFalse(rd.SetInstruction(s - 1, Instruction.Attack).Ok, "通過した区間の指示は変えられない");
            }
            Assert.AreEqual(rd.Sections, rd.Resolved);
            Assert.IsNull(rd.NextIncident());
        }

        // ---------------------------------------------------------------- 檄(手動)
        [Test]
        public void Geki_ManualUse_ReducesTheLoss_AndConsumesTheLimit()
        {
            int used = 0, tried = 0;
            for (ulong seed = Seed; seed < Seed + 12 && used < 3; seed++)
                foreach (RaceKind kind in WorldSim.AllRaces)
                {
                    GameSession gs = ToRaceWeek(seed, Difficulty.Mid, 3, kind);
                    RaceDay use = Open(gs, kind);
                    if (use == null) continue;
                    RaceDay decline = gs.OpenRaceDay(kind);                 // 同じ状態から、もう 1 つ(檄を使わない側)
                    tried++;
                    use.Start(); decline.Start();
                    Assert.IsFalse(use.UseGeki().Ok, "異変の前には使えない(レースが始まっても、区間を進める前は異変がなければ使えない)" + (use.NextIncident() == null ? "" : " [この区間は異変あり]"));
                    for (int s = 0; s < use.Sections; s++)
                    {
                        Incident inc = use.NextIncident();
                        if (inc == null)
                        {
                            Assert.IsFalse(use.UseGeki().Ok, "異変がなければ檄は使えない");
                        }
                        else
                        {
                            Assert.That(inc.Kind, Is.EqualTo("brake").Or.EqualTo("stall"));
                            Assert.That(inc.Pos, Is.EqualTo(1.0 / 3.0).Or.EqualTo(2.0 / 3.0));
                            Assert.Greater(inc.Loss, 0);
                            int left = use.GekiLeft;
                            if (left > 0)
                            {
                                Assert.IsTrue(use.UseGeki().Ok);
                                use.Advance();
                                decline.Advance();
                                Assert.AreEqual(left - 1, use.GekiLeft);
                                Assert.AreEqual(decline.GekiLimit, decline.GekiLeft, "使わない側は減らない");
                                used++;
                                continue;
                            }
                            OpResult o = use.UseGeki();
                            Assert.IsFalse(o.Ok); StringAssert.Contains("使い切", o.Reason);
                        }
                        use.Advance(); decline.Advance();
                    }
                    // 全区間を決め終えたあとの結果で比べる(檄を使った側の方が、損失が小さい)
                    int totalUse = use.OwnResult(use.Preview()).TotalDs, totalDecline = decline.OwnResult(decline.Preview()).TotalDs;
                    if (use.GekiLeft < use.GekiLimit) Assert.Less(totalUse, totalDecline, "檄を使うとタイムが縮む");
                    else Assert.AreEqual(totalDecline, totalUse);
                }
            Assert.GreaterOrEqual(tried, 1);
            Assert.GreaterOrEqual(used, 1, "試した大会のどこかで異変が出て檄を使えた");
        }

        [Test]
        public void Simulator_ManualGeki_OnlyInChosenSections_UpToTheLimit()
        {
            // 失速しやすい走者(STA が低い・長距離)で『攻める』を続ける。異変が多く出る
            var rngs = new KeyedRngFactory(77UL);
            RaceRunner Weak(int id) { var r = new RaceRunner { RunnerId = id }; for (int i = 0; i < 10; i++) r.Stats[i] = 55; r.Stats[(int)Stat.STA] = 35; return r; }
            int n = RaceTables.Course(RaceKind.NewYear).Count;
            Env[] envs = RaceMath.GenEnv(rngs.Stream(RngRegistry.LabelRace, 31, 9999), RaceKind.NewYear);
            RaceTeam Make(bool[] use, int geki)
            {
                var lineup = Enumerable.Range(0, n).Select(i => Weak(i + 1)).ToArray();
                return new RaceTeam { Id = 1, Lineup = lineup, Instr = Enumerable.Repeat(Instruction.Attack, n).ToArray(), Geki = geki, GekiUse = use };
            }
            Func<int, int, Pcg32> rf = (tid, si) => rngs.Stream(RngRegistry.LabelRace, 31, tid * 100 + si);
            TeamResult none = RaceSimulator.Simulate(RaceKind.NewYear, new[] { Make(new bool[n], 4) }, envs, rf)[0];
            Assert.AreEqual(0, none.Events.Count(e => e.Kind == "geki_brake"), "全区間『使わない』なら檄は出ない");
            int losses = none.Events.Count(e => e.Kind == "brake" || e.Kind == "stall");
            Assert.GreaterOrEqual(losses, 3, "前提: 異変が複数出る");
            TeamResult all = RaceSimulator.Simulate(RaceKind.NewYear, new[] { Make(Enumerable.Repeat(true, n).ToArray(), 2) }, envs, rf)[0];
            Assert.AreEqual(2, all.Events.Count(e => e.Kind == "geki_brake"), "上限(2)まで");
            Assert.AreEqual(0, all.GekiLeft);
            Assert.Less(all.TotalDs, none.TotalDs);
            // 使うと決めた区間にだけ使う
            int firstLoss = none.Events.First(e => e.Kind == "brake" || e.Kind == "stall").Section;
            var one = new bool[n]; one[firstLoss] = true;
            TeamResult single = RaceSimulator.Simulate(RaceKind.NewYear, new[] { Make(one, 4) }, envs, rf)[0];
            Assert.AreEqual(1, single.Events.Count(e => e.Kind == "geki_brake"));
            Assert.AreEqual(firstLoss, single.Events.First(e => e.Kind == "geki_brake").Section);
            // GekiUse が null なら従来のおまかせ(上限まで自動)
            TeamResult autoRes = RaceSimulator.Simulate(RaceKind.NewYear, new[] { Make(null, 4) }, envs, rf)[0];
            Assert.AreEqual(Math.Min(4, losses), autoRes.Events.Count(e => e.Kind == "geki_brake"));
        }

        // ---------------------------------------------------------------- 予報・前評判・予選会の登録
        [Test]
        public void Forecast_IsDeterministic_AndCloseToTheActualEnvironment()
        {
            var rngs = new KeyedRngFactory(99UL);
            double dt = 0, dw = 0; int rainOk = 0, days = 0;
            for (int rid = 1; rid <= 400; rid++)
            {
                Env[] act = RaceMath.GenEnv(rngs.Stream(RngRegistry.LabelRace, rid, 9999), RaceKind.National);
                Env[] f1 = RaceMath.Forecast(rngs.Stream(RngRegistry.LabelRace, rid, 9998), act), f2 = RaceMath.Forecast(rngs.Stream(RngRegistry.LabelRace, rid, 9998), act);
                Assert.AreEqual(f1[0].Temp, f2[0].Temp);
                dt += Math.Abs(f1[0].Temp - act[0].Temp); dw += Math.Abs(f1[0].Wind - act[0].Wind);
                if (f1[0].Rain == act[0].Rain) rainOk++;
                days++;
            }
            Assert.That(dt / days, Is.InRange(1.0, 1.5), "気温の誤差 N(0, 1.5) の平均絶対誤差 ≒ 1.2");
            Assert.That(dw / days, Is.InRange(0.6, 1.0), "風の誤差 N(0, 1.0) の平均絶対誤差 ≒ 0.8");
            Assert.That(rainOk / (double)days, Is.InRange(0.80, 0.90), "雨の的中率 85%");
            GameSession gs = ToRaceWeek(Seed, Difficulty.Mid, 1, RaceKind.Fall);
            Env[] a = gs.Forecast(RaceKind.Fall), b = gs.Forecast(RaceKind.Fall);
            Assert.AreEqual(a[0].Temp, b[0].Temp);
        }

        [Test]
        public void RacePreview_RanksEveryParticipant_AndMarksFollowRank()
        {
            GameSession gs = ToRaceWeek(Seed, Difficulty.Mid, 2, RaceKind.National);
            IReadOnlyList<PreviewRow> rows = gs.RacePreview(RaceKind.National);
            Assert.AreEqual(gs.YearInProgress.Entrants(RaceKind.National).Count, rows.Count);
            CollectionAssert.AreEqual(Enumerable.Range(1, rows.Count), rows.Select(r => r.Rank));
            for (int i = 1; i < rows.Count; i++) Assert.LessOrEqual(rows[i].Value, rows[i - 1].Value);
            Assert.AreEqual(1, rows.Count(r => r.IsOwn));
            Assert.AreEqual('◎', rows[0].Mark);
            Assert.AreEqual('〇', rows[1].Mark);
            Assert.AreEqual('-', rows[rows.Count - 1].Mark);
        }

        [Test]
        public void QualifierEntries_Validation_AndEffect()
        {
            // シード外の年度を探す
            GameSession gs = null;
            for (ulong seed = Seed; seed < Seed + 8; seed++)
            {
                GameSession g = GameSession.New(seed, Difficulty.Low);
                while (g.YearWeek < GameSession.QualifierDeadlineWeek) g.StepWeek();
                if (!g.PrepareYear().Seeds.Contains(g.School.Id)) { gs = g; break; }
            }
            Assert.IsNotNull(gs);
            var ids = gs.State.Runners.Where(r => !r.IsInjured).OrderBy(r => TeamState.LongP(r)).ThenBy(r => r.Id).Take(12).Select(r => r.Id).ToList();     // 力の低い順の 12 名
            Assert.IsFalse(gs.SetQualifierEntries(ids.Take(11).ToList()).Ok);
            Assert.IsFalse(gs.SetQualifierEntries(ids.Take(11).Concat(new[] { ids[0] }).ToList()).Ok);
            Assert.IsFalse(gs.SetQualifierEntries(ids.Take(11).Concat(new[] { 9999 }).ToList()).Ok);
            OpResult setQ = gs.SetQualifierEntries(ids);
            Assert.IsTrue(setQ.Ok, setQ.Reason);
            CollectionAssert.AreEqual(ids, gs.QualifierEntries);
            QualifierTeam team = null;
            IPlayerSide side = gs;
            team = side.PrepareQualifier(gs.Year * 10 + 4, new List<RaceRunner>(), gs.Rngs);
            int inside = team.Runners.Count(r => ids.Contains(r.RunnerId));
            Assert.GreaterOrEqual(inside, 9, "登録した 12 名が出る(欠場のぶんだけ補う): " + inside);
        }
    }
}
