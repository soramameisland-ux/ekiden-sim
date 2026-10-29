using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;
using Ekiden.Domain.World;
using Ekiden.Persistence.Json;
using Ekiden.Persistence.Snapshot;
using NUnit.Framework;

namespace Ekiden.Persistence.Tests
{
    /// <summary>実体の選手を持つ自校 + 世界(<see cref="GameSession"/>)の保存(<see cref="GameSnapshot"/>)。T3: 『保存 → 読み込み → 続行』と『続行のみ』がバイト一致。年度の途中(大会の前後・予選会)でも。</summary>
    public class GameSnapshotTests
    {
        private const ulong Seed = 20260307UL;

        private static GameSession Play(Difficulty d, int weeks, ulong seed = Seed)
        {
            GameSession gs = GameSession.New(seed, d);
            for (int i = 0; i < weeks; i++) gs.StepWeek();
            return gs;
        }

        [Test]
        public void Notifications_SurviveASaveAndLoad_InOrder_WithReadFlags_AndAnOldSaveReadsAsEmpty()
        {
            GameSession gs = Play(Difficulty.Mid, 60);
            Assert.Greater(gs.State.Notifications.Count, 3, "試験の前提: 60 週で通知が溜まっている");
            gs.MarkNotificationRead(1);
            JsonObject snap = GameSnapshot.Capture(gs);
            GameSession back = GameSnapshot.Restore((JsonObject)JsonValue.Parse(snap.ToJson()));
            Assert.AreEqual(gs.State.Notifications.Count, back.State.Notifications.Count);
            for (int i = 0; i < gs.State.Notifications.Count; i++)
            {
                Notification a = gs.State.Notifications[i], b = back.State.Notifications[i];
                Assert.AreEqual(a.Kind, b.Kind); Assert.AreEqual(a.AbsWeek, b.AbsWeek); Assert.AreEqual(a.Code, b.Code);
                Assert.AreEqual(a.RunnerId, b.RunnerId); Assert.AreEqual(a.N, b.N); Assert.AreEqual(a.Unread, b.Unread);
            }
            Assert.IsFalse(back.State.Notifications[1].Unread);
            ((JsonObject)((JsonObject)snap["team"])["player_team"]).Remove("notifications");
            GameSession old = GameSnapshot.Restore((JsonObject)JsonValue.Parse(snap.ToJson()));
            Assert.IsEmpty(old.State.Notifications);
        }

        [Test]
        public void ObCountry_SurvivesASaveAndLoad_AndAnOldSaveWithoutItReadsAsNone()
        {
            GameSession gs = Play(Difficulty.Mid, 5);
            gs.State.Obs.Add(OBRecord.FromGraduate(new Runner { Overseas = true, Country = 7, FamilyName = "留", GivenName = "学" }, 1, 1, CareerPath.Employment));
            JsonObject snap = GameSnapshot.Capture(gs);
            GameSession back = GameSnapshot.Restore((JsonObject)JsonValue.Parse(snap.ToJson()));
            Assert.AreEqual(7, back.State.Obs.Last().Country);
            JsonObject team = (JsonObject)snap["team"];
            foreach (JsonValue ob in (JsonArray)team["ob_records"]) ((JsonObject)ob).Remove("origin_country");
            GameSession old = GameSnapshot.Restore((JsonObject)JsonValue.Parse(snap.ToJson()));
            Assert.IsTrue(old.State.Obs.All(o => o.Country == 0));
        }

