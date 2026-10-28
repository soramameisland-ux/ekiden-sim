using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.App;
using Ekiden.Domain.Game;
using Ekiden.Domain.World;
using Ekiden.Persistence;
using Ekiden.Persistence.Container;
using Ekiden.Persistence.Json;
using Ekiden.Persistence.Snapshot;
using Ekiden.Platform;
using NUnit.Framework;

namespace Ekiden.App.Tests
{
    /// <summary>App 層の入口 <see cref="GameHost"/>(09 §4.4〜§4.6・08 §6.5): 保存の呼び出し位置・中断セーブの再開・書き込みの失敗・回復の順。</summary>
    public class HostTests
    {
        private const ulong Seed = 20260403UL;
        private const string Lineage = "3f2c9a6e-0000-4000-8000-000000000001";

        private static HostEnvironment Env(MemoryFileStore fs, FakeClock clock)
        {
            return new HostEnvironment { Files = fs, Clock = clock, AppVersion = "1.0.0", MasterHash = "a1b2c3d4", NewLineageId = () => Lineage };
        }

        private static string Digest(GameHost h) { return GameSnapshot.Capture(h.Session).ToJson(); }

        /// <summary>何も操作せず『続ける』で、指定の種類の停止まで進める。</summary>
        private static Stop ToStop(GameHost h, Func<Stop, bool> until)
        {
            for (int i = 0; i < 100000; i++)
            {
                Stop s = h.Advance();
                if (until(s)) return s;
            }
            throw new InvalidOperationException("停止に着かない");
        }

        private static Stop ToYearEnd(GameHost h) { return ToStop(h, s => s.Kind == StopKind.YearEndTurnover); }

        // ------------------------------------------------------------ 新規ゲームと見出し
        [Test]
        public void NewGame_WritesTheFirstAutosave_WithTheHeaderMeta()
        {
            var fs = new MemoryFileStore();
            var clock = new FakeClock();
            GameHost h = GameHost.NewGame(Env(fs, clock), 2, Seed, Difficulty.Mid, "山田");
            Assert.AreEqual("山田", h.Session.State.ManagerName);
            Assert.AreEqual(1, h.Saves.Log.Count);
            Assert.AreEqual(SaveReason.Initial, h.Saves.Log[0].Reason);
            SlotLoad l = new SlotStore(fs).LoadAuto(2);
            Assert.AreEqual(1, l.Header.SaveCounter);
            Assert.AreEqual("auto", l.Header.Kind);
            Assert.AreEqual(Lineage, l.Header.LineageId);
            Assert.AreEqual(2, l.Header.Num("slot", -1));
            Assert.AreEqual(1, l.Header.Num("year", -1));
            Assert.AreEqual(0, l.Header.Num("month_index", -1));
            Assert.AreEqual("school." + h.Session.School.Id + ".name", l.Header.Str("school_name"), "Loc がなければ文字列のキー");
            Assert.AreEqual(h.Session.State.Scenario.Id, l.Header.Str("scenario_id"));
            Assert.AreEqual("Mid", l.Header.Str("difficulty"));
            Assert.AreEqual("1.0.0", l.Header.Str("app_version"));
            Assert.AreEqual(1800000000L, l.Header.Num("saved_at", 0));
            Assert.AreEqual(0, l.Header.Num("play_seconds", -1));
            Assert.AreEqual(Digest(h), l.Payload.ToJson(), "最初の自動セーブ = 開始の状態");
        }

        [Test]
        public void NewGame_ClearsThatSlotOnly()
        {
            var fs = new MemoryFileStore();
            var clock = new FakeClock();
            GameHost a = GameHost.NewGame(Env(fs, clock), 1, Seed, Difficulty.Mid);
            ToStop(a, s => s.Kind == StopKind.MonthReport);
            GameHost b = GameHost.NewGame(Env(fs, clock), 2, Seed + 1, Difficulty.Mid);
            Assert.AreEqual(1, new SlotStore(fs).LoadAuto(2).Header.SaveCounter);
            Assert.Greater(new SlotStore(fs).LoadAuto(1).Header.SaveCounter, 0, "ほかのスロットは触らない");
            GameHost again = GameHost.NewGame(Env(fs, clock), 1, Seed, Difficulty.Mid);
            Assert.AreEqual(1, new SlotStore(fs).LoadAuto(1).Header.SaveCounter, "同じスロットは新しい歴史として最初から");
            Assert.IsFalse(fs.Exists(SlotStore.AutoPath(1, 'b')), "古い世代は残らない");
        }

