using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Recruit;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.Training;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>07 の招聘選手の生成・券の状態遷移・開始状態を、参照モデルのゴールデンデータと突き合わせる(`verify/gen_golden_07.py`)。</summary>
    public class SpecialGoldenTests
    {
        private static JsonElement _root;

        [OneTimeSetUp]
        public void Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Golden", "special_cases.json.gz");
            using (var fs = File.OpenRead(path)) using (var gz = new GZipStream(fs, CompressionMode.Decompress)) using (var doc = JsonDocument.Parse(gz)) _root = doc.RootElement.Clone();
        }

        private static void Near(double expected, double actual, string what)
        {
            Assert.AreEqual(expected, actual, 1e-9 * Math.Max(1.0, Math.Abs(expected)), what);
        }

        [Test]
        public void Tables_MatchReference()
        {
            CollectionAssert.AreEqual(_root.GetProperty("countries").EnumerateArray().Select(x => x.GetString()).ToArray(), Geo.Countries);
            CollectionAssert.AreEqual(_root.GetProperty("skill_pool").EnumerateArray().Select(x => x.GetString()).ToArray(), SpecialRecruitGenerator.SkillPool);
        }

        [Test]
        public void SpecialRecruits_MatchReference()
        {
            int n = 0, withSkill = 0, shifted = 0, gifted = 0;
            foreach (JsonElement c in _root.GetProperty("recruits").EnumerateArray())
            {
                n++;
                ulong master = c.GetProperty("master").GetUInt64();
                var kind = c.GetProperty("kind").GetString() == "Rookie" ? TicketKind.Rookie : TicketKind.Intl;
                int pref = c.GetProperty("pref_in").ValueKind == JsonValueKind.Number ? c.GetProperty("pref_in").GetInt32() : 0;
                SpecialRecruit r = SpecialRecruitGenerator.Generate(new KeyedRngFactory(master), c.GetProperty("year").GetInt32(), kind, pref, c.GetProperty("index").GetInt32());
                string tag = "case " + n;
                Near(c.GetProperty("a0").GetDouble(), r.A0, tag + " a0");
                Near(c.GetProperty("gap").GetDouble(), r.Gap, tag + " gap");
                Near(c.GetProperty("Q").GetDouble(), r.Q, tag + " Q");
                Near(c.GetProperty("V").GetDouble(), r.V, tag + " V");
                Assert.AreEqual(c.GetProperty("growth").GetString(), r.Growth.ToString(), tag + " growth");
                Assert.AreEqual(c.GetProperty("ptype").GetString(), r.Profile.ToString(), tag + " ptype");
                CollectionAssert.AreEqual(c.GetProperty("stats").EnumerateArray().Select(x => x.GetInt32()).ToArray(), r.Stats, tag + " stats");
                CollectionAssert.AreEqual(c.GetProperty("pot_base").EnumerateArray().Select(x => x.GetInt32()).ToArray(), r.PotentialBase, tag + " pot_base");
                CollectionAssert.AreEqual(c.GetProperty("pot").EnumerateArray().Select(x => x.GetInt32()).ToArray(), r.Potential, tag + " pot(素養の加算後)");
                string[] skills = c.GetProperty("skills").EnumerateArray().Select(x => x.GetString()).ToArray();
                Assert.AreEqual(skills.Length == 0 ? null : skills[0], r.Skill, tag + " skill");
                Assert.AreEqual(c.GetProperty("shift").GetInt32(), r.Shift, tag + " shift");
                Assert.AreEqual(c.GetProperty("look_seed").GetUInt32(), r.LookSeed, tag + " look_seed");
                if (kind == TicketKind.Intl) Assert.AreEqual(c.GetProperty("country").GetString(), r.Country, tag + " country");
                else
                {
                    Assert.AreEqual(c.GetProperty("pref").GetInt32(), r.Pref, tag + " pref");
                    Near(c.GetProperty("hs_u").GetDouble(), r.HighSchoolU, tag + " hs_u");
                }
                Assert.AreEqual(c.GetProperty("style").GetString(), r.Style.Code, tag + " style");
                CollectionAssert.AreEqual(c.GetProperty("gifts").EnumerateArray().Select(x => x.GetString()).ToArray(), r.Gifts.Select(g => g.ToString()).ToArray(), tag + " gifts");
                Assert.AreEqual(c.GetProperty("errors").GetArrayLength() == 0, SpecialRecruitGenerator.Validate(r).Count == 0, tag + " 検証");
                if (r.Skill != null) withSkill++;
                if (r.Shift > 0) shifted++;
                if (r.Gifts.Length > 0) gifted++;
            }
            Assert.AreEqual(3000, n);
            Assert.Greater(withSkill, 700);
            Assert.Greater(shifted, 0);
            Assert.Greater(gifted, 300);
            TestContext.Out.WriteLine("招聘選手: " + n + " 人が参照モデルと一致(特殊能力あり " + withSkill + "・上限で下げた " + shifted + "・素養あり " + gifted + ")");
        }

        [Test]
        public void Tickets_StateMachine_MatchesReference()
        {
            int runs = 0, steps = 0;
            foreach (JsonElement run in _root.GetProperty("tickets").EnumerateArray())
            {
                runs++;
                var t = new TicketState();
                JsonElement ops = run.GetProperty("ops"), states = run.GetProperty("states");
                for (int i = 0; i < ops.GetArrayLength(); i++)
                {
                    JsonElement op = ops[i];
                    int res = 0;
                    switch (op[0].GetString())
                    {
                        case "earn": res = t.Earn(op[1].GetString(), op[2].GetInt32()) ? 1 : 0; break;
                        case "plan":
                            {
                                bool? owned = op[2].ValueKind == JsonValueKind.Null ? (bool?)null : op[2].GetBoolean();
                                res = t.TryPlanGrant(op[1].GetInt32(), owned);
                                break;
                            }
                        case "declare":
                            {
                                int year = op[1].GetInt32();
                                bool can = t.CanDeclare(year, op[2].GetInt32(), op[3].GetInt32());
                                if (can) t.Declare(year, op[4].GetString() == "Rookie" ? TicketKind.Rookie : TicketKind.Intl, op[5].GetInt32());
                                res = can ? 1 : 0;
                                break;
                            }
                        case "enroll": t.ConfirmEnrollment(); break;
                    }
                    JsonElement exp = states[i];
                    string tag = "run " + runs + " step " + i + " (" + op + ")";
                    Assert.AreEqual(exp[0].GetInt32(), res, tag + " 結果");
                    JsonElement s = exp[1];
                    Assert.AreEqual(s.GetProperty("stock").GetInt32(), t.Stock, tag + " stock");
                    Assert.AreEqual(s.GetProperty("bank").GetInt32(), t.Bank, tag + " bank");
                    Assert.AreEqual(s.GetProperty("last_used_year").GetInt32(), t.LastUsedYear, tag + " last_used_year");
                    Assert.AreEqual(s.GetProperty("invites").GetInt32(), t.InvitesDeclared, tag + " invites");
                    JsonElement pen = s.GetProperty("pending");
                    if (pen.ValueKind == JsonValueKind.Null) Assert.IsNull(t.Pending, tag + " pending");
                    else
                    {
                        Assert.IsNotNull(t.Pending, tag + " pending");
                        Assert.AreEqual(pen[0].GetString(), t.Pending.Kind.ToString(), tag);
                        Assert.AreEqual(pen[1].GetInt32(), t.Pending.Pref, tag);
                        Assert.AreEqual(pen[2].GetInt32(), t.Pending.EnrollYear, tag);
                    }
                    JsonElement anchor = s.GetProperty("anchor");
                    Assert.AreEqual(anchor.ValueKind == JsonValueKind.Null ? (int?)null : anchor.GetInt32(), t.PlanAnchorYear, tag + " anchor");
                    CollectionAssert.AreEqual(s.GetProperty("granted").EnumerateArray().Select(x => x.GetInt32()).ToArray(), t.PlanGrantedYears.ToArray(), tag + " granted");
                    CollectionAssert.AreEqual(s.GetProperty("milestones").EnumerateArray().Select(x => x.GetString()).ToArray(), t.Milestones.ToArray(), tag + " milestones");
                    JsonElement log = s.GetProperty("log");
                    Assert.AreEqual(log.GetArrayLength(), t.Log.Count, tag + " log");
                    for (int k = 0; k < t.Log.Count; k++)
                    {
                        Assert.AreEqual(log[k][0].GetInt32(), t.Log[k].Year, tag + " log year");
                        Assert.AreEqual(log[k][1].GetInt32(), t.Log[k].Delta, tag + " log delta");
                        Assert.AreEqual(log[k][2].GetString(), t.Log[k].Source, tag + " log source");
                    }
                    Assert.IsTrue(t.Stock >= 0 && t.Stock <= 2 && (t.Stock == 2 || t.Bank == 0), tag + " 不変条件");
                    steps++;
                }
            }
            Assert.AreEqual(200, runs);
            TestContext.Out.WriteLine("券の状態遷移: " + runs + " 本・" + steps + " 手が参照モデルと一致");
        }

        [Test]
        public void Scenarios_MatchReference()
        {
            JsonElement sc = _root.GetProperty("scenarios");
            var byName = new Dictionary<string, ScenarioDef> { { "Elite", Scenarios.Elite() }, { "Mid", Scenarios.Mid() }, { "Low", Scenarios.Low() }, { "Club", Scenarios.Club() } };
            foreach (var kv in byName)
            {
                JsonElement f = sc.GetProperty("start_fac").GetProperty(kv.Key).GetProperty("fac");
                FacilityLevels fl = kv.Value.Facilities;
                Assert.AreEqual(f.GetProperty("coach").GetInt32(), fl.Coach, kv.Key);
                Assert.AreEqual(f.GetProperty("trainer").GetInt32(), fl.Trainer, kv.Key);
                Assert.AreEqual(f.GetProperty("nutri").GetInt32(), fl.Nutri, kv.Key);
                Assert.AreEqual(f.GetProperty("dorm").GetInt32(), fl.Dorm, kv.Key);
                Assert.AreEqual(f.GetProperty("track").GetInt32(), fl.Track, kv.Key);
                Assert.AreEqual(f.GetProperty("hill").GetInt32(), fl.Hill, kv.Key);
                Assert.AreEqual(f.GetProperty("gym").GetInt32(), fl.Gym, kv.Key);
                Assert.AreEqual(f.GetProperty("medical").GetInt32(), fl.Medical, kv.Key);
                Assert.AreEqual(f.GetProperty("scout").GetInt32(), fl.Scout, kv.Key);
                Assert.AreEqual(sc.GetProperty("strength_rank").GetProperty(kv.Key).GetInt32(), kv.Value.StrengthRank, kv.Key);
                Assert.AreEqual(1500, kv.Value.Funds);
            }
            JsonElement koki = sc.GetProperty("scenarios").GetProperty("koki");
            ScenarioDef k = Scenarios.Koki();
            Assert.AreEqual(koki.GetProperty("prestige").GetDouble(), k.Prestige.Value);
            Assert.AreEqual(koki.GetProperty("hist").GetDouble(), k.FameHist.Value);
            Assert.AreEqual(koki.GetProperty("funds").GetDouble(), k.Funds);
            Assert.AreEqual(koki.GetProperty("ob").GetInt32(), k.ObLevel.Value);
            Assert.AreEqual(koki.GetProperty("fac").GetProperty("coach").GetInt32(), k.Facilities.Coach);
            Assert.AreEqual(sc.GetProperty("scenarios").GetProperty("club5").GetProperty("members").GetInt32(), Scenarios.Club().Members.Value);
        }
    }
}
