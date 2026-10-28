using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Rng;
using Ekiden.Persistence.Json;
using Ekiden.Persistence.Validation;

namespace Ekiden.SimCli
{
    /// <summary>Python の乱数に似せた薄い包み(Pcg32)。</summary>
    internal sealed class Rnd
    {
        private readonly Pcg32 _g;
        public Rnd(ulong seed) { _g = new KeyedRngFactory(seed).Stream(99, 0, 0); }
        public int Int(int lo, int hi) { return _g.RandInt(lo, hi); }
        public double Real() { return _g.Uniform(); }
        public int Range(int n) { return _g.RandInt(0, n - 1); }
        public uint U32() { return _g.U32(); }
        public double Normal(double mu, double sd) { return _g.Normal(mu, sd); }
        public T Pick<T>(params T[] a) { return a[Range(a.Length)]; }
        public bool Chance(double p) { return Real() < p; }
    }

    /// <summary>簡易セーブ(<c>ref_model_09.py</c> の <c>mini_save</c>)と、壊し方(<c>corrupt</c>)の C# 版。検証と修復(09 §7)のファズの入力。</summary>
    internal static class MiniSave
    {
        private static JsonNumber I(long v) { return new JsonNumber(v); }
        private static JsonString S(string v) { return new JsonString(v); }
        private static JsonArray A(IEnumerable<JsonValue> v) { return new JsonArray(v); }
        private static JsonValue Null { get { return JsonNull.Instance; } }

        private static JsonObject O(params (string, JsonValue)[] kv)
        {
            var o = new JsonObject();
            foreach (var (k, v) in kv) o[k] = v;
            return o;
        }

        public static JsonObject Build(Rnd rng, int n = 40)
        {
            var runners = new JsonObject();
            for (int i = 1; i <= n; i++)
            {
                int[] st = Enumerable.Range(0, 10).Select(_ => rng.Int(30, 80)).ToArray();
                bool jp = rng.Real() < 0.9;
                int[] pot = st.Select(x => Math.Min(100, x + rng.Int(2, 25))).ToArray();
                int fatigue = rng.Int(0, 80), motivation = rng.Int(1, 5), form = rng.Int(-2, 2), enroll = rng.Int(1, 10);
                string style = rng.Pick("S", "B", "L") + rng.Pick("P", "F", "S") + rng.Pick("C", "E", "L");
                string[] gifts = rng.Pick(new string[0], new string[0], new string[0], new[] { "Climber" }, new[] { "Ace", "Descender" });
                bool revealed = rng.Real() < 0.5;
                JsonObject origin = jp
                    ? O(("origin_type", S("Japan")), ("prefecture_id", I(rng.Int(1, 47))), ("high_school_id", I(rng.Int(1, 216))), ("country_id", Null))
                    : O(("origin_type", S("Overseas")), ("prefecture_id", Null), ("high_school_id", Null), ("country_id", I(rng.Int(1, 24))));
                runners[i.ToString()] = O(("id", I(i)), ("stats", A(st.Select(x => (JsonValue)I(x)))), ("potential", A(pot.Select(x => (JsonValue)I(x)))), ("fatigue", I(fatigue)), ("motivation", I(motivation)), ("form", I(form)),
                    ("injury", O(("severity", S("None")), ("weeks_left", I(0)))), ("bonds", new JsonArray()), ("enroll_year", I(enroll)), ("grade", I(1 + i % 4)),
                    ("recruit_source", S(i == 7 || i == 8 ? "Ticket" : "Normal")), ("style", S(style)), ("gifts", A(gifts.Select(x => (JsonValue)S(x)))), ("gift_revealed", JsonBool.Of(revealed)), ("origin", origin));
            }
            for (int i = 1; i <= n; i++)
                for (int j = i + 1; j <= n; j++)
                    if (rng.Real() < 0.2) ((JsonArray)((JsonObject)runners[i.ToString()])["bonds"]).Add(A(new JsonValue[] { I(j), I(rng.Int(1, 900)) }));
            var rivals = A(Enumerable.Range(0, 72).Select(k => (JsonValue)O(("id", I(1000 + k)), ("school_id", I(1 + k / 36)), ("prog", new JsonNumber(Math.Round(rng.Real() * 0.9, 4))))));
            var schools = A(new JsonValue[]
            {
                O(("id", I(0)), ("is_player", JsonBool.True), ("roster", A(Enumerable.Range(1, n).Select(x => (JsonValue)I(x))))),
                O(("id", I(1)), ("roster", A(Enumerable.Range(0, 36).Select(k => (JsonValue)I(1000 + k))))),
                O(("id", I(2)), ("roster", A(Enumerable.Range(0, 36).Select(k => (JsonValue)I(1036 + k))))),
            });
            var net = new long[48];
            var obs = new JsonArray();
            for (int k = 0; k < 30; k++)
            {
                bool career = rng.Real() < 0.9;
                int origin = rng.Range(48);
                if (career) net[origin]++;
                obs.Add(O(("runner_id", I(5000 + k)), ("career_path", career ? (JsonValue)S("Company") : Null), ("origin_index", I(origin))));
            }
            JsonObject clock = O(("year", I(12)), ("month_index", I(3)), ("week", I(1)));
            long now = SaveValidator.AbsWeek(clock);
            int nm = rng.Int(0, 4);
            var mods = new JsonArray();
            for (int k = 0; k < nm; k++) mods.Add(O(("id", S("m" + k)), ("until", I(now + rng.Int(1, 20)))));
            return O(("schema_version", I(1)), ("clock", clock), ("scenario", O(("id", S("sc.mid")))), ("rng", O(("algorithm", S("pcg32-keyed-1")), ("master_seed", I(1)))), ("schools", schools),
                ("run", O(("invites_declared", I(3)), ("cleared", Null))), ("counters", O(("next_runner_id", I(n + 1 > 1072 ? n + 1 : 2000)))), ("runners", runners), ("rival_runners", rivals), ("ob_records", obs),
                ("event_state", O(("mods", mods))),
                ("player_team", O(("funds", I(1500)), ("facilities", O(("Dorm", I(3)), ("Track", I(2)))), ("captain_id", I(3)), ("tickets", O(("stock", I(1)), ("bank", I(0)))), ("guidance_points", I(4)), ("scout_points", I(12)),
                    ("individual_menu", new JsonObject()), ("talked", new JsonArray()), ("ob_network", A(net.Select(x => (JsonValue)I(x)))))));
        }

