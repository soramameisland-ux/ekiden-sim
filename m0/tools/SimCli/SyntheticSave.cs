using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Persistence.Json;

namespace Ekiden.SimCli
{
    /// <summary>サイズの見積もり用の合成セーブ(<c>ref_model_09.py</c> の <c>build_save</c> の C# 版。09 §5.4)。第 years 年度の途中(自校の部員 40 人・他校 35 校 × 36 人・入学予定の候補 520 人)を、
    /// 02 §4.9 の構造に沿って作る。名前・能力はランダム(= 圧縮に不利な側)。刈り込み(09 §5.2)を入れた形。</summary>
    internal static class SyntheticSave
    {
        private const string Kanji = "佐鈴高田渡辺伊山中西小林加藤吉木松井清斎石橋森池阿部原野村上大川坂本岡崎前内藤長谷島田安藤青柳";
        private const string Kana = "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわをん";

        private static JsonNumber I(long v) { return new JsonNumber(v); }
        private static JsonNumber D(double v) { return new JsonNumber(Math.Round(v, 6)); }
        private static JsonString S(string v) { return new JsonString(v); }
        private static JsonBool B(bool v) { return JsonBool.Of(v); }
        private static JsonValue Null { get { return JsonNull.Instance; } }
        private static JsonArray A(IEnumerable<JsonValue> v) { return new JsonArray(v); }
        private static JsonArray Ints(IEnumerable<int> v) { return new JsonArray(v.Select(x => (JsonValue)I(x))); }

        private static JsonObject O(params (string, JsonValue)[] kv)
        {
            var o = new JsonObject();
            foreach (var (k, v) in kv) o[k] = v;
            return o;
        }

        private static string Name(Rnd r, int n) { return new string(Enumerable.Range(0, n).Select(_ => Kanji[r.Range(Kanji.Length)]).ToArray()); }
        private static string KanaStr(Rnd r, int n) { return new string(Enumerable.Range(0, n).Select(_ => Kana[r.Range(Kana.Length)]).ToArray()); }

        private static JsonObject Appearance(Rnd r)
        {
            return O(("hair_style", I(r.Int(0, 9))), ("hair_color", I(r.Int(0, 11))), ("face", I(r.Int(0, 4))), ("body", I(r.Int(0, 2))), ("skin_tone", I(r.Int(0, 5))));
        }

        private static int[] Stats(Rnd r, double mean) { return Enumerable.Range(0, 10).Select(_ => Math.Max(20, Math.Min(99, (int)r.Normal(mean, 6)))).ToArray(); }

        private static string StyleCode(int rid) { return "SBL"[rid % 3].ToString() + "PFS"[(rid / 3) % 3] + "CEL"[(rid / 9) % 3]; }

        private static JsonArray GiftsOf(int rid)
        {
            if (rid % 5 != 0) return new JsonArray();
            return rid % 25 != 0 ? A(new JsonValue[] { S("Climber") }) : A(new JsonValue[] { S("Ace"), S("Descender") });
        }

        private static JsonObject Runner(Rnd r, int rid, int year, int grade)
        {
            int[] st = Stats(r, 55 + 4 * (grade - 1));
            string[] skillPool = { "skill.hold_lead", "skill.pack_runner", "skill.big_stage", "skill.iron_legs", "skill.summer_boy" };
            string[] traitPool = { "Serious", "Competitive", "MyPace", "Passionate", "Careful", "Cheerful", "Quiet", "Caring" };
            return O(("id", I(rid)), ("family_name", S(Name(r, 2))), ("given_name", S(Name(r, 2))), ("family_kana", S(KanaStr(r, 4))), ("given_kana", S(KanaStr(r, 3))),
                ("origin_type", S("Japan")), ("prefecture_id", I(r.Int(1, 47))), ("country_id", Null), ("high_school_id", I(r.Int(1, 216))),
                ("school_id", I(0)), ("enroll_year", I(year - grade + 1)), ("growth_type", S(r.Pick("Early", "Normal", "Normal", "Late"))),
                ("stats", Ints(st)), ("potential", Ints(st.Select(s => Math.Min(100, s + r.Int(2, 30))))), ("xp", Ints(Enumerable.Range(0, 10).Select(_ => r.Int(0, 400)))),
                ("skills", A(Enumerable.Range(0, r.Pick(0, 0, 1, 2)).Select(i => (JsonValue)S(skillPool[(rid + i * 2) % skillPool.Length])))),
                ("fatigue", I(r.Int(10, 60))), ("form", I(r.Int(-1, 2))), ("taper_weeks", I(r.Int(0, 3))), ("sharpen_bits", I(r.Int(0, 7))),
                ("motivation", I(r.Int(2, 5))), ("injury", O(("severity", S("None")), ("weeks_left", I(0)))), ("group", S(r.Pick("A", "B", "C"))),
                ("bonds", new JsonArray()), ("traits", A(Enumerable.Range(0, r.Pick(1, 2)).Select(i => (JsonValue)S(traitPool[(rid + i * 3) % traitPool.Length])))),
                ("is_captain", JsonBool.False), ("recruit_source", S("Normal")), ("ticket_kind", Null),
                ("career", O(("races", I(r.Int(0, 12))), ("section_wins", I(r.Int(0, 3))), ("relay_wins", I(r.Int(0, 2))), ("brakes", I(r.Int(0, 2))))),
                ("ctr", O(new[] { "hill_weeks", "injuries", "injury_free", "camps", "camp_overload", "peaked_races", "overtakes", "lead_sections" }.Select(k => (k, (JsonValue)I(r.Int(0, 9)))).ToArray())),
                ("appearance", Appearance(r)), ("look_seed", I(r.U32())),
                ("style", S(StyleCode(rid))), ("gifts", GiftsOf(rid)), ("gift_revealed", B(rid % 3 == 0)));
        }

