using System;
using System.Collections.Generic;
using Ekiden.Persistence.Json;
using Ekiden.Persistence.Sync;

namespace Ekiden.Persistence.Store
{
    /// <summary>端末ごとの内部状態(09 §3.3。<c>local_state.json</c>。同期しない・利用者には見せない)。壊れたら既定値で起動する(致命にしない)。</summary>
    public sealed class LocalState
    {
        public const int CurrentSchema = 1;

        public int SchemaVersion = CurrentSchema;
        public long ClockHighWater;                                                                 // 端末の時刻(UTC 秒)の高水位
        public long RunTotalSeconds;                                                                // アプリを動かした累計の秒(単調時計から)
        public readonly SortedDictionary<string, EntitlementCache> Entitlements = new SortedDictionary<string, EntitlementCache>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, SyncPoint> Sync = new SortedDictionary<string, SyncPoint>(StringComparer.Ordinal);   // 記録 → 最後に同期した時点
        public readonly List<PendingUnlock> PendingUnlocks = new List<PendingUnlock>();             // ストアへまだ通知していない実績の解除
        public string InstallId = "";                                                               // 診断のためだけ。ランダム。外へ送らない

        public sealed class SyncPoint
        {
            public string LineageId = "";
            public long Counter;
            public bool Deleted;                                                                    // 墓石
        }

        public sealed class PendingUnlock
        {
            public string AchievementId = "";
            public long QueuedAt;
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o["schema_version"] = new JsonNumber((long)SchemaVersion);
            o["clock_hw"] = new JsonNumber(ClockHighWater);
            o["run_total_s"] = new JsonNumber(RunTotalSeconds);
            var ent = new JsonObject();
            foreach (var kv in Entitlements)
            {
                var e = new JsonObject();
                e["owned"] = JsonBool.Of(kv.Value.Owned);
                e["verified_at"] = new JsonNumber((long)kv.Value.VerifiedAt);
                e["verified_run"] = new JsonNumber((long)kv.Value.VerifiedRun);
                ent[kv.Key] = e;
            }
            o["entitlements"] = ent;
            var sync = new JsonObject();
            foreach (var kv in Sync)
            {
                var s = new JsonObject();
                s["lineage_id"] = new JsonString(kv.Value.LineageId);
                s["counter"] = new JsonNumber(kv.Value.Counter);
                s["deleted"] = JsonBool.Of(kv.Value.Deleted);
                sync[kv.Key] = s;
            }
            o["sync"] = sync;
            var pu = new JsonArray();
            foreach (PendingUnlock p in PendingUnlocks)
            {
                var u = new JsonObject();
                u["ach_id"] = new JsonString(p.AchievementId);
                u["queued_at"] = new JsonNumber(p.QueuedAt);
                pu.Add(u);
            }
            o["pending_unlocks"] = pu;
            o["install_id"] = new JsonString(InstallId);
            return o;
        }

        /// <summary>読む。形が合わない項目は既定値に戻し、読めない(JSON でない)ときは既定値の状態を返す。</summary>
        public static LocalState FromJsonOrDefault(string text)
        {
            var st = new LocalState();
            JsonObject o;
            try { o = JsonValue.Parse(text) as JsonObject; }
            catch (JsonException) { return st; }
            if (o == null) return st;
            try
            {
                st.ClockHighWater = Long(o, "clock_hw");
                st.RunTotalSeconds = Long(o, "run_total_s");
                var ent = o.Get("entitlements") as JsonObject;
                if (ent != null)
                    foreach (string k in ent.Keys)
                    {
                        var e = ent[k] as JsonObject;
                        if (e != null) st.Entitlements[k] = new EntitlementCache { Owned = Bool(e, "owned"), VerifiedAt = Long(e, "verified_at"), VerifiedRun = Long(e, "verified_run") };
                    }
                var sync = o.Get("sync") as JsonObject;
                if (sync != null)
                    foreach (string k in sync.Keys)
                    {
                        var s = sync[k] as JsonObject;
                        if (s != null) st.Sync[k] = new SyncPoint { LineageId = Str(s, "lineage_id"), Counter = Long(s, "counter"), Deleted = Bool(s, "deleted") };
                    }
                var pu = o.Get("pending_unlocks") as JsonArray;
                if (pu != null)
                    foreach (JsonValue v in pu)
                    {
                        var u = v as JsonObject;
                        if (u != null) st.PendingUnlocks.Add(new PendingUnlock { AchievementId = Str(u, "ach_id"), QueuedAt = Long(u, "queued_at") });
                    }
                st.InstallId = Str(o, "install_id");
            }
            catch (InvalidCastException)
            {
                return new LocalState();
            }
            return st;
        }

        private static long Long(JsonObject o, string k) { var n = o.Get(k) as JsonNumber; return n == null ? 0 : (long)n.Value; }

        private static bool Bool(JsonObject o, string k) { var b = o.Get(k) as JsonBool; return b != null && b.Value; }

        private static string Str(JsonObject o, string k) { var s = o.Get(k) as JsonString; return s == null ? "" : s.Value; }

        /// <summary>同期の記録(<see cref="SyncRecord"/>)の last_synced に使う、その記録の最後に同期した時点。</summary>
        public long LastSyncedCounter(string record)
        {
            SyncPoint p;
            return Sync.TryGetValue(record, out p) ? p.Counter : 0;
        }
    }
}
