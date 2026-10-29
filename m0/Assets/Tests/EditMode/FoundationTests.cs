using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ekiden.App;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using Ekiden.Persistence.Container;
using Ekiden.Persistence.Snapshot;
using Ekiden.Platform;
using Ekiden.Verification;
using NUnit.Framework;
using UnityEngine;

namespace Ekiden.Unity.Tests
{
    /// <summary>進め方を決められる時計(Platform/Fake の FakeClock は asmdef がなく、試験の asmdef から参照できないので同じものを置く)。</summary>
    public sealed class TestClock : IClock
    {
        public long Utc = 1800000000L;
        public double Monotonic;
        public long UtcNow() { return Utc; }
        public double MonotonicSeconds() { return Monotonic; }
        public void Advance(double seconds) { Monotonic += seconds; Utc += (long)Math.Floor(seconds); }
    }

    /// <summary>U2 の足場の試験: 文字列の表・端末のファイル・App の入口(新規 → 1 年度 → 保存 → 復元 → 続行)・全 View。</summary>
    public class FoundationTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Application.temporaryCachePath, "ekiden-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private GameApp NewApp(TestClock clock = null)
        {
            Dictionary<string, string> table = Strings.FromProject();
            return new GameApp(new UnityFileStore(_dir), clock ?? new TestClock(), table, new TableLocalization(table, true), "test");
        }

        // ------------------------------------------------------------ ① 文字列の表
        [Test]
        public void StringsTable_LoadsFromProject_AndHasNoLintIssues()
        {
            Dictionary<string, string> table = Strings.FromProject();
            Assert.Greater(table.Count, 700, "ja.json が読めている");
            List<LintIssue> issues = StringsLint.CheckTable(table);
            Assert.IsEmpty(issues, string.Join("\n", issues.Select(i => i.ToString())));
            ILocalization loc = new TableLocalization(table, true);
            Assert.AreEqual("⟦no.such.key⟧", loc.Get("no.such.key"), "開発ビルドの欠落の表示");
        }

        // ------------------------------------------------------------ ② 端末のファイル
        [Test]
        public void FileStore_RoundTrip_ListAndDelete()
        {
            var fs = new UnityFileStore(_dir);
            byte[] a = Encoding.UTF8.GetBytes("あいう");
            fs.WriteAtomic("saves/slot1.a.eksv", a, b => b.Length == a.Length);
            fs.WriteAtomic("saves/slot1.b.eksv", new byte[] { 1, 2, 3 }, null);
            fs.WriteAtomic("profile.a.eksv", new byte[] { 9 }, null);
            CollectionAssert.AreEqual(a, fs.ReadAllBytes("saves/slot1.a.eksv"));
            CollectionAssert.AreEqual(new[] { "saves/slot1.a.eksv", "saves/slot1.b.eksv" }, fs.List("saves/").ToArray(), "昇順・接頭辞");
            Assert.AreEqual(3, fs.List("").Count);
            Assert.IsFalse(File.Exists(Path.Combine(_dir, "saves", "slot1.a.eksv.tmp")), ".tmp は残らない");
            fs.Delete("saves/slot1.b.eksv");
            Assert.IsFalse(fs.Exists("saves/slot1.b.eksv"));
            Assert.Throws<FileNotFoundException>(() => fs.ReadAllBytes("saves/slot1.b.eksv"));
            Assert.Greater(fs.FreeSpace(), 0);
        }

        [Test]
        public void FileStore_FailedOrInterruptedWrite_DoesNotBreakTheOriginal()
        {
            var fs = new UnityFileStore(_dir);
            byte[] good = Encoding.UTF8.GetBytes("本物の中身");
            fs.WriteAtomic("saves/slot1.a.eksv", good, null);

            // 検証に落ちた書き込み: 例外・本物はそのまま・.tmp は消える
            Assert.Throws<IOException>(() => fs.WriteAtomic("saves/slot1.a.eksv", new byte[] { 0, 1 }, b => false));
            CollectionAssert.AreEqual(good, fs.ReadAllBytes("saves/slot1.a.eksv"));
            Assert.IsFalse(File.Exists(Path.Combine(_dir, "saves", "slot1.a.eksv.tmp")));
            Assert.Throws<IOException>(() => fs.WriteAtomic("saves/slot1.a.eksv", new byte[] { 0, 1 }, b => throw new InvalidOperationException()));
            CollectionAssert.AreEqual(good, fs.ReadAllBytes("saves/slot1.a.eksv"));

            // 途中で切れた書き込み(電源断で .tmp が半分だけ残った): 本物は読める・次の書き込みは .tmp を上書きして成功する
            File.WriteAllBytes(Path.Combine(_dir, "saves", "slot1.a.eksv.tmp"), new byte[] { 0xEF, 0xBB });
            CollectionAssert.AreEqual(good, fs.ReadAllBytes("saves/slot1.a.eksv"));
            byte[] next = Encoding.UTF8.GetBytes("次の中身");
            fs.WriteAtomic("saves/slot1.a.eksv", next, b => b.Length == next.Length);
            CollectionAssert.AreEqual(next, fs.ReadAllBytes("saves/slot1.a.eksv"));
        }