        // ------------------------------------------------------------ 呼び出し位置
        [Test]
        public void OneYear_AutosavesOnlyAtTheDocumentedMoments()
        {
            var fs = new MemoryFileStore();
            GameHost h = GameHost.NewGame(Env(fs, new FakeClock()), 1, Seed, Difficulty.Mid);
            var weeks = new List<KeyValuePair<int, bool>>();                       // 練習が解決される直前の (週, 自校が出る大会か)
            h.Flow.ResolvingWeek += f => weeks.Add(new KeyValuePair<int, bool>(f.Session.YearWeek, f.Session.RaceThisWeek.HasValue || (f.Session.YearWeek == GameSession.QualifierWeek && !f.Session.PrepareYear().Seeds.Contains(f.Session.School.Id))));
            ToYearEnd(h);
            Assert.AreEqual(48, weeks.Count, "1 年度 48 週");

            List<SaveRecord> log = h.Saves.Log;
            Assert.AreEqual(SaveReason.Initial, log[0].Reason);
            var weekSaves = log.Where(r => r.Reason == SaveReason.Month || r.Reason == SaveReason.PreRace).ToList();
            var expected = weeks.Where(w => w.Key % 4 == 0 || w.Value).ToList();
            Assert.AreEqual(expected.Count, weekSaves.Count, "月の最初の週と自校が出る大会の週にだけ書く(重なる週は 1 回)");
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.AreEqual(expected[i].Value ? SaveReason.PreRace : SaveReason.Month, weekSaves[i].Reason, "週 " + expected[i].Key);
                Assert.AreEqual(expected[i].Key / 4, weekSaves[i].MonthIndex);
            }
            Assert.AreEqual(12, weeks.Count(w => w.Key % 4 == 0), "月の最初の週は 12 回");
            Assert.IsTrue(weeks.Any(w => w.Value), "この年度のうちには自校が出る大会がある(試験の前提)");
            Assert.AreEqual(1, log.Count(r => r.Reason == SaveReason.YearEndSettle));
            Assert.AreEqual(1, log.Count(r => r.Reason == SaveReason.YearEndTurnover));
            Assert.AreEqual(SaveReason.YearEndSettle, log[log.Count - 2].Reason);
            Assert.AreEqual(SaveReason.YearEndTurnover, log[log.Count - 1].Reason);
            for (int i = 0; i < log.Count; i++)
            {
                Assert.AreEqual(i + 1, log[i].Counter, "保存番号は 1 から増えるだけ");
                Assert.AreEqual(i % 2 == 0 ? 'a' : 'b', log[i].Generation, "世代は交互");
            }
        }

        [Test]
        public void EveryAutosaveOfAYear_RestoresAndContinuesToTheSameYearEnd()
        {
            var fs = new MemoryFileStore();
            GameHost a = GameHost.NewGame(Env(fs, new FakeClock()), 1, Seed, Difficulty.Mid);
            var snaps = new List<KeyValuePair<MemoryFileStore, SaveRecord>>();
            int seen = a.Saves.Log.Count;
            snaps.Add(new KeyValuePair<MemoryFileStore, SaveRecord>(fs.Clone(), a.Saves.Log[0]));
            string reference = null;
            while (true)
            {
                Stop s = a.Advance();
                while (seen < a.Saves.Log.Count) { snaps.Add(new KeyValuePair<MemoryFileStore, SaveRecord>(fs.Clone(), a.Saves.Log[seen])); seen++; }
                if (s.Kind == StopKind.YearEndTurnover) { reference = Digest(a); break; }
            }
            Assert.GreaterOrEqual(snaps.Count, 16);
            foreach (var kv in snaps)
            {
                SaveRecord rec = kv.Value;
                OpenResult o = GameHost.Open(Env(kv.Key, new FakeClock()), 1);
                Assert.IsNotNull(o.Host, rec.Reason + " " + rec.Counter);
                Assert.IsFalse(o.Host.ResumedFromSuspend);
                Assert.IsEmpty(o.Notices);
                if (rec.Reason == SaveReason.YearEndTurnover)
                {
                    Assert.AreEqual(reference, Digest(o.Host), "年度末の入学者発表のあとの保存 = そのときの状態");
                    continue;
                }
                ToYearEnd(o.Host);
                Assert.AreEqual(reference, Digest(o.Host), "保存 " + rec.Counter + "(" + rec.Reason + " 月 " + rec.MonthIndex + ")から続けても、続けたまま = 同じ年度末");
            }
        }

