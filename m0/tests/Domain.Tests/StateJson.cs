using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Team;
using Ekiden.Domain.Training;

namespace Ekiden.Domain.Tests
{
    /// <summary>ゴールデンデータ(verify/gen_golden_06.py の snap)と TeamState の相互変換、および JSON の突き合わせ。テスト専用。</summary>
    public static class StateJson
    {
        private static readonly string[] ModNames = { "E_mult", "r_add", "inj_mult", "form_add", "funds_year", "scout_pts" };
        private static readonly string[] RankNames = { "fall", "national", "newyear" };

        public static string ModName(ModKind k) { return ModNames[(int)k]; }

        public static ModKind ModOf(string s) { return (ModKind)Array.IndexOf(ModNames, s); }

        public static TeamState Load(JsonElement j)
        {
            var s = new TeamState { Seed = j.GetProperty("seed").GetUInt64(), AbsWeek = j.GetProperty("abs_week").GetInt32() };
            JsonElement f = j.GetProperty("fac");
            s.Fac = new FacilityLevels
            {
                Dorm = f.GetProperty("dorm").GetInt32(), Track = f.GetProperty("track").GetInt32(), Hill = f.GetProperty("hill").GetInt32(), Gym = f.GetProperty("gym").GetInt32(),
                Medical = f.GetProperty("medical").GetInt32(), Coach = f.GetProperty("coach").GetInt32(), Trainer = f.GetProperty("trainer").GetInt32(),
                Nutri = f.GetProperty("nutri").GetInt32(), Scout = f.GetProperty("scout").GetInt32(),
            };
            s.Funds = j.GetProperty("funds").GetInt32();
            s.Prestige = j.GetProperty("prestige").GetDouble();
            s.Morale = j.GetProperty("morale").GetDouble();
            s.CoachRank = j.GetProperty("coach_rank").GetInt32();
            s.ScoutPts = j.GetProperty("scout_pts").GetDouble();
            s.GuidanceLeft = j.GetProperty("guidance_left").GetInt32();
            for (int i = 0; i < 3; i++)
            {
                s.Rank[i] = j.GetProperty("rank")[i].GetInt32();
                s.LastRank[i] = j.GetProperty("last_rank")[i].GetInt32();
            }
            s.YearPoints = j.GetProperty("year_points").GetInt32();
            foreach (JsonProperty p in j.GetProperty("year_counts").EnumerateObject()) s.YearCounts[p.Name] = p.Value.GetInt32();
            s.YearTotal = j.GetProperty("year_total").GetInt32();
            foreach (JsonProperty p in j.GetProperty("fired").EnumerateObject()) s.Fired[p.Name] = new EventRecord { Count = p.Value[0].GetInt32(), Last = p.Value[1].GetInt32() };
            foreach (JsonElement e in j.GetProperty("pair_cd").EnumerateArray())
            {
                string id = e[0].GetString();
                Dictionary<long, int> d;
                if (!s.PairCooldown.TryGetValue(id, out d)) s.PairCooldown[id] = d = new Dictionary<long, int>();
                d[BondTable.Key(e[1].GetInt32(), e[2].GetInt32())] = e[3].GetInt32();
            }
            foreach (JsonElement e in j.GetProperty("mods").EnumerateArray())
                s.Mods.Add(new TimedModifier { Kind = ModOf(e[0].GetString()), To = e[1].GetInt32(), Value = e[2].GetDouble(), Until = e[3].GetInt32(), Src = e[4].GetString() });
            foreach (JsonElement e in j.GetProperty("ob").EnumerateArray())
                s.Obs.Add(new OBRecord { Pref = e[0].GetInt32(), Corp = e[1].GetInt32() != 0, Power = e[2].GetDouble(), Role = (ObRole)Enum.Parse(typeof(ObRole), e[3].GetString()), Until = e[4].GetInt32() });
            foreach (JsonElement e in j.GetProperty("bench").EnumerateArray()) s.Bench.Add(e.GetInt32());
            foreach (JsonElement e in j.GetProperty("forced").EnumerateArray()) s.Forced.Add(e.GetInt32());
            foreach (JsonElement e in j.GetProperty("bonds").EnumerateArray()) s.Bonds.Set(e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32());
            foreach (JsonElement e in j.GetProperty("runners").EnumerateArray()) s.Runners.Add(LoadRunner(e));
            return s;
        }

