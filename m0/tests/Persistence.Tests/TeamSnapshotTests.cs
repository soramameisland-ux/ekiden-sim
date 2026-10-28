using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Team;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.Training;
using Ekiden.Persistence.Container;
using Ekiden.Persistence.Json;
using Ekiden.Persistence.Migration;
using Ekiden.Persistence.Snapshot;
using Ekiden.Persistence.Validation;
using NUnit.Framework;

namespace Ekiden.Persistence.Tests
{
    /// <summary>自校の保存(09 §5.6)。取り出し → 文字列 → 読み込み → 復元 → 取り出しが一致し、検証器が修復なしで通し、『保存 → 読み込み → 続行』が『続行のみ』とバイト一致する。</summary>
    public class TeamSnapshotTests
    {
        private const ulong Seed = 20260201UL;
        private static readonly TeamSimDriver Driver = new TeamSimDriver();

        /// <summary>検証器が要る世界の側の最小の写し(自校の名簿と、他校の選手なし)を足して、保存全体の形にする。</summary>
        private static JsonObject WithWorldStub(JsonObject team)
        {
            var doc = (JsonObject)team.DeepClone();
            var school = new JsonObject();
            school["id"] = new JsonNumber(0L);
            school["is_player"] = JsonBool.True;
            school["roster"] = new JsonArray(((JsonObject)doc["runners"]).Keys.Select(k => (JsonValue)new JsonNumber(long.Parse(k))));
            doc["schools"] = new JsonArray(new JsonValue[] { school });
            doc["rival_runners"] = new JsonArray();
            return doc;
        }

        private static string Json(TeamState s) { return TeamSnapshot.Capture(s).ToJson(); }

        private static TeamState Reload(TeamState s, bool viaContainer)
        {
            JsonObject snap = TeamSnapshot.Capture(s);
            if (viaContainer)
            {
                byte[] b = Eksv.Encode(new HeaderBuilder { Kind = "auto", Slot = 1, LineageId = "3f2c9a6e-0000-4000-8000-000000000001", SaveCounter = s.AbsWeek + 1, AppVersion = "0.0.0", MasterHash = "00000000", SavedAt = 1800000000L + s.AbsWeek, Year = s.AbsWeek / 48 + 1 }.Build(), snap);
                JsonValue p;
                Eksv.Decode(b, out p);
                return TeamSnapshot.Restore((JsonObject)p);
            }
            return TeamSnapshot.Restore((JsonObject)JsonValue.Parse(snap.ToJson()));
        }

