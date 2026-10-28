using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.World;
using System.Diagnostics;
using Ekiden.Persistence.Container;
using Ekiden.Platform;
using Ekiden.Persistence.Json;
using Ekiden.Persistence.Snapshot;
using NUnit.Framework;

namespace Ekiden.Persistence.Tests
{
    /// <summary>
    /// レース進行中の中断セーブ(09 §4.6・08 §6.5)。大会の当日の画面・区間の途中・走り終わったあとのどこで中断しても、
    /// 『中断 → 再開 → 続ける』が、中断せずに続けた結果と(週の終わりの状態で)バイト一致する。
    /// 中断セーブ = 練習が解決される直前の状態(<see cref="GameFlow.ResolvingWeek"/>)+ 当日の決定の記録(<see cref="RaceDayRecord"/>)。
    /// </summary>
    public class SuspendRaceTests
    {
        private const ulong Seed = 20261029UL;

        // ------------------------------------------------------------ 画面の操作(決まった方針)
        private static void Handle(GameSession gs, GameFlow flow, Stop s)
        {
            switch (s.Kind)
            {
                case StopKind.Order:
                    {
                        int[] sug = gs.SuggestOrder(s.Race.Value);
                        if (sug != null) gs.SetOrder(s.Race.Value, sug.Reverse().ToArray());
                        break;
                    }
                case StopKind.Event:
                    flow.ChooseEvent(s.Choices.Last().Id);
                    break;
            }
        }

        /// <summary>当日の準備(まだ何も変えていないときだけ)。当日変更・指示。</summary>
        private static void Prep(RaceDay rd)
        {
            if (rd.Touched) return;
            rd.ApplyGauge();
            if (rd.Bench.Count > 0) rd.Swap(0, 0);
            if (rd.Sections > 3) rd.SetInstruction(2, Instruction.Attack);
            if (rd.Sections > 5) rd.SetInstruction(5, Instruction.Hold);
        }

        /// <summary>区間 <paramref name="to"/> の手前まで進める(いま通過した区間から。区間ごとに指示を決め、奇数の区間で異変が出れば檄)。</summary>
        private static void StepTo(RaceDay rd, int to)
        {
            if (!rd.Started) rd.Start();
            while (rd.Resolved < to)
            {
                int s = rd.Resolved;
                rd.SetInstruction(s, (Instruction)(s % 3));
                if (s % 2 == 1 && rd.NextIncident() != null && rd.GekiLeft > 0) rd.UseGeki();       // 偶数の区間の異変には檄を使わない(おまかせの自動とは違う決定にする)
                rd.Advance();
            }
        }

        /// <summary>大会の週の残りを、次の週の最初の停止まで流す。<paramref name="first"/> は走ったあとの最初の停止。戻り値 = その時点の状態、last = 次の週の最初の停止(まだ操作していない)。</summary>
        private static string FinishWeek(GameSession gs, GameFlow flow, int weekAbs, Stop first, out Stop last)
        {
            Stop s = first;
            while (gs.State.AbsWeek <= weekAbs)
            {
                Handle(gs, flow, s);
                s = flow.Advance();
            }
            last = s;
            return GameSnapshot.Capture(gs).ToJson();
        }

        // ------------------------------------------------------------ 中断 → 再開
        /// <summary>checkpoint から復元した流れを、当日の停止まで進める(週の初めの停止は、元の流れと同じ操作で済ませる)。</summary>
        private static GameFlow ToRaceDay(JsonObject body, out GameSession gs, out SuspendCheckpoint cp, out Stop prep)
        {
            gs = GameSnapshot.Restore(body);
            var flow = new GameFlow(gs);
            cp = new SuspendCheckpoint();
            cp.Attach(flow);
            while (true)
            {
                Stop s = flow.Advance();
                Handle(gs, flow, s);
                if (s.Kind == StopKind.RaceDayPrep) { prep = s; return flow; }
            }
        }

        private static JsonValue ThroughText(SuspendCheckpoint cp, GameFlow flow)
        {
            JsonObject payload = cp.BuildPayload(flow);
            Assert.IsNotNull(payload, "中断セーブのペイロードが作れる");
            return JsonValue.Parse(payload.ToJson());
        }

