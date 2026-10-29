using System;
using System.Collections.Generic;
using System.Globalization;
using Calendar = Ekiden.Domain.Team.Calendar;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.Training;
using Ekiden.Domain.World;
using Ekiden.Persistence.Json;

namespace Ekiden.Persistence.Snapshot
{
    /// <summary>自校(育成の側)の状態を、保存の正規形(09 §5.3・§5.6)の JSON にして取り出し・復元する。
    /// 持つ鍵: <c>schema_version scenario run clock rng counters player_team runners ob_records event_state settings</c>。
    /// 世界の側(<c>schools rival_runners candidates history high_schools</c>)は持たない(世界との合成は M3)。検証器(<see cref="Validation.SaveValidator"/>)に通すときは、呼ぶ側が <c>schools</c> と <c>rival_runners</c> を足す。
    /// 取り出しは**週の境目**(1 週の処理が終わり、次の週の <see cref="TeamState.Refresh"/> の前)で行う。派生値(力の順位・レギュラー・絆の平均・素質との差・遠方/近郊・素養の乗数)は保存せず、復元では作り直さない
    /// (次の週の最初に <c>Refresh</c> が作り直す。遠方/近郊と素養の乗数だけは復元で作る)。1 週の途中にだけ意味を持つ値(<see cref="Runner.RaceFlags"/>)は保存しない。</summary>
    public static class TeamSnapshot
    {
        public const int SchemaVersion = 1;
        public const string RngAlgorithm = "pcg32-keyed-1";

        private static readonly string[] ModNames = { "E_mult", "r_add", "inj_mult", "form_add", "funds_year", "scout_pts" };
        private static readonly string[] Growth = { "Early", "Normal", "Late" };
        private static readonly string[] Severity = { "None", "Minor", "Moderate", "Severe" };
        private static readonly string[] GroupNames = { "A", "B", "C" };
        private static readonly string[] RaceNames = { "Fall", "National", "NewYear" };
        private static readonly string[] PeakingKeys = { "Fall", "National", "NewYear", "Qualifier", "Meet" };
        private static readonly string[] PeakingNames = { "Standard", "Short", "None" };
        private static readonly string[] FocusNames = { "Balanced", "Speed", "Long" };
        private static readonly string[] CareerNames = { "Corporate", "Teacher", "Employment", "GradSchool" };
        private static readonly string[] RecruitNames = { "Normal", "Ticket" };
        private static readonly string[] RoleNames = { "None", "Coach", "ScoutHelper", "Sponsor" };
        private static readonly string[] NotifyNames = { "Injury", "Warning", "Event", "Ticket", "Schedule", "Entitlement", "AutosaveFailed" };
        private static readonly string[] GiftNames = { "Climber", "Descender", "Ace" };
        private static readonly string[] TicketKindNames = { "Intl", "Rookie" };

        // ------------------------------------------------------------ 取り出し
        private static JsonNumber N(long v) { return new JsonNumber(v); }
        private static JsonNumber D(double v) { return new JsonNumber(v); }
        private static JsonString S(string v) { return new JsonString(v); }
        private static JsonBool B(bool v) { return JsonBool.Of(v); }
        private static JsonValue Null { get { return JsonNull.Instance; } }
        private static JsonArray Ints(IEnumerable<int> v) { return new JsonArray(v.Select(x => (JsonValue)N(x))); }

        private static int[] IntArr(JsonArray a) { var r = new int[a.Count]; for (int i = 0; i < r.Length; i++) r[i] = checked((int)((JsonNumber)a[i]).Int); return r; }
        private static JsonArray Strs(IEnumerable<string> v) { return new JsonArray(v.Select(x => (JsonValue)S(x))); }

        private static JsonObject O(params (string, JsonValue)[] kv)
        {
            var o = new JsonObject();
            foreach (var (k, v) in kv) o[k] = v;
            return o;
        }

        public static JsonObject Capture(TeamState s)
        {
            var ids = new HashSet<int>(s.Runners.Select(r => r.Id));
            if (ids.Count != s.Runners.Count) throw new InvalidOperationException("選手 ID が重複している(I-11)");
            var o = new JsonObject();
            o["schema_version"] = N(SchemaVersion);
            o["scenario"] = O(("id", S(s.Scenario.Id)), ("difficulty", s.Scenario.Difficulty == null ? Null : S(s.Scenario.Difficulty)), ("player_school_id", N(s.Scenario.PlayerSchoolId)));
            o["run"] = O(("invites_declared", N(s.Tickets.InvitesDeclared)), ("cleared", s.Cleared == null ? Null : O(("year", N(s.Cleared.Year)), ("no_invite", B(s.Cleared.NoInvite)))));
            int yw = Calendar.FloorMod(s.AbsWeek, Calendar.WeeksPerYear);
            o["clock"] = O(("year", N(Calendar.FloorDiv(s.AbsWeek, Calendar.WeeksPerYear) + 1)), ("month_index", N(yw / 4)), ("week", N(yw % 4 + 1)));
            o["rng"] = O(("algorithm", S(RngAlgorithm)), ("master_seed", S(s.Seed.ToString(CultureInfo.InvariantCulture))));
            o["counters"] = O(("next_runner_id", N(s.NextRunnerId)), ("next_staff_id", N(s.NextStaffId)));
            o["player_team"] = CapturePlayerTeam(s);
            // 絆は ID の小さい側の選手に持たせる(I-08)
            var bonds = new Dictionary<int, List<int[]>>();
            foreach (int[] b in s.Bonds.Items())
            {
                if (!ids.Contains(b[0]) || !ids.Contains(b[1])) throw new InvalidOperationException("絆の相手が名簿にいない: " + b[0] + "-" + b[1]);
                List<int[]> l;
                if (!bonds.TryGetValue(b[0], out l)) bonds[b[0]] = l = new List<int[]>();
                l.Add(b);
            }
            var runners = new JsonObject();
            foreach (Runner r in s.Runners.OrderBy(x => x.Id))
            {
                List<int[]> mine;
                bonds.TryGetValue(r.Id, out mine);
                runners[r.Id.ToString(CultureInfo.InvariantCulture)] = CaptureRunner(r, mine, s);
            }
            o["runners"] = runners;
            o["ob_records"] = new JsonArray(s.Obs.Select(b => CaptureOb(b, s.AbsWeek)));
            o["event_state"] = CaptureEventState(s);
            o["settings"] = CaptureSettings(s.Settings);
            return o;
        }