        // ------------------------------------------------------------ 中断セーブ
        private static void Suspend_ResumeEqualsContinue(Func<Stop, bool> where, Action<GameHost> setup = null)
        {
            var fs = new MemoryFileStore();
            var clock = new FakeClock();
            GameHost a = GameHost.NewGame(Env(fs, clock), 1, Seed, Difficulty.Mid);
            if (setup != null) setup(a);
            Stop at = ToStop(a, where);
            Assert.IsTrue(a.OnAppPause(), "中断セーブが書ける: " + at.Key);
            Assert.IsTrue(fs.Exists(SlotStore.SuspendPath));
            MemoryFileStore copy = fs.Clone();
            ToYearEnd(a);
            string reference = Digest(a);

            OpenResult o = GameHost.Open(Env(copy, new FakeClock()), 1);
            Assert.IsNotNull(o.Host);
            Assert.IsTrue(o.Host.ResumedFromSuspend, "再開の条件に合う: " + at.Key);
            Assert.IsTrue(o.Host.Saves.SuspendOutstanding);
            Assert.IsTrue(copy.Exists(SlotStore.SuspendPath), "再開した直後はまだ消さない(落ちても失わない)");
            ToYearEnd(o.Host);
            Assert.AreEqual(reference, Digest(o.Host), "中断 → 再開 = 中断せずに続けた年度末(" + at.Key + ")");
            Assert.IsFalse(copy.Exists(SlotStore.SuspendPath), "最初の自動セーブが成功したあとに消える");
            Assert.IsFalse(o.Host.Saves.SuspendOutstanding);
        }

        [Test]
        public void Suspend_AtTheStartOfAWeek_ResumesToTheSameYear()
        {
            Suspend_ResumeEqualsContinue(s => s.Kind == StopKind.MonthPlan || s.Kind == StopKind.AnnualStart);
        }

        [Test]
        public void Suspend_AtAMonthReport_MidWeek_ResumesToTheSameYear()
        {
            Suspend_ResumeEqualsContinue(s => s.Kind == StopKind.MonthReport);
        }

        [Test]
        public void Suspend_AtTheRaceDay_ResumesToTheSameYear()
        {
            Suspend_ResumeEqualsContinue(s => s.Kind == StopKind.RaceDay, h => h.Session.State.Settings.SpectateMode = "Full");
        }

        [Test]
        public void Suspend_AtAnEventChoice_ResumesToTheSameYear()
        {
            Suspend_ResumeEqualsContinue(s => s.Kind == StopKind.Event, h => h.Session.State.Settings.EventAuto = false);
        }

        [Test]
        public void Suspend_IsDiscarded_WhenAnAutosaveWentFurther_OrItBelongsToAnotherSlot()
        {
            var fs = new MemoryFileStore();
            GameHost a = GameHost.NewGame(Env(fs, new FakeClock()), 1, Seed, Difficulty.Mid);
            ToStop(a, s => s.Kind == StopKind.MonthReport);
            Assert.IsTrue(a.OnAppPause());
            MemoryFileStore other = fs.Clone();
            ToStop(a, s => s.Kind == StopKind.MonthReport);                           // 自動セーブが先に進んだ
            Assert.IsFalse(new SlotStore(fs).CanResume(1));
            OpenResult o = GameHost.Open(Env(fs, new FakeClock()), 1);
            Assert.IsFalse(o.Host.ResumedFromSuspend);
            Assert.IsTrue(o.Notices.Any(n => n.Kind == NoticeKind.SuspendDiscarded));
            Assert.IsFalse(fs.Exists(SlotStore.SuspendPath), "合わない中断セーブは捨てる");

            GameHost two = GameHost.NewGame(Env(other, new FakeClock()), 2, Seed + 7, Difficulty.Mid);
            Assert.IsTrue(other.Exists(SlotStore.SuspendPath), "別のスロットの中断セーブには触らない");
            OpenResult o2 = GameHost.Open(Env(other, new FakeClock()), 2);
            Assert.IsFalse(o2.Host.ResumedFromSuspend);
            Assert.IsEmpty(o2.Notices);
            Assert.IsTrue(other.Exists(SlotStore.SuspendPath));
        }