        [Test]
        public void MonthStats_SurviveASaveAndLoad_AndRollTheSameAfterwards()
        {
            GameSession gs = Play(Difficulty.Mid, 48 + 6);
            GameSession back = GameSnapshot.Restore((JsonObject)JsonValue.Parse(GameSnapshot.Capture(gs).ToJson()));
            foreach (Runner r in gs.State.Runners)
            {
                Runner b = back.State.FindRunner(r.Id);
                CollectionAssert.AreEqual(r.MonthStartStats, b.MonthStartStats);
                CollectionAssert.AreEqual(r.PrevMonthStats, b.PrevMonthStats);
                var da = gs.RunnerDetail(r.Id).Stats; var db = back.RunnerDetail(r.Id).Stats;
                CollectionAssert.AreEqual(da.Select(x => x.DeltaThisMonth).ToArray(), db.Select(x => x.DeltaThisMonth).ToArray());
                CollectionAssert.AreEqual(da.Select(x => x.DeltaLastMonth).ToArray(), db.Select(x => x.DeltaLastMonth).ToArray());
            }
            for (int i = 0; i < 9; i++) { gs.StepWeek(); back.StepWeek(); }             // 月をまたいで続けても同じ
            foreach (Runner r in gs.State.Runners)
            {
                Runner b = back.State.FindRunner(r.Id);
                CollectionAssert.AreEqual(r.MonthStartStats, b.MonthStartStats);
                CollectionAssert.AreEqual(r.PrevMonthStats, b.PrevMonthStats);
            }
            Assert.IsTrue(gs.State.Runners.Any(r => r.MonthStartStats != null && r.PrevMonthStats != null && !r.MonthStartStats.SequenceEqual(r.PrevMonthStats)), "試験の前提: 能力が動いている");
        }

        [Test]
        public void CaptureRestoreCapture_IsIdempotent_AtEveryKindOfWeek()
        {
            foreach (int weeks in new[] { 0, 1, 21, 25, 26, 27, 36, 47, 48, 60, 100 })
            {
                GameSession gs = Play(Difficulty.Mid, weeks);
                string a = GameSnapshot.Capture(gs).ToJson();
                GameSession back = GameSnapshot.Restore((JsonObject)JsonValue.Parse(a));
                string b = GameSnapshot.Capture(back).ToJson();
                Assert.AreEqual(a, b, "週 " + weeks);
                Assert.AreEqual(gs.State.AbsWeek, back.State.AbsWeek);
                Assert.AreEqual(gs.YearInProgress == null, back.YearInProgress == null, "週 " + weeks);
            }
        }

        [Test]
        public void Saved_DoublesAreQ6Closed()
        {
            foreach (int weeks in new[] { 20, 37, 48, 95, 150 })
                Assert.IsEmpty(GameSnapshot.NotQ6Closed(Play(Difficulty.Low, weeks)), "週 " + weeks);
        }

        [Test]
        public void MidYear_RestoresQualifierAndRaceResults()
        {
            GameSession gs = Play(Difficulty.Elite, 29);                                       // 第二戦(28)のあと
            GameSession back = GameSnapshot.Restore((JsonObject)JsonValue.Parse(GameSnapshot.Capture(gs).ToJson()));
            Assert.IsNotNull(back.YearInProgress);
            Assert.IsTrue(back.YearInProgress.QualifierDone);
            CollectionAssert.AreEqual(gs.YearInProgress.Result.Qualified, back.YearInProgress.Result.Qualified);
            foreach (var kv in gs.YearInProgress.Result.Order)
            {
                CollectionAssert.AreEqual(kv.Value, back.YearInProgress.Result.Order[kv.Key]);
                Assert.AreEqual(kv.Value[0], back.YearInProgress.Result.Winner[kv.Key]);
            }
            Assert.AreEqual(2, back.YearInProgress.Result.Order.Count, "第一戦・第二戦が終わっている");
            CollectionAssert.AreEqual(gs.YearInProgress.FallIds, back.YearInProgress.FallIds, "出場権は BeginYear のやり直しで同じになる");
            CollectionAssert.AreEqual(gs.YearInProgress.Seeds, back.YearInProgress.Seeds);
        }

