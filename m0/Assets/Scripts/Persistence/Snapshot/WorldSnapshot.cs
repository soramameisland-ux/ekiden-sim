using System;
using System.Collections.Generic;
using System.Globalization;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.Training;
using Ekiden.Domain.World;
using Ekiden.Persistence.Json;

namespace Ekiden.Persistence.Snapshot
{
    /// <summary>世界(36 校)と自校の抽象の状態を、保存の正規形(09 §5.3)の JSON にして取り出し・復元する。
    /// 乱数は鍵つきで状態を持たない(09 §8)ので、<c>master_seed</c> と学校・選手の状態だけで続きを進められる。
    /// 年度の切れ目(<see cref="WorldSim.PlayYear"/> の呼び出しの間)で取り出す。年度の途中にだけ意味を持つ値(名声ポイントの集計)は含めない。
    /// M0 の統合(T3: 保存 → 読み込み → 続行 = 続行のみ)の検査と、100 年度のセーブの大きさ・読み込みの時間の実測のための形。
    /// 自校の育成(Runner・イベント・日程)は含まない: ゲーム本体の自校の保存は M1 以降で、このクラスの隣に足す。</summary>
    public static class WorldSnapshot
    {
        public const int SchemaVersion = 1;
        public const string RngAlgorithm = "pcg32-keyed-1";

        public sealed class Restored
        {
            public WorldSim World;
            public PlayerAgent Agent;                 // 自校のない世界では null
            public int NextYear;
            public ulong MasterSeed;
        }

        // ------------------------------------------------------------ 取り出し
        public static JsonObject Capture(WorldSim w, PlayerAgent agent, int nextYear)
        {
            var kf = w.Rngs as KeyedRngFactory;
            if (kf == null) throw new InvalidOperationException("鍵つきの乱数(KeyedRngFactory)だけを保存できる");
            var o = new JsonObject();
            o["schema_version"] = N(SchemaVersion);
            var rng = new JsonObject();
            rng["algorithm"] = new JsonString(RngAlgorithm);
            rng["master_seed"] = new JsonString(kf.MasterSeed.ToString(CultureInfo.InvariantCulture));       // uint64 は文字列(09 §5.3)
            o["rng"] = rng;
            var clock = new JsonObject();
            clock["next_year"] = N(nextYear);
            o["clock"] = clock;
            var world = new JsonObject();
            world["elite_pref_bonus"] = D(w.ElitePrefBonus);
            o["world"] = world;
            var schools = new JsonArray();
            foreach (School s in w.Schools) schools.Add(CaptureSchool(s));
            o["schools"] = schools;
            o["history"] = CaptureHistory(w.History);
            o["player"] = agent == null ? (JsonValue)JsonNull.Instance : CapturePlayer(agent);
            return o;
        }

        private static JsonNumber N(long v) { return new JsonNumber(v); }
        private static JsonNumber D(double v) { return new JsonNumber(v); }
        private static JsonBool B(bool v) { return JsonBool.Of(v); }

        /// <summary>履歴(02 §4.9 の <c>history</c>)。大会の順位表は『学校 ID・合計タイム・繰り上げ(0/1)』の組を順位順に、記録は大会ごと(区間の添字順。なければ null)。</summary>
        /// <summary>大会の順位表 1 件の JSON。順位表は大会が終わったあとは変わらない(追記だけ)ので、作った JSON を使い回す(100 年度で 300 件。取り出しの時間の約 3 割。使う側は読むだけ)。</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RaceEntry, JsonObject> EntryJson = new System.Runtime.CompilerServices.ConditionalWeakTable<RaceEntry, JsonObject>();

        private static JsonObject BuildEntry(RaceEntry e)
        {
            var ro = new JsonObject();
            ro["year"] = N(e.Year);
            ro["kind"] = N((int)e.Kind);
            var rows = new JsonArray();
            foreach (RaceRow r in e.Rows) rows.Add(new JsonArray(new JsonValue[] { N(r.SchoolId), N(r.TotalDs), N(r.Kuriage ? 1 : 0) }));
            ro["rows"] = rows;
            return ro;
        }