        private static JsonObject Rival(Rnd r, int rid, int year, int grade)
        {
            return O(("id", I(rid)), ("a0", D(r.Normal(50, 8))), ("gap", D(r.Normal(24, 7))), ("prog", D(r.Real() * 0.9)),
                ("growth_type", S(r.Pick("Early", "Normal", "Normal", "Late"))), ("profile", S(r.Pick("Balanced", "Endurance", "Speed", "Mountain"))),
                ("grade", I(grade)), ("enroll_year", I(year - grade + 1)), ("origin", I(r.Int(1, 47))), ("name", S(Name(r, 4))), ("kana", S(KanaStr(r, 7))),
                ("appearance", Appearance(r)), ("look_seed", I(r.U32())));
        }

        private static JsonObject Candidate(Rnd r, int cid, int year)
        {
            return O(("candidate_id", I(cid)), ("a0", D(r.Normal(37, 7.5))), ("gap", D(r.Normal(24, 7))), ("growth_type", S(r.Pick("Early", "Normal", "Late"))),
                ("profile", S(r.Pick("Balanced", "Endurance", "Speed", "Mountain"))), ("origin", A(new JsonValue[] { I(r.Int(1, 47)), I(r.Int(1, 216)) })),
                ("look_seed", I(r.U32())), ("candidate_year", I(year + 1)), ("status", S("Unseen")), ("interest", I(r.Int(0, 90))),
                ("info_level", A(new JsonValue[] { I(r.Int(0, 2)), I(r.Int(0, 1)), I(0), I(0) })), ("rating", I(r.Int(1, 5))), ("name", S(Name(r, 4))), ("kana", S(KanaStr(r, 7))));
        }

        private static JsonObject Ob(Rnd r, int rid, int year)
        {
            int[] st = Stats(r, 66);
            return O(("runner_id", I(rid)), ("name", S(Name(r, 4))), ("kana", S(KanaStr(r, 7))), ("origin", I(r.Int(1, 47))), ("high_school_name", S(Name(r, 2) + "高")),
                ("school_id", I(0)), ("enroll_year", I(year - 3)), ("grad_year", I(year)), ("final_stats", Ints(st)),
                ("skills", A(Enumerable.Range(0, r.Pick(0, 1)).Select(i => (JsonValue)S(i == 0 ? "skill.hold_lead" : "skill.big_stage")))),
                ("pb", O(("t5000", I(r.Int(7900, 9000))), ("t10000", I(r.Int(16500, 19000))), ("t_half", I(r.Int(36000, 41000))))),
                ("career", O(("race_wins", I(r.Int(0, 3))), ("section_wins", I(r.Int(0, 5))), ("records", I(r.Int(0, 1))))),
                ("career_path", S(r.Pick("Corporate", "Teacher", "Employment", "GradSchool"))), ("hall_of_fame", B(r.Real() < 0.05)),
                ("role", S("None")), ("role_until", I(0)), ("recruit_source", S("Normal")), ("appearance", Appearance(r)),
                ("style", S(StyleCode(rid))), ("gifts", rid % 3 == 0 ? GiftsOf(rid) : new JsonArray()));
        }