        private static JsonObject Appearance(Appearance a)
        {
            return O(("hair_style", N(a.HairStyle)), ("hair_color", N(a.HairColor)), ("face", N(a.Face)), ("body", N(a.Body)), ("skin_tone", N(a.SkinTone)));
        }

        private static JsonObject CaptureRunner(Runner r, List<int[]> bonds, TeamState s)
        {
            if (r.EnrollWeek % Calendar.WeeksPerYear != 0) throw new InvalidOperationException("入学は年度の最初の週(選手 " + r.Id + " の入学週 " + r.EnrollWeek + ")");
            JsonObject origin = r.Overseas
                ? O(("origin_type", S("Overseas")), ("prefecture_id", Null), ("high_school_id", Null), ("country_id", N(r.Country)))
                : O(("origin_type", S("Japan")), ("prefecture_id", N(r.Pref)), ("high_school_id", N(r.HighSchool)), ("country_id", Null));
            JsonValue lo = Null;
            if (r.LookOverride != null)
            {
                var ov = new JsonObject();
                if (r.LookOverride.HairStyle.HasValue) ov["hair_style"] = N(r.LookOverride.HairStyle.Value);
                if (r.LookOverride.HairColor.HasValue) ov["hair_color"] = N(r.LookOverride.HairColor.Value);
                lo = ov;
            }
            int sharpen = (r.SharpenHistory[0] ? 1 : 0) | (r.SharpenHistory[1] ? 2 : 0) | (r.SharpenHistory[2] ? 4 : 0);
            return O(
                ("id", N(r.Id)), ("family_name", S(r.FamilyName)), ("given_name", S(r.GivenName)), ("family_kana", S(r.FamilyKana)), ("given_kana", S(r.GivenKana)),
                ("origin", origin), ("school_id", N(s.Scenario.PlayerSchoolId)), ("enroll_year", N(Calendar.FloorDiv(r.EnrollWeek, Calendar.WeeksPerYear) + 1)), ("grade", N(r.Grade)),
                ("growth_type", S(Growth[(int)r.GrowthType])), ("stats", Ints(r.Stats)), ("potential", Ints(r.Potential)), ("xp", Ints(r.Xp)),
                ("skills", Strs(r.Skills.OrderBy(x => x, StringComparer.Ordinal))), ("fatigue", N(r.Fatigue)), ("form", N(r.Form)), ("taper_weeks", N(r.TaperWeeks)), ("sharpen_bits", N(sharpen)),
                ("motivation", N(r.Motivation)),
                ("injury", O(("severity", S(Severity[(int)r.InjurySeverity])), ("weeks_left", N(r.InjuryWeeksLeft)), ("age", N(r.InjuryAge)))),
                ("group", S(GroupNames[r.Group])),
                ("bonds", new JsonArray((bonds ?? new List<int[]>()).Select(b => (JsonValue)new JsonArray(new JsonValue[] { N(b[1]), N(b[2]) })))),
                ("traits", Strs(r.Traits)), ("is_captain", B(r.Captain)), ("recruit_source", S(RecruitNames[(int)r.RecruitSource])),
                ("ticket_kind", r.RecruitSource == RecruitSource.Ticket ? (JsonValue)S(TicketKindNames[(int)r.TicketKind]) : Null),
                ("career", O(("races", N(r.Races)), ("section_wins", N(r.SectionWins)), ("relay_wins", N(r.RelayWins)), ("brakes", N(r.Brakes)), ("records", N(r.Records)))),
                ("ctr", ObjSorted(r.Counters)),
                ("monthly", O(("gain", N(r.MonthGain)), ("high_fatigue_weeks", N(r.HighFatigueWeeks)), ("overload_pending", B(r.OverloadPending)), ("camp_max_fatigue", N(r.CampMaxFatigue)),
                                       ("stats_start", r.MonthStartStats == null ? Null : (JsonValue)Ints(r.MonthStartStats)), ("stats_prev", r.PrevMonthStats == null ? Null : (JsonValue)Ints(r.PrevMonthStats)))),
                ("appearance", Appearance(r.Look)), ("appearance_override", lo), ("look_seed", N(r.LookSeed)),
                ("style", S((r.Style ?? Style.Neutral).Code)), ("gifts", Strs(r.Gifts.Select(g => GiftNames[(int)g]))), ("gift_revealed", B(r.GiftRevealed)));
        }