        internal static JsonObject CaptureHistory(RaceHistory h)
        {
            var o = new JsonObject();
            var races = new JsonArray();
            foreach (RaceEntry e in h.Races) races.Add(EntryJson.GetValue(e, BuildEntry));
            o["race_records"] = races;
            var course = new JsonArray();
            for (int k = 0; k < 3; k++)
            {
                var kind = new JsonArray();
                if (h.Course[k] != null)
                    foreach (CourseRecord c in h.Course[k])
                        kind.Add(c == null ? (JsonValue)JsonNull.Instance : new JsonObject { ["school_id"] = N(c.SchoolId), ["runner_id"] = N(c.RunnerId), ["time_ds"] = N(c.TimeDs), ["year"] = N(c.Year) });
                course.Add(kind);
            }
            o["course_records"] = course;
            var team = new JsonArray();
            for (int k = 0; k < 3; k++)
            {
                TeamRecord t = h.Team[k];
                team.Add(t == null ? (JsonValue)JsonNull.Instance : new JsonObject { ["school_id"] = N(t.SchoolId), ["total_ds"] = N(t.TotalDs), ["year"] = N(t.Year) });
            }
            o["team_records"] = team;
            return o;
        }

        private static JsonObject CaptureSchool(School s)
        {
            var o = new JsonObject();
            o["id"] = N(s.Id);
            o["region"] = N((int)s.Region);
            o["tier"] = N((int)s.Tier);
            o["prestige"] = D(s.Prestige);
            o["fame_hist"] = D(s.Hist);
            o["base_prestige"] = D(s.Base);
            o["coach_q"] = D(s.CoachQ);
            o["is_player"] = B(s.IsPlayer);
            o["year_points"] = N(s.YearPoints);
            o["prev_points"] = N(s.PrevPoints);
            o["last_newyear_rank"] = s.LastNewYearRank.HasValue ? (JsonValue)N(s.LastNewYearRank.Value) : JsonNull.Instance;
            o["cum_points"] = D(s.CumPoints);
            if (s.IsPlayer)
            {
                o["funds"] = D(s.Funds);
                o["scout_lv"] = N(s.ScoutLv);
                FacilityLevels f = s.Fac;
                var fo = new JsonObject();
                fo["coach"] = N(f.Coach); fo["trainer"] = N(f.Trainer); fo["nutri"] = N(f.Nutri); fo["dorm"] = N(f.Dorm); fo["track"] = N(f.Track);
                fo["hill"] = N(f.Hill); fo["gym"] = N(f.Gym); fo["medical"] = N(f.Medical); fo["scout"] = N(f.Scout);
                o["facilities"] = fo;
                var keys = new List<int>(s.Ob.Keys);
                keys.Sort();
                var ob = new JsonArray();
                foreach (int k in keys) ob.Add(new JsonArray(new JsonValue[] { N(k), N(s.Ob[k]) }));
                o["ob_by_region"] = ob;
            }
            var roster = new JsonArray();
            foreach (WorldRunner r in s.Roster) roster.Add(CaptureRunner(r));
            o["roster"] = roster;
            return o;
        }

        private static JsonObject CaptureRunner(WorldRunner r)
        {
            var o = new JsonObject();
            o["a0"] = D(r.A0);
            o["gap"] = D(r.Gap);
            o["prog"] = D(r.Prog);
            o["growth"] = N((int)r.Growth);
            o["profile"] = N((int)r.Profile);
            o["grade"] = N(r.Grade);
            o["overseas"] = B(r.Overseas);
            o["pref"] = N(r.PrefectureId);
            o["region"] = N((int)r.Region);
            o["country"] = N(r.CountryIndex);
            o["hs_u"] = D(r.HighSchoolU);
            o["look_seed"] = N(r.LookSeed);
            o["school_id"] = N(r.SchoolId);
            o["ticket"] = B(r.Ticket);
            o["index"] = N(r.Index);
            return o;
        }