        [Test]
        public void SaveLoadContinue_EqualsContinueOnly_AtEveryWeekOfTwoYears_ViaJsonText()
        {
            const int weeks = 48 * 2 + 5;
            string reference = Determinism.RunGame(Seed, Difficulty.Mid, weeks);
            Assert.AreEqual(reference, Determinism.RunGame(Seed, Difficulty.Mid, weeks), "同じ入力で 2 回");
            foreach (int w in new[] { 1, 2, 4, 8, 20, 21, 22, 24, 25, 26, 27, 28, 29, 35, 36, 37, 40, 44, 47, 48, 49, 50, 73, 96 })
                Assert.AreEqual(reference, Determinism.RunGame(Seed, Difficulty.Mid, weeks, new HashSet<int> { w }), "週 " + w + " で保存 → 読み込み");
        }

        [Test]
        public void SaveLoadContinue_EveryWeek_AndViaContainer()
        {
            const int weeks = 48 + 30;
            string reference = Determinism.RunGame(Seed, Difficulty.Low, weeks);
            Assert.AreEqual(reference, Determinism.RunGame(Seed, Difficulty.Low, weeks, new HashSet<int>(Enumerable.Range(1, weeks - 1))), "毎週 読み込み直す");
            Assert.AreEqual(reference, Determinism.RunGame(Seed, Difficulty.Low, weeks, new HashSet<int> { 10, 26, 47, 60 }, Determinism.Via.Container), "コンテナ経由");
        }

        [Test]
        public void SaveLoadContinue_WithTicketsDeclaredAndPending()
        {
            const int weeks = 48 * 3;
            string Run(ISet<int> at)
            {
                GameSession gs = GameSession.New(Seed, Difficulty.Mid);
                gs.State.Tickets.Earn(Ekiden.Domain.Tickets.TicketMilestones.WinFall, 0);
                gs.State.Tickets.Earn(Ekiden.Domain.Tickets.TicketMilestones.WinNational, 0);
                for (int w = 1; w <= weeks; w++)
                {
                    gs.StepWeek();
                    if (at != null && at.Contains(w)) gs = GameSnapshot.Restore((JsonObject)JsonValue.Parse(GameSnapshot.Capture(gs).ToJson()));
                }
                return GameSnapshot.Capture(gs).ToJson();
            }
            string reference = Run(null);
            Assert.AreEqual(reference, Run(new HashSet<int> { 41, 47, 48 }), "宣言のあと・入学の前後で保存");
            Assert.AreEqual(reference, Run(new HashSet<int> { 89, 95, 96, 100 }));
        }

        [Test]
        public void DifferentSeedsDiffer_AndClubWorks()
        {
            Assert.AreNotEqual(Determinism.RunGame(Seed, Difficulty.Mid, 60), Determinism.RunGame(Seed + 1, Difficulty.Mid, 60));
            const int weeks = 48 + 10;
            string reference = Determinism.RunGame(Seed, Difficulty.Club, weeks);
            Assert.AreEqual(reference, Determinism.RunGame(Seed, Difficulty.Club, weeks, new HashSet<int> { 5, 30, 48 }));
        }

        // ---------------------------------------------------------------- 操作と進行制御(GameFlow)つきの保存
        private static GameSession RoundTrip(GameSession gs)
        {
            return GameSnapshot.Restore((JsonObject)JsonValue.Parse(GameSnapshot.Capture(gs).ToJson()));
        }

        /// <summary>操作つきの 2 年度。onStop は停止ごとに画面の操作をする。saveAt の停止(CanSave のとき)で保存 → 読み込み → 新しい GameFlow で続ける。</summary>
        private static string PlayWithOperations(Func<Stop, bool> saveAt, out int saves)
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            gs.State.Settings.EventAuto = false;
            gs.State.Tickets.Earn(Ekiden.Domain.Tickets.TicketMilestones.WinFall, 0);
            var flow = new GameFlow(gs);
            saves = 0;
            int years = 0;
            var savedAt = new HashSet<string>();                 // 同じ停止では 1 度だけ保存する(読み込み直した流れは、その週の最初の停止から再開するので、同じ停止がもう一度出る)
            var upgraded = new HashSet<int>();
            while (years < 2)
            {
                Stop s = flow.Advance();
                Apply(gs, flow, s, upgraded);
                if (saveAt != null && flow.CanSave && saveAt(s) && savedAt.Add(gs.State.AbsWeek + ":" + s.Key))
                {
                    gs = RoundTrip(gs);
                    flow = new GameFlow(gs);
                    saves++;
                }
                if (s.Kind == StopKind.YearEndTurnover) years++;
            }
            return GameSnapshot.Capture(gs).ToJson();
        }