        private enum Cut { PrepUntouched, PrepTouched, Section, Finished, AfterRace }

        /// <summary>checkpoint から当日の途中(cut / section)まで進めて中断 → 再開 → 最後まで。週の終わりの状態を返す。<paramref name="twice"/> なら、再開した流れで区間の終わりまで進めてもう一度中断 → 再開する。</summary>
        private static string Interrupted(JsonObject body, int weekAbs, Cut cut, int section, bool twice)
        {
            GameSession gs1;
            SuspendCheckpoint cp1;
            Stop prep1;
            GameFlow flow1 = ToRaceDay(body, out gs1, out cp1, out prep1);
            RaceDay rd1 = prep1.RaceDay;
            if (cut != Cut.PrepUntouched) Prep(rd1);
            if (cut == Cut.Section) StepTo(rd1, section);
            if (cut == Cut.Finished || cut == Cut.AfterRace) StepTo(rd1, rd1.Sections);
            if (cut == Cut.AfterRace) flow1.Advance();                                  // 走ったあとの最初の停止(結果・イベント)で中断
            JsonValue text = ThroughText(cp1, flow1);

            var cp2 = new SuspendCheckpoint();
            GameFlow flow2 = cp2.Resume(text);
            Assert.IsNotNull(flow2, "再開できる");
            Stop s = flow2.Advance();
            if (s.Kind == StopKind.RaceDayPrep)
            {
                Assert.AreEqual(cut == Cut.Section ? section : 0, s.RaceDay.Resolved, "中断した区間の手前から");
                Prep(s.RaceDay);
                if (twice)
                {
                    int mid = Math.Min(s.RaceDay.Sections, Math.Max(s.RaceDay.Resolved + 1, s.RaceDay.Sections - 2));
                    StepTo(s.RaceDay, mid);
                    JsonValue text2 = ThroughText(cp2, flow2);
                    var cp3 = new SuspendCheckpoint();
                    flow2 = cp3.Resume(text2);
                    Assert.IsNotNull(flow2, "2 回目の再開");
                    s = flow2.Advance();
                    Assert.AreEqual(StopKind.RaceDayPrep, s.Kind);
                    Assert.AreEqual(mid, s.RaceDay.Resolved);
                }
                StepTo(s.RaceDay, s.RaceDay.Sections);
                s = flow2.Advance();
            }
            Stop last;
            return FinishWeek(flow2.Session, flow2, weekAbs, s, out last);
        }

        [Test]
        public void SuspendAtEveryPointOfTheRaceDay_ResumeEqualsUninterrupted()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            var flow = new GameFlow(gs);
            var cp = new SuspendCheckpoint();
            cp.Attach(flow);
            var seen = new HashSet<RaceKind>();
            int resumes = 0;
            bool anySwap = false, anyGeki = false;
            Stop s = flow.Advance();
            while (seen.Count < 3)
            {
                Handle(gs, flow, s);
                if (s.Kind != StopKind.RaceDayPrep) { s = flow.Advance(); continue; }
                JsonObject body = cp.Body;
                Assert.IsNotNull(body, "大会の週の練習が解決される直前の状態が取れている");
                int weekAbs = gs.State.AbsWeek;
                RaceDay rd = s.RaceDay;
                int n = rd.Sections;
                RaceKind kind = rd.Kind;
                Prep(rd);
                StepTo(rd, n);
                RaceDayRecord final = rd.Capture();
                anySwap |= final.Swaps.Count > 0;
                anyGeki |= final.GekiSpent > 0;
                Stop next;
                string reference = FinishWeek(gs, flow, weekAbs, flow.Advance(), out next);

                var points = new List<Tuple<Cut, int>> { Tuple.Create(Cut.PrepUntouched, 0), Tuple.Create(Cut.PrepTouched, 0), Tuple.Create(Cut.Finished, n), Tuple.Create(Cut.AfterRace, n) };
                for (int k = 0; k < n; k++) points.Add(Tuple.Create(Cut.Section, k));
                foreach (Tuple<Cut, int> p in points)
                {
                    Assert.AreEqual(reference, Interrupted(body, weekAbs, p.Item1, p.Item2, false), kind + " " + p.Item1 + " " + p.Item2);
                    resumes++;
                }
                Assert.AreEqual(reference, Interrupted(body, weekAbs, Cut.Section, 1, true), kind + " 2 回中断");
                resumes++;
                seen.Add(kind);
                s = next;
            }
            Assert.Greater(resumes, 30);
            Assert.IsTrue(anySwap, "当日変更を使った大会がある(試験が空振りしない)");
            Assert.IsTrue(anyGeki, "檄を使った大会がある(試験が空振りしない)");
        }