        private static JsonObject ObjSorted(IDictionary<string, int> d)
        {
            var o = new JsonObject();
            foreach (string k in d.Keys.OrderBy(x => x, StringComparer.Ordinal)) o[k] = N(d[k]);
            return o;
        }

        private static JsonArray MenuTriple(MenuSpec m) { return Strs(new[] { m.Key1, m.Key2, m.Mileage }); }

        private static JsonObject CapturePlayerTeam(TeamState s)
        {
            FacilityLevels f = s.Fac;
            int[] staffLv = { f.Coach, f.Trainer, f.Nutri, f.Scout };
            var staff = new JsonArray();
            for (int i = 0; i < StaffRoles.Always; i++)
            {
                int lv = staffLv[i];
                if (lv < 1 || lv > WorldK.ExpStaff.Length) throw new InvalidOperationException("スタッフのレベル " + lv);
                staff.Add(O(("id", N(i + 1)), ("role", S(StaffRoles.Names[i])), ("name", S(s.StaffNames[i])), ("level", N(lv)), ("salary", N((long)WorldK.ExpStaff[lv - 1]))));
            }
            if (f.Manager < 0 || f.Manager > WorldK.ExpStaff.Length) throw new InvalidOperationException("主務のレベル " + f.Manager);
            if (f.Manager >= 1)                                                           // 主務(L-70 ②)は置いたときだけ
                staff.Add(O(("id", N(StaffRoles.Always + 1)), ("role", S(StaffRoles.Names[StaffRoles.Always])), ("name", S(s.StaffNames[StaffRoles.Always])), ("level", N(f.Manager)), ("salary", N((long)WorldK.ExpStaff[f.Manager - 1]))));
            int captain = 0;
            foreach (Runner r in s.Runners) if (r.Captain) { if (captain != 0) throw new InvalidOperationException("主将が複数いる"); captain = r.Id; }
            var peaking = new JsonObject();
            for (int i = 0; i < AnnualPlan.PeakingKinds; i++) peaking[PeakingKeys[i]] = S(PeakingNames[(int)s.Plan.Peaking[i]]);
            JsonValue custom = Null;
            if (s.CustomWeeks != null)
            {
                var c = new JsonObject();
                for (int g = 0; g < 3; g++) c[GroupNames[g]] = new JsonArray(s.CustomWeeks[g].Select(m => (JsonValue)MenuTriple(m)));
                custom = c;
            }
            var menus = new JsonObject();
            foreach (var kv in s.IndividualMenus) menus[kv.Key.ToString(CultureInfo.InvariantCulture)] = O(("until_abs_week", N(kv.Value.UntilAbsWeek)), ("menu", MenuTriple(kv.Value.Menu)));
            var net = new int[48];
            foreach (OBRecord ob in s.Obs) net[ob.Pref]++;
            JsonObject facilities = O(("Dorm", N(f.Dorm)), ("Track", N(f.Track)), ("Hill", N(f.Hill)), ("Gym", N(f.Gym)), ("Medical", N(f.Medical)));
            if (f.Altitude > 0) facilities["Altitude"] = N(f.Altitude);                  // 新設の施設(L-70 ②)は置いたときだけ
            if (f.Mental > 0) facilities["Mental"] = N(f.Mental);
            if (f.Camp > 0) facilities["Camp"] = N(f.Camp);
            return O(
                ("funds", N(s.Funds)),
                ("facilities", facilities),
                ("staff", staff), ("morale", D(s.Morale)), ("captain_id", captain == 0 ? Null : N(captain)), ("manager_name", S(s.ManagerName)),
                ("annual_plan", O(("focus", S(FocusNames[(int)s.Plan.Focus])), ("target_race", S(RaceNames[s.Plan.TargetRace])), ("peaking_mode", peaking))),
                ("monthly_plan", O(("A", S(s.MonthlyPlan[0])), ("B", S(s.MonthlyPlan[1])), ("C", S(s.MonthlyPlan[2])))), ("custom_weeks", custom),
                ("guidance_points", N(s.GuidanceLeft)), ("individual_menu", menus), ("talked", Ints(s.Talked)), ("scout_points", D(s.ScoutPts)), ("coach_rank", N(s.CoachRank)),
                ("ob_network", Ints(net)), ("tickets", CaptureTickets(s.Tickets)),
                ("prestige", D(s.Prestige)), ("year_rank", Ints(s.Rank)), ("last_year_rank", Ints(s.LastRank)), ("year_points", N(s.YearPoints)), ("school_prefecture_id", N(s.SchoolPref)),
                ("entries", O(("Fall", Ints(s.Entries[0])), ("National", Ints(s.Entries[1])), ("NewYear", Ints(s.Entries[2])))),
                ("notifications", new JsonArray(s.Notifications.Select(n => (JsonValue)O(("kind", S(NotifyNames[(int)n.Kind])), ("abs_week", N(n.AbsWeek)), ("code", S(n.Code)), ("runner_id", N(n.RunnerId)), ("n", N(n.N)), ("unread", B(n.Unread)))))));
        }

