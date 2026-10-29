using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Training;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>参照モデル(Python)が出したゴールデンデータとの差分テスト(`verify/gen_golden_03.py`)。整数・状態は完全一致、小数は相対 1e-9 以内。</summary>
    public class GoldenTests
    {
        private static readonly TrainingMaster M = TrainingMaster.CreateDefault();

        private static JsonElement Load(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Golden", name);
            using (var fs = File.OpenRead(path))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var doc = JsonDocument.Parse(gz)) return doc.RootElement.Clone();
        }

        private static int[] Ints(JsonElement e) { return e.EnumerateArray().Select(x => x.GetInt32()).ToArray(); }

        private static FacilityLevels Fac(JsonElement e)
        {
            var f = FacilityLevels.AllOne;
            foreach (JsonProperty p in e.EnumerateObject())
            {
                int v = p.Value.GetInt32();
                switch (p.Name)
                {
                    case "coach": f.Coach = v; break; case "trainer": f.Trainer = v; break; case "nutri": f.Nutri = v; break; case "dorm": f.Dorm = v; break;
                    case "track": f.Track = v; break; case "hill": f.Hill = v; break; case "gym": f.Gym = v; break; case "medical": f.Medical = v; break;
                    case "altitude": f.Altitude = v; break; case "mental": f.Mental = v; break; case "camp": f.Camp = v; break;      // L-70 ②: 新設の施設(キーなし = 未設置 = 0)
                }
            }
            return f;
        }

        private static Style? ParseStyle(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Null) return null;
            return new Style((StyleForm)Enum.Parse(typeof(StyleForm), e.GetProperty("form").GetString()),
                             (StyleRun)Enum.Parse(typeof(StyleRun), e.GetProperty("run").GetString()),
                             (StyleFlow)Enum.Parse(typeof(StyleFlow), e.GetProperty("flow").GetString()));
        }

        private static Gift[] ParseGifts(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Null) return new Gift[0];
            return e.EnumerateArray().Select(x => (Gift)Enum.Parse(typeof(Gift), x.GetString())).ToArray();
        }

        private static InjurySeverity Sev(string s) { return (InjurySeverity)Enum.Parse(typeof(InjurySeverity), s); }

        private static void NearRel(double expected, double actual, string what)
        {
            double tol = 1e-9 * Math.Max(1.0, Math.Abs(expected));
            Assert.AreEqual(expected, actual, tol, what);
        }

        [Test]
        public void WeekResolution_MatchesReference()
        {
            JsonElement cases = Load("week_cases.json.gz");
            int n = 0, injuries = 0;
            foreach (JsonElement c in cases.EnumerateArray())
            {
                n++;
                JsonElement b = c.GetProperty("before");
                var r = new Runner();
                Array.Copy(Ints(b.GetProperty("stats")), r.Stats, 10);
                Array.Copy(Ints(b.GetProperty("pot")), r.Potential, 10);
                Array.Copy(Ints(b.GetProperty("xp")), r.Xp, 10);
                r.Fatigue = b.GetProperty("fatigue").GetInt32();
                r.Motivation = b.GetProperty("motivation").GetInt32();
                r.Grade = b.GetProperty("grade").GetInt32();
                r.GrowthType = (GrowthType)Enum.Parse(typeof(GrowthType), b.GetProperty("gtype").GetString());
                r.TaperWeeks = b.GetProperty("taper_weeks").GetInt32();
                r.SharpenHistory = b.GetProperty("sharpen_hist").EnumerateArray().Select(x => x.GetBoolean()).ToArray();
                r.InjurySeverity = Sev(b.GetProperty("inj")[0].GetString());
                r.InjuryWeeksLeft = b.GetProperty("inj")[1].GetInt32();
                foreach (JsonElement s in b.GetProperty("skills").EnumerateArray()) r.Skills.Add(s.GetString());
                r.BondAvg = b.GetProperty("bond_avg").GetDouble();
                r.SetIndividuality(ParseStyle(b.GetProperty("style")), ParseGifts(b.GetProperty("gifts")));

                JsonElement m = c.GetProperty("menu");
                var menu = new Menu(m[0].GetString(), m[1].GetString(), m[2].ValueKind == JsonValueKind.Null ? null : m[2].GetString());
                double? bondAvg = c.GetProperty("bond_avg").ValueKind == JsonValueKind.Null ? (double?)null : c.GetProperty("bond_avg").GetDouble();
                var rngs = new KeyedRngFactory(ulong.Parse(c.GetProperty("seed").GetString()));
                WeekReport rep = new WeekResolver(M).Resolve(r, menu, Fac(c.GetProperty("fac")), c.GetProperty("month_index").GetInt32(), rngs,
                    c.GetProperty("runner_id").GetInt32(), c.GetProperty("abs_week").GetInt32(), c.GetProperty("camp").GetBoolean(),
                    c.GetProperty("safety").GetBoolean(), c.GetProperty("individual").GetBoolean(), bondAvg);

                string tag = "case " + n;
                JsonElement a = c.GetProperty("after"), er = c.GetProperty("report");
                CollectionAssert.AreEqual(Ints(a.GetProperty("stats")), r.Stats, tag + " stats");
                CollectionAssert.AreEqual(Ints(a.GetProperty("xp")), r.Xp, tag + " xp");
                Assert.AreEqual(a.GetProperty("fatigue").GetInt32(), r.Fatigue, tag + " fatigue");
                Assert.AreEqual(Sev(a.GetProperty("inj")[0].GetString()), r.InjurySeverity, tag + " inj sev");
                Assert.AreEqual(a.GetProperty("inj")[1].GetInt32(), r.InjuryWeeksLeft, tag + " inj weeks");
                Assert.AreEqual(a.GetProperty("taper_weeks").GetInt32(), r.TaperWeeks, tag + " taper");
                CollectionAssert.AreEqual(a.GetProperty("sharpen_hist").EnumerateArray().Select(x => x.GetBoolean()).ToArray(), r.SharpenHistory, tag + " sharpen");
                Assert.AreEqual(a.GetProperty("form").GetInt32(), r.Form, tag + " form");
                Assert.AreEqual(a.GetProperty("week_levelups").GetInt32(), r.WeekLevelups, tag + " levelups");
                Assert.AreEqual(er.GetProperty("menu")[0].GetString(), rep.Menu.Key1, tag + " menu1");
                Assert.AreEqual(er.GetProperty("menu")[1].GetString(), rep.Menu.Key2, tag + " menu2");
                NearRel(er.GetProperty("E").GetDouble(), rep.E, tag + " E");
                NearRel(er.GetProperty("r").GetDouble(), rep.R, tag + " r");
                Assert.AreEqual(er.GetProperty("L").GetDouble(), rep.L, tag + " L");
                Assert.AreEqual(er.GetProperty("F_end").GetInt32(), rep.FEnd, tag + " F_end");
                CollectionAssert.AreEqual(Ints(er.GetProperty("xp_add")), rep.XpAdd, tag + " xp_add");
                if (er.GetProperty("injury_p").ValueKind == JsonValueKind.Null) Assert.IsNull(rep.InjuryP, tag + " injury_p null");
                else NearRel(er.GetProperty("injury_p").GetDouble(), rep.InjuryP.Value, tag + " injury_p");
                if (er.GetProperty("injury").ValueKind == JsonValueKind.Null) Assert.IsNull(rep.NewInjury, tag + " new injury");
                else
                {
                    injuries++;
                    Assert.AreEqual(Sev(er.GetProperty("injury")[0].GetString()), rep.NewInjury, tag + " new injury sev");
                    Assert.AreEqual(er.GetProperty("injury")[1].GetInt32(), rep.NewInjuryWeeks, tag + " new injury weeks");
                }
                Assert.AreEqual(er.GetProperty("peaking").GetInt32(), rep.Peaking, tag + " peaking");
                NearRel(er.GetProperty("noise").GetDouble(), rep.Noise, tag + " noise");
                NearRel(er.GetProperty("form_score").GetDouble(), rep.FormScore, tag + " form_score");
                Assert.AreEqual(er.GetProperty("form").GetInt32(), rep.Form, tag + " form");
            }
            Assert.GreaterOrEqual(n, 5000);
            Assert.GreaterOrEqual(injuries, 100, "新しい故障の分岐が十分に通っている");
            TestContext.Out.WriteLine("週の解決: " + n + " 件(新しい故障 " + injuries + " 件)がすべて参照モデルと一致");
        }

        [Test]
        public void FourYearCareers_MatchReference()
        {
            JsonElement careers = Load("careers.json.gz");
            var races = YearPlanner.Races;
            int n = 0;
            foreach (JsonElement c in careers.EnumerateArray())
            {
                n++;
                var r = new Runner { Grade = 1 };
                Array.Copy(Ints(c.GetProperty("stats0")), r.Stats, 10);
                Array.Copy(Ints(c.GetProperty("pot0")), r.Potential, 10);
                r.GrowthType = (GrowthType)Enum.Parse(typeof(GrowthType), c.GetProperty("gtype").GetString());
                foreach (JsonElement s in c.GetProperty("skills").EnumerateArray()) r.Skills.Add(s.GetString());
                r.SetIndividuality(ParseStyle(c.GetProperty("style")), ParseGifts(c.GetProperty("gifts")));
                FacilityLevels fac = Fac(c.GetProperty("fac"));
                string[] plan = c.GetProperty("plan").EnumerateArray().Select(x => x.GetString()).ToArray();
                var rngs = new KeyedRngFactory(ulong.Parse(c.GetProperty("seed").GetString()));
                int rid = c.GetProperty("runner_id").GetInt32();
                int[] fatigue = Ints(c.GetProperty("fatigue")), forms = Ints(c.GetProperty("forms"));
                var res = new WeekResolver(M);
                int idx = 0, injWeeks = 0;
                for (int y = 0; y < 4; y++)
                {
                    r.Grade = y + 1;
                    for (int yw = 0; yw < 48; yw++)
                    {
                        Menu menu = YearPlanner.WeekMenu(M, yw, plan, races, true, out bool camp);
                        int aw = y * 48 + yw;
                        res.Resolve(r, menu, fac, yw / 4, rngs, rid, aw, camp: camp, safety: true, bondAvg: Math.Min(1000.0, aw * 10.0 * 0.7));
                        Assert.AreEqual(fatigue[idx], r.Fatigue, "career " + n + " week " + aw + " fatigue");
                        Assert.AreEqual(forms[idx], r.Form, "career " + n + " week " + aw + " form");
                        if (r.IsInjured) injWeeks++;
                        idx++;
                        if (races.ContainsKey(yw)) r.Fatigue = Math.Min(100, r.Fatigue + Rounding.HalfUp(races[yw] * TrainingMaster.K_RACE_FATIGUE_PER_KM));
                    }
                }
                CollectionAssert.AreEqual(Ints(c.GetProperty("final_stats")), r.Stats, "career " + n + " final stats");
                CollectionAssert.AreEqual(Ints(c.GetProperty("final_xp")), r.Xp, "career " + n + " final xp");
                Assert.AreEqual(c.GetProperty("injury_weeks").GetInt32(), injWeeks, "career " + n + " injury weeks");
            }
            Assert.AreEqual(60, n);
            TestContext.Out.WriteLine("4年間の育成: " + n + " 人(各 192 週)が参照モデルと一致");
        }
    }
}
