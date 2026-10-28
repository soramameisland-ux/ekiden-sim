using System;
using System.Collections.Generic;
using Ekiden.Persistence;
using Ekiden.Persistence.Container;
using Ekiden.Persistence.Json;
using Ekiden.Platform;

namespace Ekiden.App
{
    /// <summary>保存のきっかけ(08 §6.5)。</summary>
    public enum SaveReason { Initial, Month, PreRace, YearEndSettle, YearEndTurnover, Manual, Suspend }

    /// <summary>利用者に知らせること(トースト + 通知。09 §4.5・§4.6)。</summary>
    public enum NoticeKind
    {
        AutosaveFailed,         // 『自動セーブに失敗しました』(ゲームは続ける。次のきっかけでもう一度)
        DiskFull,               // 『空き容量が足りません』
        ManualSaveFailed,
        RecoveredFromOlder,     // 『最新のセーブが壊れていたため、ひとつ前から再開しました』
        SuspendDiscarded,       // 中断セーブが再開の条件に合わず捨てられた(他の端末で進んだ・自動セーブが先に進んだ)
    }

    public sealed class HostNotice
    {
        public NoticeKind Kind;
        public string Detail = "";
        public long At;
    }

    /// <summary>セーブのヘッダに入れる見出し(09 §4.2 の SaveMeta)。</summary>
    public sealed class SaveMeta
    {
        public int Year, MonthIndex;
        public string SchoolName = "", ScenarioId = "", Difficulty;
        public long PlaySeconds;
    }

    /// <summary>書いた記録(診断・試験用)。</summary>
    public sealed class SaveRecord
    {
        public SaveReason Reason;
        public long Counter;
        public char Generation;             // 'a' / 'b' / 'm'(手動)/ 's'(中断)
        public long At;
        public int Year, MonthIndex;        // 書いた時点のゲーム内の年度・月
    }

    /// <summary>
    /// 1 つのスロットへの書き込みの段取り(09 §4.4・§4.6): 自動セーブの要求を**待っている 1 件に畳み**(新しいほうだけを書く)、書き込みの失敗は
    /// <see cref="SaveConstants.K_SAVE_RETRY_S"/> 秒後に 1 回だけ再試行し(<see cref="Tick"/>)、だめなら通知してゲームは続ける(次のきっかけで再び試す)。
    /// 中断セーブは同期で書く。再開した中断セーブは、最初の自動セーブが成功してから消す(使い切り)。
    /// 状態の取り出し(JSON の組み立て)は呼ぶ側がメインスレッドで済ませる。<see cref="Synchronous"/> を false にすれば、<see cref="Pump"/> を別のスレッドから呼べる。
    /// </summary>
    public sealed class SaveService
    {
        private sealed class Job
        {
            public SaveReason Reason;
            public JsonObject Body;
            public SaveMeta Meta;
        }

        private readonly SlotStore _store;
        private readonly IFileStore _fs;
        private readonly IClock _clock;
        private readonly object _lock = new object();
        private Job _pending;
        private Job _retry;
        private double _retryAt;

        public int Slot { get; }
        public string LineageId { get; }
        public string AppVersion { get; }
        public string MasterHash { get; }

        /// <summary>true = 要求のたびにその場で書く(既定)。false = <see cref="Pump"/> を呼ぶまで待つ(別のスレッドで書くため)。</summary>
        public bool Synchronous { get; set; } = true;

        /// <summary>再開した中断セーブが残っている。最初の自動セーブが成功したら消す。</summary>
        public bool SuspendOutstanding { get; set; }

        public readonly List<SaveRecord> Log = new List<SaveRecord>();
        public readonly List<HostNotice> Notices = new List<HostNotice>();
        public event Action<HostNotice> Notice;

        public SaveService(SlotStore store, IFileStore fs, IClock clock, int slot, string lineageId, string appVersion, string masterHash)
        {
            _store = store;
            _fs = fs;
            _clock = clock;
            Slot = slot;
            LineageId = lineageId;
            AppVersion = appVersion;
            MasterHash = masterHash;
        }

        public bool HasPending { get { lock (_lock) return _pending != null; } }

        public bool RetryScheduled { get { lock (_lock) return _retry != null; } }

        private void Raise(NoticeKind kind, string detail)
        {
            var n = new HostNotice { Kind = kind, Detail = detail, At = _clock.UtcNow() };
            Notices.Add(n);
            if (Notice != null) Notice(n);
        }

        internal void Report(NoticeKind kind, string detail) { Raise(kind, detail); }

        private HeaderBuilder Header(string kind, long counter, SaveMeta m)
        {
            return new HeaderBuilder
            {
                Kind = kind, Slot = Slot, LineageId = LineageId, SaveCounter = counter, AppVersion = AppVersion, MasterHash = MasterHash, SavedAt = _clock.UtcNow(),
                Year = m.Year, MonthIndex = m.MonthIndex, SchoolName = m.SchoolName, ScenarioId = m.ScenarioId, Difficulty = m.Difficulty, PlaySeconds = m.PlaySeconds,
            };
        }

