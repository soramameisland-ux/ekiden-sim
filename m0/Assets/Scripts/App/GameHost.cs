using System;
using System.Collections.Generic;
using Ekiden.Domain.Game;
using Ekiden.Domain.World;
using Ekiden.Persistence.Container;
using Ekiden.Persistence.Json;
using Ekiden.Persistence.Snapshot;
using Ekiden.Platform;

namespace Ekiden.App
{
    /// <summary>アプリが用意する外の世界(ファイル・時計・文字列・版)。</summary>
    public sealed class HostEnvironment
    {
        public IFileStore Files;
        public IClock Clock;
        public ILocalization Loc;                       // セーブの見出しの校名を引く。null なら文字列のキーのまま
        public string AppVersion = "0.0.0";
        public string MasterHash = "";
        public Persistence.Sync.ProfileData Profile;      // 実績・累計・クリア(アカウント単位。Unity 側が読み込んで渡す)。null なら空から始める
        public Func<string> NewLineageId = () => Guid.NewGuid().ToString();       // 歴史の識別子(UUID v4。App の層なので Guid を使ってよい。Domain は使わない = D-1)
    }

    /// <summary>スロットを開いた結果。読めなければ <see cref="Host"/> は null で <see cref="Error"/> に理由(ファイルは消さない。09 §4.5)。</summary>
    public sealed class OpenResult
    {
        public GameHost Host;
        public string Error;
        public readonly List<HostNotice> Notices = new List<HostNotice>();
    }

    /// <summary>
    /// App 層の入口(09 §15.2): 1 回のプレイ(<see cref="GameSession"/> + <see cref="GameFlow"/>)に、保存の呼び出し位置(08 §6.5)をつなぐ。画面は <see cref="Advance"/> で進め、
    /// <c>Flow.Session</c> の操作と <c>Views</c> で見せる。保存は次のところで行う:
    /// <list type="bullet">
    /// <item><description>新規ゲームの開始(<see cref="SaveReason.Initial"/>)</description></item>
    /// <item><description>月の最初の週の練習が解決される直前(<see cref="SaveReason.Month"/>。月末レポートの確認のあと・その月の計画が決まったあと)</description></item>
    /// <item><description>自校が出る大会(予選会を含む)の週の、練習が解決される直前(<see cref="SaveReason.PreRace"/>。オーダー確定後。ここまで戻ってやり直せる)</description></item>
    /// <item><description>年度末の精算の停止・卒業と入学の停止(<see cref="SaveReason.YearEndSettle"/> / <see cref="SaveReason.YearEndTurnover"/>)</description></item>
    /// <item><description>手動(<see cref="SaveManual"/>)・アプリの中断(<see cref="OnAppPause"/> = 中断セーブ)</description></item>
    /// </list>
    /// 自動セーブは練習が解決される直前の状態(<see cref="SuspendCheckpoint.Body"/>)を使うので、復元すると、その週の週の初めの停止からやり直す(決めたことは状態に残っている)。
    /// 1 つのスレッドから呼ぶこと(書き込みだけは <see cref="SaveService.Synchronous"/> = false で別のスレッドに回せる)。
    /// </summary>
    public sealed class GameHost
    {
        private readonly HostEnvironment _env;
        private readonly SlotStore _store;
        private readonly SuspendCheckpoint _checkpoint;
        private long _playBase;
        private double? _activeSince;

        public GameFlow Flow { get; private set; }
        public GameSession Session { get { return Flow.Session; } }
        public SaveService Saves { get; }

        /// <summary>実績の解除の記録(<see cref="HostEnvironment.Profile"/>)。<see cref="Advance"/> のたびに新しく満たした実績を記録する。</summary>
        public AchievementTracker Achievements { get; }
        public int Slot { get { return Saves.Slot; } }

        /// <summary>中断セーブから再開した(最初の『続ける』は中断した場所へ戻る)。</summary>
        public bool ResumedFromSuspend { get; private set; }

        private GameHost(HostEnvironment env, SlotStore store, int slot, string lineage, long playSeconds, GameFlow flow, SuspendCheckpoint cp)
        {
            _env = env;
            _store = store;
            _checkpoint = cp;
            Flow = flow;
            Saves = new SaveService(store, env.Files, env.Clock, slot, lineage, env.AppVersion, env.MasterHash);
            Achievements = new AchievementTracker(env.Profile);
            Saves.Notice += OnSaveNotice;
            _playBase = playSeconds;
            _activeSince = env.Clock.MonotonicSeconds();
            flow.ResolvingWeek += OnResolvingWeek;
        }