        private static JsonArray RaceYear(Rnd r, int year)
        {
            var recs = new JsonArray();
            foreach (var (kind, nTeam, nSec) in new[] { (1, 20, 6), (2, 30, 8), (3, 20, 10), (4, 26, 0) })
            {
                var ids = Enumerable.Range(0, 36).OrderBy(_ => r.U32()).Take(nTeam).ToList();     // 抽出の順はランダムでよい(サイズの見積もり)
                var results = new JsonArray();
                for (int rank = 1; rank <= ids.Count; rank++)
                {
                    int sid = ids[rank - 1];
                    var res = O(("school_id", I(sid)), ("rank", I(rank)), ("total_ds", I(r.Int(60000, 400000))), ("kuriage", B(r.Real() < 0.05)));
                    if (sid == 0)                                                                      // 刈り込み: 他校の区間は保存しない(§5.2)
                        res["sections"] = A(Enumerable.Range(0, nSec).Select(_ => (JsonValue)O(("runner_id", I(r.Int(1, 40000))), ("time_ds", I(r.Int(9000, 40000))), ("section_rank", I(r.Int(1, 20))),
                            ("cumulative_rank", I(r.Int(1, 20))), ("instruction", S("Keep")), ("events", new JsonArray()))));
                    results.Add(res);
                }
                recs.Add(O(("race_kind", I(kind)), ("year", I(year)), ("results", results)));
            }
            return recs;
        }