        private static Runner LoadRunner(JsonElement j)
        {
            var r = new Runner { Id = j.GetProperty("id").GetInt32() };
            for (int i = 0; i < StatInfo.Count; i++)
            {
                r.Stats[i] = j.GetProperty("stats")[i].GetInt32();
                r.Potential[i] = j.GetProperty("pot")[i].GetInt32();
                r.Xp[i] = j.GetProperty("xp")[i].GetInt32();
            }
            r.Fatigue = j.GetProperty("fatigue").GetInt32();
            r.Form = j.GetProperty("form").GetInt32();
            r.Motivation = j.GetProperty("motivation").GetInt32();
            r.Grade = j.GetProperty("grade").GetInt32();
            r.InjurySeverity = (InjurySeverity)Enum.Parse(typeof(InjurySeverity), j.GetProperty("inj")[0].GetString());
            r.InjuryWeeksLeft = j.GetProperty("inj")[1].GetInt32();
            r.InjuryAge = j.GetProperty("inj_age").GetInt32();
            foreach (JsonElement e in j.GetProperty("skills").EnumerateArray()) r.Skills.Add(e.GetString());
            r.Group = j.GetProperty("group").GetInt32();
            r.Captain = j.GetProperty("captain").GetInt32() != 0;
            r.Overseas = j.GetProperty("overseas").GetInt32() != 0;
            r.Far = j.GetProperty("far").GetInt32() != 0;
            r.Near = j.GetProperty("near").GetInt32() != 0;
            r.Pref = j.GetProperty("pref").GetInt32();
            r.HighSchool = j.GetProperty("hs").GetInt32();
            r.EnrollWeek = j.GetProperty("enroll_week").GetInt32();
            foreach (JsonProperty p in j.GetProperty("ctr").EnumerateObject()) r.Counters[p.Name] = p.Value.GetInt32();
            r.Races = j.GetProperty("races").GetInt32();
            r.SectionWins = j.GetProperty("section_wins").GetInt32();
            r.Brakes = j.GetProperty("brakes").GetInt32();
            r.PRank = j.GetProperty("p_rank").GetInt32();
            r.Regular = j.GetProperty("regular").GetInt32() != 0;
            r.GapAvg = j.GetProperty("gap_avg").GetDouble();
            r.BondAvg = j.GetProperty("bond_avg").GetDouble();
            r.TaperWeeks = j.GetProperty("taper_weeks").GetInt32();
            JsonElement rf = j.GetProperty("race");
            r.RaceFlags = new RaceFlags { InLineup = rf[0].GetInt32(), Brake = rf[1].GetInt32(), SectionRank = rf[2].GetInt32(), SectionWin = rf[3].GetInt32(), Record = rf[4].GetInt32() };
            r.LastEventMonth = j.GetProperty("last_ev_month").GetInt32();
            foreach (JsonElement e in j.GetProperty("ev_once").EnumerateArray()) r.EventOnce.Add(e.GetString());
            foreach (JsonProperty p in j.GetProperty("cooldown").EnumerateObject()) r.EventCooldown[p.Name] = p.Value.GetInt32();
            Style? style = null;
            if (j.GetProperty("style").ValueKind == JsonValueKind.String)
            {
                Style st;
                if (!Style.TryParse(j.GetProperty("style").GetString(), out st)) throw new InvalidOperationException("style");
                style = st;
            }
            var gifts = new List<Gift>();
            foreach (JsonElement e in j.GetProperty("gifts").EnumerateArray()) gifts.Add((Gift)Enum.Parse(typeof(Gift), e.GetString()));
            r.SetIndividuality(style, gifts);
            return r;
        }