        [Test]
        public void SlotStore_OnUnityFileStore_RecoversFromATruncatedNewestGeneration()
        {
            var clock = new TestClock();
            GameApp app = NewApp(clock);
            GameHost host = app.NewGame(1, Difficulty.Mid, "検算監督", seed: 7);
            int saves = 0;
            for (int i = 0; i < 400 && saves < 3; i++)
            {
                Stop s = host.Advance();
                clock.Advance(5);
                if (s.Kind == StopKind.MonthPlan) saves++;
            }
            var store = new SlotStore(app.Env.Files);
            char newest = store.PickTarget(1) == 'a' ? 'b' : 'a';                      // 次に書く世代の反対が最新
            string path = SlotStore.AutoPath(1, newest);
            byte[] bytes = app.Env.Files.ReadAllBytes(path);
            File.WriteAllBytes(Path.Combine(_dir, path.Replace('/', Path.DirectorySeparatorChar)), bytes.Take(bytes.Length / 3).ToArray());   // 書きかけで切れた
            OpenResult r = NewApp(clock).Open(1);
            Assert.IsNotNull(r.Host, "古い世代から読めた: " + r.Error);
            Assert.IsTrue(r.Notices.Any(n => n.Kind == NoticeKind.RecoveredFromOlder));
        }

        // ------------------------------------------------------------ ③ 新規 → 1 年度 → 保存 → 復元 → 続行
        private static Stop AdvanceUntil(GameHost h, Func<Stop, bool> done, int cap = 3000)
        {
            for (int i = 0; i < cap; i++)
            {
                Stop s = h.Advance();
                if (done(s)) return s;
            }
            Assert.Fail("停止に着かなかった");
            return null;
        }

        [Test]
        public void GameHost_NewGame_OneYear_SaveManual_Open_Continue()
        {
            var clock = new TestClock();
            GameApp app = NewApp(clock);
            GameHost host = app.NewGame(1, Difficulty.Mid, "検算監督", seed: 11);
            int startYear = host.Session.Year;
            AdvanceUntil(host, s => s.Kind == StopKind.YearEndTurnover);
            Stop first = AdvanceUntil(host, s => host.Flow.CanSave);
            Assert.AreEqual(startYear + 1, host.Session.Year, "1 年度が終わった");
            Assert.IsTrue(host.SaveManual(), "手動セーブ");

            OpenResult r = NewApp(clock).Open(1);
            Assert.IsNotNull(r.Host, r.Error);
            GameHost back = r.Host;
            Assert.AreEqual(host.Session.Year, back.Session.Year);
            for (int i = 0; i < 60; i++) back.Advance();                                // 続行できる(例外なし)
            Assert.Greater(back.Session.YearWeek + back.Session.Year * 100, host.Session.YearWeek + host.Session.Year * 100, "復元したプレイが進んだ");
            Assert.AreEqual(1, app.Slots().Count(x => x.HasData && x.HasManual && x.Slot == 1));
        }

        /// <summary>約束 5(保存 → 読み込み → 続行 = 続行のみ)を Unity の上で。L-71(Unity の double.Parse が 1 ulp ずれる)が境目に当たると崩れうる。</summary>
        [Test]
        public void GameHost_SaveOpenContinue_IsByteEqualToContinueOnly()
        {
            var clock = new TestClock();
            GameApp app = NewApp(clock);
            GameHost host = app.NewGame(1, Difficulty.Mid, "検算監督", seed: 11);
            AdvanceUntil(host, s => host.Flow.CanSave && host.Session.YearWeek >= 16);
            Assert.IsTrue(host.Flow.CanSave);
            Assert.IsTrue(host.SaveManual());
            GameHost back = NewApp(clock).Open(1).Host;
            // 復元した流れは同じ停止をもう一度出すことがあるので、回数ではなく同じ節目(年度末の卒業と入学)でそろえて比べる
            AdvanceUntil(host, s => s.Kind == StopKind.YearEndTurnover);
            AdvanceUntil(back, s => s.Kind == StopKind.YearEndTurnover);
            Assert.AreEqual(GameSnapshot.Capture(host.Session).ToJson(), GameSnapshot.Capture(back.Session).ToJson());
        }

