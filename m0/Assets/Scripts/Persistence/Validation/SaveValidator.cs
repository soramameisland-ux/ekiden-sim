using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Persistence.Json;

namespace Ekiden.Persistence.Validation
{
    public sealed class ValidationResult
    {
        public JsonObject Repaired;                 // 致命のときは null
        public string Fatal;                        // 致命の理由(例 "fatal:I-11 key 7")。致命でなければ null
        public readonly List<string> Fixes = new List<string>();
    }

    /// <summary>読み込み時の検証と修復(09 §7)。直せるものは決定的に直して続け、直すと『データを作ってしまう』ものは致命にして 1 つ前の世代へ進む。修復は冪等で、正常なセーブは 1 項目も変わらない。</summary>
    public static class SaveValidator
    {
        public static readonly string[] FatalKeys = { "schema_version", "clock", "player_team", "runners", "schools", "scenario", "rng" };
        private const string StyleForm = "SBL", StyleRun = "PFS", StyleFlow = "CEL";
        private static readonly string[] GiftsOk = { "Climber", "Descender", "Ace" };

        // ---- DOM の読み書き(型が合わないものは、Python の参照モデルと同じく 0 / 空として扱わず、そのまま例外にしないよう最小限の変換を行う)
        private static long L(JsonValue v) { var n = v as JsonNumber; return n == null ? 0 : (long)n.Value; }

        private static double D(JsonValue v) { var n = v as JsonNumber; return n == null ? 0 : n.Value; }

        private static string Str(JsonValue v) { var s = v as JsonString; return s == null ? null : s.Value; }

        private static JsonNumber N(long v) { return new JsonNumber(v); }

        private static bool StyleOk(JsonValue c)
        {
            string s = Str(c);
            return s != null && s.Length == 3 && StyleForm.IndexOf(s[0]) >= 0 && StyleRun.IndexOf(s[1]) >= 0 && StyleFlow.IndexOf(s[2]) >= 0;
        }

        public static long AbsWeek(JsonObject clock)
        {
            long week = clock.ContainsKey("week") ? L(clock["week"]) : 1;
            return (L(clock["year"]) - 1) * 48 + L(clock["month_index"]) * 4 + (week - 1);
        }

        private static bool OriginBad(JsonObject og)
        {
            string type = Str(og["origin_type"]);
            JsonValue pref = og["prefecture_id"], hs = og["high_school_id"], country = og["country_id"];
            if (type == "Japan")
            {
                long p = pref.IsNull ? 0 : L(pref);
                return !(1 <= p && p <= 47 && !hs.IsNull && country.IsNull);
            }
            return !(!country.IsNull && pref.IsNull);
        }

        /// <summary>修復すると『データを作ってしまう』ものを致命にする。理由のリスト(先頭が戻り値の理由)。</summary>
        private static List<string> FatalCheck(JsonObject s)
        {
            var why = new List<string>();
            var runners = (JsonObject)s["runners"];
            var ids = new HashSet<long>();
            foreach (string k in runners.Keys)
            {
                var r = (JsonObject)runners[k];
                ids.Add(long.Parse(k));
                if (long.Parse(k) != L(r["id"])) why.Add("I-11 key " + k);
                if (OriginBad((JsonObject)r["origin"])) why.Add("I-02 " + k);
                long g = L(r["grade"]);
                if (!(1 <= g && g <= 4)) why.Add("I-03 " + k);
            }
            var seen = new HashSet<long>();
            foreach (JsonValue scv in (JsonArray)s["schools"])
            {
                var sc = (JsonObject)scv;
                var roster = (JsonArray)sc["roster"];
                foreach (JsonValue rid in roster)
                {
                    if (!seen.Add(L(rid))) why.Add("I-06 " + L(rid));
                }
                bool isPlayer = sc.ContainsKey("is_player") && sc["is_player"] is JsonBool && ((JsonBool)sc["is_player"]).Value;
                if (roster.Count > (isPlayer ? 60 : 40)) why.Add("I-07/I-13 roster " + L(sc["id"]));
            }
            var rivalIds = ((JsonArray)s["rival_runners"]).Items.Select(x => L(((JsonObject)x)["id"])).ToList();
            if (new HashSet<long>(rivalIds).Count != rivalIds.Count || rivalIds.Any(ids.Contains)) why.Add("I-11 dup");
            return why;
        }

