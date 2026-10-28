using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Rng;

namespace Ekiden.Persistence.Sync
{
    /// <summary>同期シミュレーションの乱数(開発ツール用。SplitMix64。<c>System.Random</c> の版による差を避ける)。</summary>
    public sealed class SimRng
    {
        private ulong _s;

        public SimRng(ulong seed) { _s = seed; }

        public ulong Next()
        {
            unchecked
            {
                _s += 0x9E3779B97F4A7C15UL;
                return Mix.Mix64(_s);
            }
        }

        public double NextDouble() { return (Next() >> 11) * (1.0 / 9007199254740992.0); }

        public int NextInt(int n) { return (int)(Next() % (ulong)n); }
    }

    /// <summary>クラウド 1 つ + 端末 n 台(09 §9.7。参照モデルの <c>SyncWorld</c>)。版の木(各更新が節点・親は更新前)を作り、どこにも残っていない葉 = 失われた更新を数える。同期の読み取りと書き込みのあいだに、ほかの端末が割り込める。</summary>
    public sealed class SyncWorld
    {
        public sealed class Rec
        {
            public int Lineage;
            public long Counter;
            public int? Node;          // null = 墓石
            public long LastSynced;

            public Rec Copy() { return new Rec { Lineage = Lineage, Counter = Counter, Node = Node, LastSynced = LastSynced }; }

            public bool SameAs(Rec o)
            {
                if (o == null) return false;
                return Lineage == o.Lineage && Counter == o.Counter && Node == o.Node;
            }
        }

        public sealed class Pending
        {
            public Rec BaseCloud;
            public string Kind;
        }

        public sealed class Device
        {
            public Rec Local;
            public readonly List<Rec> Backups = new List<Rec>();
            public Pending Pending;
        }

        public sealed class Stats
        {
            public int Play, Sync, Upload, Download, Conflict, CasFail, Delete, NewGame, Abort;
        }

        private readonly SimRng _rng;
        private readonly bool _cas;
        public string Policy = "random";                     // 競合の解決: random(利用者の気分)/ theirs(クラウドを残す)
        public Rec Cloud;
        public readonly List<Device> Dev = new List<Device>();
        private readonly Dictionary<int, int?> _parent = new Dictionary<int, int?>();
        private readonly HashSet<int> _discarded = new HashSet<int>();
        private int _nextUid = 1, _nextLineage = 1;
        public readonly Stats Stat = new Stats();

        public SyncWorld(int ndev, SimRng rng, bool cas = true)
        {
            _rng = rng;
            _cas = cas;
            for (int i = 0; i < ndev; i++) Dev.Add(new Device());
        }

        private int NewNode(int? parent)
        {
            int u = _nextUid++;
            _parent[u] = parent;
            return u;
        }

        private static SyncRecord ToRecord(Rec r)
        {
            return r == null ? null : new SyncRecord { LineageId = r.Lineage.ToString(), Counter = r.Counter, IsTombstone = !r.Node.HasValue, LastSynced = r.LastSynced };
        }

        // ---- 利用者の操作
        public bool Play(int d)
        {
            Rec l = Dev[d].Local;
            if (l == null || !l.Node.HasValue) return false;
            l.Counter++;
            l.Node = NewNode(l.Node);
            Stat.Play++;
            return true;
        }

        public bool NewGame(int d)
        {
            Device s = Dev[d];
            if (s.Local != null && s.Local.Node.HasValue) return false;
            int lin = _nextLineage++;
            s.Local = new Rec { Lineage = lin, Counter = 1, Node = NewNode(null), LastSynced = 0 };
            Stat.NewGame++;
            return true;
        }

        public bool Delete(int d)
        {
            Rec l = Dev[d].Local;
            if (l == null || !l.Node.HasValue) return false;
            int? n = l.Node;
            while (n.HasValue)                                   // 利用者が『削除』を選んだ = その系譜の履歴を明示的に捨てる
            {
                _discarded.Add(n.Value);
                n = _parent[n.Value];
            }
            l.Counter++;
            l.Node = null;                                       // 墓石
            Stat.Delete++;
            return true;
        }

        // ---- 同期(2 段階)
        public void SyncBegin(int d)
        {
            Device s = Dev[d];
            Rec c = Cloud == null ? null : Cloud.Copy();
            Rec l = s.Local;
            SyncAction act = SyncDecider.Decide(ToRecord(l), ToRecord(c));
            s.Pending = null;
            Stat.Sync++;
            if (act == SyncAction.Noop) return;
            if (act == SyncAction.Download) { DoDownload(d, c); return; }
            if (act == SyncAction.Conflict)
            {
                Stat.Conflict++;
                if (Policy == "random" && _rng.NextDouble() < 0.5)              // 利用者が『この端末を残す』
                {
                    ToBackup(d, c);                                             // クラウド側の内容は、バックアップに残す
                    l.Counter = Math.Max(l.Counter, c.Counter) + 1;
                    s.Pending = new Pending { BaseCloud = c, Kind = "upload_after_conflict" };
                }
                else                                                            // 『クラウドを残す』
                {
                    if (l.Node.HasValue) s.Backups.Add(l.Copy());
                    DoDownload(d, c);
                }
                return;
            }
            s.Pending = new Pending { BaseCloud = c, Kind = "upload" };
        }

        private void ToBackup(int d, Rec c)
        {
            if (c != null && c.Node.HasValue) Dev[d].Backups.Add(new Rec { Lineage = c.Lineage, Counter = c.Counter, Node = c.Node, LastSynced = c.Counter });
        }

        private void DoDownload(int d, Rec c)
        {
            Stat.Download++;
            if (c == null) return;
            Dev[d].Local = new Rec { Lineage = c.Lineage, Counter = c.Counter, Node = c.Node, LastSynced = c.Counter };
        }