        [Test]
        public void RoundTrip_EveryFewWeeks_ForFiveYears_AndPassesTheValidatorUntouched()
        {
            TeamState s = Driver.NewGame(Seed);
            var seen = new HashSet<string>();
            for (int w = 0; w < 48 * 5; w++)
            {
                if (w % 3 == 0)
                {
                    JsonObject snap = TeamSnapshot.Capture(s);
                    string text = snap.ToJson();
                    TeamState back = TeamSnapshot.Restore((JsonObject)JsonValue.Parse(text));
                    Assert.AreEqual(text, Json(back), "週 " + w + ": 取り出し → 復元 → 取り出し");
                    JsonObject doc = WithWorldStub(snap);
                    ValidationResult v = SaveValidator.ValidateAndRepair(doc);
                    Assert.IsNull(v.Fatal, "週 " + w + ": " + v.Fatal);
                    CollectionAssert.IsEmpty(v.Fixes, "週 " + w + ": 修復が入った");
                    CollectionAssert.IsEmpty(SaveInvariants.Check(doc), "週 " + w + ": 不変条件");
                    CollectionAssert.IsEmpty(TeamSnapshot.NotQ6Closed(s), "週 " + w);
                    if (((JsonArray)((JsonObject)snap["event_state"])["log"]).Count > 0) seen.Add("event_log");
                    if (((JsonObject)((JsonObject)snap["event_state"])["fired"]).Count > 0) seen.Add("fired");
                    if (((JsonArray)((JsonObject)snap["event_state"])["mods"]).Count > 0) seen.Add("mods");
                    if (((JsonObject)((JsonObject)snap["event_state"])["pair_cd"]).Count > 0) seen.Add("pair_cd");
                    if (((JsonArray)snap["ob_records"]).Count > 0) seen.Add("ob");
                    if (((JsonObject)((JsonObject)snap["player_team"])["individual_menu"]).Count > 0) seen.Add("individual_menu");
                    if (!((JsonObject)snap["player_team"])["tickets"].IsNull && !((JsonObject)((JsonObject)snap["player_team"])["tickets"])["pending"].IsNull) seen.Add("pending");
                    if (((JsonArray)((JsonObject)snap["event_state"])["bench"]).Count > 0) seen.Add("bench");
                    if (((JsonArray)((JsonObject)snap["event_state"])["forced"]).Count > 0) seen.Add("forced");
                    if (((JsonArray)((JsonObject)snap["player_team"])["talked"]).Count > 0) seen.Add("talked");
                    if (!((JsonObject)snap["player_team"])["captain_id"].IsNull) seen.Add("captain");
                    foreach (string k in ((JsonObject)snap["runners"]).Keys)
                    {
                        var r = (JsonObject)((JsonObject)snap["runners"])[k];
                        if (((JsonArray)r["bonds"]).Count > 0) seen.Add("bonds");
                        if (((JsonObject)r["ctr"]).Count > 0) seen.Add("ctr");
                        if (((JsonArray)r["gifts"]).Count > 0) seen.Add("gifts");
                        if (((JsonArray)r["skills"]).Count > 0) seen.Add("skills");
                        if (((JsonString)r["recruit_source"]).Value == "Ticket") seen.Add("ticket_runner");
                        if (((JsonString)((JsonObject)r["injury"])["severity"]).Value != "None") seen.Add("injured");
                        if (!r["appearance_override"].IsNull) seen.Add("appearance_override");
                        if (((JsonString)((JsonObject)r["origin"])["origin_type"]).Value == "Overseas") seen.Add("overseas");
                        if (((JsonObject)((JsonObject)((JsonObject)snap["event_state"])["runner_ev"])[k])["once"] is JsonArray once && once.Count > 0) seen.Add("event_once");
                    }
                }
                Driver.Step(s);
            }
            foreach (string need in new[] { "event_log", "fired", "ob", "individual_menu", "captain", "bonds", "ctr", "skills", "ticket_runner", "injured", "pair_cd", "bench", "forced", "talked", "pending", "mods", "event_once", "appearance_override", "overseas" })
                Assert.Contains(need, seen.ToList(), "試験の入力がその機能を使っていない: " + need);
            TestContext.Out.WriteLine("使った機能: " + string.Join(", ", seen.OrderBy(x => x)));
        }

        [Test]
        public void SaveLoadContinue_EqualsContinueOnly_ForManyStartingWeeks()
        {
            const int total = 48 * 6;
            TeamState live = Driver.NewGame(Seed);
            var snapshots = new Dictionary<int, JsonObject>();
            for (int w = 0; w < total; w++)
            {
                if (w < 2 || w % 11 == 0 || w % 48 == 47 || w % 48 == 0 || w % 48 == 1) snapshots[w] = TeamSnapshot.Capture(live);
                Driver.Step(live);
            }
            string reference = Json(live);
            Assert.GreaterOrEqual(snapshots.Count, 40);
            foreach (var kv in snapshots)
            {
                TeamState t = TeamSnapshot.Restore((JsonObject)JsonValue.Parse(kv.Value.ToJson()));
                for (int w = kv.Key; w < total; w++) Driver.Step(t);
                Assert.AreEqual(reference, Json(t), "週 " + kv.Key + " で保存 → 読み込み → 続行");
            }
        }

        [Test]
        public void SaveLoadContinue_ViaContainer_AndReloadingEveryWeek()
        {
            const int total = 48 * 3 + 7;
            TeamState a = Driver.NewGame(Seed + 1);
            for (int w = 0; w < total; w++) Driver.Step(a);
            TeamState b = Driver.NewGame(Seed + 1);
            for (int w = 0; w < total; w++)
            {
                Driver.Step(b);
                b = Reload(b, viaContainer: w % 5 == 0);          // 毎週 読み込み直す(5 週に 1 度はコンテナ経由)
            }
            Assert.AreEqual(Json(a), Json(b));
        }