        [Test]
        public void Suspend_BeforeAnyAutosave_CannotBeWritten_AndNothingIsLeftBehind()
        {
            var fs = new MemoryFileStore();
            var clock = new FakeClock();
            GameHost a = GameHost.NewGame(Env(fs, clock), 1, Seed, Difficulty.Mid);
            fs.Delete(SlotStore.AutoPath(1, 'a'));
            Assert.IsFalse(a.OnAppPause(), "土台の自動セーブがなければ中断セーブは書かない");
            Assert.IsFalse(fs.Exists(SlotStore.SuspendPath));
        }

        // ------------------------------------------------------------ 書き込みの失敗
        [Test]
        public void AFailedAutosave_IsRetriedOnceAfterOneSecond_AndTheGameKeepsGoing()
        {
            var fs = new MemoryFileStore();
            var clock = new FakeClock();
            GameHost h = GameHost.NewGame(Env(fs, clock), 1, Seed, Difficulty.Mid);
            long before = new SlotStore(fs).LoadAuto(1).Header.SaveCounter;
            fs.Free = 0;                                                            // 書き込みが失敗する
            ToStop(h, s => s.Kind == StopKind.MonthReport);                         // ゲームは続く(例外を出さない)
            Assert.AreEqual(1, h.Saves.Log.Count, "書けていない");
            Assert.IsTrue(h.Saves.RetryScheduled);
            Assert.IsEmpty(h.Saves.Notices, "最初の失敗では知らせない(1 回だけ再試行する)");
            Assert.AreEqual(before, new SlotStore(fs).LoadAuto(1).Header.SaveCounter, "直前の世代は壊れない");

            fs.Free = long.MaxValue;
            clock.Advance(0.5);
            h.Tick();
            Assert.AreEqual(1, h.Saves.Log.Count, "1 秒たつまでは試さない");
            clock.Advance(0.6);
            h.Tick();
            Assert.AreEqual(2, h.Saves.Log.Count, "1 秒後の再試行で書けた");
            Assert.IsFalse(h.Saves.RetryScheduled);
            Assert.IsEmpty(h.Saves.Notices);
        }

        [Test]
        public void SaveFailures_AlsoGoToTheGamesNotificationLog()
        {
            var fs = new MemoryFileStore();
            var clock = new FakeClock();
            GameHost h = GameHost.NewGame(Env(fs, clock), 1, Seed, Difficulty.Mid);
            Assert.IsEmpty(h.Session.State.Notifications.Where(n => n.Kind == Ekiden.Domain.Team.NotifyKind.AutosaveFailed));
            fs.Free = 0;
            ToStop(h, s => s.Kind == StopKind.MonthReport);
            clock.Advance(2);
            h.Tick();                                                                          // 再試行も失敗: 通知
            var logged = h.Session.State.Notifications.Where(n => n.Kind == Ekiden.Domain.Team.NotifyKind.AutosaveFailed).ToList();
            Assert.AreEqual(1, logged.Count);
            Assert.AreEqual("disk_full", logged[0].Code, "空き容量が足りないとき");
            Assert.AreEqual("nt.autosave.disk_full", h.Session.Notifications().Items.First(n => n.Kind == Ekiden.Domain.Team.NotifyKind.AutosaveFailed).TextKey);
        }

        /// <summary>空き容量の不足ではない書き込みの失敗(権限・I/O エラーなど)を起こすための包み。</summary>
        private sealed class FlakyStore : IFileStore
        {
            private readonly IFileStore _inner;
            public bool Fail;
            public FlakyStore(IFileStore inner) { _inner = inner; }
            public bool Exists(string path) { return _inner.Exists(path); }
            public byte[] ReadAllBytes(string path) { return _inner.ReadAllBytes(path); }
            public void WriteAtomic(string path, byte[] bytes, Func<byte[], bool> verify) { if (Fail) throw new System.IO.IOException("書き込めない"); _inner.WriteAtomic(path, bytes, verify); }
            public void Delete(string path) { _inner.Delete(path); }
            public IReadOnlyList<string> List(string prefix) { return _inner.List(prefix); }
            public long FreeSpace() { return _inner.FreeSpace(); }
        }