        private static JsonObject CaptureTickets(TicketState t)
        {
            JsonValue pending = Null;
            if (t.Pending != null)
                pending = O(("kind", S(TicketKindNames[(int)t.Pending.Kind])), ("enroll_year", N(t.Pending.EnrollYear)), ("prefecture_id", t.Pending.Pref == 0 ? Null : N(t.Pending.Pref)));
            var log = t.Log.Skip(Math.Max(0, t.Log.Count - SaveConstants.K_TICKET_LOG_MAX));
            return O(("stock", N(t.Stock)), ("bank", N(t.Bank)), ("last_used_year", N(t.LastUsedYear)), ("plan_anchor_year", t.PlanAnchorYear.HasValue ? (JsonValue)N(t.PlanAnchorYear.Value) : Null),
                ("plan_granted_years", Ints(t.PlanGrantedYears)), ("milestones", Strs(t.Milestones)), ("pending", pending),
                ("log", new JsonArray(log.Select(e => (JsonValue)O(("year", N(e.Year)), ("delta", N(e.Delta)), ("source", S(e.Source)))))));
        }

        private static JsonObject CaptureOb(OBRecord b, int absWeek)
        {
            ObRole role = b.Role != ObRole.None && b.Until <= absWeek ? ObRole.None : b.Role;       // 期限の切れた役割は書かない(ExpireTimed と同じ。I-19)
            return O(("runner_id", N(b.RunnerId)), ("name", S(b.Name)), ("kana", S(b.Kana)), ("origin_index", N(b.Pref)), ("origin_country", N(b.Country)), ("high_school_name", S(b.HighSchoolName)), ("school_id", N(b.SchoolId)),
                ("enroll_year", N(b.EnrollYear)), ("grad_year", N(b.GradYear)), ("final_stats", Ints(b.FinalStats)), ("skills", Strs(b.Skills)),
                ("pb", O(("t5000", N(b.T5000)), ("t10000", N(b.T10000)), ("t_half", N(b.THalf)))),
                ("career", O(("race_wins", N(b.RaceWins)), ("section_wins", N(b.SectionWins)), ("records", N(b.Records)))),
                ("career_path", S(CareerNames[(int)b.CareerPath])), ("hall_of_fame", B(b.HallOfFame)), ("role", S(RoleNames[(int)role])), ("role_until", N(b.Until)),
                ("recruit_source", S(RecruitNames[(int)b.RecruitSource])), ("appearance", Appearance(b.Look)), ("style", S((b.Style ?? Style.Neutral).Code)), ("gifts", Strs(b.Gifts.Select(g => GiftNames[(int)g]))));
        }

        private static JsonObject CaptureEventState(TeamState s)
        {
            var fired = new JsonObject();
            foreach (string k in s.Fired.Keys.OrderBy(x => x, StringComparer.Ordinal)) fired[k] = O(("count", N(s.Fired[k].Count)), ("last_week", N(s.Fired[k].Last)));
            var yc = new JsonObject();
            foreach (string k in s.YearCounts.Keys.Concat(new[] { "_total" }).OrderBy(x => x, StringComparer.Ordinal)) yc[k] = N(k == "_total" ? s.YearTotal : s.YearCounts[k]);
            var rev = new JsonObject();
            foreach (Runner r in s.Runners.OrderBy(x => x.Id))
                rev[r.Id.ToString(CultureInfo.InvariantCulture)] = O(("last_month", N(r.LastEventMonth)), ("once", Strs(r.EventOnce.OrderBy(x => x, StringComparer.Ordinal))), ("cooldown", ObjSorted(r.EventCooldown)));
            var pairs = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var ev in s.PairCooldown)
                foreach (var kv in ev.Value) pairs[ev.Key + "|" + (kv.Key >> 32).ToString(CultureInfo.InvariantCulture) + "|" + (kv.Key & 0xFFFFFFFFL).ToString(CultureInfo.InvariantCulture)] = kv.Value;
            var pc = new JsonObject();
            foreach (var kv in pairs) pc[kv.Key] = N(kv.Value);
            // 期限の切れた補正は書かない(次の週の最初の ExpireTimed が消すものと同じ。I-19)
            var mods = new JsonArray(s.Mods.Where(m => m.Until > s.AbsWeek).Select(m => (JsonValue)O(("kind", S(ModNames[(int)m.Kind])), ("to", m.To == TimedModifier.TeamTarget ? (JsonValue)S("team") : N(m.To)), ("value", D(m.Value)), ("until", N(m.Until)), ("src", S(m.Src)))));
            var log = s.EventLog.Skip(Math.Max(0, s.EventLog.Count - EventConstants.K_EVENT_LOG_MAX));
            return O(("fired", fired), ("year_counts", yc), ("runner_ev", rev), ("pair_cd", pc), ("mods", mods), ("bench", Ints(s.Bench.OrderBy(x => x))), ("forced", Ints(s.Forced.OrderBy(x => x))),
                ("log", new JsonArray(log.Select(e => (JsonValue)O(("id", S(e.Id)), ("week", N(e.Week)), ("choice", S(e.Choice)))))));
        }

        private static JsonObject CaptureSettings(TeamSettings t)
        {
            return O(("spectate_mode", S(t.SpectateMode)), ("auto_safety_f", N(t.AutoSafetyF)), ("event_auto", B(t.EventAuto)), ("plan_auto", B(t.PlanAuto)), ("scout_auto", B(t.ScoutAuto)),
                ("school_look", O(("uniform_pattern", N(t.UniformPattern)), ("cheer_song", N(t.CheerSong)), ("bgm_set", N(t.BgmSet)))));
        }