        [Test]
        public void NonDefaultValues_AllRoundTrip()
        {
            TeamState s = Driver.NewGame(Seed);
            for (int w = 0; w < 48 + 30; w++) Driver.Step(s);
            s.ManagerName = "山田監督";
            s.Scenario = new ScenarioRef { Id = "pack.koki", Difficulty = null, PlayerSchoolId = 0 };
            s.Cleared = new ClearInfo { Year = 9, NoInvite = true };
            s.NextRunnerId += 17; s.NextStaffId = 9;
            s.StaffNames[0] = "田中コーチ"; s.StaffNames[3] = "鈴木スカウト";
            s.Plan = new AnnualPlan { Focus = Ekiden.Domain.Screens.PlanFocus.Long, TargetRace = 0 };
            s.Plan.Peaking[0] = PeakingMode.Short; s.Plan.Peaking[2] = PeakingMode.None; s.Plan.Peaking[4] = PeakingMode.Standard;
            s.MonthlyPlan[0] = "th.speed"; s.MonthlyPlan[1] = "th.mountain"; s.MonthlyPlan[2] = "th.custom";
            s.CustomWeeks = new MenuSpec[3][];
            for (int g = 0; g < 3; g++) s.CustomWeeks[g] = Enumerable.Range(0, 4).Select(k => new MenuSpec("ks.core", "ks.pace_run", k % 2 == 0 ? "mil.low" : "mil.std")).ToArray();
            s.Settings = new TeamSettings { SpectateMode = "ResultOnly", AutoSafetyF = 75, EventAuto = true, PlanAuto = true, ScoutAuto = true, UniformPattern = 3, CheerSong = 2, BgmSet = 1 };
            s.Morale = 72.5; s.ScoutPts = 11.25; s.Prestige = 612.345678;
            s.Fac = new FacilityLevels { Dorm = 5, Track = 4, Hill = 3, Gym = 2, Medical = 1, Coach = 5, Trainer = 4, Nutri = 3, Scout = 2 };
            s.Tickets.PlanAnchorYear = 3; s.Tickets.PlanGrantedYears.Add(3); s.Tickets.PlanGrantedYears.Add(7);
            s.Tickets.Pending = new PendingInvite { Kind = TicketKind.Rookie, Pref = 27, EnrollYear = 3 };
            Runner r = s.Runners[3];
            r.Look = new Appearance { HairStyle = 9, HairColor = 11, Face = 4, Body = 2, SkinTone = 5 };
            r.LookOverride = new AppearanceOverride { HairStyle = 7, HairColor = 2 };
            r.Traits.Clear(); r.Traits.AddRange(new[] { "Serious", "Quiet", "Caring" });
            r.SharpenHistory = new[] { true, false, true };
            r.InjurySeverity = InjurySeverity.Moderate; r.InjuryWeeksLeft = 5; r.InjuryAge = 2;
            r.RelayWins = 4; r.SectionWins = 3; r.Races = 11; r.Brakes = 1;
            string text = Json(s);
            TeamState t = TeamSnapshot.Restore((JsonObject)JsonValue.Parse(text));
            Assert.AreEqual(text, Json(t));
            Assert.AreEqual("山田監督", t.ManagerName);
            Assert.IsNull(t.Scenario.Difficulty);
            Assert.AreEqual(9, t.Cleared.Year);
            Assert.IsTrue(t.Cleared.NoInvite);
            Assert.AreEqual(Ekiden.Domain.Screens.PlanFocus.Long, t.Plan.Focus);
            CollectionAssert.AreEqual(s.Plan.Peaking, t.Plan.Peaking);
            Assert.AreEqual(4, t.CustomWeeks[2].Length);
            Assert.AreEqual("mil.std", t.CustomWeeks[1][3].Mileage);
            Assert.AreEqual(612.345678, t.Prestige);
            Assert.AreEqual(27, t.Tickets.Pending.Pref);
            Runner u = t.FindRunner(r.Id);
            CollectionAssert.AreEqual(new[] { true, false, true }, u.SharpenHistory);
            Assert.AreEqual(2, u.InjuryAge);
            Assert.AreEqual(7, u.LookOverride.HairStyle);
            CollectionAssert.AreEqual(r.Traits, u.Traits);
            Assert.AreEqual("th.custom", t.MonthlyPlan[2]);
            Assert.AreEqual(5, t.Fac.Coach);
            Assert.AreEqual(2, t.Fac.Scout);
            // 検証器も通る(スタッフ・施設のレベルの範囲、券の整合など)
            JsonObject doc = WithWorldStub(TeamSnapshot.Capture(s));
            ValidationResult v = SaveValidator.ValidateAndRepair(doc);
            Assert.IsNull(v.Fatal);
            CollectionAssert.IsEmpty(v.Fixes.Where(f => !f.StartsWith("I-18", StringComparison.Ordinal) && !f.StartsWith("I-14", StringComparison.Ordinal)), string.Join(",", v.Fixes));
        }