        [Test]
        public void ASaveFailure_ThatIsNotDiskFull_GoesToTheLog_ForManualAndAutosave()
        {
            var flaky = new FlakyStore(new MemoryFileStore());
            var clock = new FakeClock();
            var env = new HostEnvironment { Files = flaky, Clock = clock, AppVersion = "1.0.0", MasterHash = "a1b2c3d4", NewLineageId = () => Lineage };
            GameHost h = GameHost.NewGame(env, 1, Seed, Difficulty.Mid);
            flaky.Fail = true;
            Assert.IsFalse(h.SaveManual());
            Assert.IsTrue(h.Saves.Notices.Any(n => n.Kind == NoticeKind.ManualSaveFailed), "空き容量の不足ではない手動セーブの失敗");
            var logged = h.Session.State.Notifications.Where(n => n.Kind == Ekiden.Domain.Team.NotifyKind.AutosaveFailed).ToList();
            Assert.AreEqual(1, logged.Count); Assert.AreEqual("failed", logged[0].Code);
            ToStop(h, s => s.Kind == StopKind.MonthReport);
            clock.Advance(2);
            h.Tick();                                                                          // 自動セーブの再試行も失敗
            logged = h.Session.State.Notifications.Where(n => n.Kind == Ekiden.Domain.Team.NotifyKind.AutosaveFailed).ToList();
            Assert.AreEqual(2, logged.Count, "自動セーブの失敗も足される");
            Assert.IsTrue(logged.All(n => n.Code == "failed"));
            Assert.AreEqual("nt.autosave.failed", h.Session.Notifications().Items.First(n => n.Kind == Ekiden.Domain.Team.NotifyKind.AutosaveFailed).TextKey);
        }

        [Test]
        public void ASecondFailure_Notifies_AndTheNextMomentTriesAgain()
        {
            var fs = new MemoryFileStore();
            var clock = new FakeClock();
            GameHost h = GameHost.NewGame(Env(fs, clock), 1, Seed, Difficulty.Mid);
            var notices = new List<HostNotice>();
            h.Saves.Notice += notices.Add;
            fs.Free = 0;
            ToStop(h, s => s.Kind == StopKind.MonthReport);
            clock.Advance(2);
            h.Tick();
            Assert.AreEqual(1, notices.Count);
            Assert.AreEqual(NoticeKind.DiskFull, notices[0].Kind, "空きが足りないときは『空き容量が足りません』");
            Assert.IsFalse(h.Saves.RetryScheduled);
            clock.Advance(5);
            h.Tick();
            Assert.AreEqual(1, notices.Count, "再試行は 1 回だけ");

            fs.Free = long.MaxValue;
            ToStop(h, s => s.Kind == StopKind.MonthReport);                         // 次のきっかけでもう一度
            Assert.AreEqual(2, h.Saves.Log.Count);
            Assert.AreEqual(2, new SlotStore(fs).LoadAuto(1).Header.SaveCounter, "失敗した分の番号は飛ばさない");
            Assert.IsTrue(h.Flow.Session.Year >= 1);
        }

        [Test]
        public void ANewerAutosave_SupersedesAPendingRetry_SoAStaleStateIsNeverWrittenLater()
        {
            var fs = new MemoryFileStore();
            var clock = new FakeClock();
            GameHost h = GameHost.NewGame(Env(fs, clock), 1, Seed, Difficulty.Mid);
            fs.Free = 0;
            ToStop(h, s => s.Kind == StopKind.MonthReport);
            Assert.IsTrue(h.Saves.RetryScheduled);
            fs.Free = long.MaxValue;
            ToStop(h, s => s.Kind == StopKind.MonthReport);                         // 次のきっかけで書けた
            Assert.IsFalse(h.Saves.RetryScheduled, "新しい要求が来たら、古い再試行は取り消す");
            int written = h.Saves.Log.Count;
            long month = h.Saves.Log[written - 1].MonthIndex;
            clock.Advance(10);
            h.Tick();
            Assert.AreEqual(written, h.Saves.Log.Count, "古い状態を、あとから新しい番号で書き足さない");
            Assert.AreEqual(month, h.Saves.Log[written - 1].MonthIndex);
        }