        private static JsonObject CapturePlayer(PlayerAgent a)
        {
            var o = new JsonObject();
            o["school_id"] = N(a.School.Id);
            o["next_kind"] = N(a.NextKind);
            o["tickets_enabled"] = B(a.TicketsEnabled);
            o["power_bonus"] = D(a.PowerBonus);
            TicketState t = a.Tickets;
            var to = new JsonObject();
            to["stock"] = N(t.Stock);
            to["bank"] = N(t.Bank);
            to["last_used_year"] = N(t.LastUsedYear);
            if (t.Pending == null) to["pending"] = JsonNull.Instance;
            else
            {
                var p = new JsonObject();
                p["kind"] = N((int)t.Pending.Kind); p["pref"] = N(t.Pending.Pref); p["enroll_year"] = N(t.Pending.EnrollYear);
                to["pending"] = p;
            }
            to["plan_anchor_year"] = t.PlanAnchorYear.HasValue ? (JsonValue)N(t.PlanAnchorYear.Value) : JsonNull.Instance;
            var granted = new JsonArray();
            foreach (int y in t.PlanGrantedYears) granted.Add(N(y));
            to["plan_granted_years"] = granted;
            var ms = new JsonArray();
            foreach (string m in t.Milestones) ms.Add(new JsonString(m));
            to["milestones"] = ms;
            var log = new JsonArray();
            foreach (TicketLogEntry e in t.Log) log.Add(new JsonObject { ["year"] = N(e.Year), ["delta"] = N(e.Delta), ["source"] = new JsonString(e.Source) });
            to["log"] = log;
            to["invites_declared"] = N(t.InvitesDeclared);
            o["tickets"] = to;
            var stats = new JsonArray();
            foreach (PlayerYearStats s in a.Stats) stats.Add(CaptureStats(s));
            o["stats"] = stats;
            return o;
        }

        internal static JsonObject CaptureStats(PlayerYearStats s)
        {
            var o = new JsonObject();
            o["year"] = N(s.Year);
            o["p"] = D(s.P);
            o["prestige"] = D(s.Prestige);
            o["funds"] = D(s.Funds);
            o["lv"] = D(s.Lv);
            o["power_rank"] = N(s.PowerRank);
            o["points"] = N(s.Points);
            o["wins"] = new JsonArray(new JsonValue[] { B(s.Wins[0]), B(s.Wins[1]), B(s.Wins[2]) });
            o["place"] = new JsonArray(new JsonValue[] { N(s.Place[0]), N(s.Place[1]), N(s.Place[2]) });
            o["top1"] = D(s.Top1);
            o["coach_rank"] = N(s.CoachRank);
            o["cum"] = D(s.Cum);
            return o;
        }

        // ------------------------------------------------------------ 復元
        public static Restored Restore(JsonObject o)
        {
            if (Int(o, "schema_version") != SchemaVersion) throw new FormatException("schema_version");
            JsonObject rng = Obj(o, "rng");
            if (Str(rng, "algorithm") != RngAlgorithm) throw new FormatException("rng.algorithm");
            ulong seed = ulong.Parse(Str(rng, "master_seed"), CultureInfo.InvariantCulture);
            var schools = new List<School>();
            foreach (JsonValue sv in Arr(o, "schools")) schools.Add(RestoreSchool((JsonObject)sv));
            RaceHistory history = o.ContainsKey("history") ? RestoreHistory(Obj(o, "history")) : new RaceHistory();           // 追加の項目: ないセーブは空の履歴(09 §6)
            WorldSim w = WorldSim.Restore(new KeyedRngFactory(seed), Dbl(Obj(o, "world"), "elite_pref_bonus"), schools, history);
            PlayerAgent agent = null;
            JsonValue pv = o.Get("player");
            if (!pv.IsNull) agent = RestorePlayer((JsonObject)pv, w);
            return new Restored { World = w, Agent = agent, NextYear = Int(Obj(o, "clock"), "next_year"), MasterSeed = seed };
        }