        [Test]
        public void DifferentSeed_Differs()
        {
            TeamState a = Driver.NewGame(Seed), b = Driver.NewGame(Seed + 1);
            for (int w = 0; w < 60; w++) { Driver.Step(a); Driver.Step(b); }
            Assert.AreNotEqual(Json(a), Json(b));
        }

        [Test]
        public void Layout_KeyOrder_And_Clock()
        {
            TeamState s = Driver.NewGame(Seed);
            JsonObject o = TeamSnapshot.Capture(s);
            CollectionAssert.AreEqual(new[] { "schema_version", "scenario", "run", "clock", "rng", "counters", "player_team", "runners", "ob_records", "event_state", "settings" }, o.Keys);
            CollectionAssert.AreEqual(new[] { "funds", "facilities", "staff", "morale", "captain_id", "manager_name", "annual_plan", "monthly_plan", "custom_weeks", "guidance_points", "individual_menu", "talked",
                "scout_points", "coach_rank", "ob_network", "tickets", "prestige", "year_rank", "last_year_rank", "year_points", "school_prefecture_id", "entries" }, ((JsonObject)o["player_team"]).Keys);
            var r1 = (JsonObject)((JsonObject)o["runners"])["1"];
            CollectionAssert.AreEqual(new[] { "id", "family_name", "given_name", "family_kana", "given_kana", "origin", "school_id", "enroll_year", "grade", "growth_type", "stats", "potential", "xp", "skills", "fatigue", "form",
                "taper_weeks", "sharpen_bits", "motivation", "injury", "group", "bonds", "traits", "is_captain", "recruit_source", "ticket_kind", "career", "ctr", "monthly", "appearance", "appearance_override", "look_seed", "style", "gifts",
                "gift_revealed" }, r1.Keys);
            CollectionAssert.AreEqual(new[] { "fired", "year_counts", "runner_ev", "pair_cd", "mods", "bench", "forced", "log" }, ((JsonObject)o["event_state"]).Keys);
            CollectionAssert.AreEqual(new[] { "severity", "weeks_left", "age" }, ((JsonObject)r1["injury"]).Keys);
            Assert.AreEqual("20260201", ((JsonString)((JsonObject)o["rng"])["master_seed"]).Value.Substring(0, 0) + Seed.ToString().Substring(0, 8));
            Assert.IsInstanceOf<JsonString>(((JsonObject)o["rng"])["master_seed"], "uint64 は文字列(09 §5.3)");
            // 時計: 通算週 = (year−1)×48 + month_index×4 + (week−1)
            foreach (var (abs, y, m, w) in new[] { (0, 1, 0, 1), (3, 1, 0, 4), (4, 1, 1, 1), (47, 1, 11, 4), (48, 2, 0, 1), (48 * 9 + 25, 10, 6, 2) })
            {
                s.AbsWeek = abs;
                var c = (JsonObject)TeamSnapshot.Capture(s)["clock"];
                Assert.AreEqual(new long[] { y, m, w }, new[] { ((JsonNumber)c["year"]).Int, ((JsonNumber)c["month_index"]).Int, ((JsonNumber)c["week"]).Int }, "通算週 " + abs);
                Assert.AreEqual(abs, SaveValidator.AbsWeek(c));
                Assert.AreEqual(abs, TeamSnapshot.Restore(TeamSnapshot.Capture(s)).AbsWeek);
            }
        }

        [Test]
        public void DerivedValues_AreNotSaved()
        {
            TeamState s = Driver.NewGame(Seed);
            for (int w = 0; w < 30; w++) Driver.Step(s);
            string text = Json(s);
            foreach (string derived in new[] { "p_rank", "bond_avg", "regular", "gap_avg", "pb", "gm", "bond_k", "ind_mult", "far", "near", "race_flags", "week_levelups" })
                Assert.IsFalse(text.Contains("\"" + derived + "\""), derived + " は保存しない(派生値)");
            TeamState back = TeamSnapshot.Restore((JsonObject)JsonValue.Parse(text));
            foreach (Runner r in back.Runners)
            {
                Runner live = s.FindRunner(r.Id);
                Assert.AreEqual(live.Far, r.Far, "遠方は出身県と自校の所在県から作り直す: " + r.Id);
                Assert.AreEqual(live.Near, r.Near);
                Assert.AreEqual(live.BondK, r.BondK, 0);
                Assert.AreEqual(live.IndMult, r.IndMult, 0);
                Assert.AreEqual(live.Gm == null, r.Gm == null);
            }
        }