        [Test]
        public void Suspend_WritesTheWaitingAutosaveFirst_SoTheBaseCounterIsTheNewHead()
        {
            var fs = new MemoryFileStore();
            GameHost h = GameHost.NewGame(Env(fs, new FakeClock()), 1, Seed, Difficulty.Mid);
            h.Saves.Synchronous = false;
            ToStop(h, s => s.Kind == StopKind.MonthReport);
            Assert.IsTrue(h.Saves.HasPending);
            int before = h.Saves.Log.Count;
            Assert.IsTrue(h.OnAppPause());
            Assert.IsFalse(h.Saves.HasPending, "中断の前に、待っていた自動セーブを書く");
            Assert.AreEqual(before + 2, h.Saves.Log.Count, "自動セーブ + 中断セーブ");
            var store = new SlotStore(fs);
            Assert.AreEqual(store.LoadAuto(1).Header.SaveCounter, store.ReadSuspend().Header.Num("base_counter", -1));
            Assert.IsTrue(store.CanResume(1));
        }

        [Test]
        public void ASuspendedResume_KeepsTheSuspendFile_UntilAnAutosaveSucceeds()
        {
            var fs = new MemoryFileStore();
            GameHost a = GameHost.NewGame(Env(fs, new FakeClock()), 1, Seed, Difficulty.Mid);
            ToStop(a, s => s.Kind == StopKind.MonthReport);
            Assert.IsTrue(a.OnAppPause());
            var clock = new FakeClock();
            GameHost b = GameHost.Open(Env(fs, clock), 1).Host;
            Assert.IsTrue(b.ResumedFromSuspend);
            Assert.AreEqual(StopKind.MonthReport, b.Advance().Kind, "再開は中断した停止(月末レポート)に戻る");
            fs.Free = 0;
            ToStop(b, s => s.Kind == StopKind.MonthReport);                         // 次の月の自動セーブは失敗する
            Assert.IsTrue(b.Saves.RetryScheduled);
            Assert.IsTrue(fs.Exists(SlotStore.SuspendPath), "自動セーブが成功するまで中断セーブは残る");
            fs.Free = long.MaxValue;
            clock.Advance(2);
            b.Tick();
            Assert.IsFalse(fs.Exists(SlotStore.SuspendPath), "成功したら消える");
        }

        [Test]
        public void Autosaves_AreCoalescedToTheLatestOne_WhenWritingIsDeferred()
        {
            var fs = new MemoryFileStore();
            GameHost h = GameHost.NewGame(Env(fs, new FakeClock()), 1, Seed, Difficulty.Mid);
            h.Saves.Synchronous = false;
            int logBefore = h.Saves.Log.Count;
            ToStop(h, s => s.Kind == StopKind.YearEndSettle);                        // 年度の途中で何度も要求が来る
            Assert.IsTrue(h.Saves.HasPending);
            Assert.AreEqual(logBefore, h.Saves.Log.Count, "待っている間は書かない");
            Assert.IsTrue(h.Saves.Pump());
            Assert.AreEqual(logBefore + 1, h.Saves.Log.Count, "待っている 1 件だけを書く");
            Assert.AreEqual(SaveReason.YearEndSettle, h.Saves.Log[h.Saves.Log.Count - 1].Reason, "新しいほうだけ");
            Assert.IsFalse(h.Saves.HasPending);
            Assert.IsFalse(h.Saves.Pump());
        }

        // ------------------------------------------------------------ 読み込みと回復の順
        [Test]
        public void Open_WhenTheNewestGenerationIsBroken_UsesThePreviousOne_AndSaysSo()
        {
            var fs = new MemoryFileStore();
            GameHost a = GameHost.NewGame(Env(fs, new FakeClock()), 1, Seed, Difficulty.Mid);
            ToStop(a, s => s.Kind == StopKind.MonthReport);
            ToStop(a, s => s.Kind == StopKind.MonthReport);
            var store = new SlotStore(fs);
            SlotLoad head = store.LoadAuto(1);
            long headCounter = head.Header.SaveCounter;
            string path = SlotStore.AutoPath(1, head.Generation);
            byte[] bytes = fs.ReadAllBytes(path);
            fs.PutRaw(path, bytes.Take(bytes.Length / 2).ToArray());                 // 書きかけ
            OpenResult o = GameHost.Open(Env(fs, new FakeClock()), 1);
            Assert.IsNotNull(o.Host);
            Assert.IsTrue(o.Notices.Any(n => n.Kind == NoticeKind.RecoveredFromOlder));
            Assert.IsTrue(o.Host.Saves.Notices.Any(n => n.Kind == NoticeKind.RecoveredFromOlder), "通知の一覧にも入る");
            ToStop(o.Host, s => s.Kind == StopKind.MonthReport);
            Assert.AreEqual(headCounter + 1, store.LoadAuto(1).Header.SaveCounter, "保存番号は戻らない。壊れていた世代に書き直す");
        }