        /// <summary>TeamState を、ゴールデンデータと同じ形・同じ並びの JSON にする。</summary>
        public static JsonElement Dump(TeamState s)
        {
            var o = new JsonObject
            {
                ["abs_week"] = s.AbsWeek,
                ["seed"] = s.Seed,
                ["fac"] = new JsonObject
                {
                    ["dorm"] = s.Fac.Dorm, ["track"] = s.Fac.Track, ["hill"] = s.Fac.Hill, ["gym"] = s.Fac.Gym, ["medical"] = s.Fac.Medical,
                    ["coach"] = s.Fac.Coach, ["trainer"] = s.Fac.Trainer, ["nutri"] = s.Fac.Nutri, ["scout"] = s.Fac.Scout,
                },
                ["funds"] = s.Funds, ["prestige"] = s.Prestige, ["morale"] = s.Morale, ["coach_rank"] = s.CoachRank, ["scout_pts"] = s.ScoutPts,
                ["guidance_left"] = s.GuidanceLeft,
                ["rank"] = new JsonArray(s.Rank.Select(x => (JsonNode)x).ToArray()),
                ["last_rank"] = new JsonArray(s.LastRank.Select(x => (JsonNode)x).ToArray()),
                ["year_points"] = s.YearPoints,
            };
            var yc = new JsonObject();
            foreach (var kv in s.YearCounts) yc[kv.Key] = kv.Value;
            o["year_counts"] = yc;
            o["year_total"] = s.YearTotal;
            var fired = new JsonObject();
            foreach (var kv in s.Fired) fired[kv.Key] = new JsonArray(kv.Value.Count, kv.Value.Last);
            o["fired"] = fired;
            var pcd = new List<JsonNode>();
            foreach (var kv in s.PairCooldown.OrderBy(k => k.Key, StringComparer.Ordinal))
                foreach (var e in kv.Value.OrderBy(x => x.Key))
                    pcd.Add(new JsonArray(kv.Key, (int)(e.Key >> 32), (int)(e.Key & 0xFFFFFFFFL), e.Value));
            o["pair_cd"] = new JsonArray(pcd.ToArray());
            o["mods"] = new JsonArray(s.Mods.Select(m => (JsonNode)new JsonArray(ModName(m.Kind), m.To, m.Value, m.Until, m.Src)).ToArray());
            o["ob"] = new JsonArray(s.Obs.Select(b => (JsonNode)new JsonArray(b.Pref, b.Corp ? 1 : 0, b.Power, b.Role.ToString(), b.Until)).ToArray());
            o["bench"] = new JsonArray(s.Bench.OrderBy(x => x).Select(x => (JsonNode)x).ToArray());
            o["forced"] = new JsonArray(s.Forced.OrderBy(x => x).Select(x => (JsonNode)x).ToArray());
            o["bonds"] = new JsonArray(s.Bonds.Items().Select(b => (JsonNode)new JsonArray(b[0], b[1], b[2])).ToArray());
            o["runners"] = new JsonArray(s.Runners.Select(DumpRunner).ToArray());
            using (JsonDocument d = JsonDocument.Parse(o.ToJsonString())) return d.RootElement.Clone();
        }