        /// <summary>停止に応じた画面の操作(決まった方針。何度呼んでも同じ状態になる)。</summary>
        private static void Apply(GameSession gs, GameFlow flow, Stop s, HashSet<int> upgraded)
        {
            TeamState t = gs.State;
            switch (s.Kind)
            {
                case StopKind.AnnualStart:
                    gs.SetAnnualPlan(Ekiden.Domain.Screens.PlanFocus.Speed, 2, new[] { PeakingMode.Standard, PeakingMode.Short, PeakingMode.Standard, PeakingMode.Short, PeakingMode.None });
                    break;
                case StopKind.MonthPlan:
                    gs.SetMonthlyPlan(2, "th.speed");
                    break;
                case StopKind.Entry:
                    gs.SetEntries(s.Race.Value, t.Runners.OrderByDescending(r => TeamState.LongP(r)).ThenBy(r => r.Id).Take(RaceTables.Rule(s.Race.Value).Entry).Select(r => r.Id).ToList());
                    break;
                case StopKind.Event:
                    flow.ChooseEvent(s.Choices.Last().Id);
                    break;
                case StopKind.Order:
                    {
                        int[] sug = gs.SuggestOrder(s.Race.Value);
                        if (sug != null) gs.SetOrder(s.Race.Value, sug.Reverse().ToArray());
                        break;
                    }
                case StopKind.QualifierEntry:
                    gs.SetQualifierEntries(t.Runners.Where(r => !r.IsInjured).OrderBy(r => TeamState.LongP(r)).ThenBy(r => r.Id).Take(12).Select(r => r.Id).ToList());
                    break;
                case StopKind.RaceDayPrep:
                    {
                        RaceDay rd = s.RaceDay;
                        rd.ApplyGauge();
                        if (rd.Bench.Count > 0) rd.Swap(0, 0);
                        rd.Start();
                        for (int i = 0; i < rd.Sections; i++)
                        {
                            if (rd.NextIncident() != null && rd.GekiLeft > 0) rd.UseGeki();
                            rd.Advance();
                        }
                        break;
                    }
                case StopKind.MonthReport:
                    break;
                case StopKind.YearEndSettle:
                    if (upgraded.Add(gs.Year)) gs.UpgradeFacility("dorm");
                    break;
            }
            int m = TeamWeek.YearWeek(t) / 4;
            if (s.Kind == StopKind.MonthPlan || s.Kind == StopKind.AnnualStart)
            {
                Runner r = t.Runners.OrderBy(x => x.Id).First(x => !x.IsInjured);
                if (!t.Talked.Contains(r.Id)) gs.Talk(r.Id);
                if (m >= 2 && m <= 7) for (int i = 0; i < 2; i++) gs.ScoutObserve(i + m * 3);
                if (m >= 8 && m <= 10) { gs.ScoutTalk(m * 3); gs.ScoutInvite(m * 3); }
            }
        }