        public bool SyncCommit(int d)
        {
            Device s = Dev[d];
            Pending p = s.Pending;
            if (p == null) return false;
            s.Pending = null;
            bool same = (Cloud == null && p.BaseCloud == null) || (Cloud != null && Cloud.SameAs(p.BaseCloud));
            if (_cas && !same)
            {
                Stat.CasFail++;                                  // 読み取り後にクラウドが変わった → 最初からやり直す(次の機会)
                return false;
            }
            Rec l = s.Local;
            Cloud = new Rec { Lineage = l.Lineage, Counter = l.Counter, Node = l.Node };
            l.LastSynced = l.Counter;
            Stat.Upload++;
            return true;
        }

        public void Abort(int d)
        {
            if (Dev[d].Pending != null)
            {
                Dev[d].Pending = null;
                Stat.Abort++;
            }
        }

        // ---- 不変条件
        private HashSet<int> StoredNodes()
        {
            var st = new HashSet<int>();
            if (Cloud != null && Cloud.Node.HasValue) st.Add(Cloud.Node.Value);
            foreach (Device s in Dev)
            {
                if (s.Local != null && s.Local.Node.HasValue) st.Add(s.Local.Node.Value);
                foreach (Rec b in s.Backups) if (b.Node.HasValue) st.Add(b.Node.Value);
            }
            return st;
        }

        /// <summary>失われた更新 = 保存先のどこにも残っていない葉。</summary>
        public List<int> Lost()
        {
            var covered = new HashSet<int>();
            foreach (int node in StoredNodes())
            {
                int? n = node;
                while (n.HasValue && !covered.Contains(n.Value))
                {
                    covered.Add(n.Value);
                    n = _parent[n.Value];
                }
            }
            return _parent.Keys.Where(u => !covered.Contains(u) && !_discarded.Contains(u)).ToList();
        }

        public int Nodes { get { return _parent.Count; } }

        public int MaxBackups { get { return Dev.Max(x => x.Backups.Count); } }

        public sealed class Outcome
        {
            public int? LostDuring;
            public int LostEnd;
            public bool Converged;
            public Stats Stat;
            public int Backups, Nodes;
        }

        /// <summary>端末 2〜3 台が、乱数でプレイ・同期の開始・同期の確定・中断・削除・新規を繰り返す。最後に『クラウドを使う』で同期を 3 巡させて収束を見る。</summary>
        public static Outcome Simulate(int ndev, int steps, ulong seed, bool cas = true)
        {
            var rng = new SimRng(seed);
            var w = new SyncWorld(ndev, rng, cas);
            int? firstLost = null;
            for (int t = 0; t < steps; t++)
            {
                int d = rng.NextInt(ndev);
                double r = rng.NextDouble();
                if (r < 0.50) { if (!w.Play(d)) w.NewGame(d); }
                else if (r < 0.68) w.SyncBegin(d);
                else if (r < 0.86) w.SyncCommit(d);
                else if (r < 0.90) w.Abort(d);
                else if (r < 0.93) w.Delete(d);
                else if (r < 0.97) w.NewGame(d);
                else { w.SyncBegin(d); w.SyncCommit(d); }
                if (!firstLost.HasValue && w.Lost().Count > 0) firstLost = t;
            }
            w.Policy = "theirs";
            for (int k = 0; k < 3; k++)
                for (int d = 0; d < ndev; d++)
                {
                    w.Abort(d);
                    w.SyncBegin(d);
                    w.SyncCommit(d);
                }
            var live = new HashSet<string>(w.Dev.Select(s => s.Local != null && s.Local.Node.HasValue ? s.Local.Lineage + ":" + s.Local.Node.Value : "-"));
            string cloudLive = w.Cloud != null && w.Cloud.Node.HasValue ? w.Cloud.Lineage + ":" + w.Cloud.Node.Value : "-";
            bool converged = (live.Count == 1 && live.Contains(cloudLive)) || (cloudLive == "-" && live.All(x => x == "-"));
            return new Outcome { LostDuring = firstLost, LostEnd = w.Lost().Count, Converged = converged, Stat = w.Stat, Backups = w.MaxBackups, Nodes = w.Nodes };
        }

        /// <summary>ふつうの使い方: 起動時と終了時に同期(起動時にオフライン p_off・終了時に同期できない p_fail)。2 台を 7:3 で行き来する。(セッション数, 競合, 失われた更新)を返す。</summary>
        public static void Usage(int n, int sessions, ulong seed0, double pOff, double pFail, out int totSessions, out int totConflicts, out int totLost)
        {
            totSessions = totConflicts = totLost = 0;
            for (int k = 0; k < n; k++)
            {
                var rng = new SimRng(seed0 + (ulong)k);
                var w = new SyncWorld(2, rng, true);
                w.NewGame(0);
                for (int i = 0; i < sessions; i++)
                {
                    int d = (i == 0 || rng.NextDouble() < 0.7) ? 0 : 1;            // 最初のセッションは新規ゲームを作った端末
                    if (rng.NextDouble() >= pOff) { w.SyncBegin(d); w.SyncCommit(d); }
                    if (w.Dev[d].Local == null || !w.Dev[d].Local.Node.HasValue) w.NewGame(d);
                    int plays = 1 + rng.NextInt(20);
                    for (int p = 0; p < plays; p++) w.Play(d);
                    if (rng.NextDouble() >= pFail) { w.SyncBegin(d); w.SyncCommit(d); }
                    totSessions++;
                }
                totConflicts += w.Stat.Conflict;
                totLost += w.Lost().Count;
            }
        }
    }
}
