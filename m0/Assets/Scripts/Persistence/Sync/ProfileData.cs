using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Persistence.Json;

namespace Ekiden.Persistence.Sync
{
    public sealed class ClearRecord
    {
        public string ScenarioId;
        public string Difficulty;           // 本体の難易度。シナリオパックは null
        public long Date;
        public long ClearYearIndex;
        public bool NoInvite;
    }

    /// <summary>実績・累計・クリアの記録(02 §3.10)。アカウント単位で、競合の確認はせずマージする(09 §9.6)。</summary>
    public sealed class ProfileData
    {
        public int SchemaVersion = 1;
        public readonly SortedDictionary<string, long> Achievements = new SortedDictionary<string, long>(StringComparer.Ordinal);   // 実績ID → 解除の日時
        public readonly SortedDictionary<string, long> Counters = new SortedDictionary<string, long>(StringComparer.Ordinal);
        public readonly List<ClearRecord> Clears = new List<ClearRecord>();

        /// <summary>マージ: schema_version は大きいほう・実績は和集合(解除の日時は早いほう)・累計はキーごとに最大・クリアは和集合(scenario_id + difficulty + date が同じものは 1 件)。可換・結合的・冪等。</summary>
        public static ProfileData Merge(ProfileData a, ProfileData b)
        {
            var o = new ProfileData { SchemaVersion = Math.Max(a.SchemaVersion, b.SchemaVersion) };
            foreach (ProfileData src in new[] { a, b })
                foreach (var kv in src.Achievements)
                {
                    long cur;
                    o.Achievements[kv.Key] = o.Achievements.TryGetValue(kv.Key, out cur) ? Math.Min(cur, kv.Value) : kv.Value;     // 一度解除したら消えない
                }
            foreach (ProfileData src in new[] { a, b })
                foreach (var kv in src.Counters)
                {
                    long cur;
                    o.Counters[kv.Key] = o.Counters.TryGetValue(kv.Key, out cur) ? Math.Max(cur, kv.Value) : kv.Value;             // 累計は単調増加なので最大
                }
            var clears = new Dictionary<Tuple<string, string, long>, ClearRecord>();
            foreach (ProfileData src in new[] { a, b })
                foreach (ClearRecord c in src.Clears) clears[Tuple.Create(c.ScenarioId, c.Difficulty ?? "", c.Date)] = c;           // 同じクリアは 1 件
            foreach (var k in clears.Keys.OrderBy(t => t.Item1, StringComparer.Ordinal).ThenBy(t => t.Item2, StringComparer.Ordinal).ThenBy(t => t.Item3))
                o.Clears.Add(clears[k]);
            return o;
        }

        public static ProfileData FromJson(JsonObject j)
        {
            var p = new ProfileData { SchemaVersion = (int)((JsonNumber)j["schema_version"]).Value };
            foreach (string k in ((JsonObject)j["achievements"]).Keys) p.Achievements[k] = (long)((JsonNumber)((JsonObject)j["achievements"])[k]).Value;
            foreach (string k in ((JsonObject)j["counters"]).Keys) p.Counters[k] = (long)((JsonNumber)((JsonObject)j["counters"])[k]).Value;
            foreach (JsonValue cv in (JsonArray)j["clears"])
            {
                var c = (JsonObject)cv;
                p.Clears.Add(new ClearRecord
                {
                    ScenarioId = ((JsonString)c["scenario_id"]).Value, Difficulty = c["difficulty"].IsNull ? null : ((JsonString)c["difficulty"]).Value,
                    Date = (long)((JsonNumber)c["date"]).Value, ClearYearIndex = (long)((JsonNumber)c["clear_year_index"]).Value, NoInvite = ((JsonBool)c["no_invite"]).Value,
                });
            }
            return p;
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o["schema_version"] = new JsonNumber((long)SchemaVersion);
            var ach = new JsonObject();
            foreach (var kv in Achievements) ach[kv.Key] = new JsonNumber(kv.Value);
            o["achievements"] = ach;
            var cnt = new JsonObject();
            foreach (var kv in Counters) cnt[kv.Key] = new JsonNumber(kv.Value);
            o["counters"] = cnt;
            var cl = new JsonArray();
            foreach (ClearRecord c in Clears)
            {
                var co = new JsonObject();
                co["scenario_id"] = new JsonString(c.ScenarioId);
                co["difficulty"] = c.Difficulty == null ? (JsonValue)JsonNull.Instance : new JsonString(c.Difficulty);
                co["date"] = new JsonNumber(c.Date);
                co["clear_year_index"] = new JsonNumber(c.ClearYearIndex);
                co["no_invite"] = JsonBool.Of(c.NoInvite);
                cl.Add(co);
            }
            o["clears"] = cl;
            return o;
        }
    }
}
