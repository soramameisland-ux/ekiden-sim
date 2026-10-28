using System;
using System.Collections.Generic;
using System.Globalization;
using Ekiden.Domain.Game;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;
using Ekiden.Domain.World;
using Ekiden.Persistence.Json;

namespace Ekiden.Persistence.Snapshot
{
    /// <summary>ゲーム本体の 1 回のプレイ(<see cref="GameSession"/>)を、保存の正規形(09 §5.3・§5.6)の JSON にして取り出し・復元する。
    /// 世界(<see cref="WorldSnapshot"/>。自校は施設・資金・累計ポイントだけを持つ学校として入る)+ 自校(<see cref="TeamSnapshot"/>)+ 進行の状態(<c>session</c>:
    /// 年度ごとの記録・今年度の優勝の集合・第三戦の週の戦力・招聘の種別の交互・年度の途中なら予選会と大会の結果)。取り出しは**週の境目**で行う。
    /// 年度の途中の出場権・他校の大会用の選手は保存せず、復元で <see cref="WorldSim.BeginYear"/> をやり直す(年度の途中では変わらない。09 §5.6)。</summary>
    public static class GameSnapshot
    {
        public const int SchemaVersion = 1;

        private static readonly string[] RaceNames = { "fall", "national", "newyear" };

        private static JsonNumber N(long v) { return new JsonNumber(v); }
        private static JsonNumber D(double v) { return new JsonNumber(v); }

        public static JsonObject Capture(GameSession gs)
        {
            GameSessionExtras ex = gs.CaptureExtras();
            var o = new JsonObject();
            o["schema_version"] = N(SchemaVersion);
            o["world"] = WorldSnapshot.Capture(gs.World, null, gs.Year);
            o["team"] = TeamSnapshot.Capture(gs.State);
            var s = new JsonObject();
            s["year_wins"] = N(ex.YearWins);
            s["race_power"] = D(ex.RacePower);
            s["next_ticket_kind"] = N(ex.NextTicketKind);
            s["year_settled"] = JsonBool.Of(ex.YearSettled);
            var scout = new JsonArray();
            foreach (var kv in ex.Scout) scout.Add(new JsonArray(new JsonValue[] { N(kv.Key), N((int)kv.Value) }));
            s["scout"] = scout;
            var guided = new JsonArray();
            foreach (int id in ex.Guided) guided.Add(N(id));
            s["guided"] = guided;
            var orders = new JsonArray();                                          // 提出された区間オーダー(第一〜第三戦。null = おまかせ)
            for (int i = 0; i < 3; i++) orders.Add(ex.Orders[i] == null ? (JsonValue)JsonNull.Instance : IntArray(ex.Orders[i]));
            s["orders"] = orders;
            s["qualifier_entries"] = ex.QualifierEntries == null ? (JsonValue)JsonNull.Instance : IntArray(ex.QualifierEntries);
            var stats = new JsonArray();
            foreach (PlayerYearStats st in ex.Stats) stats.Add(WorldSnapshot.CaptureStats(st));
            s["stats"] = stats;
            if (ex.YearInProgress == null) s["year_result"] = JsonNull.Instance;
            else
            {
                var yr = new JsonObject();
                yr["year"] = N(ex.YearInProgress.Year);
                var q = new JsonArray();
                foreach (int id in ex.YearInProgress.Qualified) q.Add(N(id));
                yr["qualified"] = q;
                var order = new JsonObject();                              // 終わった大会だけ(順位順の学校 ID)
                foreach (RaceKind k in WorldSim.AllRaces)
                {
                    List<int> ids;
                    if (!ex.YearInProgress.Order.TryGetValue(k, out ids)) continue;
                    var a = new JsonArray();
                    foreach (int id in ids) a.Add(N(id));
                    order[RaceNames[(int)k]] = a;
                }
                yr["order"] = order;
                s["year_result"] = yr;
            }
            o["session"] = s;
            return o;
        }