        [Test]
        public void Open_WhenTheNewestGenerationIsValidButNotAGame_FallsBackToTheOlderOne()
        {
            var fs = new MemoryFileStore();
            GameHost a = GameHost.NewGame(Env(fs, new FakeClock()), 1, Seed, Difficulty.Mid);
            ToStop(a, s => s.Kind == StopKind.MonthReport);
            var store = new SlotStore(fs);
            SlotLoad head = store.LoadAuto(1);
            var junk = new JsonObject();
            junk["hello"] = new JsonNumber(1L);
            fs.WriteAtomic(SlotStore.AutoPath(1, head.Generation), Eksv.Encode(new HeaderBuilder { Slot = 1, LineageId = Lineage, SaveCounter = head.Header.SaveCounter + 5 }.Build(), junk), null);
            OpenResult o = GameHost.Open(Env(fs, new FakeClock()), 1);
            Assert.IsNotNull(o.Host, "コンテナとして有効でも、中身が復元できなければ次の候補へ");
            Assert.AreEqual(1, o.Host.Saves.Log.Count + 1);
            Assert.IsTrue(o.Notices.Any(n => n.Kind == NoticeKind.RecoveredFromOlder));
        }

        [Test]
        public void Open_WhenNothingCanBeRead_FailsWithoutDeletingFiles()
        {
            var fs = new MemoryFileStore();
            GameHost.NewGame(Env(fs, new FakeClock()), 1, Seed, Difficulty.Mid);
            fs.PutRaw(SlotStore.AutoPath(1, 'a'), new byte[] { 1, 2, 3, 4 });
            OpenResult o = GameHost.Open(Env(fs, new FakeClock()), 1);
            Assert.IsNull(o.Host);
            Assert.AreEqual("load_failed", o.Error);
            Assert.IsTrue(fs.Exists(SlotStore.AutoPath(1, 'a')), "ファイルは消さない(利用者が手で取り出せるように。09 §4.5)");
            OpenResult empty = GameHost.Open(Env(fs, new FakeClock()), 3);
            Assert.IsNull(empty.Host);
        }

        // ------------------------------------------------------------ スロットの一覧
        [Test]
        public void SlotCatalog_ShowsEachSlotFromItsHeader()
        {
            var fs = new MemoryFileStore();
            var clock = new FakeClock();
            Assert.IsTrue(SlotCatalog.List(fs).All(s => !s.HasData && !s.Broken), "空のスロット");
            Assert.AreEqual(3, SlotCatalog.List(fs).Count);
            GameHost a = GameHost.NewGame(Env(fs, clock), 1, Seed, Difficulty.Elite);
            clock.Advance(42);
            ToStop(a, s => s.Kind == StopKind.MonthReport);
            a.SaveManual();
            GameHost.NewGame(Env(fs, clock), 3, Seed + 1, Difficulty.Low);
            fs.PutRaw(SlotStore.AutoPath(2, 'a'), new byte[] { 9, 9, 9 });

            List<SlotSummary> list = SlotCatalog.List(fs);
            SlotSummary s1 = list[0], s2 = list[1], s3 = list[2];
            Assert.IsTrue(s1.HasData);
            Assert.AreEqual("school." + a.Session.School.Id + ".name", s1.SchoolNameKey);
            Assert.AreEqual(a.Session.State.Scenario.Id, s1.ScenarioId);
            Assert.AreEqual("Elite", s1.Difficulty);
            Assert.AreEqual(1, s1.Year);
            Assert.AreEqual(a.Session.YearWeek / 4, s1.MonthIndex);
            Assert.AreEqual(42, s1.PlaySeconds);
            Assert.IsTrue(s1.HasManual);
            Assert.IsFalse(s1.CanResume);
            Assert.IsTrue(s2.Broken, "壊れたスロット(ファイルはある)");
            Assert.IsFalse(s2.HasData);
            Assert.IsTrue(s3.HasData);
            Assert.AreEqual("Low", s3.Difficulty);
            Assert.AreEqual(1, s3.SaveCounter);

            a.OnAppPause();
            Assert.IsTrue(SlotCatalog.List(fs)[0].CanResume, "中断セーブがあれば『続きから』は中断した場所から");
        }