        // ------------------------------------------------------------ 開始
        /// <summary>新規ゲーム。そのスロットの中身は消す(上書きの確認は呼ぶ側。08 §7.6)。最初の自動セーブを書く(スロットを作る・中断セーブの土台にする)。</summary>
        public static GameHost NewGame(HostEnvironment env, int slot, ulong seed, Difficulty difficulty, string managerName = null)
        {
            var store = new SlotStore(env.Files);
            store.CleanTemp();
            store.DeleteSlot(slot);
            GameSession session = GameSession.New(seed, difficulty);
            if (managerName != null) session.State.ManagerName = managerName;
            var flow = new GameFlow(session);
            var cp = new SuspendCheckpoint();
            cp.Attach(flow);
            var host = new GameHost(env, store, slot, env.NewLineageId(), 0, flow, cp);
            host.AutosaveNow(SaveReason.Initial);
            return host;
        }

        /// <summary>スロットを開く。回復の順(09 §4.5)に試し、中身が復元できたものを使う(コンテナとして有効でも、復元できなければ次の候補へ)。
        /// 最新の世代が使えなかったときは <see cref="NoticeKind.RecoveredFromOlder"/>。中断セーブが再開の条件に合えば(同じ歴史・base_counter が先頭と等しい)そこから再開し、
        /// 合わなければ捨てる(<see cref="NoticeKind.SuspendDiscarded"/>)。すべて失敗したら Host が null(ファイルは消さない)。</summary>
        public static OpenResult Open(HostEnvironment env, int slot, Func<bool> confirmManual = null)
        {
            var store = new SlotStore(env.Files);
            store.CleanTemp();
            var result = new OpenResult();
            SlotRecovery used = null;
            GameSession session = null;
            int tried = 0;
            foreach (SlotRecovery rec in store.RecoveryCandidates(slot, confirmManual))
            {
                session = TryRestore(rec.Payload);
                if (session != null) { used = rec; break; }
                tried++;
            }
            if (used == null)
            {
                result.Error = "load_failed";
                return result;
            }
            long now = env.Clock.UtcNow();
            if (used.NewestWasBroken || tried > 0 || used.Source != RecoverySource.Auto)
                result.Notices.Add(new HostNotice { Kind = NoticeKind.RecoveredFromOlder, Detail = used.Source + " counter " + used.Header.SaveCounter, At = now });

            string lineage = used.Header.LineageId;
            if (string.IsNullOrEmpty(lineage)) lineage = env.NewLineageId();
            long play = used.Header.Num("play_seconds", 0);
            var cp = new SuspendCheckpoint();
            GameFlow flow = null;
            bool resumed = false;
            int suspendSlot = store.SuspendSlot();
            if (suspendSlot == slot)
            {
                SlotLoad sus = null;
                if (tried == 0 && used.Source == RecoverySource.Auto && store.CanResume(slot))
                {
                    sus = store.ReadSuspend();
                    if (sus != null) flow = cp.Resume(sus.Payload);
                }
                if (flow != null)
                {
                    resumed = true;
                    play = Math.Max(play, sus.Header.Num("play_seconds", 0));
                }
                else
                {
                    store.DeleteSuspend();                                           // 条件に合わない・壊れている: 捨てて、スロットの先頭を使う
                    result.Notices.Add(new HostNotice { Kind = NoticeKind.SuspendDiscarded, At = now });
                }
            }
            if (flow == null)
            {
                flow = new GameFlow(session);
                cp.Attach(flow);
            }
            var host = new GameHost(env, store, slot, lineage, play, flow, cp) { ResumedFromSuspend = resumed };
            host.Saves.SuspendOutstanding = resumed;
            foreach (HostNotice n in result.Notices) host.Saves.Notices.Add(n);
            result.Host = host;
            return result;
        }