        private static JsonNode DumpRunner(Runner r)
        {
            var ctr = new JsonObject();
            foreach (string k in Counters.All.OrderBy(x => x, StringComparer.Ordinal)) ctr[k] = r.Ctr(k);
            var cd = new JsonObject();
            foreach (var kv in r.EventCooldown) cd[kv.Key] = kv.Value;
            return new JsonObject
            {
                ["id"] = r.Id,
                ["stats"] = new JsonArray(r.Stats.Select(x => (JsonNode)x).ToArray()),
                ["pot"] = new JsonArray(r.Potential.Select(x => (JsonNode)x).ToArray()),
                ["xp"] = new JsonArray(r.Xp.Select(x => (JsonNode)x).ToArray()),
                ["fatigue"] = r.Fatigue, ["form"] = r.Form, ["motivation"] = r.Motivation, ["grade"] = r.Grade,
                ["inj"] = new JsonArray(r.InjurySeverity.ToString(), r.InjuryWeeksLeft),
                ["inj_age"] = r.InjuryAge,
                ["skills"] = new JsonArray(r.Skills.OrderBy(x => x, StringComparer.Ordinal).Select(x => (JsonNode)x).ToArray()),
                ["group"] = r.Group, ["captain"] = r.Captain ? 1 : 0, ["overseas"] = r.Overseas ? 1 : 0, ["far"] = r.Far ? 1 : 0, ["near"] = r.Near ? 1 : 0,
                ["pref"] = r.Pref, ["hs"] = r.HighSchool, ["enroll_week"] = r.EnrollWeek,
                ["ctr"] = ctr, ["races"] = r.Races, ["section_wins"] = r.SectionWins, ["brakes"] = r.Brakes, ["p_rank"] = r.PRank,
                ["regular"] = r.Regular ? 1 : 0, ["gap_avg"] = r.GapAvg, ["bond_avg"] = r.BondAvg, ["taper_weeks"] = r.TaperWeeks,
                ["race"] = new JsonArray(r.RaceFlags.InLineup, r.RaceFlags.Brake, r.RaceFlags.SectionRank, r.RaceFlags.SectionWin, r.RaceFlags.Record),
                ["last_ev_month"] = r.LastEventMonth,
                ["ev_once"] = new JsonArray(r.EventOnce.OrderBy(x => x, StringComparer.Ordinal).Select(x => (JsonNode)x).ToArray()),
                ["cooldown"] = cd,
                ["style"] = r.Style.HasValue ? JsonValue.Create(r.Style.Value.Code) : null,
                ["gifts"] = new JsonArray(r.Gifts.Select(x => (JsonNode)x.ToString()).ToArray()),
            };
        }

        /// <summary>期待値と実際の JSON を突き合わせる(数は相対誤差 1e-9。オブジェクトはキーの集合と各値)。違いの場所を返す(なければ null)。</summary>
        public static string Diff(JsonElement expected, JsonElement actual, string path = "")
        {
            if (expected.ValueKind == JsonValueKind.Number && actual.ValueKind == JsonValueKind.Number)
            {
                double e = expected.GetDouble(), a = actual.GetDouble();
                return Math.Abs(e - a) <= 1e-9 * Math.Max(1.0, Math.Abs(e)) ? null : path + ": 期待 " + e + " / 実際 " + a;
            }
            if (expected.ValueKind != actual.ValueKind)
            {
                bool bothBool = (expected.ValueKind == JsonValueKind.True || expected.ValueKind == JsonValueKind.False) && (actual.ValueKind == JsonValueKind.True || actual.ValueKind == JsonValueKind.False);
                if (!bothBool) return path + ": 型が違う(期待 " + expected.ValueKind + " / 実際 " + actual.ValueKind + ")";
            }
            switch (expected.ValueKind)
            {
                case JsonValueKind.Object:
                    {
                        var ek = expected.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal).ToList();
                        var ak = actual.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal).ToList();
                        if (!ek.SequenceEqual(ak)) return path + ": キーが違う(期待 {" + string.Join(",", ek) + "} / 実際 {" + string.Join(",", ak) + "})";
                        foreach (string k in ek)
                        {
                            string d = Diff(expected.GetProperty(k), actual.GetProperty(k), path + "." + k);
                            if (d != null) return d;
                        }
                        return null;
                    }
                case JsonValueKind.Array:
                    {
                        if (expected.GetArrayLength() != actual.GetArrayLength()) return path + ": 長さが違う(期待 " + expected.GetArrayLength() + " / 実際 " + actual.GetArrayLength() + ")";
                        for (int i = 0; i < expected.GetArrayLength(); i++)
                        {
                            string d = Diff(expected[i], actual[i], path + "[" + i + "]");
                            if (d != null) return d;
                        }
                        return null;
                    }
                case JsonValueKind.String:
                    return expected.GetString() == actual.GetString() ? null : path + ": 期待 \"" + expected.GetString() + "\" / 実際 \"" + actual.GetString() + "\"";
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return expected.GetBoolean() == actual.GetBoolean() ? null : path + ": 真偽が違う";
                default:
                    return null;
            }
        }
    }
}