        internal static RaceHistory RestoreHistory(JsonObject o)
        {
            var h = new RaceHistory();
            foreach (JsonValue rv in Arr(o, "race_records"))
            {
                var ro = (JsonObject)rv;
                var e = new RaceEntry { Year = Int(ro, "year"), Kind = (RaceKind)Int(ro, "kind") };
                foreach (JsonValue row in Arr(ro, "rows"))
                {
                    var a = (JsonArray)row;
                    e.Rows.Add(new RaceRow { SchoolId = (int)((JsonNumber)a[0]).Int, TotalDs = (int)((JsonNumber)a[1]).Int, Kuriage = ((JsonNumber)a[2]).Int != 0 });
                }
                h.Races.Add(e);
            }
            JsonArray course = Arr(o, "course_records");
            for (int k = 0; k < 3; k++)
            {
                var kind = (JsonArray)course[k];
                if (kind.Count == 0) continue;
                h.Course[k] = new CourseRecord[kind.Count];
                for (int si = 0; si < kind.Count; si++)
                {
                    if (kind[si].IsNull) continue;
                    var c = (JsonObject)kind[si];
                    h.Course[k][si] = new CourseRecord { SchoolId = Int(c, "school_id"), RunnerId = Int(c, "runner_id"), TimeDs = Int(c, "time_ds"), Year = Int(c, "year") };
                }
            }
            JsonArray team = Arr(o, "team_records");
            for (int k = 0; k < 3; k++)
            {
                if (team[k].IsNull) continue;
                var t = (JsonObject)team[k];
                h.Team[k] = new TeamRecord { SchoolId = Int(t, "school_id"), TotalDs = Int(t, "total_ds"), Year = Int(t, "year") };
            }
            return h;
        }

        private static School RestoreSchool(JsonObject o)
        {
            var s = new School
            {
                Id = Int(o, "id"), Region = (Region)Int(o, "region"), Tier = (Tier)Int(o, "tier"),
                Prestige = Dbl(o, "prestige"), Hist = Dbl(o, "fame_hist"), Base = Dbl(o, "base_prestige"), CoachQ = Dbl(o, "coach_q"),
                IsPlayer = Bool(o, "is_player"), YearPoints = Int(o, "year_points"), PrevPoints = Int(o, "prev_points"), CumPoints = Dbl(o, "cum_points"),
            };
            JsonValue lr = o.Get("last_newyear_rank");
            s.LastNewYearRank = lr.IsNull ? (int?)null : (int)((JsonNumber)lr).Int;
            if (s.IsPlayer)
            {
                s.Funds = Dbl(o, "funds");
                s.ScoutLv = Int(o, "scout_lv");
                JsonObject f = Obj(o, "facilities");
                s.Fac = new FacilityLevels { Coach = Int(f, "coach"), Trainer = Int(f, "trainer"), Nutri = Int(f, "nutri"), Dorm = Int(f, "dorm"), Track = Int(f, "track"), Hill = Int(f, "hill"), Gym = Int(f, "gym"), Medical = Int(f, "medical"), Scout = Int(f, "scout") };
                foreach (JsonValue kv in Arr(o, "ob_by_region")) { var pair = (JsonArray)kv; s.Ob[(int)((JsonNumber)pair[0]).Int] = (int)((JsonNumber)pair[1]).Int; }
            }
            foreach (JsonValue rv in Arr(o, "roster")) s.Roster.Add(RestoreRunner((JsonObject)rv));
            return s;
        }

        private static WorldRunner RestoreRunner(JsonObject o)
        {
            var r = new WorldRunner
            {
                A0 = Dbl(o, "a0"), Gap = Dbl(o, "gap"), Prog = Dbl(o, "prog"), Growth = (GrowthKind)Int(o, "growth"), Profile = (Profile)Int(o, "profile"), Grade = Int(o, "grade"),
                Overseas = Bool(o, "overseas"), PrefectureId = Int(o, "pref"), Region = (Region)Int(o, "region"), CountryIndex = Int(o, "country"), HighSchoolU = Dbl(o, "hs_u"),
                LookSeed = (uint)Long(o, "look_seed"), SchoolId = Int(o, "school_id"), Ticket = Bool(o, "ticket"), Index = Int(o, "index"),
            };
            r.SetValues();
            return r;
        }