        [Test]
        public void Capture_RejectsStatesThatCannotBeSavedFaithfully()
        {
            TeamState s = Driver.NewGame(Seed);
            s.Runners[1].Id = s.Runners[0].Id;
            Assert.Throws<InvalidOperationException>(() => TeamSnapshot.Capture(s), "選手 ID の重複");
            s = Driver.NewGame(Seed);
            s.Bonds.Set(1, 99999, 100);
            Assert.Throws<InvalidOperationException>(() => TeamSnapshot.Capture(s), "絆の相手がいない");
            s = Driver.NewGame(Seed);
            s.Runners[0].EnrollWeek = 5;
            Assert.Throws<InvalidOperationException>(() => TeamSnapshot.Capture(s), "入学は年度の最初の週");
            s = Driver.NewGame(Seed);
            s.Runners[0].Captain = s.Runners[1].Captain = true;
            Assert.Throws<InvalidOperationException>(() => TeamSnapshot.Capture(s), "主将が 2 人");
        }

        [Test]
        public void Logs_AreCappedAtFifty()
        {
            TeamState s = Driver.NewGame(Seed);
            for (int i = 0; i < 80; i++) s.LogEvent("ev.x" + i, i, "a");
            Assert.AreEqual(EventConstants.K_EVENT_LOG_MAX, s.EventLog.Count);
            Assert.AreEqual("ev.x30", s.EventLog[0].Id);
            for (int i = 0; i < 70; i++) s.Tickets.Log.Add(new TicketLogEntry { Year = i, Delta = 1, Source = "Earned:x" + i });
            JsonObject o = TeamSnapshot.Capture(s);
            Assert.AreEqual(50, ((JsonArray)((JsonObject)((JsonObject)o["player_team"])["tickets"])["log"]).Count);
            Assert.AreEqual(50, ((JsonArray)((JsonObject)o["event_state"])["log"]).Count);
            Assert.AreEqual(SaveConstants.K_TICKET_LOG_MAX, 50);
            Assert.AreEqual(SaveConstants.K_EVENT_LOG_MAX, EventConstants.K_EVENT_LOG_MAX, "Domain の定数と 09 の定数が同じ");
        }

        [Test]
        public void Restore_ReadsWhatTheValidatorRepaired_AndTheResultNeedsNoFurtherRepair()
        {
            TeamState s = Driver.NewGame(Seed);
            for (int w = 0; w < 48 * 2 + 10; w++) Driver.Step(s);
            JsonObject doc = WithWorldStub(TeamSnapshot.Capture(s));
            var runners = (JsonObject)doc["runners"];
            var first = (JsonObject)runners[runners.Keys.First()];
            ((JsonArray)first["stats"])[0] = new JsonNumber(250L);                             // I-01
            ((JsonObject)doc["player_team"])["funds"] = new JsonNumber(-40L);                  // I-12
            ((JsonObject)doc["player_team"])["guidance_points"] = new JsonNumber(9L);          // I-20
            ((JsonObject)((JsonObject)doc["player_team"])["tickets"])["stock"] = new JsonNumber(5L);   // I-14
            ((JsonObject)first["injury"])["weeks_left"] = new JsonNumber(3L);                  // I-04(severity None なのに残り週)
            ((JsonArray)((JsonObject)doc["event_state"])["mods"]).Add(MakeMod("old", 0));       // I-19(切れた補正)
            ValidationResult v = SaveValidator.ValidateAndRepair(doc);
            Assert.IsNull(v.Fatal);
            Assert.GreaterOrEqual(v.Fixes.Count, 6, string.Join(",", v.Fixes));
            TeamState t = TeamSnapshot.Restore(v.Repaired);
            Assert.AreEqual(0, t.Funds);
            Assert.AreEqual(2, t.Tickets.Stock);
            JsonObject again = WithWorldStub(TeamSnapshot.Capture(t));
            ValidationResult v2 = SaveValidator.ValidateAndRepair(again);
            CollectionAssert.IsEmpty(v2.Fixes, "復元 → 取り出しの結果は、もう修復が要らない");
            CollectionAssert.IsEmpty(SaveInvariants.Check(again));
        }