        public static JsonObject Build(int years, int seed = 1)
        {
            var r = new Rnd((ulong)seed);
            int rid = 1000 + years * 360;
            var runners = new List<JsonObject>();
            for (int i = 0; i < 40; i++) runners.Add(Runner(r, rid - i, years, 1 + i % 4));
            for (int i = 0; i < runners.Count; i++)
                for (int j = i + 1; j < runners.Count; j++)
                {
                    bool same = ((JsonString)runners[i]["group"]).Value == ((JsonString)runners[j]["group"]).Value;
                    if ((same && r.Real() < 0.8) || r.Real() < 0.08)
                    {
                        long a = ((JsonNumber)runners[i]["id"]).Int, b = ((JsonNumber)runners[j]["id"]).Int;
                        JsonObject owner = a < b ? runners[i] : runners[j];
                        ((JsonArray)owner["bonds"]).Add(A(new JsonValue[] { I(Math.Max(a, b)), I(r.Int(10, 600)) }));
                    }
                }
            var rivals = new JsonArray();
            for (int s = 1; s < 36; s++) for (int k = 0; k < 36; k++) rivals.Add(Rival(r, 100000 + s * 100 + k, years, 1 + k % 4));
            var pool = A(Enumerable.Range(0, 520).Select(i => (JsonValue)Candidate(r, i, years)));
            var ownOb = new List<JsonObject>();
            for (int i = 0; i < 10 * years; i++) ownOb.Add(Ob(r, 2000 + i, 1 + i / 10));
            var races = new JsonArray();
            for (int y = 1; y <= years; y++) foreach (JsonValue x in RaceYear(r, y)) races.Add(x);
            var schools = new JsonArray();
            for (int s = 0; s < 36; s++)
                schools.Add(O(("id", I(s)), ("name", S(Name(r, 3) + "大学")), ("name_short", S(Name(r, 2))), ("kana", S(KanaStr(r, 6))), ("prefecture_id", I(r.Int(1, 47))), ("tier", S("Mid")),
                    ("prestige", I(r.Int(60, 900))), ("fame_hist", D(r.Real() * 30)), ("colors", O(("primary", I(r.U32() & 0xFFFFFF)), ("secondary", I(r.U32() & 0xFFFFFF)))),
                    ("emblem_id", I(r.Int(0, 7))), ("is_player", B(s == 0)), ("roster", A(Enumerable.Range(0, 36).Select(k => (JsonValue)I(100000 + s * 100 + k)))), ("year_points", I(r.Int(0, 20))),
                    ("cum_points", I(r.Int(0, 300))), ("base_prestige", D(r.Real() * 800)), ("coach_q", D(r.Normal(0, 0.1)))));
            var runnerMap = new JsonObject();
            foreach (JsonObject x in runners) runnerMap[((JsonNumber)x["id"]).Int.ToString()] = x;
            var staff = A(new[] { "Coach", "Trainer", "Nutritionist", "Scout" }.Select((role, i) => (JsonValue)O(("id", I(i)), ("role", S(role)), ("name", S(Name(r, 4))), ("level", I(3)), ("salary", I(90)))));
            int ticketN = Math.Min(14, years / 3);
            var player = O(("funds", I(1500)), ("facilities", O(("Dorm", I(3)), ("Track", I(3)), ("Hill", I(2)), ("Gym", I(3)), ("Medical", I(2)))), ("staff", staff), ("morale", I(60)),
                ("captain_id", I(((JsonNumber)runners[0]["id"]).Int)),
                ("annual_plan", O(("focus", S("Balanced")), ("target_race", S("NewYear")), ("peaking_mode", O(("Fall", S("Standard")), ("National", S("Standard")), ("NewYear", S("Standard")), ("Qualifier", S("Short")), ("Meet", S("None")))))),
                ("monthly_plan", O(("A", S("th.base_build")), ("B", S("th.base_build")), ("C", S("th.speed")))), ("guidance_points", I(4)), ("scout_points", I(24)), ("coach_rank", I(6)),
                ("ob_network", Ints(Enumerable.Range(0, 48).Select(_ => r.Int(0, 25)))), ("manager_name", S("監督")), ("individual_menu", new JsonObject()), ("talked", new JsonArray()),
                ("tickets", O(("stock", I(1)), ("bank", I(0)), ("last_used_year", I(years - 2)), ("plan_anchor_year", Null), ("plan_granted_years", new JsonArray()),
                    ("milestones", A(Enumerable.Repeat((JsonValue)S("tm.win.fall"), ticketN))), ("pending", Null),
                    ("log", A(Enumerable.Range(0, Math.Min(years / 3, 50)).Select(i => (JsonValue)O(("year", I(years - i)), ("delta", I(1)), ("source", S("Earned")))))))));
            var courseRecords = new JsonObject();
            foreach (int k in new[] { 1, 2, 3 }) for (int s = 0; s < 6; s++) courseRecords[k + ":" + s] = O(("name", S(Name(r, 4))), ("school_id", I(r.Int(0, 35))), ("year", I(r.Int(1, Math.Max(1, years)))), ("time_ds", I(r.Int(9000, 40000))));
            var teamRecords = new JsonObject();
            foreach (int k in new[] { 1, 2, 3 }) teamRecords[k.ToString()] = O(("school_id", I(r.Int(0, 35))), ("total_ds", I(r.Int(60000, 400000))), ("year", I(years)));
            var hall = A(ownOb.Where(o => ((JsonBool)o["hall_of_fame"]).Value).Select(o => o["runner_id"]));
            var history = O(("race_records", races), ("course_records", courseRecords), ("team_records", teamRecords), ("hall_of_fame", hall));
            var highSchools = A(Enumerable.Range(0, 216).Select(i => (JsonValue)O(("id", I(i)), ("name", S(Name(r, 2) + "高")), ("prefecture_id", I(1 + i % 47)), ("strength", I(r.Int(0, 100))))));
            var fired = new JsonObject();
            for (int i = 0; i < 70; i++) fired["ev." + i] = O(("count", I(r.Int(0, 20))), ("last_week", I(r.Int(0, 48 * years))));
            var yearCounts = new JsonObject();
            for (int i = 0; i < 8; i++) yearCounts["ev." + i] = I(1);
            var runnerEv = new JsonObject();
            foreach (JsonObject x in runners) runnerEv[((JsonNumber)x["id"]).Int.ToString()] = O(("last_month", I(0)), ("once", new JsonArray()), ("cooldown", new JsonObject()));
            var pairCd = new JsonObject();
            for (int i = 0; i < 30; i++) pairCd["ev.x|" + r.Int(1, 9999) + "|" + r.Int(1, 9999)] = I(r.Int(0, 48 * years));
            var eventState = O(("fired", fired), ("year_counts", yearCounts), ("runner_ev", runnerEv), ("pair_cd", pairCd), ("mods", new JsonArray()), ("bench", new JsonArray()), ("forced", new JsonArray()),
                ("log", A(Enumerable.Range(0, 50).Select(i => (JsonValue)O(("id", S("ev." + i)), ("week", I(i)), ("choice", S("a")))))));
            return O(("schema_version", I(1)), ("scenario", O(("id", S("sc.mid")), ("difficulty", S("Mid")), ("player_school_id", I(0)))),
                ("run", O(("invites_declared", I(Math.Min(years / 3, 14))), ("cleared", years > 12 ? (JsonValue)O(("year", I(12)), ("no_invite", JsonBool.False)) : Null))),
                ("clock", O(("year", I(years)), ("month_index", I(6)), ("week", I(2)))), ("rng", O(("algorithm", S("pcg32-keyed-1")), ("master_seed", S(((ulong)r.U32() << 31 | r.U32()).ToString())))),
                ("counters", O(("next_runner_id", I(rid + 100000)), ("next_staff_id", I(40)))), ("schools", schools), ("player_team", player), ("runners", runnerMap), ("rival_runners", rivals),
                ("candidates", pool), ("ob_records", A(ownOb)), ("history", history), ("high_schools", highSchools), ("event_state", eventState),
                ("settings", O(("spectate_mode", S("Full")), ("auto_safety_f", I(85)), ("event_auto", JsonBool.False), ("plan_auto", JsonBool.False), ("scout_auto", JsonBool.False),
                    ("school_look", O(("uniform_pattern", I(0)), ("cheer_song", I(0)), ("bgm_set", I(0)))))),
                ("pack_snapshot", A(new JsonValue[] { S("pack.golden_plan") })));
        }
    }
}