        /// <summary>検証と修復。入力は変えない(複製に直す)。致命なら Fatal に理由。</summary>
        public static ValidationResult ValidateAndRepair(JsonValue input)
        {
            var res = new ValidationResult();
            var root = input as JsonObject;
            if (root == null) { res.Fatal = "fatal:not-dict"; return res; }
            foreach (string k in FatalKeys)
                if (!root.ContainsKey(k)) { res.Fatal = "fatal:missing-" + k; return res; }
            List<string> why = FatalCheck(root);
            if (why.Count > 0) { res.Fatal = "fatal:" + why[0]; return res; }
            var s = (JsonObject)root.DeepClone();
            var fixes = res.Fixes;
            var runners = (JsonObject)s["runners"];
            var ids = new HashSet<long>(runners.Keys.Select(long.Parse));
            foreach (string k in runners.Keys.ToList())
            {
                var r = (JsonObject)runners[k];
                if (!StyleOk(r.Get("style")))                         // I-21: 個性は中立('BFE')に
                {
                    r["style"] = new JsonString("BFE");
                    fixes.Add("I-21 style " + k);
                }
                JsonValue g0 = r.Get("gifts");
                var g1 = new List<string>();
                var g0a = g0 as JsonArray;
                if (g0a != null)
                    foreach (JsonValue x in g0a)
                    {
                        string name = Str(x);
                        if (name != null && Array.IndexOf(GiftsOk, name) >= 0 && !g1.Contains(name) && g1.Count < 2) g1.Add(name);
                    }
                bool same = g0a != null && g0a.Count == g1.Count && Enumerable.Range(0, g1.Count).All(i => Str(g0a[i]) == g1[i]);
                if (!same)
                {
                    r["gifts"] = new JsonArray(g1.Select(x => (JsonValue)new JsonString(x)));
                    fixes.Add("I-21 gifts " + k);
                }
                var stats = (JsonArray)r["stats"];
                var pot = (JsonArray)r["potential"];
                for (int i = 0; i < 10; i++)
                {
                    long st = Math.Max(0, Math.Min(100, L(stats[i])));
                    long po = Math.Max(st, Math.Min(100, L(pot[i])));         // I-01: 能力 ≤ 素質 ≤ 100(素質を引き上げる)
                    if (st != L(stats[i]) || po != L(pot[i])) fixes.Add("I-01 " + k + "." + i);
                    stats[i] = N(st);
                    pot[i] = N(po);
                }
                var inj = (JsonObject)r["injury"];
                string sev = Str(inj["severity"]);
                long weeks = L(inj["weeks_left"]);
                if (sev != "None" && weeks <= 0)
                {
                    inj["severity"] = new JsonString("None");
                    inj["weeks_left"] = N(0);
                    fixes.Add("I-04 " + k);
                }
                else if (sev == "None" && weeks != 0)
                {
                    inj["weeks_left"] = N(0);
                    fixes.Add("I-04 " + k);
                }
                long nf = Math.Max(0, Math.Min(100, L(r["fatigue"]))), nm = Math.Max(1, Math.Min(5, L(r["motivation"]))), nfo = Math.Max(-2, Math.Min(2, L(r["form"])));
                if (nf != L(r["fatigue"]) || nm != L(r["motivation"]) || nfo != L(r["form"])) fixes.Add("I-05 " + k);
                r["fatigue"] = N(nf);
                r["motivation"] = N(nm);
                r["form"] = N(nfo);
                long myId = L(r["id"]);
                var nb = new JsonArray();
                foreach (JsonValue bv in ((JsonArray)r["bonds"]).Items.ToList())
                {
                    var b = (JsonArray)bv;
                    long other = L(b[0]);
                    if (ids.Contains(other) && other != myId)
                    {
                        if (other < myId)                                 // I-08: 小さい ID の側にだけ持つ(向きを直す)
                        {
                            ((JsonArray)((JsonObject)runners[other.ToString()])["bonds"]).Add(new JsonArray(new JsonValue[] { N(myId), N(L(b[1])) }));
                            fixes.Add("I-08 flip " + k);
                        }
                        else
                        {
                            long val = Math.Max(0, Math.Min(1000, L(b[1])));
                            if (val > 0) nb.Add(new JsonArray(new JsonValue[] { N(other), N(val) }));
                            if (val != L(b[1])) fixes.Add("I-08 clamp " + k);
                        }
                    }
                    else
                    {
                        fixes.Add("I-08 drop " + k);
                    }
                }
                r["bonds"] = nb;
            }
            foreach (string k in runners.Keys)                            // 向きを直したものを重複なしに整える(大きいほう・ID 昇順)
            {
                var r = (JsonObject)runners[k];
                var d = new SortedDictionary<long, long>();
                foreach (JsonValue bv in (JsonArray)r["bonds"])
                {
                    var b = (JsonArray)bv;
                    long id = L(b[0]), v = L(b[1]);
                    long cur;
                    d[id] = d.TryGetValue(id, out cur) ? Math.Max(cur, v) : Math.Max(0, v);
                }
                r["bonds"] = new JsonArray(d.Select(kv => (JsonValue)new JsonArray(new JsonValue[] { N(kv.Key), N(kv.Value) })));
            }
            foreach (JsonValue xv in (JsonArray)s["rival_runners"])
            {
                var x = (JsonObject)xv;
                double p = D(x["prog"]);
                double pr = Math.Max(0.0, Math.Min(0.95, p));             // I-13: 進捗は 0〜0.95
                if (pr != p)
                {
                    x["prog"] = new JsonNumber(pr);
                    fixes.Add("I-13 prog " + L(x["id"]));
                }
            }
            var pt = (JsonObject)s["player_team"];
            if (L(pt["funds"]) < 0)
            {
                pt["funds"] = N(0);
                fixes.Add("I-12 funds");
            }
            var fac = (JsonObject)pt["facilities"];
            foreach (string f in fac.Keys.ToList())
            {
                long lv = L(fac[f]);
                if (!(1 <= lv && lv <= 5))
                {
                    fac[f] = N(Math.Max(1, Math.Min(5, lv)));
                    fixes.Add("I-12 fac");
                }
            }
            JsonValue cap = pt.Get("captain_id");
            if (cap != null && !cap.IsNull)
            {
                string key = L(cap).ToString();
                if (!runners.ContainsKey(key) || L(((JsonObject)runners[key])["grade"]) < 3)
                {
                    pt["captain_id"] = JsonNull.Instance;
                    fixes.Add("I-09");
                }
            }
            var t = (JsonObject)pt["tickets"];
            long total = Math.Max(0, L(t["stock"])) + Math.Max(0, L(t["bank"]));   // I-14: 合計を保って整える(失効させない)
            long stock = Math.Min(2, total);
            if (stock != L(t["stock"]) || total - stock != L(t["bank"]))
            {
                t["stock"] = N(stock);
                t["bank"] = N(total - stock);
                fixes.Add("I-14");
            }
            var oldNet = (JsonArray)pt["ob_network"];
            var net = new long[oldNet.Count];                              // I-16: OB の人数は記録から数え直す(派生値)
            foreach (JsonValue ov in (JsonArray)s["ob_records"])
            {
                var o = (JsonObject)ov;
                if (!o["career_path"].IsNull) net[L(o["origin_index"])]++;
            }
            bool netSame = true;
            for (int i = 0; i < net.Length; i++) if (net[i] != L(oldNet[i])) netSame = false;
            if (!netSame)
            {
                pt["ob_network"] = new JsonArray(net.Select(x => (JsonValue)N(x)));
                fixes.Add("I-16");
            }
            long nTicket = runners.Keys.Count(k => Str(((JsonObject)runners[k])["recruit_source"]) == "Ticket");   // I-18: 宣言の総数は、招聘で入学した人数以上
            var run = (JsonObject)s["run"];
            if (L(run["invites_declared"]) < nTicket)
            {
                run["invites_declared"] = N(nTicket);
                fixes.Add("I-18");
            }
            long now = AbsWeek((JsonObject)s["clock"]);
            var es = (JsonObject)s["event_state"];
            var mods = (JsonArray)es["mods"];
            var live = new JsonArray(mods.Items.Where(m => L(((JsonObject)m)["until"]) > now));   // I-19: 切れた補正は消す
            if (live.Count != mods.Count)
            {
                es["mods"] = live;
                fixes.Add("I-19 mods");
            }
            long gp = Math.Max(0, Math.Min(4, L(pt["guidance_points"]))), sp = Math.Max(0, L(pt["scout_points"]));
            if (gp != L(pt["guidance_points"]) || sp != L(pt["scout_points"]))
            {
                pt["guidance_points"] = N(gp);
                pt["scout_points"] = N(sp);
                fixes.Add("I-20 points");
            }
            var menu = (JsonObject)pt["individual_menu"];
            foreach (string k in menu.Keys.ToList())
            {
                if (!runners.ContainsKey(k) || Str(((JsonObject)((JsonObject)runners[k])["injury"])["severity"]) != "None")
                {
                    menu.Remove(k);
                    fixes.Add("I-20 menu " + k);
                }
            }
            var talked = (JsonArray)pt["talked"];
            int n0 = talked.Count;
            var kept = new JsonArray(talked.Items.Where(x => runners.ContainsKey(L(x).ToString())));
            if (kept.Count != n0)
            {
                pt["talked"] = kept;
                fixes.Add("I-20 talked");
            }
            long mx = 0;
            foreach (long id in ids) mx = Math.Max(mx, id);
            foreach (JsonValue xv in (JsonArray)s["rival_runners"]) mx = Math.Max(mx, L(((JsonObject)xv)["id"]));
            var counters = (JsonObject)s["counters"];
            if (L(counters["next_runner_id"]) <= mx)
            {
                counters["next_runner_id"] = N(mx + 1);
                fixes.Add("counters");
            }
            res.Repaired = s;
            return res;
        }
    }
}
