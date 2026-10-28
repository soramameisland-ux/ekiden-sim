using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Training;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>画面のための規則(08 §8 R-01〜R-17)を、参照モデルのゴールデンデータと突き合わせる(`verify/gen_golden_08.py`)。</summary>
    public class ScreenGoldenTests
    {
        private static JsonElement _root;
        private static readonly TrainingMaster M = TrainingMaster.CreateDefault();

        [OneTimeSetUp]
        public void Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Golden", "screen_cases.json.gz");
            using (var fs = File.OpenRead(path)) using (var gz = new GZipStream(fs, CompressionMode.Decompress)) using (var doc = JsonDocument.Parse(gz)) _root = doc.RootElement.Clone();
        }

        private static void Near(double expected, double actual, string what, double tol = 1e-9)
        {
            Assert.AreEqual(expected, actual, tol * Math.Max(1.0, Math.Abs(expected)), what);
        }

        private static string[] Strs(JsonElement a) { return a.EnumerateArray().Select(x => x.GetString()).ToArray(); }

        private static int[] Ints(JsonElement a) { return a.EnumerateArray().Select(x => x.GetInt32()).ToArray(); }

        [Test]
        public void Ranks_Hints_Pbs_Rivalry_Previews()
        {
            foreach (JsonElement c in _root.GetProperty("ranks").EnumerateArray()) Assert.AreEqual(c[1].GetString(), DisplayRules.RankLetter(c[0].GetDouble()).ToString(), "rank " + c[0]);
            foreach (JsonElement c in _root.GetProperty("hints").EnumerateArray())
                Assert.AreEqual(c[2].GetString(), DisplayRules.Hint(c[0].GetInt32(), c[1].GetInt32()).ToString().ToLowerInvariant(), "hint " + c);
            int n = 0;
            foreach (JsonElement c in _root.GetProperty("pbs").EnumerateArray())
            {
                int[] st = Ints(c.GetProperty("stats"));
                Assert.AreEqual(c.GetProperty("t5000").GetInt32(), DisplayRules.DerivePb(st, DisplayRules.PbEvent.M5000), "5000 " + n);
                Assert.AreEqual(c.GetProperty("t10000").GetInt32(), DisplayRules.DerivePb(st, DisplayRules.PbEvent.M10000), "10000 " + n);
                Assert.AreEqual(c.GetProperty("half").GetInt32(), DisplayRules.DerivePb(st, DisplayRules.PbEvent.Half), "half " + n);
                n++;
            }
            string[] lab = { "ほぼなし", "少", "中", "多" };
            foreach (JsonElement c in _root.GetProperty("rivalry").EnumerateArray())
            {
                double win;
                var lv = DisplayRules.Rivalry(c[0].GetDouble(), out win);
                Assert.AreEqual(c[1].GetString(), lab[(int)lv], "競合度 " + c[0]);
                Assert.AreEqual(c[2].GetDouble(), win, 0.0);
            }
            foreach (JsonElement c in _root.GetProperty("previews").EnumerateArray()) Assert.AreEqual(c[1].GetString(), DisplayRules.PreviewMark(c[0].GetInt32()).ToString(), "前評判 " + c[0]);
        }

        [Test]
        public void Plans_AndAutoGroups()
        {
            var names = new[] { PlanFocus.Balanced, PlanFocus.Speed, PlanFocus.Long };
            foreach (var f in names)
            {
                string[] want = Strs(_root.GetProperty("plans").GetProperty(f.ToString()));
                for (int mi = 0; mi < 12; mi++) Assert.AreEqual(want[mi], PlanRules.AutoPlan(f, mi), f + " " + mi);
            }
            foreach (JsonElement c in _root.GetProperty("groups").EnumerateArray())
            {
                var p = c.GetProperty("p").EnumerateArray().Select(x => new KeyValuePair<int, double>(x[0].GetInt32(), x[1].GetDouble())).ToList();
                Dictionary<int, int> got = PlanRules.AutoGroups(p);
                foreach (JsonProperty kv in c.GetProperty("groups").EnumerateObject()) Assert.AreEqual(kv.Value.GetInt32(), got[int.Parse(kv.Name)], "グループ " + kv.Name);
                Assert.AreEqual(p.Count, got.Count);
            }
        }

        private static Runner RunnerOf(JsonElement j)
        {
            var r = new Runner
            {
                Fatigue = j.GetProperty("fatigue").GetInt32(), Motivation = j.GetProperty("motivation").GetInt32(), Grade = j.GetProperty("grade").GetInt32(),
                GrowthType = (GrowthType)Enum.Parse(typeof(GrowthType), j.GetProperty("gtype").GetString()), TaperWeeks = j.GetProperty("taper_weeks").GetInt32(),
                BondAvg = j.GetProperty("bond_avg").GetDouble(), Form = j.GetProperty("form").GetInt32(),
                InjurySeverity = (InjurySeverity)Enum.Parse(typeof(InjurySeverity), j.GetProperty("inj")[0].GetString()), InjuryWeeksLeft = j.GetProperty("inj")[1].GetInt32(),
            };
            for (int i = 0; i < 10; i++)
            {
                r.Stats[i] = j.GetProperty("stats")[i].GetInt32();
                r.Potential[i] = j.GetProperty("pot")[i].GetInt32();
                r.Xp[i] = j.GetProperty("xp")[i].GetInt32();
            }
            for (int i = 0; i < 3; i++) r.SharpenHistory[i] = j.GetProperty("sharpen_hist")[i].GetBoolean();
            foreach (string s in Strs(j.GetProperty("skills"))) r.Skills.Add(s);
            Style? style = null;
            if (j.GetProperty("style").ValueKind == JsonValueKind.String)
            {
                Style st;
                Style.TryParse(j.GetProperty("style").GetString(), out st);
                style = st;
            }
            r.SetIndividuality(style, Strs(j.GetProperty("gifts")).Select(g => (Gift)Enum.Parse(typeof(Gift), g)).ToArray());
            return r;
        }

        private static FacilityLevels FacOf(JsonElement f)
        {
            return new FacilityLevels
            {
                Coach = f.GetProperty("coach").GetInt32(), Trainer = f.GetProperty("trainer").GetInt32(), Nutri = f.GetProperty("nutri").GetInt32(), Dorm = f.GetProperty("dorm").GetInt32(),
                Track = f.GetProperty("track").GetInt32(), Hill = f.GetProperty("hill").GetInt32(), Gym = f.GetProperty("gym").GetInt32(), Medical = f.GetProperty("medical").GetInt32(),
            };
        }

        [Test]
        public void Forecast_MatchesReference()
        {
            int n = 0, withMods = 0, injured = 0, safety = 0;
            foreach (JsonElement c in _root.GetProperty("forecast").EnumerateArray())
            {
                n++;
                string tag = "case " + n;
                Runner r = RunnerOf(c.GetProperty("runner"));
                string before = string.Join(",", r.Stats) + "/" + string.Join(",", r.Xp) + "/" + r.Fatigue;
                var weeks = new List<PlannedWeek>();
                foreach (JsonElement w in c.GetProperty("weeks").EnumerateArray())
                {
                    string[] mn = w.GetProperty("menu").EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Null ? null : x.GetString()).ToArray();
                    weeks.Add(new PlannedWeek { Menu = new Menu(mn[0], mn[1], mn[2]), MonthIndex = w.GetProperty("month").GetInt32(), Camp = w.GetProperty("camp").GetBoolean(), Individual = w.GetProperty("individual").GetBoolean() });
                }
                WeekMods? mods = null;
                if (c.GetProperty("mods").ValueKind == JsonValueKind.Object)
                {
                    JsonElement mj = c.GetProperty("mods");
                    var wm = WeekMods.None;
                    JsonElement v;
                    if (mj.TryGetProperty("E_mult", out v)) wm.EMult = v.GetDouble();
                    if (mj.TryGetProperty("r_add", out v)) wm.RAdd = v.GetDouble();
                    if (mj.TryGetProperty("inj_mult", out v)) wm.InjMult = v.GetDouble();
                    if (mj.TryGetProperty("form_add", out v)) wm.FormAdd = v.GetDouble();
                    mods = wm;
                    withMods++;
                }
                double? bond = c.GetProperty("bond").ValueKind == JsonValueKind.Number ? c.GetProperty("bond").GetDouble() : (double?)null;
                ForecastResult res = Forecaster.Run(M, r, weeks, FacOf(c.GetProperty("fac")), c.GetProperty("safety").GetBoolean(), bond, mods);
                Assert.AreEqual(before, string.Join(",", r.Stats) + "/" + string.Join(",", r.Xp) + "/" + r.Fatigue, tag + ": 入力の選手は変えない");
                JsonElement e = c.GetProperty("expected");
                CollectionAssert.AreEqual(Ints(e.GetProperty("F")), res.F, tag + " F");
                Assert.AreEqual(e.GetProperty("F_max").GetInt32(), res.FMax, tag);
                Near(e.GetProperty("p_injury").GetDouble(), res.PInjury, tag + " p_injury", 1e-12);
                CollectionAssert.AreEqual(Ints(e.GetProperty("levelups")), res.Levelups, tag + " levelups");
                CollectionAssert.AreEqual(Ints(e.GetProperty("stats_after")), res.End.Stats, tag + " stats_after");
                Assert.AreEqual(e.GetProperty("safety_weeks").GetInt32(), res.SafetyWeeks, tag + " safety_weeks");
                JsonElement l = e.GetProperty("last");
                Near(l.GetProperty("mu").GetDouble(), res.Last.Mu, tag + " mu", 1e-12);
                Near(l.GetProperty("sigma").GetDouble(), res.Last.Sigma, tag + " sigma", 1e-12);
                Assert.AreEqual(l.GetProperty("peaking").GetInt32(), res.Last.Peaking, tag + " peaking");
                Assert.AreEqual(l.GetProperty("injured_now").GetBoolean(), res.Last.InjuredNow, tag + " injured_now");
                Assert.AreEqual(l.GetProperty("F_end").GetInt32(), res.Last.FEnd, tag);
                Near(l.GetProperty("injury_p").GetDouble(), res.Last.InjuryP, tag + " injury_p", 1e-12);
                double[] form = e.GetProperty("form").EnumerateArray().Select(x => x.GetDouble()).ToArray();
                for (int i = 0; i < 5; i++) Near(form[i], res.Form[i], tag + " form " + i, 1e-9);
                JsonElement end = e.GetProperty("end");
                CollectionAssert.AreEqual(Ints(end.GetProperty("xp")), res.End.Xp, tag + " xp");
                Assert.AreEqual(end.GetProperty("taper_weeks").GetInt32(), res.End.TaperWeeks, tag + " taper");
                Assert.AreEqual(end.GetProperty("fatigue").GetInt32(), res.End.Fatigue, tag + " fatigue");
                Assert.AreEqual(end.GetProperty("inj")[0].GetString(), res.End.InjurySeverity.ToString(), tag + " inj");
                Assert.AreEqual(end.GetProperty("inj")[1].GetInt32(), res.End.InjuryWeeksLeft, tag + " inj weeks");
                for (int i = 0; i < 3; i++) Assert.AreEqual(end.GetProperty("sharpen_hist")[i].GetBoolean(), res.End.SharpenHistory[i], tag + " sharpen");
                if (r.IsInjured) injured++;
                if (res.SafetyWeeks > 0) safety++;
            }
            Assert.AreEqual(900, n);
            Assert.Greater(withMods, 200);
            Assert.Greater(injured, 100);
            Assert.Greater(safety, 20);
            TestContext.Out.WriteLine("月末の予測: " + n + " 件(補正つき " + withMods + "・故障中 " + injured + "・安全網あり " + safety + ")が参照モデルと一致");
        }

        [Test]
        public void FormProbs_MatchReference_AndSumToOne()
        {
            foreach (JsonElement c in _root.GetProperty("forms").EnumerateArray())
            {
                double[] want = c.GetProperty("p").EnumerateArray().Select(x => x.GetDouble()).ToArray();
                double[] got = Forecaster.FormProbs(c.GetProperty("mu").GetDouble(), c.GetProperty("sigma").GetDouble());
                for (int i = 0; i < 5; i++) Assert.AreEqual(want[i], got[i], 1e-12, "mu " + c.GetProperty("mu") + " sigma " + c.GetProperty("sigma") + " [" + i + "]");
                Assert.AreEqual(1.0, got.Sum(), 1e-12);
            }
        }

        [Test]
        public void Erf_IsAccurate()
        {
            Assert.AreEqual(0.0, MathX.Erf(0.0));
            Assert.AreEqual(0.8427007929497149, MathX.Erf(1.0), 1e-15);
            Assert.AreEqual(0.9953222650189527, MathX.Erf(2.0), 1e-15);
            Assert.AreEqual(0.9999779095030014, MathX.Erf(3.0), 1e-15);
            Assert.AreEqual(-0.5204998778130465, MathX.Erf(-0.5), 1e-15);
            Assert.AreEqual(1.0, MathX.Erf(8.0));
            Assert.AreEqual(0.5, MathX.NormCdf(0.0), 1e-15);
            Assert.AreEqual(0.9772498680518208, MathX.NormCdf(2.0), 1e-14);
        }

        [Test]
        public void ThemeGainTable_MatchesReference()
        {
            foreach (JsonElement c in _root.GetProperty("themes").EnumerateArray())
            {
                var got = ThemeTable.Gains(M, Ints(c.GetProperty("stats")), c.GetProperty("f0").GetInt32(), c.GetProperty("grade").GetInt32(),
                                           (GrowthType)Enum.Parse(typeof(GrowthType), c.GetProperty("gtype").GetString()));
                string[] types = { "Fast", "Mid", "Long", "Climb", "Descent" };
                foreach (JsonProperty th in c.GetProperty("table").EnumerateObject())
                {
                    Assert.AreEqual(th.Value.GetProperty("F_end").GetInt32(), got[th.Name].FEnd, th.Name);
                    for (int t = 0; t < 5; t++) Near(th.Value.GetProperty("dP").GetProperty(types[t]).GetDouble(), got[th.Name].DP[t], th.Name + " " + types[t], 1e-9);
                }
                Assert.AreEqual(c.GetProperty("table").EnumerateObject().Count(), got.Count);
            }
        }

        [Test]
        public void Stops_MatchReference()
        {
            foreach (JsonElement c in _root.GetProperty("stops_before").EnumerateArray())
            {
                var q = new HashSet<string>(Strs(c.GetProperty("qualifies")));
                CollectionAssert.AreEqual(Strs(c.GetProperty("stops")), FlowRules.StopsBeforeWeek(c.GetProperty("week").GetInt32(), q, c.GetProperty("seed").GetBoolean(), c.GetProperty("auto").GetBoolean()), c.ToString());
            }
            int n = 0, capped = 0;
            string[] sevs = { "", "Minor", "Moderate", "Severe" };
            foreach (JsonElement c in _root.GetProperty("stops_after").EnumerateArray())
            {
                var events = c.GetProperty("events").EnumerateArray().Select(e => new FlowRules.EventInfo { Id = e[0].GetString(), Choices = e[1].GetInt32() }).ToList();
                var inj = c.GetProperty("injuries").EnumerateArray().Select(i => new FlowRules.InjuryInfo { RunnerId = i[0].GetInt32(), Severity = Array.IndexOf(sevs, i[1].GetString()), Group = "ABC".IndexOf(i[2].GetString()[0]), Registered = i[3].GetBoolean() }).ToList();
                List<string> stops, deferred;
                FlowRules.StopsAfterWeek(c.GetProperty("week").GetInt32(), events, inj, c.GetProperty("warn").GetBoolean(), c.GetProperty("race").ValueKind == JsonValueKind.Null ? null : c.GetProperty("race").GetString(),
                                         c.GetProperty("event_auto").GetBoolean(), out stops, out deferred);
                CollectionAssert.AreEqual(Strs(c.GetProperty("stops")), stops, "stops " + n + " " + c);
                CollectionAssert.AreEqual(Strs(c.GetProperty("deferred")), deferred, "deferred " + n);
                if (deferred.Any(x => x.EndsWith("(上限)"))) capped++;
                n++;
            }
            Assert.Greater(capped, 20, "週の上限で月末レポートへ回る例が含まれる");
        }

        [Test]
        public void Guide_MatchesReference()
        {
            int okCount = 0;
            foreach (JsonElement c in _root.GetProperty("guide").EnumerateArray())
            {
                Tuple<string, string, string> menu = null;
                if (c.GetProperty("menu").ValueKind == JsonValueKind.Array)
                {
                    string[] mn = c.GetProperty("menu").EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Null ? null : x.GetString()).ToArray();
                    menu = Tuple.Create(mn[0], mn[1], mn[2]);
                    CollectionAssert.AreEqual(Strs(c.GetProperty("errors")), PlanRules.ValidateMenu(M, mn[0], mn[1], mn[2]), "menu " + c);
                }
                var got = PlanRules.CanGuide(M, c.GetProperty("points").GetInt32(), c.GetProperty("kind").GetString(), c.GetProperty("injured").GetBoolean(), c.GetProperty("talked").GetBoolean(), menu);
                Assert.AreEqual(c.GetProperty("ok").GetBoolean(), got.Key, c.ToString());
                Assert.AreEqual(c.GetProperty("reason").GetString(), got.Value, c.ToString());
                if (got.Key) okCount++;
            }
            Assert.Greater(okCount, 100);
        }

        [Test]
        public void ReportSelection_MatchesReference()
        {
            foreach (JsonElement c in _root.GetProperty("reports").EnumerateArray())
            {
                var items = c.GetProperty("items").EnumerateArray().Select(i => new FlowRules.ReportItem { Kind = i[0].GetString(), Key = i[1].GetString(), Score = i[2].GetDouble() }).ToList();
                var shown = FlowRules.SelectReportItems(items);
                var want = c.GetProperty("shown").EnumerateArray().Select(i => i[0].GetString() + ":" + i[1].GetString()).ToArray();
                CollectionAssert.AreEqual(want, shown.Select(i => i.Kind + ":" + i.Key).ToArray(), c.ToString());
                Assert.AreEqual(c.GetProperty("rest").GetInt32(), items.Count - shown.Count);
            }
        }

        [Test]
        public void Traits_MatchReference()
        {
            CollectionAssert.AreEqual(Strs(_root.GetProperty("trait_names")), Enum.GetNames(typeof(Trait)));
            int two = 0;
            foreach (JsonElement c in _root.GetProperty("traits").EnumerateArray())
            {
                Trait[] got = Presentation.Traits(new KeyedRngFactory(c.GetProperty("master").GetUInt64()), c.GetProperty("look_seed").GetUInt32());
                CollectionAssert.AreEqual(Strs(c.GetProperty("traits")), got.Select(t => t.ToString()).ToArray(), c.ToString());
                if (got.Length == 2) two++;
            }
            Assert.Greater(two, 200);
        }

        [Test]
        public void FitMarks_MatchReference()
        {
            int good = 0, ok = 0, bad = 0;
            foreach (JsonElement c in _root.GetProperty("fit").EnumerateArray())
            {
                Style st;
                Assert.IsTrue(Style.TryParse(c.GetProperty("style").GetString(), out st));
                Gift[] gifts = Strs(c.GetProperty("gifts")).Select(g => (Gift)Enum.Parse(typeof(Gift), g)).ToArray();
                double v = Presentation.FitView(M, st, gifts, c.GetProperty("revealed").GetBoolean(), c.GetProperty("theme").GetString());
                Near(c.GetProperty("value").GetDouble(), v, c.ToString(), 1e-12);
                string mark = Presentation.FitMark(v).ToString();
                Assert.AreEqual(c.GetProperty("mark").GetString(), mark, c.ToString());
                if (mark == "◎") good++; else if (mark == "〇") ok++; else bad++;
            }
            Assert.Greater(good, 50);
            Assert.Greater(ok, 50);
            Assert.Greater(bad, 50);
        }

        [Test]
        public void StyleChips_ShowOnlyUnusualAxes()
        {
            Assert.AreEqual(new[] { "standard" }, Presentation.StyleChips(Style.Neutral));
            Style st;
            Style.TryParse("SPC", out st);
            Assert.AreEqual(new[] { "form.speed", "run.pack", "flow.chase" }, Presentation.StyleChips(st));
            Style.TryParse("LSL", out st);
            Assert.AreEqual(new[] { "form.long", "run.solo", "flow.lead" }, Presentation.StyleChips(st));
            Style.TryParse("BPE", out st);
            Assert.AreEqual(new[] { "run.pack" }, Presentation.StyleChips(st));
        }

        // ---- 予測と実際の週の解決の一致(R-05: 疲労・能力・XP・安全網は正確)----

        [Test]
        public void Forecast_AgreesWithTheRealWeek_WhenNoInjuryHappens()
        {
            var resolver = new WeekResolver(M);
            var rngs = new KeyedRngFactory(777UL);
            int checkedCount = 0;
            for (int k = 0; k < 1500; k++)
            {
                Pcg32 g = rngs.Stream(RngRegistry.LabelWorldGen, 8100, k);
                var a = new Runner { Id = k + 1, Grade = 1 + (int)(g.Uniform() * 4), Motivation = 2 + (int)(g.Uniform() * 4), Fatigue = 10 + (int)(g.Uniform() * 70) };
                for (int i = 0; i < 10; i++) { a.Stats[i] = 40 + (int)(g.Uniform() * 30); a.Potential[i] = a.Stats[i] + 5 + (int)(g.Uniform() * 25); }
                a.TaperWeeks = (int)(g.Uniform() * 3);
                if (k % 2 == 0) a.SetIndividuality(new Style((StyleForm)(int)(g.Uniform() * 3), (StyleRun)(int)(g.Uniform() * 3), (StyleFlow)(int)(g.Uniform() * 3)), new[] { Gift.Climber });
                Runner b = a.Clone();
                string theme = M.Themes.Keys.ElementAt((int)(g.Uniform() * M.Themes.Count));
                Menu menu = M.Themes[theme][(int)(g.Uniform() * 4)];
                bool camp = theme == "th.camp", indiv = k % 5 == 0;
                int mi = (int)(g.Uniform() * 12);
                var mods = new WeekMods { EMult = 1.0 + 0.2 * (g.Uniform() - 0.5), RAdd = 0.06 * (g.Uniform() - 0.5), InjMult = 1.0, FormAdd = 0.0 };
                ExpectedWeekReport e = Forecaster.ExpectedWeek(M, a, menu, FacilityLevels.AllTwo, mi, camp, true, indiv, 0.0, mods);
                WeekReport real = resolver.Resolve(b, menu, FacilityLevels.AllTwo, mi, rngs, a.Id, 100 + k, camp, true, indiv, 0.0, mods);
                if (real.NewInjury.HasValue) continue;                            // 故障が起きた週は、予測(故障なし)と一致しない
                checkedCount++;
                string tag = "case " + k;
                Assert.AreEqual(real.FEnd, e.FEnd, tag + " F");
                CollectionAssert.AreEqual(b.Stats, a.Stats, tag + " 能力");
                CollectionAssert.AreEqual(b.Xp, a.Xp, tag + " XP");
                Assert.AreEqual(b.TaperWeeks, a.TaperWeeks, tag + " 調整週");
                Assert.AreEqual(real.InjuryP ?? 0.0, e.InjuryP, 1e-12, tag + " 故障の確率");
                Assert.AreEqual(real.FormScore - real.Noise, e.Mu, 1e-9, tag + " 調子の中心");
            }
            Assert.Greater(checkedCount, 1300);
            TestContext.Out.WriteLine("予測と実際の週の解決(補正つき): " + checkedCount + " 件で F・能力・XP・調整週・故障確率・調子の中心が一致");
        }

        [Test]
        public void WeekMods_ChangeTheRealWeek_AsDocumented()
        {
            var resolver = new WeekResolver(M);
            var rngs = new KeyedRngFactory(5UL);
            Func<Runner> make = () =>
            {
                var r = new Runner { Id = 1, Grade = 2, Motivation = 3, Fatigue = 50 };
                for (int i = 0; i < 10; i++) { r.Stats[i] = 55; r.Potential[i] = 80; }
                return r;
            };
            Menu menu = M.Themes["th.base_build"][0];
            Runner none = make(), up = make(), rec = make();
            resolver.Resolve(none, menu, FacilityLevels.AllTwo, 0, rngs, 1, 10, bondAvg: 0.0);
            resolver.Resolve(up, menu, FacilityLevels.AllTwo, 0, rngs, 1, 10, bondAvg: 0.0, mods: new WeekMods { EMult = 1.2, RAdd = 0, InjMult = 1, FormAdd = 0 });
            resolver.Resolve(rec, menu, FacilityLevels.AllTwo, 0, rngs, 1, 10, bondAvg: 0.0, mods: new WeekMods { EMult = 1, RAdd = 0.06, InjMult = 1, FormAdd = 0 });
            Assert.Greater(up.Xp.Sum() + up.Stats.Sum() * 1000, none.Xp.Sum() + none.Stats.Sum() * 1000, "E_mult > 1 で XP が増える");
            Assert.Less(rec.Fatigue, none.Fatigue, "r_add > 0 で疲労が減る");
        }

        // ---- 08 §12 のテストベクタ(文書の値)----

        [Test]
        public void TV1_RankLetters()
        {
            var want = new Dictionary<int, char> { { 0, 'G' }, { 29, 'G' }, { 30, 'F' }, { 39, 'F' }, { 40, 'E' }, { 49, 'E' }, { 50, 'D' }, { 59, 'D' }, { 60, 'C' }, { 69, 'C' }, { 70, 'B' }, { 79, 'B' }, { 80, 'A' }, { 89, 'A' }, { 90, 'S' }, { 100, 'S' } };
            foreach (var kv in want) Assert.AreEqual(kv.Value, DisplayRules.RankLetter(kv.Key), kv.Key.ToString());
        }

        [Test]
        public void TV2_PotentialHints()
        {
            Assert.AreEqual(PotentialHint.Room, DisplayRules.Hint(50, 70));
            Assert.AreEqual(PotentialHint.Late, DisplayRules.Hint(60, 70));
            Assert.AreEqual(PotentialHint.Late, DisplayRules.Hint(66, 70), "比 0.943 < 0.95");
            Assert.AreEqual(PotentialHint.Cap, DisplayRules.Hint(68, 70));
            Assert.AreEqual(PotentialHint.Max, DisplayRules.Hint(70, 70));
            Assert.AreEqual(PotentialHint.Max, DisplayRules.Hint(82, 80));
        }

        [Test]
        public void TV3_PersonalBests_AllNinety()
        {
            int[] st = { 90, 90, 90, 90, 90, 90, 90, 90, 90, 90 };
            Assert.AreEqual(8000, DisplayRules.DerivePb(st, DisplayRules.PbEvent.M5000), "5000m = 13:20.0");
            Assert.AreEqual(16416, DisplayRules.DerivePb(st, DisplayRules.PbEvent.M10000), "10000m = 27:21.6(01 の『27:22 ≒ 能力90相当』)");
            Assert.AreEqual(35578, DisplayRules.DerivePb(st, DisplayRules.PbEvent.Half), "ハーフ = 59:17.8");
        }

        [Test]
        public void TV4_AutoGroups_TieBreakById()
        {
            var plist = new List<KeyValuePair<int, double>>();
            for (int i = 1; i <= 26; i++) plist.Add(new KeyValuePair<int, double>(i, 72.0 - 0.5 * i));
            plist[10] = new KeyValuePair<int, double>(11, 67.0);                   // 10 番(67.0)と同値 → ID の小さい 10 番が上
            Dictionary<int, int> g = PlanRules.AutoGroups(plist);
            for (int i = 1; i <= 10; i++) Assert.AreEqual(0, g[i], "A " + i);
            for (int i = 11; i <= 20; i++) Assert.AreEqual(1, g[i], "B " + i);
            for (int i = 21; i <= 26; i++) Assert.AreEqual(2, g[i], "C " + i);
        }

        [Test]
        public void TV6_Forecast_Doc03Tv3Input()
        {
            var r = new Runner { Id = 1, Grade = 2, Motivation = 3, Fatigue = 40 };
            int[] st = { 58, 60, 56, 52, 45, 44, 55, 50, 52, 54 }, pot = { 80, 82, 78, 74, 70, 68, 75, 72, 70, 74 };
            for (int i = 0; i < 10; i++) { r.Stats[i] = st[i]; r.Potential[i] = pot[i]; }
            var weeks = Enumerable.Range(0, 4).Select(w => new PlannedWeek { Menu = M.Themes["th.base_build"][w], MonthIndex = 0 }).ToList();
            ForecastResult fc = Forecaster.Run(M, r, weeks, FacilityLevels.AllTwo, true, 0.0);
            CollectionAssert.AreEqual(new[] { 46, 54, 59, 45 }, fc.F, "F の推移(03 TV-4 と一致)");
            Assert.AreEqual(0.0291, fc.PInjury, 5e-5, "4 週の故障確率 2.91%");
            Assert.AreEqual(0.235, fc.Form[3] + fc.Form[4], 5e-4, "4 週目の調子 好調以上 23.5%");
            Assert.AreEqual(0, fc.SafetyWeeks);
        }

        [Test]
        public void TV7_FormDistribution()
        {
            double[] p = Forecaster.FormProbs(0.0, 8.0);
            Assert.AreEqual(1.0, p.Sum(), 1e-12);
            Assert.AreEqual(0.6827, p[2], 5e-4, "mu = 0・sigma = 8: 普通(−8 < s < 8)は ±1σ で約 68%");
            double[] hi = Forecaster.FormProbs(26.0, 8.0);
            Assert.Greater(hi[3] + hi[4], 0.9, "mu = 26 なら好調以上が大半");
        }

        [Test]
        public void TV9_RivalryAndPreviewMarks()
        {
            double win;
            Assert.AreEqual(DisplayRules.RivalryLevel.None, DisplayRules.Rivalry(51.4, out win));
            Assert.AreEqual(DisplayRules.RivalryLevel.Low, DisplayRules.Rivalry(51.5, out win));
            Assert.AreEqual(DisplayRules.RivalryLevel.Mid, DisplayRules.Rivalry(55.0, out win));
            Assert.AreEqual(0.92, win);
            Assert.AreEqual(DisplayRules.RivalryLevel.High, DisplayRules.Rivalry(59.0, out win));
            Assert.AreEqual(0.65, win);
            Assert.AreEqual("◎〇〇▲▲△△-", new string(new[] { 1, 2, 3, 4, 6, 7, 10, 11 }.Select(DisplayRules.PreviewMark).ToArray()));
        }

        [Test]
        public void TV12_ReportItems()
        {
            var items = new List<FlowRules.ReportItem> { new FlowRules.ReportItem { Kind = "injury_severe", Key = "r12", Score = 1.0 } };
            for (int i = 0; i < 5; i++) items.Add(new FlowRules.ReportItem { Kind = "rank_up", Key = "r" + i, Score = 70 - i });
            for (int i = 0; i < 4; i++) items.Add(new FlowRules.ReportItem { Kind = "big_growth", Key = "g" + i, Score = i });
            for (int i = 0; i < 3; i++) items.Add(new FlowRules.ReportItem { Kind = "notice", Key = "n" + i, Score = 0 });
            items.Add(new FlowRules.ReportItem { Kind = "awaken", Key = "a1", Score = 1 });
            items.Add(new FlowRules.ReportItem { Kind = "return", Key = "t1", Score = 0 });
            var shown = FlowRules.SelectReportItems(items);
            Assert.AreEqual(15, items.Count);
            Assert.AreEqual(new[] { "injury_severe:r12", "awaken:a1", "rank_up:r0", "rank_up:r1", "rank_up:r2", "big_growth:g3", "big_growth:g2" }, shown.Select(x => x.Kind + ":" + x.Key).ToArray());
            Assert.AreEqual(8, items.Count - shown.Count, "ほか 8 件");
        }
    }
}
