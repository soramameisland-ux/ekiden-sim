using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Persistence.Json;

namespace Ekiden.Persistence.Validation
{
    /// <summary>修復後に満たすべき不変条件(02 §5 の部分集合。09 §7.1)。<see cref="SaveValidator"/> とは別に書いた独立の検査(ファズの判定役)。違反の文言のリストを返す。</summary>
    public static class SaveInvariants
    {
        private static long L(JsonValue v) { var n = v as JsonNumber; return n == null ? 0 : (long)n.Value; }

        private static string S(JsonValue v) { var s = v as JsonString; return s == null ? null : s.Value; }

        private static bool StyleOk(JsonValue c)
        {
            string s = S(c);
            return s != null && s.Length == 3 && "SBL".IndexOf(s[0]) >= 0 && "PFS".IndexOf(s[1]) >= 0 && "CEL".IndexOf(s[2]) >= 0;
        }

        private static bool GiftsOk(JsonValue g)
        {
            var a = g as JsonArray;
            if (a == null || a.Count > 2) return false;
            var names = a.Items.Select(S).ToList();
            return names.All(x => x == "Climber" || x == "Descender" || x == "Ace") && names.Distinct().Count() == names.Count;
        }

        public static List<string> Check(JsonObject s)
        {
            var v = new List<string>();
            var runners = (JsonObject)s["runners"];
            var ids = new HashSet<long>(runners.Keys.Select(long.Parse));
            foreach (string k in runners.Keys)
            {
                var r = (JsonObject)runners[k];
                if (long.Parse(k) != L(r["id"])) v.Add("I-11 key " + k);
                if (!StyleOk(r.Get("style")) || !GiftsOk(r.Get("gifts"))) v.Add("I-21 " + k);
                var stats = (JsonArray)r["stats"];
                var pot = (JsonArray)r["potential"];
                for (int i = 0; i < 10; i++)
                    if (!(0 <= L(stats[i]) && L(stats[i]) <= L(pot[i]) && L(pot[i]) <= 100)) v.Add("I-01 " + k);
                var og = (JsonObject)r["origin"];
                if (S(og["origin_type"]) == "Japan")
                {
                    long p = og["prefecture_id"].IsNull ? 0 : L(og["prefecture_id"]);
                    if (!(1 <= p && p <= 47 && !og["high_school_id"].IsNull && og["country_id"].IsNull)) v.Add("I-02 " + k);
                }
                else if (!(!og["country_id"].IsNull && og["prefecture_id"].IsNull)) v.Add("I-02 " + k);
                long g = L(r["grade"]);
                if (!(1 <= g && g <= 4)) v.Add("I-03 " + k);
                var inj = (JsonObject)r["injury"];
                if ((S(inj["severity"]) == "None") != (L(inj["weeks_left"]) == 0)) v.Add("I-04 " + k);
                if (!(0 <= L(r["fatigue"]) && L(r["fatigue"]) <= 100 && 1 <= L(r["motivation"]) && L(r["motivation"]) <= 5 && -2 <= L(r["form"]) && L(r["form"]) <= 2)) v.Add("I-05 " + k);
                foreach (JsonValue bv in (JsonArray)r["bonds"])
                {
                    var b = (JsonArray)bv;
                    if (!(ids.Contains(L(b[0])) && L(b[0]) > L(r["id"]) && 0 < L(b[1]) && L(b[1]) <= 1000)) v.Add("I-08 " + k);
                }
            }
            var seen = new HashSet<long>();
            foreach (JsonValue scv in (JsonArray)s["schools"])
            {
                var sc = (JsonObject)scv;
                foreach (JsonValue rid in (JsonArray)sc["roster"])
                    if (!seen.Add(L(rid))) v.Add("I-06 " + L(rid));
                bool isPlayer = sc.ContainsKey("is_player") && ((JsonBool)sc["is_player"]).Value;
                if (((JsonArray)sc["roster"]).Count > (isPlayer ? 60 : 40)) v.Add("I-07/I-13 roster " + L(sc["id"]));
            }
            var rival = (JsonArray)s["rival_runners"];
            var rivalIds = rival.Items.Select(x => L(((JsonObject)x)["id"])).ToList();
            if (rivalIds.Distinct().Count() != rivalIds.Count || rivalIds.Any(ids.Contains)) v.Add("I-11 dup");
            foreach (JsonValue xv in rival)
            {
                double pr = ((JsonNumber)((JsonObject)xv)["prog"]).Value;
                if (!(0 <= pr && pr <= 0.95)) v.Add("I-13 prog " + L(((JsonObject)xv)["id"]));
            }
            var pt = (JsonObject)s["player_team"];
            if (L(pt["funds"]) < 0) v.Add("I-12 funds");
            var fac = (JsonObject)pt["facilities"];
            if (fac.Keys.Any(k => !(1 <= L(fac[k]) && L(fac[k]) <= Ekiden.Domain.Training.TrainingMaster.FacMax))) v.Add("I-12 fac");
            JsonValue cap = pt.Get("captain_id");
            if (cap != null && !cap.IsNull && (!runners.ContainsKey(L(cap).ToString()) || L(((JsonObject)runners[L(cap).ToString()])["grade"]) < 3)) v.Add("I-09");
            var t = (JsonObject)pt["tickets"];
            if (!(0 <= L(t["stock"]) && L(t["stock"]) <= 2 && L(t["bank"]) >= 0 && (L(t["stock"]) == 2 || L(t["bank"]) == 0))) v.Add("I-14");
            long netSum = ((JsonArray)pt["ob_network"]).Items.Sum(L);
            long career = ((JsonArray)s["ob_records"]).Items.Count(o => !((JsonObject)o)["career_path"].IsNull);
            if (netSum != career) v.Add("I-16");
            if (L(((JsonObject)s["run"])["invites_declared"]) < runners.Keys.Count(k => S(((JsonObject)runners[k])["recruit_source"]) == "Ticket")) v.Add("I-18");
            long now = SaveValidator.AbsWeek((JsonObject)s["clock"]);
            if (((JsonArray)((JsonObject)s["event_state"])["mods"]).Items.Any(m => L(((JsonObject)m)["until"]) <= now)) v.Add("I-19 mods");
            if (!(0 <= L(pt["guidance_points"]) && L(pt["guidance_points"]) <= Ekiden.Domain.World.WorldMath.GuidanceMax && L(pt["scout_points"]) >= 0)) v.Add("I-20 points");
            var menu = (JsonObject)pt["individual_menu"];
            foreach (string k in menu.Keys)
                if (!runners.ContainsKey(k) || S(((JsonObject)((JsonObject)runners[k])["injury"])["severity"]) != "None") v.Add("I-20 menu " + k);
            if (((JsonArray)pt["talked"]).Items.Any(x => !runners.ContainsKey(L(x).ToString()))) v.Add("I-20 talked");
            long mx = 0;
            foreach (long id in ids) mx = Math.Max(mx, id);
            foreach (long id in rivalIds) mx = Math.Max(mx, id);
            if (L(((JsonObject)s["counters"])["next_runner_id"]) <= mx) v.Add("counters");
            return v;
        }
    }
}