        // ------------------------------------------------------------ ④ 全 View が例外にならない(1 年度のあいだ毎週)
        private static int CallEveryView(GameSession gs, Stop s)
        {
            int n = 0;
            gs.Home(); n++;
            List<RunnerRowView> rows = gs.RosterRows(); n++;
            foreach (RunnerRowView r in rows) { gs.RunnerRow(r.Id); gs.RunnerDetail(r.Id); gs.InjuryDialog(r.Id); n += 3; }
            gs.Plan(); gs.AnnualPlanView(); gs.ConditionWarning(); n += 3;
            gs.Ops(); gs.WorldOverview(); gs.Obs(); gs.History(); gs.Notifications(); n += 5;
            gs.YearSummary(); gs.Graduation(); gs.Enrollment(); n += 3;
            gs.ScoutMap(); gs.ScoutTargets(); n += 2;
            List<ScoutCandidateView> cands = gs.ScoutCandidates(); n++;
            for (int i = 0; i < Math.Min(3, cands.Count); i++) { gs.ScoutCandidate(i); n++; }
            foreach (RaceKind k in new[] { RaceKind.Fall, RaceKind.National, RaceKind.NewYear })
            {
                gs.Entry(k); gs.Preview(k); n += 2;
                OrderView o = gs.Order(k); n++;
                for (int sec = 0; sec < o.Sections.Count; sec++) { gs.OrderCandidates(k, sec); n++; }
                foreach (RunnerRowView r in rows.Take(5)) { gs.FormOutlook(r.Id, k); n++; }
            }
            if (s == null) return n;
            if (s.Kind == StopKind.Event && s.Event != null) { gs.EventDialog(s.Event); n++; }
            if (s.Kind == StopKind.MonthReport && s.Report != null) { gs.MonthReportView(s.Report); n++; }
            if (s.RaceDay != null) { gs.RaceDayView(s.RaceDay); gs.RaceLive(s.RaceDay); n += 2; }
            if (s.Week != null && s.Week.Race != null) { gs.RaceResult(s.Week.Race); n++; }
            if (s.Week != null && s.Week.OwnRace.HasValue) { gs.RaceResult(s.Week.Race, s.Week.OwnRace); n++; }
            gs.QualifierResult(); n++;                                                  // 予選会の前は Held = false
            return n;
        }

        /// <summary>毎週: 進行制御を通さず 1 週ずつ進め(『おまかせ』と同じ)、週の境目ごとに全 View を呼ぶ。</summary>
        [Test]
        public void EveryView_EveryWeekOfAYear_DoesNotThrow()
        {
            GameSession gs = GameSession.New(23UL, Difficulty.Mid);
            int calls = CallEveryView(gs, null);
            for (int w = 0; w < 49; w++)
            {
                gs.StepWeek();
                calls += CallEveryView(gs, null);
            }
            Assert.AreEqual(2, gs.Year, "1 年度を通った");
            Debug.Log("毎週の View の呼び出し " + calls + " 回");
        }

        /// <summary>停止のときだけ出る View(当日・レース進行・月末レポート・イベント・大会の結果)を、進行制御の 1 年度で。</summary>
        [Test]
        public void EveryView_AtEveryStopOfAYear_DoesNotThrow()
        {
            GameApp app = NewApp();
            GameHost host = app.NewGame(1, Difficulty.Mid, "検算監督", seed: 23);
            host.Session.State.Settings.SpectateMode = "Full";                         // 当日(SC-24・25)の View も通す。設定の画面の役(SC-04)
            int stops = 0, calls = 0, days = 0, reports = 0;
            for (int i = 0; i < 3000; i++)
            {
                Stop s = host.Advance();
                calls += CallEveryView(host.Session, s);
                stops++;
                if (s.RaceDay != null) days++;
                if (s.Report != null) reports++;
                if (s.Kind == StopKind.YearEndTurnover) break;
            }
            Assert.Greater(days, 0, "当日の View も通った");
            Assert.GreaterOrEqual(reports, 11, "月末レポート");
            Debug.Log("停止 " + stops + " 回・View の呼び出し " + calls + " 回");
        }
    }
}