        // ------------------------------------------------------------ 復元
        public static TeamState Restore(JsonObject o)
        {
            if (Int(o, "schema_version") != SchemaVersion) throw new FormatException("schema_version");
            JsonObject rng = Obj(o, "rng");
            if (Str(rng, "algorithm") != RngAlgorithm) throw new FormatException("rng.algorithm");
            var s = new TeamState { Seed = ulong.Parse(Str(rng, "master_seed"), CultureInfo.InvariantCulture) };
            JsonObject sc = Obj(o, "scenario");
            s.Scenario = new ScenarioRef { Id = Str(sc, "id"), Difficulty = o_NullStr(sc, "difficulty"), PlayerSchoolId = Int(sc, "player_school_id") };
            JsonObject run = Obj(o, "run");
            s.Tickets.InvitesDeclared = Int(run, "invites_declared");
            JsonValue cl = run.Get("cleared");
            if (!cl.IsNull) s.Cleared = new ClearInfo { Year = Int((JsonObject)cl, "year"), NoInvite = Bool((JsonObject)cl, "no_invite") };
            JsonObject clock = Obj(o, "clock");
            s.AbsWeek = (Int(clock, "year") - 1) * Calendar.WeeksPerYear + Int(clock, "month_index") * 4 + (Int(clock, "week") - 1);
            JsonObject cn = Obj(o, "counters");
            s.NextRunnerId = Int(cn, "next_runner_id");
            s.NextStaffId = Int(cn, "next_staff_id");
            RestorePlayerTeam(s, Obj(o, "player_team"));
            JsonObject runners = Obj(o, "runners");
            foreach (string k in runners.Keys) s.Runners.Add(RestoreRunner((JsonObject)runners[k], s));
            s.Runners.Sort((a, b) => a.Id.CompareTo(b.Id));
            JsonValue captain = Obj(o, "player_team").Get("captain_id");         // 主将は captain_id が正。is_captain は各選手の写し(検証の修復は captain_id だけを直す)
            int captainId = captain.IsNull ? 0 : (int)((JsonNumber)captain).Int;
            foreach (Runner r in s.Runners) r.Captain = r.Id == captainId;
            foreach (Runner r in s.Runners)
                foreach (JsonValue bv in Arr((JsonObject)runners[r.Id.ToString(CultureInfo.InvariantCulture)], "bonds"))
                {
                    var b = (JsonArray)bv;
                    s.Bonds.Set(r.Id, (int)((JsonNumber)b[0]).Int, (int)((JsonNumber)b[1]).Int);
                }
            foreach (JsonValue ov in Arr(o, "ob_records")) s.Obs.Add(RestoreOb((JsonObject)ov));
            RestoreEventState(s, Obj(o, "event_state"), runners);
            RestoreSettings(s.Settings, Obj(o, "settings"));
            return s;
        }

        private static string o_NullStr(JsonObject o, string k) { JsonValue v = o.Get(k); return v.IsNull ? null : ((JsonString)v).Value; }

        private static int Index(string[] names, string v)
        {
            int i = Array.IndexOf(names, v);
            if (i < 0) throw new FormatException("未知の値: " + v);
            return i;
        }

        private static Appearance RestoreAppearance(JsonObject a)
        {
            return new Appearance { HairStyle = Int(a, "hair_style"), HairColor = Int(a, "hair_color"), Face = Int(a, "face"), Body = Int(a, "body"), SkinTone = Int(a, "skin_tone") };
        }