        public static readonly string[] FatalOps = { "grade", "origin", "dup_roster", "key", "roster_big", "rival_dup" };
        public static readonly string[] RepairOps =
        {
            "stat", "pot", "inj1", "inj2", "fat", "mot", "form", "bond_dir", "bond_missing", "bond_big", "funds", "fac", "cap", "tix", "gp", "menu", "talked", "counter",
            "prog", "obnet", "mod", "invites", "style", "gifts",
        };

        /// <summary>壊す。戻り値 = 壊したセーブ。used に使った操作を入れる。</summary>
        public static JsonObject Corrupt(JsonObject src, Rnd rng, List<string> used, bool allowFatal = true)
        {
            var s = (JsonObject)src.DeepClone();
            var runners = (JsonObject)s["runners"];
            var keys = runners.Keys.ToList();
            string[] ops = allowFatal ? RepairOps.Concat(FatalOps).ToArray() : RepairOps;
            var pt = (JsonObject)s["player_team"];
            int count = rng.Int(1, 6);
            for (int c = 0; c < count; c++)
            {
                string op = rng.Pick(ops);
                used.Add(op);
                var r = (JsonObject)runners[rng.Pick(keys.ToArray())];
                long id = ((JsonNumber)r["id"]).Int;
                switch (op)
                {
                    case "stat": ((JsonArray)r["stats"])[rng.Range(10)] = I(rng.Pick(-5, 150, 999)); break;
                    case "pot": ((JsonArray)r["potential"])[rng.Range(10)] = I(rng.Pick(0, 20, 130)); break;
                    case "inj1": r["injury"] = O(("severity", S("Minor")), ("weeks_left", I(0))); break;
                    case "inj2": r["injury"] = O(("severity", S("None")), ("weeks_left", I(5))); break;
                    case "fat": r["fatigue"] = I(rng.Pick(-10, 300)); break;
                    case "mot": r["motivation"] = I(rng.Pick(0, 9)); break;
                    case "form": r["form"] = I(rng.Pick(-7, 6)); break;
                    case "bond_dir": ((JsonArray)r["bonds"]).Add(A(new JsonValue[] { I(Math.Max(1, id - 1)), I(100) })); break;
                    case "bond_missing": ((JsonArray)r["bonds"]).Add(A(new JsonValue[] { I(99999), I(100) })); break;
                    case "bond_big": ((JsonArray)r["bonds"]).Add(A(new JsonValue[] { I(id + 1), I(rng.Pick(-5, 5000)) })); break;
                    case "funds": pt["funds"] = I(-rng.Int(1, 500)); break;
                    case "fac": ((JsonObject)pt["facilities"])["Dorm"] = I(rng.Pick(0, 9)); break;
                    case "cap": pt["captain_id"] = I(rng.Chance(0.5) ? 99999 : long.Parse(rng.Pick(keys.ToArray()))); break;
                    case "tix": pt["tickets"] = O(("stock", I(rng.Pick(-1, 3, 5, 1))), ("bank", I(rng.Pick(-2, 0, 4)))); break;
                    case "gp": pt["guidance_points"] = I(rng.Pick(-3, 99)); pt["scout_points"] = I(rng.Pick(-5, 10)); break;
                    case "menu": ((JsonObject)pt["individual_menu"])[rng.Chance(0.5) ? "99999" : rng.Pick(keys.ToArray())] = O(("until_abs_week", I(3)), ("menu", new JsonArray())); break;
                    case "talked": pt["talked"] = A(new JsonValue[] { I(99999), I(long.Parse(rng.Pick(keys.ToArray()))) }); break;
                    case "counter": ((JsonObject)s["counters"])["next_runner_id"] = I(3); break;
                    case "prog": ((JsonObject)((JsonArray)s["rival_runners"])[rng.Range(((JsonArray)s["rival_runners"]).Count)])["prog"] = new JsonNumber(rng.Pick(-0.2, 1.5, 0.99)); break;
                    case "obnet": { var net = (JsonArray)pt["ob_network"]; int i = rng.Range(48); net[i] = I(((JsonNumber)net[i]).Int + rng.Pick(-1, 3)); break; }
                    case "mod": ((JsonArray)((JsonObject)s["event_state"])["mods"]).Add(O(("id", S("old")), ("until", I(SaveValidator.AbsWeek((JsonObject)s["clock"]) - rng.Int(0, 5))))); break;
                    case "invites": ((JsonObject)s["run"])["invites_declared"] = I(rng.Pick(0, 1, -3)); break;
                    case "style": { int k = rng.Range(6); r["style"] = k == 2 ? Null : (k == 5 ? (JsonValue)I(7) : S(new[] { "XYZ", "SP", "", "sPc", "SPCC" }[k])); break; }
                    case "gifts":
                        {
                            switch (rng.Range(6))
                            {
                                case 0: r["gifts"] = A(new JsonValue[] { S("Foo") }); break;
                                case 1: r["gifts"] = A(new JsonValue[] { S("Ace"), S("Ace") }); break;
                                case 2: r["gifts"] = A(new JsonValue[] { S("Climber"), S("Descender"), S("Ace") }); break;
                                case 3: r["gifts"] = S("Ace"); break;
                                case 4: r["gifts"] = Null; break;
                                default: r["gifts"] = A(new JsonValue[] { S("Ace"), S("Bar") }); break;
                            }
                            break;
                        }
                    case "grade": r["grade"] = I(rng.Pick(0, 5, 9)); break;
                    case "origin":
                        switch (rng.Range(4))
                        {
                            case 0: r["origin"] = O(("origin_type", S("Japan")), ("prefecture_id", I(99)), ("high_school_id", I(1)), ("country_id", Null)); break;
                            case 1: r["origin"] = O(("origin_type", S("Japan")), ("prefecture_id", I(3)), ("high_school_id", Null), ("country_id", Null)); break;
                            case 2: r["origin"] = O(("origin_type", S("Overseas")), ("prefecture_id", I(3)), ("high_school_id", Null), ("country_id", Null)); break;
                            default: r["origin"] = O(("origin_type", S("Japan")), ("prefecture_id", I(3)), ("high_school_id", I(1)), ("country_id", I(2))); break;
                        }
                        break;
                    case "dup_roster": ((JsonArray)((JsonObject)((JsonArray)s["schools"])[1])["roster"])[0] = ((JsonArray)((JsonObject)((JsonArray)s["schools"])[0])["roster"])[0]; break;
                    case "key": r["id"] = I(id + 1000); break;
                    case "roster_big": { var ro = (JsonArray)((JsonObject)((JsonArray)s["schools"])[1])["roster"]; for (int k = 7000; k < 7010; k++) ro.Add(I(k)); break; }
                    case "rival_dup": ((JsonObject)((JsonArray)s["rival_runners"])[1])["id"] = ((JsonObject)((JsonArray)s["rival_runners"])[0])["id"]; break;
                }
            }
            return s;
        }
    }
}