        [Test]
        public void MidWeekSuspend_AtEveryStopOfAYear_ResumesToTheSameWeekEnd()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            gs.State.Settings.EventAuto = false;                                // イベントの選択の停止も出す
            var flow = new GameFlow(gs);
            var cp = new SuspendCheckpoint();
            cp.Attach(flow);
            var kinds = new HashSet<StopKind>();
            int resumes = 0;
            Stop s = flow.Advance();
            while (gs.Year < 2)
            {
                int weekAbs = gs.State.AbsWeek;
                var payloads = new List<Tuple<StopKind, string>>();
                while (gs.State.AbsWeek <= weekAbs && gs.Year < 2)
                {
                    if (flow.MidWeek) payloads.Add(Tuple.Create(s.Kind, cp.BuildPayload(flow).ToJson()));      // その停止を見ている(まだ選んでいない)ときに中断
                    Handle(gs, flow, s);
                    s = flow.Advance();
                }
                if (gs.Year >= 2) break;
                string reference = GameSnapshot.Capture(gs).ToJson();                                           // 次の週の最初の停止(まだ操作していない)
                foreach (Tuple<StopKind, string> p in payloads)
                {
                    GameFlow flow2 = new SuspendCheckpoint().Resume(JsonValue.Parse(p.Item2));
                    Assert.IsNotNull(flow2, "再開できる: " + p.Item1);
                    GameSession gs2 = flow2.Session;
                    int fired = 0;
                    flow2.ResolvingWeek += f => { if (f.Session.State.AbsWeek == weekAbs) fired++; };          // 次の週以降(停止のない週は『続ける』の中で進む)は数えない
                    Stop s2 = flow2.Advance();
                    CollectionAssert.DoesNotContain(new[] { StopKind.AnnualStart, StopKind.MonthPlan, StopKind.Entry, StopKind.QualifierEntry, StopKind.Order }, s2.Kind, "週の初めの停止は、中断の前に済んでいるので出さない");
                    while (gs2.State.AbsWeek <= weekAbs) { Handle(gs2, flow2, s2); s2 = flow2.Advance(); }
                    Assert.AreEqual(0, fired, "再開した週では、練習の解決の直前の通知(自動セーブの時点)をもう一度出さない");
                    Assert.AreEqual(reference, GameSnapshot.Capture(gs2).ToJson(), "週 " + weekAbs + " の " + p.Item1 + " で中断 → 再開");
                    kinds.Add(p.Item1);
                    resumes++;
                }
            }
            Assert.Greater(resumes, 12, "1 年度ぶんの週の途中の停止すべてで試した(停止のない週は中断する場面がない)");
            Assert.IsTrue(kinds.Contains(StopKind.RaceDayPrep), "当日の画面: " + string.Join(",", kinds));
            Assert.IsTrue(kinds.Contains(StopKind.MonthReport), "月末レポート: " + string.Join(",", kinds));
            Assert.IsTrue(kinds.Contains(StopKind.Event) || kinds.Contains(StopKind.Injury), "イベントか故障の停止: " + string.Join(",", kinds));
        }

        // ------------------------------------------------------------ 記録と形
        private sealed class Day
        {
            public GameSession Gs;
            public GameFlow Flow;
            public RaceDay Rd;
            public JsonObject Body;
            public SuspendCheckpoint Cp;
        }

        private static Day OpenDay()
        {
            var d = new Day();
            d.Gs = GameSession.New(Seed, Difficulty.Mid);
            d.Flow = new GameFlow(d.Gs);
            var cp = new SuspendCheckpoint();
            cp.Attach(d.Flow);
            while (true)
            {
                Stop s = d.Flow.Advance();
                Handle(d.Gs, d.Flow, s);
                if (s.Kind == StopKind.RaceDayPrep) { d.Rd = s.RaceDay; d.Body = cp.Body; d.Cp = cp; return d; }
            }
        }

        [Test]
        public void Record_RoundTripsThroughJson_AndRestoresTheSameDay()
        {
            Day d = OpenDay();
            Prep(d.Rd);
            StepTo(d.Rd, 3);
            RaceDayRecord r = d.Rd.Capture();
            RaceDayRecord back = SuspendSnapshot.ReadRace((JsonObject)JsonValue.Parse(SuspendSnapshot.CaptureRace(r).ToJson()));
            Assert.AreEqual(r.Kind, back.Kind);
            Assert.AreEqual(r.RaceId, back.RaceId);
            Assert.AreEqual(3, back.NextSection);
            Assert.IsTrue(back.Started);
            CollectionAssert.AreEqual(r.Instructions, back.Instructions);
            CollectionAssert.AreEqual(r.GekiUsed, back.GekiUsed);
            Assert.AreEqual(r.GekiSpent, back.GekiSpent);
            CollectionAssert.AreEqual(r.Swaps.Select(x => x.Section + ":" + x.RunnerId).ToArray(), back.Swaps.Select(x => x.Section + ":" + x.RunnerId).ToArray());

            Day e = OpenDay();                                                      // 同じ週を別に開いて、記録を戻す
            Assert.IsTrue(e.Rd.Restore(back).Ok);
            Assert.AreEqual(3, e.Rd.Resolved);
            Assert.IsTrue(e.Rd.Started);
            Assert.AreEqual(d.Rd.GekiLeft, e.Rd.GekiLeft);
            Assert.AreEqual(d.Rd.SwapsUsed, e.Rd.SwapsUsed);
            CollectionAssert.AreEqual(d.Rd.Lineup.Select(x => x.RunnerId).ToArray(), e.Rd.Lineup.Select(x => x.RunnerId).ToArray());
            CollectionAssert.AreEqual(d.Rd.Instructions, e.Rd.Instructions);
            var a = d.Rd.Preview();
            var b = e.Rd.Preview();
            CollectionAssert.AreEqual(a.Select(x => x.Id + ":" + x.Cum[x.Cum.Length - 1]).ToArray(), b.Select(x => x.Id + ":" + x.Cum[x.Cum.Length - 1]).ToArray(), "同じ決定なら同じ結果");
        }

        [Test]
        public void Restore_RejectsRecordsThatDoNotFit_AndLeavesTheDayAsIs()
        {
            Day d = OpenDay();
            Prep(d.Rd);
            RaceDayRecord good = d.Rd.Capture();
            int[] lineup = d.Rd.Lineup.Select(x => x.RunnerId).ToArray();
            Func<RaceDayRecord> copy = () => SuspendSnapshot.ReadRace((JsonObject)JsonValue.Parse(SuspendSnapshot.CaptureRace(good).ToJson()));

            RaceDayRecord r = copy(); r.RaceId += 1;
            Assert.IsFalse(d.Rd.Restore(r).Ok, "別の大会");
            r = copy(); r.Instructions = r.Instructions.Take(r.Instructions.Length - 1).ToArray();
            Assert.IsFalse(d.Rd.Restore(r).Ok, "区間の数");
            r = copy(); r.Instructions[0] = 7;
            Assert.IsFalse(d.Rd.Restore(r).Ok, "指示の値");
            r = copy(); r.GekiUsed[0] = 5;
            Assert.IsFalse(d.Rd.Restore(r).Ok, "檄の値");
            r = copy(); r.NextSection = 2; r.Started = false;
            Assert.IsFalse(d.Rd.Restore(r).Ok, "始まっていないのに区間が進んでいる");
            r = copy(); r.NextSection = d.Rd.Sections + 1;
            Assert.IsFalse(d.Rd.Restore(r).Ok, "区間の数を超える");
            r = copy(); r.Swaps.Add(new RaceDayRecord.Swap { Section = 0, RunnerId = 999999 });
            Assert.IsFalse(d.Rd.Restore(r).Ok, "いない選手");
            r = copy(); r.Swaps.Add(new RaceDayRecord.Swap { Section = d.Rd.Sections, RunnerId = lineup[0] });
            Assert.IsFalse(d.Rd.Restore(r).Ok, "区間がない");
            r = copy(); r.Swaps.Clear(); r.Swaps.Add(new RaceDayRecord.Swap { Section = 0, RunnerId = lineup[1] }); r.Swaps.Add(new RaceDayRecord.Swap { Section = 1, RunnerId = lineup[1] });
            Assert.IsFalse(d.Rd.Restore(r).Ok, "同じ選手が 2 回");
            r = copy(); r.GekiSpent = 99;
            Assert.IsFalse(d.Rd.Restore(r).Ok, "檄の回数");
            CollectionAssert.AreEqual(lineup, d.Rd.Lineup.Select(x => x.RunnerId).ToArray(), "拒否した記録で当日は変わらない");
            Assert.IsTrue(d.Rd.Restore(copy()).Ok, "元の記録は戻せる");
        }

        [Test]
        public void Payload_SplitsIntoBodyAndRace_AndBadShapesAreRefused()
        {
            Day d = OpenDay();
            Prep(d.Rd);
            RaceDayRecord r = d.Rd.Capture();
            JsonObject payload = SuspendSnapshot.BuildPayload(d.Body, r);
            Assert.IsTrue(payload.ContainsKey("suspend"));
            Assert.IsFalse(d.Body.ContainsKey("suspend"), "本体の側は変えない");
            JsonObject body;
            RaceDayRecord back;
            Assert.IsTrue(SuspendSnapshot.TryRead(JsonValue.Parse(payload.ToJson()), out body, out back));
            Assert.IsNotNull(back);
            Assert.IsFalse(body.ContainsKey("suspend"));
            Assert.AreEqual(d.Body.ToJson(), body.ToJson(), "本体は取ったときのまま");

            JsonObject plain = SuspendSnapshot.BuildPayload(d.Body, null);               // 大会の週の途中でないときは、suspend は空
            Assert.IsTrue(SuspendSnapshot.TryRead(plain, out body, out back));
            Assert.IsNull(back);

            JsonObject broken = (JsonObject)JsonValue.Parse(payload.ToJson());
            ((JsonObject)((JsonObject)broken.Get("suspend")).Get("race"))["instructions"] = new JsonString("x");
            Assert.IsFalse(SuspendSnapshot.TryRead(broken, out body, out back), "形が合わない");
            Assert.IsNull(new SuspendCheckpoint().Resume(broken), "再開しない(スロットの先頭を使う)");
            Assert.IsFalse(SuspendSnapshot.TryRead(new JsonArray(), out body, out back));
        }

        [Test]
        public void Resume_IsRefused_ForARecordOfAnotherWeek_OrAfterTheFlowHasStarted()
        {
            Day d = OpenDay();
            Prep(d.Rd);
            RaceDayRecord r = d.Rd.Capture();
            GameSession gs = GameSnapshot.Restore(d.Body);
            var flow = new GameFlow(gs);
            RaceDayRecord other = SuspendSnapshot.ReadRace((JsonObject)JsonValue.Parse(SuspendSnapshot.CaptureRace(r).ToJson()));
            other.Kind = (RaceKind)(((int)other.Kind + 1) % 3);
            Assert.IsFalse(flow.ResumeRace(other).Ok, "今週の大会と違う");
            Assert.IsTrue(flow.ResumeRace(r).Ok);
            flow.Advance();
            Assert.IsFalse(flow.ResumeRace(r).Ok, "続けたあとは再開できない");
        }

        [Test]
        public void SuspendRace_IsOnlyAvailableDuringTheRaceWeek_AndPlainWeeksFallBackToTheWeekStart()
        {
            Day d = OpenDay();
            Assert.IsNotNull(d.Flow.SuspendRace, "当日の画面");
            Assert.IsFalse(d.Flow.CanSave, "練習が解決されたあとは普通のセーブはできない");
            var cp = new SuspendCheckpoint();
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            var flow = new GameFlow(gs);
            cp.Attach(flow);
            Assert.IsNull(flow.SuspendRace, "始める前");
            JsonObject start = cp.BuildPayload(flow);
            Assert.IsNotNull(start, "週の初めなら、その状態をそのまま中断セーブにできる");
            JsonObject b; RaceDayRecord rec;
            Assert.IsTrue(SuspendSnapshot.TryRead(start, out b, out rec));
            Assert.IsNull(rec);
            GameFlow back = new SuspendCheckpoint().Resume(start);
            Assert.IsNotNull(back);
            Assert.AreEqual(GameSnapshot.Capture(gs).ToJson(), GameSnapshot.Capture(back.Session).ToJson());
        }

        private static JsonObject Header(long counter, string kind)
        {
            return new HeaderBuilder { Kind = kind, Slot = 1, LineageId = "3f2c9a6e-0000-4000-8000-000000000001", SaveCounter = counter, AppVersion = "1.0.0", MasterHash = "a1b2c3d4", SavedAt = 1800000000 + counter, Year = 1, MonthIndex = 6, SchoolName = "青嵐大学", ScenarioId = "sc.mid", PlaySeconds = 100 + counter }.Build();
        }

        [Test]
        public void ThroughTheContainer_AutoSaveAtOrderConfirmed_ThenSuspendDuringTheRace_ResumesToTheSameResult()
        {
            Day d = OpenDay();
            int weekAbs = d.Gs.State.AbsWeek;
            var fs = new MemoryFileStore();
            var store = new SlotStore(fs);
            store.WriteAuto(1, Header(1, "auto"), d.Body);                              // オーダー確定後の自動セーブ = 練習が解決される直前の状態(08 §6.5)

            Prep(d.Rd);
            StepTo(d.Rd, 4);                                                            // 区間 4 の指示の前でアプリが中断された
            JsonObject payload = d.Cp.BuildPayload(d.Flow);
            JsonObject sh = Header(0, "suspend");
            sh["base_counter"] = new JsonNumber(1L);
            var sw = Stopwatch.StartNew();
            store.WriteSuspend(sh, payload);
            sw.Stop();
            Assert.Less(sw.ElapsedMilliseconds, 200, "中断セーブは同期で 200 ms 以内(09 §14.2。開発機はその 1/5 が目安)");
            Assert.IsTrue(store.CanResume(1), "有効・同じ歴史・base_counter = 先頭の保存番号");

            StepTo(d.Rd, d.Rd.Sections);                                                // 中断せずに続けた場合
            Stop last;
            string reference = FinishWeek(d.Gs, d.Flow, weekAbs, d.Flow.Advance(), out last);

            JsonValue back;
            Eksv.Decode(fs.ReadAllBytes(SlotStore.SuspendPath), out back);              // 再開: 読み込み → 流れを作り直す
            GameFlow flow = new SuspendCheckpoint().Resume(back);
            Assert.IsNotNull(flow);
            Stop s = flow.Advance();
            Assert.AreEqual(StopKind.RaceDayPrep, s.Kind);
            Assert.AreEqual(4, s.RaceDay.Resolved);
            StepTo(s.RaceDay, s.RaceDay.Sections);
            Assert.AreEqual(reference, FinishWeek(flow.Session, flow, weekAbs, flow.Advance(), out last));

            store.WriteAuto(1, Header(2, "auto"), GameSnapshot.Capture(flow.Session));    // 再開したあとの最初の自動セーブで、先頭が進む
            Assert.IsFalse(store.CanResume(1), "使い切り: 自動セーブが進んだら中断セーブは使わない");
        }
    }
}