        private static Runner RestoreRunner(JsonObject j, TeamState s)
        {
            var r = new Runner
            {
                Id = Int(j, "id"), FamilyName = Str(j, "family_name"), GivenName = Str(j, "given_name"), FamilyKana = Str(j, "family_kana"), GivenKana = Str(j, "given_kana"),
                Grade = Int(j, "grade"), EnrollWeek = (Int(j, "enroll_year") - 1) * Calendar.WeeksPerYear, GrowthType = (GrowthType)Index(Growth, Str(j, "growth_type")),
                Fatigue = Int(j, "fatigue"), Form = Int(j, "form"), TaperWeeks = Int(j, "taper_weeks"), Motivation = Int(j, "motivation"),
                Group = Index(GroupNames, Str(j, "group")), Captain = Bool(j, "is_captain"), RecruitSource = (RecruitSource)Index(RecruitNames, Str(j, "recruit_source")),
                Races = Int(Obj(j, "career"), "races"), SectionWins = Int(Obj(j, "career"), "section_wins"), RelayWins = Int(Obj(j, "career"), "relay_wins"), Brakes = Int(Obj(j, "career"), "brakes"),
                Records = Obj(j, "career").ContainsKey("records") ? Int(Obj(j, "career"), "records") : 0,        // 追加の項目(ないセーブは 0。09 §6)
                Look = RestoreAppearance(Obj(j, "appearance")), LookSeed = (uint)Long(j, "look_seed"), GiftRevealed = Bool(j, "gift_revealed"),
            };
            JsonObject og = Obj(j, "origin");
            if (Str(og, "origin_type") == "Overseas") { r.Overseas = true; r.Country = Int(og, "country_id"); }
            else { r.Pref = Int(og, "prefecture_id"); r.HighSchool = Int(og, "high_school_id"); }
            JsonArray stats = Arr(j, "stats"), pot = Arr(j, "potential"), xp = Arr(j, "xp");
            for (int i = 0; i < StatInfo.Count; i++)
            {
                r.Stats[i] = (int)((JsonNumber)stats[i]).Int;
                r.Potential[i] = (int)((JsonNumber)pot[i]).Int;
                r.Xp[i] = (int)((JsonNumber)xp[i]).Int;
            }
            foreach (JsonValue x in Arr(j, "skills")) r.Skills.Add(((JsonString)x).Value);
            int bits = Int(j, "sharpen_bits");
            r.SharpenHistory = new[] { (bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0 };
            JsonObject inj = Obj(j, "injury");
            r.InjurySeverity = (InjurySeverity)Index(Severity, Str(inj, "severity"));
            r.InjuryWeeksLeft = Int(inj, "weeks_left");
            r.InjuryAge = Int(inj, "age");
            foreach (JsonValue x in Arr(j, "traits")) r.Traits.Add(((JsonString)x).Value);
            JsonValue tk = j.Get("ticket_kind");
            if (!tk.IsNull) r.TicketKind = (TicketKind)Index(TicketKindNames, ((JsonString)tk).Value);
            JsonObject ctr = Obj(j, "ctr");
            foreach (string k in ctr.Keys) r.Counters[k] = (int)((JsonNumber)ctr[k]).Int;
            JsonObject mo = Obj(j, "monthly");
            r.MonthGain = Int(mo, "gain"); r.HighFatigueWeeks = Int(mo, "high_fatigue_weeks"); r.OverloadPending = Bool(mo, "overload_pending"); r.CampMaxFatigue = Int(mo, "camp_max_fatigue");
            if (mo.ContainsKey("stats_start") && !mo.Get("stats_start").IsNull) r.MonthStartStats = IntArr(Arr(mo, "stats_start"));      // 追加の項目(ないセーブ・null は『まだ月を送っていない』。09 §6)
            if (mo.ContainsKey("stats_prev") && !mo.Get("stats_prev").IsNull) r.PrevMonthStats = IntArr(Arr(mo, "stats_prev"));
            JsonValue lo = j.Get("appearance_override");
            if (!lo.IsNull)
            {
                var ov = (JsonObject)lo;
                r.LookOverride = new AppearanceOverride { HairStyle = ov.ContainsKey("hair_style") ? Int(ov, "hair_style") : (int?)null, HairColor = ov.ContainsKey("hair_color") ? Int(ov, "hair_color") : (int?)null };
            }
            Style style;
            if (!Style.TryParse(Str(j, "style"), out style)) style = Style.Neutral;
            var gifts = new List<Gift>();
            foreach (JsonValue x in Arr(j, "gifts")) gifts.Add((Gift)Index(GiftNames, ((JsonString)x).Value));
            r.SetIndividuality(style, gifts);
            r.DeriveDistance(s.SchoolPref);
            return r;
        }

        private static MenuSpec RestoreMenu(JsonValue v)
        {
            var a = (JsonArray)v;
            return new MenuSpec(((JsonString)a[0]).Value, ((JsonString)a[1]).Value, ((JsonString)a[2]).Value);
        }

        private static void RestorePlayerTeam(TeamState s, JsonObject pt)
        {
            s.Funds = Int(pt, "funds");
            JsonObject f = Obj(pt, "facilities");
            JsonArray staff = Arr(pt, "staff");
            var fac = new FacilityLevels { Dorm = Int(f, "Dorm"), Track = Int(f, "Track"), Hill = Int(f, "Hill"), Gym = Int(f, "Gym"), Medical = Int(f, "Medical") };
            if (f.ContainsKey("Altitude")) fac.Altitude = Int(f, "Altitude");           // 新設の施設(L-70 ②)。なければ 0 = 未設置
            if (f.ContainsKey("Mental")) fac.Mental = Int(f, "Mental");
            if (f.ContainsKey("Camp")) fac.Camp = Int(f, "Camp");
            foreach (JsonValue sv in staff)
            {
                var st = (JsonObject)sv;
                int role = Index(StaffRoles.Names, Str(st, "role"));
                int lv = Int(st, "level");
                if (role == 0) fac.Coach = lv; else if (role == 1) fac.Trainer = lv; else if (role == 2) fac.Nutri = lv; else if (role == 3) fac.Scout = lv; else fac.Manager = lv;
                s.StaffNames[role] = Str(st, "name");
            }
            s.Fac = fac;
            s.Morale = Dbl(pt, "morale");
            s.ManagerName = Str(pt, "manager_name");
            JsonObject ap = Obj(pt, "annual_plan");
            s.Plan = new AnnualPlan { Focus = (PlanFocus)Index(FocusNames, Str(ap, "focus")), TargetRace = Index(RaceNames, Str(ap, "target_race")) };
            JsonObject pk = Obj(ap, "peaking_mode");
            for (int i = 0; i < AnnualPlan.PeakingKinds; i++) s.Plan.Peaking[i] = (PeakingMode)Index(PeakingNames, Str(pk, PeakingKeys[i]));
            JsonObject mp = Obj(pt, "monthly_plan");
            for (int g = 0; g < 3; g++) s.MonthlyPlan[g] = Str(mp, GroupNames[g]);
            JsonValue cw = pt.Get("custom_weeks");
            if (!cw.IsNull)
            {
                var c = (JsonObject)cw;
                s.CustomWeeks = new MenuSpec[3][];
                for (int g = 0; g < 3; g++) s.CustomWeeks[g] = Arr(c, GroupNames[g]).Items.Select(RestoreMenu).ToArray();
            }
            s.GuidanceLeft = Int(pt, "guidance_points");
            JsonObject im = Obj(pt, "individual_menu");
            foreach (string k in im.Keys)
            {
                var e = (JsonObject)im[k];
                s.IndividualMenus[int.Parse(k, CultureInfo.InvariantCulture)] = new IndividualMenu { UntilAbsWeek = Int(e, "until_abs_week"), Menu = RestoreMenu(e.Get("menu")) };
            }
            foreach (JsonValue x in Arr(pt, "talked")) s.Talked.Add((int)((JsonNumber)x).Int);
            s.ScoutPts = Dbl(pt, "scout_points");
            s.CoachRank = Int(pt, "coach_rank");
            RestoreTickets(s.Tickets, Obj(pt, "tickets"));
            s.Prestige = Dbl(pt, "prestige");
            JsonArray yr = Arr(pt, "year_rank"), lr = Arr(pt, "last_year_rank");
            for (int i = 0; i < 3; i++) { s.Rank[i] = (int)((JsonNumber)yr[i]).Int; s.LastRank[i] = (int)((JsonNumber)lr[i]).Int; }
            s.YearPoints = Int(pt, "year_points");
            s.SchoolPref = Int(pt, "school_prefecture_id");
            JsonObject en = Obj(pt, "entries");
            for (int k = 0; k < 3; k++) foreach (JsonValue x in Arr(en, RaceNames[k])) s.Entries[k].Add((int)((JsonNumber)x).Int);
            if (pt.ContainsKey("notifications"))                                                                                // 追加の項目(ないセーブは空。09 §6)
                foreach (JsonValue nv in Arr(pt, "notifications"))
                {
                    var no = (JsonObject)nv;
                    s.Notifications.Add(new Notification { Kind = (NotifyKind)Index(NotifyNames, Str(no, "kind")), AbsWeek = Int(no, "abs_week"), Code = Str(no, "code"), RunnerId = Int(no, "runner_id"), N = Int(no, "n"), Unread = Bool(no, "unread") });
                }
        }

        private static void RestoreTickets(TicketState t, JsonObject j)
        {
            t.Stock = Int(j, "stock");
            t.Bank = Int(j, "bank");
            t.LastUsedYear = Int(j, "last_used_year");
            JsonValue an = j.Get("plan_anchor_year");
            t.PlanAnchorYear = an.IsNull ? (int?)null : (int)((JsonNumber)an).Int;
            foreach (JsonValue x in Arr(j, "plan_granted_years")) t.PlanGrantedYears.Add((int)((JsonNumber)x).Int);
            foreach (JsonValue x in Arr(j, "milestones")) t.Milestones.Add(((JsonString)x).Value);
            JsonValue pv = j.Get("pending");
            if (!pv.IsNull)
            {
                var p = (JsonObject)pv;
                JsonValue pref = p.Get("prefecture_id");
                t.Pending = new PendingInvite { Kind = (TicketKind)Index(TicketKindNames, Str(p, "kind")), EnrollYear = Int(p, "enroll_year"), Pref = pref.IsNull ? 0 : (int)((JsonNumber)pref).Int };
            }
            foreach (JsonValue x in Arr(j, "log")) { var e = (JsonObject)x; t.Log.Add(new TicketLogEntry { Year = Int(e, "year"), Delta = Int(e, "delta"), Source = Str(e, "source") }); }
        }

        private static OBRecord RestoreOb(JsonObject j)
        {
            var b = new OBRecord
            {
                RunnerId = Int(j, "runner_id"), Name = Str(j, "name"), Kana = Str(j, "kana"), Pref = Int(j, "origin_index"), Country = j.ContainsKey("origin_country") ? Int(j, "origin_country") : 0, HighSchoolName = Str(j, "high_school_name"), SchoolId = Int(j, "school_id"),
                EnrollYear = Int(j, "enroll_year"), GradYear = Int(j, "grad_year"), HallOfFame = Bool(j, "hall_of_fame"), Role = (ObRole)Index(RoleNames, Str(j, "role")), Until = Int(j, "role_until"),
                RecruitSource = (RecruitSource)Index(RecruitNames, Str(j, "recruit_source")), Look = RestoreAppearance(Obj(j, "appearance")),
            };
            JsonValue cp = j.Get("career_path");
            b.CareerPath = cp.IsNull ? CareerPath.Employment : (CareerPath)Index(CareerNames, ((JsonString)cp).Value);       // 確定前(null)は Domain に持たない → 一般就職として読む
            JsonArray fs = Arr(j, "final_stats");
            for (int i = 0; i < StatInfo.Count; i++) b.FinalStats[i] = (int)((JsonNumber)fs[i]).Int;
            b.Power = TeamState.LongP(b.FinalStats);
            foreach (JsonValue x in Arr(j, "skills")) b.Skills.Add(((JsonString)x).Value);
            JsonObject pb = Obj(j, "pb");
            b.T5000 = Int(pb, "t5000"); b.T10000 = Int(pb, "t10000"); b.THalf = Int(pb, "t_half");
            JsonObject cr = Obj(j, "career");
            b.RaceWins = Int(cr, "race_wins"); b.SectionWins = Int(cr, "section_wins"); b.Records = Int(cr, "records");
            Style st;
            b.Style = Style.TryParse(Str(j, "style"), out st) ? st : Style.Neutral;
            b.Gifts = Arr(j, "gifts").Items.Select(x => (Gift)Index(GiftNames, ((JsonString)x).Value)).ToArray();
            return b;
        }

        private static void RestoreEventState(TeamState s, JsonObject es, JsonObject runners)
        {
            JsonObject fired = Obj(es, "fired");
            foreach (string k in fired.Keys) { var e = (JsonObject)fired[k]; s.Fired[k] = new EventRecord { Count = Int(e, "count"), Last = Int(e, "last_week") }; }
            JsonObject yc = Obj(es, "year_counts");
            foreach (string k in yc.Keys)
            {
                int v = (int)((JsonNumber)yc[k]).Int;
                if (k == "_total") s.YearTotal = v; else s.YearCounts[k] = v;
            }
            JsonObject rev = Obj(es, "runner_ev");
            foreach (string k in rev.Keys)
            {
                Runner r = s.FindRunner(int.Parse(k, CultureInfo.InvariantCulture));
                if (r == null) throw new FormatException("runner_ev の選手がいない: " + k);
                var e = (JsonObject)rev[k];
                r.LastEventMonth = Int(e, "last_month");
                foreach (JsonValue x in Arr(e, "once")) r.EventOnce.Add(((JsonString)x).Value);
                JsonObject cd = Obj(e, "cooldown");
                foreach (string c in cd.Keys) r.EventCooldown[c] = (int)((JsonNumber)cd[c]).Int;
            }
            JsonObject pc = Obj(es, "pair_cd");
            foreach (string k in pc.Keys)
            {
                string[] p = k.Split('|');
                if (p.Length != 3) throw new FormatException("pair_cd のキー: " + k);
                Dictionary<long, int> d;
                if (!s.PairCooldown.TryGetValue(p[0], out d)) s.PairCooldown[p[0]] = d = new Dictionary<long, int>();
                d[BondTable.Key(int.Parse(p[1], CultureInfo.InvariantCulture), int.Parse(p[2], CultureInfo.InvariantCulture))] = (int)((JsonNumber)pc[k]).Int;
            }
            foreach (JsonValue mv in Arr(es, "mods"))
            {
                var m = (JsonObject)mv;
                JsonValue to = m.Get("to");
                s.Mods.Add(new TimedModifier { Kind = (ModKind)Index(ModNames, Str(m, "kind")), To = to is JsonString ? TimedModifier.TeamTarget : (int)((JsonNumber)to).Int, Value = Dbl(m, "value"), Until = Int(m, "until"), Src = Str(m, "src") });
            }
            foreach (JsonValue x in Arr(es, "bench")) s.Bench.Add((int)((JsonNumber)x).Int);
            foreach (JsonValue x in Arr(es, "forced")) s.Forced.Add((int)((JsonNumber)x).Int);
            foreach (JsonValue x in Arr(es, "log")) { var e = (JsonObject)x; s.EventLog.Add(new EventLogEntry { Id = Str(e, "id"), Week = Int(e, "week"), Choice = Str(e, "choice") }); }
        }

        private static void RestoreSettings(TeamSettings t, JsonObject j)
        {
            t.SpectateMode = Str(j, "spectate_mode");
            t.AutoSafetyF = Int(j, "auto_safety_f");
            t.EventAuto = Bool(j, "event_auto");
            t.PlanAuto = Bool(j, "plan_auto");
            t.ScoutAuto = Bool(j, "scout_auto");
            JsonObject sl = Obj(j, "school_look");
            t.UniformPattern = Int(sl, "uniform_pattern");
            t.CheerSong = Int(sl, "cheer_song");
            t.BgmSet = Int(sl, "bgm_set");
        }

        // ------------------------------------------------------------ 読み取りの補助(型が違えば例外 = 検証で致命にする側が拾う)
        private static JsonObject Obj(JsonObject o, string k) { return (JsonObject)o.Get(k); }
        private static JsonArray Arr(JsonObject o, string k) { return (JsonArray)o.Get(k); }
        private static string Str(JsonObject o, string k) { return ((JsonString)o.Get(k)).Value; }
        private static bool Bool(JsonObject o, string k) { return ((JsonBool)o.Get(k)).Value; }
        private static long Long(JsonObject o, string k) { return ((JsonNumber)o.Get(k)).Int; }
        private static int Int(JsonObject o, string k) { return checked((int)Long(o, k)); }
        private static double Dbl(JsonObject o, string k) { return ((JsonNumber)o.Get(k)).Value; }

        /// <summary>保存する double が Q6 で閉じているか(09 D-10)。閉じていない値の名前を返す(なければ空)。</summary>
        public static List<string> NotQ6Closed(TeamState s)
        {
            var bad = new List<string>();
            void Chk(string what, double x) { if (Q6.Round(x) != x) bad.Add(what + "=" + x.ToString("R", CultureInfo.InvariantCulture)); }
            Chk("morale", s.Morale); Chk("scout_points", s.ScoutPts); Chk("prestige", s.Prestige);
            foreach (TimedModifier m in s.Mods) Chk("mod.value(" + m.Src + ")", m.Value);
            return bad;
        }
    }
}