        public static GameSession Restore(JsonObject o)
        {
            if (Int(o, "schema_version") != SchemaVersion) throw new FormatException("schema_version");
            WorldSnapshot.Restored w = WorldSnapshot.Restore((JsonObject)o.Get("world"));
            TeamState team = TeamSnapshot.Restore((JsonObject)o.Get("team"));
            JsonObject s = (JsonObject)o.Get("session");
            var ex = new GameSessionExtras { YearWins = Int(s, "year_wins"), RacePower = ((JsonNumber)s.Get("race_power")).Value, NextTicketKind = Int(s, "next_ticket_kind") };
            ex.YearSettled = ((JsonBool)s.Get("year_settled")).Value;
            foreach (JsonValue sv in (JsonArray)s.Get("scout")) { var pair = (JsonArray)sv; ex.Scout[(int)((JsonNumber)pair[0]).Int] = (ScoutStage)(int)((JsonNumber)pair[1]).Int; }
            foreach (JsonValue gv in (JsonArray)s.Get("guided")) ex.Guided.Add((int)((JsonNumber)gv).Int);
            JsonArray ordersJ = (JsonArray)s.Get("orders");
            for (int i = 0; i < 3; i++) ex.Orders[i] = ordersJ[i].IsNull ? null : ReadInts((JsonArray)ordersJ[i]);
            JsonValue qe = s.Get("qualifier_entries");
            ex.QualifierEntries = qe.IsNull ? null : ReadInts((JsonArray)qe);
            foreach (JsonValue sv in (JsonArray)s.Get("stats")) ex.Stats.Add(WorldSnapshot.RestoreStats((JsonObject)sv));
            JsonValue yv = s.Get("year_result");
            if (!yv.IsNull)
            {
                var yo = (JsonObject)yv;
                var yr = new YearResult { Year = Int(yo, "year") };
                foreach (JsonValue id in (JsonArray)yo.Get("qualified")) yr.Qualified.Add((int)((JsonNumber)id).Int);
                JsonObject order = (JsonObject)yo.Get("order");
                foreach (RaceKind k in WorldSim.AllRaces)
                {
                    JsonValue av = order.Get(RaceNames[(int)k]);
                    if (av == null || av.IsNull) continue;
                    var ids = new List<int>();
                    foreach (JsonValue id in (JsonArray)av) ids.Add((int)((JsonNumber)id).Int);
                    yr.Order[k] = ids;
                }
                ex.YearInProgress = yr;
            }
            return GameSession.Restore(w.World, team, ex);
        }

        private static JsonArray IntArray(int[] v)
        {
            var a = new JsonArray();
            foreach (int x in v) a.Add(N(x));
            return a;
        }

        private static int[] ReadInts(JsonArray a)
        {
            var r = new int[a.Count];
            for (int i = 0; i < r.Length; i++) r[i] = (int)((JsonNumber)a[i]).Int;
            return r;
        }

        private static int Int(JsonObject o, string k) { return checked((int)((JsonNumber)o.Get(k)).Int); }

        /// <summary>保存する double が Q6 で閉じているか(09 D-10)。閉じていない値の名前を返す(なければ空)。</summary>
        public static List<string> NotQ6Closed(GameSession gs)
        {
            var bad = WorldSnapshot.NotQ6Closed(gs.World, null);
            bad.AddRange(TeamSnapshot.NotQ6Closed(gs.State));
            GameSessionExtras ex = gs.CaptureExtras();
            void Chk(string what, double x) { if (Q6.Round(x) != x) bad.Add(what + "=" + x.ToString("R", CultureInfo.InvariantCulture)); }
            Chk("session.race_power", ex.RacePower);
            foreach (PlayerYearStats s in ex.Stats) { Chk("stats.p", s.P); Chk("stats.prestige", s.Prestige); Chk("stats.funds", s.Funds); Chk("stats.lv", s.Lv); Chk("stats.top1", s.Top1); Chk("stats.cum", s.Cum); }
            return bad;
        }
    }
}