        /// <summary>保存された状態から <see cref="GameSession"/> を作る。形が合わない・壊れているときは null(保存の層の境目なので、例外の種類は問わない)。</summary>
        private static GameSession TryRestore(JsonValue payload)
        {
            var o = payload as JsonObject;
            if (o == null) return null;
            try
            {
                return GameSnapshot.Restore(o);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ------------------------------------------------------------ 進める
        /// <summary>『続ける』。年度末の精算・卒業と入学の停止に着いたら自動セーブする(週の途中のきっかけは <see cref="Flow"/> の通知で行う)。</summary>
        public Stop Advance()
        {
            Stop s = Flow.Advance();
            Achievements.Update(Session, s.Week != null ? s.Week.Race : null, _env.Clock.UtcNow());
            if (s.Kind == StopKind.YearEndSettle) AutosaveNow(SaveReason.YearEndSettle);
            else if (s.Kind == StopKind.YearEndTurnover) AutosaveNow(SaveReason.YearEndTurnover);
            return s;
        }

        /// <summary>保存の失敗は、ゲームの通知の履歴(SC-38)にも残す(トーストのほかに。09 §4.5)。</summary>
        private void OnSaveNotice(HostNotice n)
        {
            if (n.Kind == NoticeKind.AutosaveFailed || n.Kind == NoticeKind.ManualSaveFailed) Session.Notify(Domain.Team.NotifyKind.AutosaveFailed, "failed");
            else if (n.Kind == NoticeKind.DiskFull) Session.Notify(Domain.Team.NotifyKind.AutosaveFailed, "disk_full");
        }

        private void OnResolvingWeek(GameFlow f)
        {
            int yw = Session.YearWeek;
            bool month = yw % 4 == 0;
            bool race = Session.RaceThisWeek.HasValue || PlaysQualifierThisWeek();
            if (!month && !race) return;
            Saves.RequestAutosave(race ? SaveReason.PreRace : SaveReason.Month, _checkpoint.Body, Meta());
        }

        private bool PlaysQualifierThisWeek()
        {
            return Session.YearWeek == GameSession.QualifierWeek && !Session.PrepareYear().Seeds.Contains(Session.School.Id);
        }

        private void AutosaveNow(SaveReason reason)
        {
            Saves.RequestAutosave(reason, GameSnapshot.Capture(Session), Meta());
        }

        // ------------------------------------------------------------ 手動・中断・時間
        /// <summary>手動セーブ(ポーズから)。週の途中(練習が解決されたあと)は、その週の練習が解決される直前の状態を書く(自動セーブと同じ。読み込むとその週のはじめからやり直す)。書けなければ false。</summary>
        public bool SaveManual()
        {
            JsonObject body = Flow.CanSave ? GameSnapshot.Capture(Session) : (Flow.MidWeek ? _checkpoint.Body : null);
            return body != null && Saves.SaveManual(body, Meta());
        }

        /// <summary>アプリの中断(バックグラウンド・省電力・終了)。中断セーブを同期で書き、プレイ時間の計測を止める。書けた(または書く必要のない)ときは true。</summary>
        public bool OnAppPause()
        {
            PausePlayClock();
            JsonObject payload = _checkpoint.BuildPayload(Flow);
            if (payload == null)
            {
                Saves.Pump();
                return false;
            }
            return Saves.WriteSuspend(payload, Meta());
        }

        /// <summary>アプリが戻った。プレイ時間の計測を再開する。</summary>
        public void OnAppResume()
        {
            if (!_activeSince.HasValue) _activeSince = _env.Clock.MonotonicSeconds();
        }

        /// <summary>時間の経過(アプリが定期的に呼ぶ)。書き込みの再試行の時刻を見る。</summary>
        public void Tick() { Saves.Tick(); }

        public long PlaySeconds
        {
            get { return _playBase + (_activeSince.HasValue ? (long)Math.Floor(_env.Clock.MonotonicSeconds() - _activeSince.Value) : 0); }
        }

        private void PausePlayClock()
        {
            if (!_activeSince.HasValue) return;
            _playBase = PlaySeconds;
            _activeSince = null;
        }

        private SaveMeta Meta()
        {
            var s = Session;
            string key = "school." + s.School.Id + ".name";
            return new SaveMeta
            {
                Year = s.Year, MonthIndex = s.YearWeek / 4, SchoolName = _env.Loc != null ? _env.Loc.Get(key) : key,
                ScenarioId = s.State.Scenario.Id, Difficulty = s.State.Scenario.Difficulty, PlaySeconds = PlaySeconds,
            };
        }
    }
}