        // ------------------------------------------------------------ 手動セーブ・時間
        [Test]
        public void ManualSave_AtAWeekStart_AndMidWeek_BothContinueToTheSameYearEnd()
        {
            foreach (Func<Stop, bool> where in new Func<Stop, bool>[] { s => s.Kind == StopKind.MonthPlan || s.Kind == StopKind.AnnualStart, s => s.Kind == StopKind.MonthReport })
            {
                var fs = new MemoryFileStore();
                GameHost a = GameHost.NewGame(Env(fs, new FakeClock()), 1, Seed, Difficulty.Mid);
                Stop at = ToStop(a, where);
                Assert.IsTrue(a.SaveManual(), at.Key);
                Assert.IsTrue(fs.Exists(SlotStore.ManualPath(1)));
                ToYearEnd(a);
                string reference = Digest(a);
                // 手動セーブから: 自動セーブの世代を消して、手動を回復の候補にする
                fs.Delete(SlotStore.AutoPath(1, 'a'));
                fs.Delete(SlotStore.AutoPath(1, 'b'));
                OpenResult o = GameHost.Open(Env(fs, new FakeClock()), 1, () => true);
                Assert.IsNotNull(o.Host);
                Assert.IsTrue(o.Notices.Any(n => n.Kind == NoticeKind.RecoveredFromOlder), "自動セーブがなくて手動から");
                ToYearEnd(o.Host);
                Assert.AreEqual(reference, Digest(o.Host), at.Key);
            }
        }

        [Test]
        public void ManualSave_FailureNotifies_AndTheCounterContinuesFromTheHighestOfAll()
        {
            var fs = new MemoryFileStore();
            GameHost h = GameHost.NewGame(Env(fs, new FakeClock()), 1, Seed, Difficulty.Mid);
            fs.Free = 0;
            Assert.IsFalse(h.SaveManual());
            Assert.IsTrue(h.Saves.Notices.Any(n => n.Kind == NoticeKind.DiskFull));
            fs.Free = long.MaxValue;
            Assert.IsTrue(h.SaveManual());
            Assert.AreEqual(2, new SlotStore(fs).NextCounter(1) - 1, "手動も自動と同じ数列");
            ToStop(h, s => s.Kind == StopKind.MonthReport);
            Assert.AreEqual(3, new SlotStore(fs).LoadAuto(1).Header.SaveCounter, "手動のあとの自動セーブは続きの番号");
        }

        [Test]
        public void PlaySeconds_CountsOnlyWhileActive_AndIsWrittenIntoTheHeader()
        {
            var fs = new MemoryFileStore();
            var clock = new FakeClock();
            GameHost h = GameHost.NewGame(Env(fs, clock), 1, Seed, Difficulty.Mid);
            clock.Advance(30);
            Assert.AreEqual(30, h.PlaySeconds);
            ToStop(h, s => s.Kind == StopKind.MonthReport);
            Assert.AreEqual(30, new SlotStore(fs).LoadAuto(1).Header.Num("play_seconds", -1), "月の最初の自動セーブには、そこまでのプレイ時間");
            h.OnAppPause();
            clock.Advance(1000);                                                    // 中断中は数えない
            Assert.AreEqual(30, h.PlaySeconds);
            h.OnAppResume();
            clock.Advance(5);
            Assert.AreEqual(35, h.PlaySeconds);

            OpenResult o = GameHost.Open(Env(fs, new FakeClock()), 1);
            Assert.IsTrue(o.Host.ResumedFromSuspend);
            Assert.AreEqual(30, o.Host.PlaySeconds, "読み込んだあと、ヘッダの値から数え続ける");
        }
    }
}