        // ------------------------------------------------------------ 自動セーブ
        /// <summary>自動セーブを要求する。待っている要求があれば置き換える(キューの深さ 1。新しいほうだけを書く)。進行中の再試行は、新しい要求で取り消す。
        /// <see cref="Synchronous"/> ならその場で書く。</summary>
        public void RequestAutosave(SaveReason reason, JsonObject body, SaveMeta meta)
        {
            lock (_lock)
            {
                _pending = new Job { Reason = reason, Body = body, Meta = meta };
                _retry = null;
            }
            if (Synchronous) Pump();
        }

        /// <summary>待っている自動セーブを書く。書いたら true。失敗したときは再試行を予約する(<see cref="Tick"/>)。</summary>
        public bool Pump()
        {
            Job job;
            lock (_lock)
            {
                job = _pending;
                _pending = null;
            }
            if (job == null) return false;
            if (TryWriteAuto(job)) return true;
            lock (_lock)
            {
                if (_pending == null) { _retry = job; _retryAt = _clock.MonotonicSeconds() + SaveConstants.K_SAVE_RETRY_S; }      // 新しい要求が来ていれば、再試行はしない
            }
            return false;
        }

        /// <summary>時間の経過を知らせる(アプリが定期的に呼ぶ)。再試行の時刻が来ていれば 1 回だけ試す。それも失敗したら通知する。</summary>
        public void Tick()
        {
            Job job;
            lock (_lock)
            {
                if (_retry == null || _clock.MonotonicSeconds() < _retryAt) return;
                job = _retry;
                _retry = null;
            }
            if (TryWriteAuto(job)) return;
            Raise(IsDiskFull() ? NoticeKind.DiskFull : NoticeKind.AutosaveFailed, job.Reason.ToString());
        }

        private bool IsDiskFull() { return _fs.FreeSpace() < SaveConstants.K_SIZE_TARGET; }

        private static bool IsIoFailure(Exception e) { return e is System.IO.IOException || e is UnauthorizedAccessException; }

        private bool TryWriteAuto(Job job)
        {
            try
            {
                long counter = _store.NextCounter(Slot);
                char gen = _store.WriteAuto(Slot, Header("auto", counter, job.Meta).Build(), job.Body);
                Log.Add(new SaveRecord { Reason = job.Reason, Counter = counter, Generation = gen, At = _clock.UtcNow(), Year = job.Meta.Year, MonthIndex = job.Meta.MonthIndex });
                if (SuspendOutstanding)
                {
                    _store.DeleteSuspend();                                  // 使い切り: 再開したあとの最初の自動セーブが成功してから消す(§4.6)
                    SuspendOutstanding = false;
                }
                return true;
            }
            catch (Exception e) when (IsIoFailure(e))
            {
                return false;
            }
        }

        // ------------------------------------------------------------ 手動セーブ
        /// <summary>手動セーブ(スロットごとに 1 つ)。上書きの確認は呼ぶ側(08 §7.6)。失敗は通知して false。</summary>
        public bool SaveManual(JsonObject body, SaveMeta meta)
        {
            Pump();                                                          // 待っている自動セーブを先に(保存番号の順が時間の順になる)
            try
            {
                long counter = _store.NextCounter(Slot);
                _store.WriteManual(Slot, Header("manual", counter, meta).Build(), body);
                Log.Add(new SaveRecord { Reason = SaveReason.Manual, Counter = counter, Generation = 'm', At = _clock.UtcNow(), Year = meta.Year, MonthIndex = meta.MonthIndex });
                return true;
            }
            catch (Exception e) when (IsIoFailure(e))
            {
                Raise(IsDiskFull() ? NoticeKind.DiskFull : NoticeKind.ManualSaveFailed, e.Message);
                return false;
            }
        }

        // ------------------------------------------------------------ 中断セーブ
        /// <summary>中断セーブを同期で書く(最速の圧縮。§4.6)。待っている自動セーブを先に書き、そのスロットの先頭の保存番号を base_counter にする。
        /// 先頭がない(自動セーブがまだない)・書けないときは false(次の起動はスロットの先頭を使う)。</summary>
        public bool WriteSuspend(JsonObject payload, SaveMeta meta)
        {
            Pump();
            try
            {
                long head = _store.LoadAuto(Slot).Header.SaveCounter;
                HeaderBuilder b = Header("suspend", 0, meta);
                b.BaseCounter = head;
                _store.WriteSuspend(b.Build(), payload);
                Log.Add(new SaveRecord { Reason = SaveReason.Suspend, Counter = head, Generation = 's', At = _clock.UtcNow(), Year = meta.Year, MonthIndex = meta.MonthIndex });
                return true;
            }
            catch (LoadException)
            {
                return false;
            }
            catch (Exception e) when (IsIoFailure(e))
            {
                return false;
            }
        }
    }
}