        private static PlayerAgent RestorePlayer(JsonObject o, WorldSim w)
        {
            var a = new PlayerAgent(w.ById(Int(o, "school_id")))
            {
                NextKind = Int(o, "next_kind"), TicketsEnabled = Bool(o, "tickets_enabled"), PowerBonus = Dbl(o, "power_bonus"),
            };
            JsonObject to = Obj(o, "tickets");
            TicketState t = a.Tickets;
            t.Stock = Int(to, "stock"); t.Bank = Int(to, "bank"); t.LastUsedYear = Int(to, "last_used_year"); t.InvitesDeclared = Int(to, "invites_declared");
            JsonValue pv = to.Get("pending");
            if (!pv.IsNull) { var p = (JsonObject)pv; t.Pending = new PendingInvite { Kind = (TicketKind)Int(p, "kind"), Pref = Int(p, "pref"), EnrollYear = Int(p, "enroll_year") }; }
            JsonValue an = to.Get("plan_anchor_year");
            t.PlanAnchorYear = an.IsNull ? (int?)null : (int)((JsonNumber)an).Int;
            foreach (JsonValue y in Arr(to, "plan_granted_years")) t.PlanGrantedYears.Add((int)((JsonNumber)y).Int);
            foreach (JsonValue m in Arr(to, "milestones")) t.Milestones.Add(((JsonString)m).Value);
            foreach (JsonValue e in Arr(to, "log")) { var eo = (JsonObject)e; t.Log.Add(new TicketLogEntry { Year = Int(eo, "year"), Delta = Int(eo, "delta"), Source = Str(eo, "source") }); }
            foreach (JsonValue sv in Arr(o, "stats")) a.Stats.Add(RestoreStats((JsonObject)sv));
            return a;
        }

        internal static PlayerYearStats RestoreStats(JsonObject o)
        {
            var s = new PlayerYearStats
            {
                Year = Int(o, "year"), P = Dbl(o, "p"), Prestige = Dbl(o, "prestige"), Funds = Dbl(o, "funds"), Lv = Dbl(o, "lv"), PowerRank = Int(o, "power_rank"), Points = Int(o, "points"),
                Top1 = Dbl(o, "top1"), CoachRank = Int(o, "coach_rank"), Cum = Dbl(o, "cum"),
            };
            var w = Arr(o, "wins");
            var p = Arr(o, "place");
            for (int i = 0; i < 3; i++) { s.Wins[i] = ((JsonBool)w[i]).Value; s.Place[i] = (int)((JsonNumber)p[i]).Int; }
            return s;
        }

        // ------------------------------------------------------------ 読み取りの補助(型が違えば例外 = 検証で FATAL にする側が拾う)
        private static JsonObject Obj(JsonObject o, string k) { return (JsonObject)o.Get(k); }
        private static JsonArray Arr(JsonObject o, string k) { return (JsonArray)o.Get(k); }
        private static string Str(JsonObject o, string k) { return ((JsonString)o.Get(k)).Value; }
        private static bool Bool(JsonObject o, string k) { return ((JsonBool)o.Get(k)).Value; }
        private static long Long(JsonObject o, string k) { return ((JsonNumber)o.Get(k)).Int; }
        private static int Int(JsonObject o, string k) { return checked((int)Long(o, k)); }
        private static double Dbl(JsonObject o, string k) { return ((JsonNumber)o.Get(k)).Value; }

        /// <summary>保存する double が Q6 で閉じているか(09 §5.3: 書式 → 読み戻しで必ず同じ値に戻る)。閉じていない値の名前を返す(なければ空)。</summary>
        public static List<string> NotQ6Closed(WorldSim w, PlayerAgent agent)
        {
            var bad = new List<string>();
            void Chk(string what, double x) { if (Q6.Round(x) != x) bad.Add(what + "=" + x.ToString("R", CultureInfo.InvariantCulture)); }
            foreach (School s in w.Schools)
            {
                Chk("school" + s.Id + ".prestige", s.Prestige); Chk("school" + s.Id + ".fame_hist", s.Hist); Chk("school" + s.Id + ".base_prestige", s.Base);
                Chk("school" + s.Id + ".coach_q", s.CoachQ); Chk("school" + s.Id + ".cum_points", s.CumPoints);
                if (s.IsPlayer) Chk("school" + s.Id + ".funds", s.Funds);
                foreach (WorldRunner r in s.Roster)
                {
                    Chk("runner.a0", r.A0); Chk("runner.gap", r.Gap); Chk("runner.prog", r.Prog); Chk("runner.hs_u", r.HighSchoolU);
                }
            }
            if (agent != null)
            {
                Chk("agent.power_bonus", agent.PowerBonus);
                foreach (PlayerYearStats s in agent.Stats) { Chk("stats.p", s.P); Chk("stats.prestige", s.Prestige); Chk("stats.funds", s.Funds); Chk("stats.lv", s.Lv); Chk("stats.top1", s.Top1); Chk("stats.cum", s.Cum); }
            }
            return bad;
        }
    }
}
