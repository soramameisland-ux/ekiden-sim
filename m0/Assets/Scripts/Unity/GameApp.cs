using System;
using System.Collections.Generic;
using Ekiden.App;
using Ekiden.Domain.World;
using Ekiden.Platform;

namespace Ekiden.Unity
{
    /// <summary>
    /// 起動の組み立て(MonoBehaviour なし。試験から直接使う)。<see cref="HostEnvironment"/> を作り、<see cref="GameHost.NewGame"/> / <see cref="GameHost.Open"/> を呼ぶ。
    /// アプリの一時停止・復帰・時間の経過は <see cref="Pause"/> / <see cref="Resume"/> / <see cref="Tick"/>(<see cref="GameBootstrap"/> が Unity のイベントからつなぐ)。
    /// </summary>
    public sealed class GameApp
    {
        public HostEnvironment Env { get; }
        public ILocalization Loc { get { return Env.Loc; } }
        public IReadOnlyDictionary<string, string> Table { get; }

        /// <summary>いま遊んでいるプレイ(タイトルでは null)。</summary>
        public GameHost Host { get; private set; }

        public GameApp(IFileStore files, IClock clock, IReadOnlyDictionary<string, string> table, ILocalization loc, string appVersion)
        {
            Table = table;
            Env = new HostEnvironment { Files = files, Clock = clock, Loc = loc, AppVersion = appVersion ?? "0.0.0" };
        }

        /// <summary>保存の一覧(SC-01・SC-03)。</summary>
        public List<SlotSummary> Slots() { return SlotCatalog.List(Env.Files); }

        /// <summary>新規ゲーム(SC-02)。seed を渡さなければ、時計と GUID から決める(App の層なので時刻を使ってよい。Domain には渡すだけ)。</summary>
        public GameHost NewGame(int slot, Difficulty difficulty, string managerName, ulong? seed = null)
        {
            Host = GameHost.NewGame(Env, slot, seed ?? NewSeed(), difficulty, managerName);
            return Host;
        }

        /// <summary>保存を開く(続きから・SC-03)。読めなければ Host は null のまま。</summary>
        public OpenResult Open(int slot)
        {
            OpenResult r = GameHost.Open(Env, slot);
            if (r.Host != null) Host = r.Host;
            return r;
        }

        /// <summary>タイトルへ戻る(保存は呼ぶ側が先に済ませる)。</summary>
        public void Close() { Host = null; }

        public bool Pause() { return Host == null || Host.OnAppPause(); }

        public void Resume() { if (Host != null) Host.OnAppResume(); }

        public void Tick() { if (Host != null) Host.Tick(); }

        /// <summary>新しい世界の種。エンディアンに依存しない組み立て(BitConverter を使わない)。</summary>
        public ulong NewSeed()
        {
            byte[] g = Guid.NewGuid().ToByteArray();
            ulong s = (ulong)Env.Clock.UtcNow();
            for (int i = 0; i < 8; i++) s = (s << 8 | s >> 56) ^ g[i] ^ ((ulong)g[i + 8] << 32);
            return s == 0 ? 1UL : s;
        }
    }
}