        [Test]
        public void ScoutActionsAndGuidance_SurviveSaveLoad()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Mid);
            gs.State.Settings.ScoutAuto = false;
            for (int i = 0; i < 8; i++) gs.StepWeek();
            Assert.IsTrue(gs.ScoutObserve(4).Ok);
            Assert.IsTrue(gs.ScoutObserve(9).Ok);
            Runner r = gs.State.Runners[3];
            Assert.IsTrue(gs.GuideMenu(r.Id, new MenuSpec("ks.core", "ks.rest", "mil.low")).Ok);
            GameSession back = RoundTrip(gs);
            Assert.AreEqual(ScoutStage.Observed, back.StageOf(4));
            Assert.AreEqual(ScoutStage.Observed, back.StageOf(9));
            Assert.AreEqual(ScoutStage.None, back.StageOf(5));
            Assert.AreEqual(gs.ScoutPointsAvailable, back.ScoutPointsAvailable);
            Assert.AreEqual(GameSnapshot.Capture(gs).ToJson(), GameSnapshot.Capture(back).ToJson());
            Assert.IsTrue(back.CancelGuide(r.Id).Ok);
            Assert.AreEqual(4, back.State.GuidanceLeft, "今週の指定の取り消しでポイントが戻る(保存をまたいでも)");
        }

        [Test]
        public void OrdersAndQualifierEntries_SurviveSaveLoad()
        {
            GameSession gs = GameSession.New(Seed, Difficulty.Low);
            while (gs.YearWeek < GameSession.QualifierDeadlineWeek) gs.StepWeek();
            gs.AutoEntries(RaceKind.Fall);
            int[] sug = gs.SuggestOrder(RaceKind.National);
            while (gs.YearWeek < 24) gs.StepWeek();
            gs.AutoEntries(RaceKind.National);
            sug = gs.SuggestOrder(RaceKind.National);
            Assert.IsTrue(gs.SetOrder(RaceKind.National, sug).Ok);
            if (!gs.PrepareYear().Seeds.Contains(gs.School.Id))
            {
                var ids = gs.State.Runners.Where(r => !r.IsInjured).Take(12).Select(r => r.Id).ToList();
                Assert.IsTrue(gs.SetQualifierEntries(ids).Ok);
            }
            GameSession back = RoundTrip(gs);
            CollectionAssert.AreEqual(gs.OrderOf(RaceKind.National), back.OrderOf(RaceKind.National));
            Assert.IsNull(back.OrderOf(RaceKind.Fall));
            Assert.AreEqual(gs.QualifierEntries == null, back.QualifierEntries == null);
            if (gs.QualifierEntries != null) CollectionAssert.AreEqual(gs.QualifierEntries, back.QualifierEntries);
            Assert.AreEqual(GameSnapshot.Capture(gs).ToJson(), GameSnapshot.Capture(back).ToJson());
        }

        /// <summary>予選会の週の調整(L-53 ②)は、保存した予選会の登録と状態から答えが決まる(保存しない述語を読み込み後に作り直す)。保存 → 読み込みのあとも、各選手の調整の添字と、その週の結果が変わらない。</summary>
        [Test]
        public void QualifierWeekTaper_SurvivesSaveLoad_AndTheWeekPlaysTheSame()
        {
            GameSession gs = null;
            for (ulong seed = Seed; seed < Seed + 12 && gs == null; seed++)
            {
                GameSession t = GameSession.New(seed, Difficulty.Low);
                while (t.YearWeek < Ekiden.Domain.Team.Calendar.QualifierWeek) t.StepWeek();
                if (!t.PrepareYear().Seeds.Contains(t.School.Id)) gs = t;
            }
            Assert.IsNotNull(gs, "試験の前提: シード外の年度がある");
            foreach (Ekiden.Domain.Entities.Runner r in gs.State.Runners) { r.InjurySeverity = Ekiden.Domain.Entities.InjurySeverity.None; r.InjuryWeeksLeft = 0; }
            var ids = gs.State.Runners.OrderBy(r => r.Id).Skip(2).Take(12).Select(r => r.Id).ToList();
            Assert.IsTrue(gs.SetQualifierEntries(ids).Ok);
            GameSession back = RoundTrip(gs);
            int[] want = gs.State.Runners.Select(r => Ekiden.Domain.Team.TeamWeek.TaperIndex(gs.State, r, Ekiden.Domain.Team.Calendar.QualifierWeek)).ToArray();
            int[] got = back.State.Runners.Select(r => Ekiden.Domain.Team.TeamWeek.TaperIndex(back.State, r, Ekiden.Domain.Team.Calendar.QualifierWeek)).ToArray();
            CollectionAssert.AreEqual(want, got);
            Assert.AreEqual(12, got.Count(i => i == 3), "登録メンバー 12 名が予選会の週の調整");
            gs.StepWeek(); back.StepWeek();
            Assert.AreEqual(GameSnapshot.Capture(gs).ToJson(), GameSnapshot.Capture(back).ToJson(), "予選会の週(調整・走った疲労)を保存をまたいで同じに解く");
        }

        [Test]
        public void SettledYear_CanBeSavedAndResumed()
        {
            GameSession a = GameSession.New(Seed, Difficulty.Mid), b = GameSession.New(Seed, Difficulty.Mid);
            a.AutoUpgrade = false; b.AutoUpgrade = false;
            for (int i = 0; i < 47; i++) { a.StepWeek(); b.StepWeek(); }
            // 年度末の前半(精算)まで済ませて保存 → 読み込み → 強化 → 後半
            foreach (GameSession gs in new[] { a, b })
            {
                gs.ResolveTraining();
                RaceInfo? own;
                gs.PlayWeekRaces(out own);
                gs.ResolveWeekEvents(gs.SelectWeekEvents(own), own, new DefaultChoicePolicy());
                gs.FinishWeek();
                gs.SettleYear();
            }
            Assert.IsTrue(a.IsYearSettled);
            b = RoundTrip(b);
            b.AutoUpgrade = false;
            Assert.IsTrue(b.IsYearSettled, "精算のあとの状態が復元される");
            Assert.AreEqual(GameSnapshot.Capture(a).ToJson(), GameSnapshot.Capture(b).ToJson());
            foreach (GameSession gs in new[] { a, b })
            {
                gs.UpgradeRecommended();
                gs.TurnoverYear();
                gs.AdvanceWeek();
            }
            Assert.AreEqual(GameSnapshot.Capture(a).ToJson(), GameSnapshot.Capture(b).ToJson(), "精算のあとで保存しても、後半の結果は変わらない");
            Assert.AreEqual(2, b.Year);
        }

        [Test]
        public void FlowWithOperations_SaveLoadContinue_EqualsContinueOnly()
        {
            int n;
            string reference = PlayWithOperations(null, out n);
            Assert.AreEqual(0, n);
            Assert.AreEqual(reference, PlayWithOperations(null, out n), "同じ操作 → 同じ結果");
            string every = PlayWithOperations(s => true, out n);
            Assert.Greater(n, 10, "保存できる停止のたびに保存 → 読み込み");
            Assert.AreEqual(reference, every, "停止ごとの保存 → 読み込み → 続行 = 続行のみ");
            string entries = PlayWithOperations(s => s.Kind == StopKind.Entry || s.Kind == StopKind.Order || s.Kind == StopKind.QualifierEntry || s.Kind == StopKind.YearEndSettle, out n);
            Assert.AreEqual(reference, entries, "登録・オーダー・年度末の精算のあと");
        }

        [Test]
        public void Restore_ContinuesToProduceTheSameStatsAsAStraightRun()
        {
            GameSession a = Play(Difficulty.Elite, 48 * 4), b = Play(Difficulty.Elite, 48 * 2);
            b = GameSnapshot.Restore((JsonObject)JsonValue.Parse(GameSnapshot.Capture(b).ToJson()));
            for (int i = 0; i < 48 * 2; i++) b.StepWeek();
            Assert.AreEqual(a.Stats.Count, b.Stats.Count);
            for (int i = 0; i < a.Stats.Count; i++)
            {
                CollectionAssert.AreEqual(a.Stats[i].Place, b.Stats[i].Place, "年度 " + (i + 1));
                Assert.AreEqual(a.Stats[i].P, b.Stats[i].P);
                Assert.AreEqual(a.Stats[i].Prestige, b.Stats[i].Prestige);
            }
        }
    }
}
