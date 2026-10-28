using System;
using System.Collections.Generic;
using Ekiden.Persistence.Container;
using Ekiden.Persistence.Json;

namespace Ekiden.Persistence.Migration
{
    /// <summary>マイグレーション(09 §6)。JSON → JSON の純関数を版ごとに連鎖して適用し、追加だけの変更は既定値の表で補う。移行は入力を変えない(複製に適用する)。</summary>
    public static class SaveMigrator
    {
        private static JsonString S(string v) { return new JsonString(v); }

        /// <summary>v0 → v1(09 §6.3。08 の確定に伴う変更)。破壊的な変更(列挙の読み替え・保存しない項目の削除)と、追加項目の既定値の補完。</summary>
        private static JsonObject Migrate0To1(JsonObject s)
        {
            JsonObject pt = ObjectOrCreate(s, "player_team");
            var ap = pt == null ? null : pt.Get("annual_plan") as JsonObject;
            if (ap != null && ap.Count > 0)
            {
                var f = ap.Get("focus") as JsonString;
                string focus = f == null ? "Balanced" : f.Value;
                // 重点を 3 つに絞った(08 R-04): Endurance → Long、Mountain → Balanced
                ap["focus"] = S(focus == "Speed" ? "Speed" : focus == "Endurance" ? "Long" : "Balanced");
                if (!ap.ContainsKey("target_race")) ap["target_race"] = S("NewYear");
            }
            var runners = s.Get("runners") as JsonObject;
            if (runners != null)
                foreach (string k in runners.Keys)
                {
                    var r = runners[k] as JsonObject;
                    if (r != null) r.Remove("pb");                        // 保存しない(08 R-03)
                }
            s["schema_version"] = new JsonNumber(1L);
            return s;
        }

        private static JsonObject ObjectOrCreate(JsonObject parent, string key)
        {
            JsonValue v;
            if (!parent.TryGet(key, out v))
            {
                var o = new JsonObject();
                parent[key] = o;
                return o;
            }
            return v as JsonObject;
        }

        /// <summary>v1 の既定値の表(09 §6.3)。足りない項目だけを補う。既存の値と未知の項目はそのまま。</summary>
        private static void FillDefaultsV1(JsonObject s)
        {
            JsonObject pt = ObjectOrCreate(s, "player_team");
            if (pt != null)
            {
                if (!pt.ContainsKey("manager_name")) pt["manager_name"] = S("監督");
                if (!pt.ContainsKey("individual_menu")) pt["individual_menu"] = new JsonObject();
                if (!pt.ContainsKey("talked")) pt["talked"] = new JsonArray();
            }
            JsonObject st = ObjectOrCreate(s, "settings");
            if (st != null)
            {
                if (!st.ContainsKey("plan_auto")) st["plan_auto"] = JsonBool.False;
                if (!st.ContainsKey("scout_auto")) st["scout_auto"] = JsonBool.False;
            }
        }

        /// <summary>目標の版まで移行する。新しい版は <c>schema_too_new</c>(開かない・直さない・消さない)。適用した移行の名前(例 <c>v0→v1</c>)を log に返す。</summary>
        public static JsonObject Migrate(JsonObject input, out List<string> log, int target = SaveConstants.K_SCHEMA_VER)
        {
            JsonObject s = (JsonObject)input.DeepClone();
            var vn = s.Get("schema_version") as JsonNumber;
            int v = vn == null ? 0 : (int)vn.Value;
            if (v > target) throw new LoadException("schema_too_new", v + " > " + target);
            log = new List<string>();
            while (v < target)
            {
                if (v != 0) throw new LoadException("no_migration", v.ToString());
                s = Migrate0To1(s);
                log.Add("v" + v + "→v" + (v + 1));
                v = (int)((JsonNumber)s["schema_version"]).Value;
            }
            FillDefaultsV1(s);
            return s;
        }
    }
}