        private static JsonObject MakeMod(string id, long until)
        {
            var m = new JsonObject();
            m["kind"] = new JsonString("E_mult"); m["to"] = new JsonString("team"); m["value"] = new JsonNumber(1.05); m["until"] = new JsonNumber(until); m["src"] = new JsonString(id);
            return m;
        }

        [Test]
        public void Restore_Tolerates_UndeterminedCareer_UnknownStyle_AndUsesCaptainId()
        {
            TeamState s = Driver.NewGame(Seed);
            for (int w = 0; w < 48 * 4 + 3; w++) Driver.Step(s);
            Assert.Greater(s.Obs.Count, 0);
            JsonObject doc = TeamSnapshot.Capture(s);
            ((JsonObject)((JsonArray)doc["ob_records"])[0])["career_path"] = JsonNull.Instance;
            var r = (JsonObject)((JsonObject)doc["runners"])[((JsonObject)doc["runners"]).Keys.First()];
            r["style"] = new JsonString("???");
            ((JsonObject)doc["player_team"])["captain_id"] = JsonNull.Instance;             // 検証が主将を外した
            TeamState t = TeamSnapshot.Restore(doc);
            Assert.AreEqual(CareerPath.Employment, t.Obs[0].CareerPath, "確定前(null)は一般就職として読む");
            Assert.AreEqual(Style.Neutral, t.FindRunner(((JsonNumber)r["id"]).Int is long id ? (int)id : 0).Style);
            Assert.IsFalse(t.Runners.Any(x => x.Captain), "captain_id が正。is_captain は写し");
        }

        [Test]
        public void Restore_RejectsForeignRng_AndBrokenShapes()
        {
            TeamState s = Driver.NewGame(Seed);
            JsonObject doc = TeamSnapshot.Capture(s);
            ((JsonObject)doc["rng"])["algorithm"] = new JsonString("mt19937");
            Assert.Throws<FormatException>(() => TeamSnapshot.Restore(doc));
            doc = TeamSnapshot.Capture(s);
            ((JsonObject)((JsonObject)doc["runners"])["1"])["growth_type"] = new JsonString("Weird");
            Assert.Throws<FormatException>(() => TeamSnapshot.Restore(doc));
        }

        [Test]
        public void ObRecords_AreBuiltFromGraduates_AndPowerIsLongPOfFinalStats()
        {
            TeamState s = Driver.NewGame(Seed);
            for (int w = 0; w < 48; w++) Driver.Step(s);
            Assert.AreEqual(10, s.Obs.Count, "1 年目の終わりに 4 年生 10 人が卒業");
            foreach (OBRecord o in s.Obs)
            {
                Assert.AreEqual(TeamState.LongP(o.FinalStats), o.Power, 0);
                Assert.AreEqual(Ekiden.Domain.Screens.DisplayRules.DerivePb(o.FinalStats, Ekiden.Domain.Screens.DisplayRules.PbEvent.Half), o.THalf);
                Assert.AreEqual(1, o.GradYear);
                Assert.AreEqual(-2, o.EnrollYear);
            }
            var net = ((JsonArray)((JsonObject)TeamSnapshot.Capture(s)["player_team"])["ob_network"]).Items.Select(x => (int)((JsonNumber)x).Int).ToArray();
            Assert.AreEqual(48, net.Length);
            Assert.AreEqual(10, net.Sum(), "I-16: ob_network の合計 = OB の件数");
        }

        [Test]
        public void MigrationOfASavedTeam_ChangesNothing()
        {
            TeamState s = Driver.NewGame(Seed);
            for (int w = 0; w < 100; w++) Driver.Step(s);
            JsonObject doc = TeamSnapshot.Capture(s);
            List<string> log;
            JsonObject migrated = SaveMigrator.Migrate(doc, out log);
            CollectionAssert.IsEmpty(log);
            Assert.IsTrue(doc.StructEquals(migrated), "v1 の保存は既定値の補完で変わらない");
        }
    }
}
