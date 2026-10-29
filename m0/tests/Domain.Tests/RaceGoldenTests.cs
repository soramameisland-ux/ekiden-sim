using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>04(レース)の参照モデルが出したゴールデンデータとの差分テスト(`verify/gen_golden_04.py`)。整数・順位・事象は完全一致、小数は相対 1e-9。</summary>
    public class RaceGoldenTests
    {
        private static JsonElement _data;

        [OneTimeSetUp]
        public void Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Golden", "race_cases.json.gz");
            using (var fs = File.OpenRead(path))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var doc = JsonDocument.Parse(gz)) _data = doc.RootElement.Clone();
        }

        private static RaceRunner Runner(JsonElement e)
        {
            var r = new RaceRunner { Form = e.GetProperty("form").GetInt32(), Fatigue = e.GetProperty("fatigue").GetInt32(), Leading = e.GetProperty("leading").GetBoolean() };
            int i = 0;
            foreach (JsonElement s in e.GetProperty("stats").EnumerateArray()) r.Stats[i++] = s.GetInt32();
            foreach (JsonElement s in e.GetProperty("skills").EnumerateArray()) r.Skills.Add(s.GetString());
            JsonElement st = e.GetProperty("style");
            if (st.ValueKind != JsonValueKind.Null)
                r.Style = new Style((StyleForm)Enum.Parse(typeof(StyleForm), st.GetProperty("form").GetString()),
                                    (StyleRun)Enum.Parse(typeof(StyleRun), st.GetProperty("run").GetString()),
                                    (StyleFlow)Enum.Parse(typeof(StyleFlow), st.GetProperty("flow").GetString()));
            JsonElement nm = e.GetProperty("name");
            if (nm.ValueKind == JsonValueKind.Number) r.Name = nm.GetInt32().ToString();
            JsonElement gf;
            if (e.TryGetProperty("geki_f", out gf) && gf.ValueKind == JsonValueKind.Number) r.GekiF = gf.GetDouble();       // 走者ごとの檄の効き(04 §9.2)
            return r;
        }

        private static Env EnvOf(JsonElement e)
        {
            return new Env { Temp = e.GetProperty("temp").GetDouble(), Wind = e.GetProperty("wind").GetDouble(), Rain = e.GetProperty("rain").GetBoolean() };
        }

        private static SectionType Typ(string s) { return (SectionType)Enum.Parse(typeof(SectionType), s); }
        private static RaceKind Race(string s) { return (RaceKind)Enum.Parse(typeof(RaceKind), s); }
        private static Instruction Instr(string s) { return (Instruction)Enum.Parse(typeof(Instruction), s, true); }

        private static void Near(double expected, double actual, string what)
        {
            Assert.AreEqual(expected, actual, 1e-9 * Math.Max(1.0, Math.Abs(expected)), what);
        }

        [Test]
        public void Legs_MatchReference()
        {
            int n = 0;
            foreach (JsonElement c in _data.GetProperty("legs").EnumerateArray())
            {
                n++;
                string tag = "leg " + n;
                RaceRunner r = Runner(c.GetProperty("runner"));
                SectionType typ = Typ(c.GetProperty("typ").GetString());
                double d = c.GetProperty("d").GetDouble(), temp = c.GetProperty("temp").GetDouble();
                Instruction instr = Instr(c.GetProperty("instr").GetString());
                var rng = new Pcg32(Mix.Derive(ulong.Parse(c.GetProperty("master").GetString()), 4, c.GetProperty("rid").GetInt32(), c.GetProperty("b").GetInt32()));
                LegDraw dr = RaceMath.DrawLeg(rng, r, c.GetProperty("weight").GetDouble(), instr, c.GetProperty("bms").GetDouble(),
                                              c.GetProperty("use_dist").GetBoolean() ? d : (double?)null, temp, typ);
                Assert.AreEqual(8, rng.Draws, tag + " 乱数の消費は 8 回");
                JsonElement dd = c.GetProperty("draw");
                Near(dd[0].GetDouble(), dr.Noise, tag + " noise"); Near(dd[1].GetDouble(), dr.Brake, tag + " brake"); Near(dd[2].GetDouble(), dr.BrakePos, tag + " pos");
                Near(dd[3].GetDouble(), dr.Boost, tag + " boost"); Near(dd[4].GetDouble(), dr.Stall, tag + " stall");
                Env env = EnvOf(c.GetProperty("env"));
                Near(c.GetProperty("form_adj").GetDouble(), RaceMath.FormAdj(r), tag + " form_adj");
                Near(c.GetProperty("env_adj").GetDouble(), RaceMath.EnvAdj(env, r), tag + " env_adj");
                Near(c.GetProperty("pace_q").GetDouble(), RaceMath.PaceQ(r, d, typ, temp), tag + " pace_q");
                Near(c.GetProperty("pace_q_notemp").GetDouble(), RaceMath.PaceQ(r, d, typ, null), tag + " pace_q(気温なし)");
                Near(c.GetProperty("rating").GetDouble(), RaceTables.Rating(r.Stats, typ), tag + " rating");
                int k = 0;
                foreach (bool lg in new[] { false, true })
                    foreach (bool il in new[] { false, true })
                        Near(c.GetProperty("section_rating")[k++].GetDouble(), RaceMath.SectionRating(r, typ, lg, il), tag + " section_rating");
                Near(c.GetProperty("base_time").GetDouble(), RaceMath.BaseTime(d, 1.0, RaceTables.Rating(r.Stats, typ)), tag + " base_time");
                RaceKind race = Race(c.GetProperty("race").GetString());
                int si = c.GetProperty("si").GetInt32();
                var rr = r.Clone(); rr.Leading = false;
                var ins = new[] { Instruction.Attack, Instruction.Keep, Instruction.Hold };
                for (int j = 0; j < 3; j++) Near(c.GetProperty("leg_ev")[j].GetDouble(), DayPlanner.LegEv(rr, si, race, env, ins[j]), tag + " leg_ev " + j);
                var g = DayPlanner.Gauge(rr, d, typ, temp);
                Assert.AreEqual(c.GetProperty("gauge")[0].GetString(), g.mark, tag + " gauge");
                Assert.AreEqual(Instr(c.GetProperty("gauge")[1].GetString()), g.instr, tag + " gauge instr");
                Assert.AreEqual(Instr(c.GetProperty("best_instr").GetString()), DayPlanner.BestInstr(rr, si, race, env), tag + " best_instr");
            }
            Assert.AreEqual(3000, n);
            TestContext.Out.WriteLine("区間の乱数・補正・期待時間: " + n + " 件が参照モデルと一致");
        }

        [Test]
        public void Hungarian_MatchesReference()
        {
            int n = 0;
            foreach (JsonElement c in _data.GetProperty("hungarian").EnumerateArray())
            {
                n++;
                double[][] cost = c.GetProperty("cost").EnumerateArray().Select(row => row.EnumerateArray().Select(x => x.GetDouble()).ToArray()).ToArray();
                int[] expected = c.GetProperty("assignment").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                CollectionAssert.AreEqual(expected, Hungarian.Solve(cost), "hungarian " + n);
            }
            Assert.AreEqual(300, n);
        }

        [Test]
        public void Races_MatchReference()
        {
            int n = 0, kuriage = 0;
            foreach (JsonElement c in _data.GetProperty("races").EnumerateArray())
            {
                n++;
                string tag = "race " + n;
                RaceKind race = Race(c.GetProperty("race").GetString());
                ulong master = ulong.Parse(c.GetProperty("master").GetString());
                int rid = c.GetProperty("rid").GetInt32();
                var teams = new List<RaceTeam>();
                foreach (JsonElement t in c.GetProperty("teams").EnumerateArray())
                {
                    var team = new RaceTeam
                    {
                        Id = t.GetProperty("id").GetInt32(), Geki = t.GetProperty("geki").GetInt32(), GekiMin = t.GetProperty("geki_min").GetDouble(), GekiF = t.GetProperty("geki_f").GetDouble(),
                        Lineup = t.GetProperty("lineup").EnumerateArray().Select(Runner).ToArray(),
                    };
                    if (t.GetProperty("instr").ValueKind != JsonValueKind.Null) team.Instr = t.GetProperty("instr").EnumerateArray().Select(x => Instr(x.GetString())).ToArray();
                    teams.Add(team);
                }
                var rngs = new KeyedRngFactory(master);
                // 環境は 04 §12 のとおり b = 9999 のストリームで抽選する(参照モデルと同じ)
                Env[] envs = RaceMath.GenEnv(rngs.Stream(4, rid, 9999), race);
                JsonElement envJson = c.GetProperty("envs");
                Assert.AreEqual(envJson.GetArrayLength(), envs.Length, tag + " 日数");
                for (int i = 0; i < envs.Length; i++) { Near(envJson[i].GetProperty("temp").GetDouble(), envs[i].Temp, tag + " temp"); Near(envJson[i].GetProperty("wind").GetDouble(), envs[i].Wind, tag + " wind"); Assert.AreEqual(envJson[i].GetProperty("rain").GetBoolean(), envs[i].Rain, tag + " rain"); }
                List<TeamResult> res = RaceSimulator.Simulate(race, teams, envs, (tid, si) => rngs.Stream(4, rid, tid * 100 + si));
                JsonElement exp = c.GetProperty("results");
                Assert.AreEqual(exp.GetArrayLength(), res.Count, tag + " チーム数");
                for (int i = 0; i < res.Count; i++)
                {
                    JsonElement e = exp[i];
                    string tt = tag + " rank " + (i + 1);
                    Assert.AreEqual(e.GetProperty("id").GetInt32(), res[i].Id, tt + " id");
                    Assert.AreEqual(e.GetProperty("rank").GetInt32(), res[i].Rank, tt + " rank");
                    CollectionAssert.AreEqual(e.GetProperty("sections").EnumerateArray().Select(x => x.GetInt32()).ToArray(), res[i].Sections, tt + " sections");
                    CollectionAssert.AreEqual(e.GetProperty("cum").EnumerateArray().Select(x => x.GetInt32()).ToArray(), res[i].Cum, tt + " cum");
                    Assert.AreEqual(e.GetProperty("total").GetInt32(), res[i].TotalDs, tt + " total");
                    Assert.AreEqual(e.GetProperty("kuriage").GetBoolean(), res[i].Kuriage, tt + " kuriage");
                    Assert.AreEqual(e.GetProperty("geki_left").GetInt32(), res[i].GekiLeft, tt + " geki_left");
                    if (res[i].Kuriage) kuriage++;
                    JsonElement ev = e.GetProperty("events");
                    Assert.AreEqual(ev.GetArrayLength(), res[i].Events.Count, tt + " events 数");
                    for (int k = 0; k < res[i].Events.Count; k++)
                    {
                        Assert.AreEqual(ev[k][0].GetString(), res[i].Events[k].Kind, tt + " event kind");
                        Assert.AreEqual(ev[k][1].GetInt32(), res[i].Events[k].Section, tt + " event section");
                        if (ev[k].GetArrayLength() > 2) Assert.AreEqual(ev[k][2].GetDouble(), res[i].Events[k].Value, 6e-5, tt + " event value");
                    }
                }
            }
            Assert.AreEqual(450, n);
            Assert.Greater(kuriage, 300);
            TestContext.Out.WriteLine("レース: " + n + " 件(繰り上げ " + kuriage + " 組)が参照モデルと一致");
        }

        [Test]
        public void PlanDay_MatchesReference()
        {
            int n = 0;
            foreach (JsonElement c in _data.GetProperty("plan_day").EnumerateArray())
            {
                n++;
                string tag = "plan_day " + n + " " + c.GetProperty("mode").GetString();
                RaceKind race = Race(c.GetProperty("race").GetString());
                var entry = c.GetProperty("entry").EnumerateArray().Select(Runner).ToList();
                Env[] envs = c.GetProperty("envs").EnumerateArray().Select(EnvOf).ToArray();
                PlanMode mode = (PlanMode)Enum.Parse(typeof(PlanMode), c.GetProperty("mode").GetString().Replace("_", ""), true);
                var (lineup, instr, swaps) = DayPlanner.PlanDay(entry, race, envs, mode);
                CollectionAssert.AreEqual(c.GetProperty("lineup").EnumerateArray().Select(x => x.GetInt32().ToString()).ToArray(), lineup.Select(r => r.Name).ToArray(), tag + " lineup");
                CollectionAssert.AreEqual(c.GetProperty("instr").EnumerateArray().Select(x => Instr(x.GetString())).ToArray(), instr, tag + " instr");
                Assert.AreEqual(c.GetProperty("swaps").GetInt32(), swaps, tag + " swaps");
            }
            Assert.AreEqual(240, n);
        }

        [Test]
        public void Qualifier_MatchesReference()
        {
            int n = 0;
            foreach (JsonElement c in _data.GetProperty("qualifier").EnumerateArray())
            {
                n++;
                string tag = "qualifier " + n;
                ulong master = ulong.Parse(c.GetProperty("master").GetString());
                int rid = c.GetProperty("rid").GetInt32();
                var rngs = new KeyedRngFactory(master);
                var teams = c.GetProperty("teams").EnumerateArray().Select(t => new QualifierTeam { Id = t.GetProperty("id").GetInt32(), Runners = t.GetProperty("runners").EnumerateArray().Select(Runner).ToArray() }).ToList();
                var res = Qualifier.Run(teams, (tid, ri) => rngs.Stream(4, rid, tid * 100 + ri), EnvOf(c.GetProperty("env")));
                JsonElement exp = c.GetProperty("results");
                Assert.AreEqual(exp.GetArrayLength(), res.Count, tag);
                for (int i = 0; i < res.Count; i++)
                {
                    Assert.AreEqual(exp[i].GetProperty("id").GetInt32(), res[i].Id, tag + " id");
                    Assert.AreEqual(exp[i].GetProperty("total").GetInt32(), res[i].Total, tag + " total");
                    CollectionAssert.AreEqual(exp[i].GetProperty("times").EnumerateArray().Select(x => x.GetInt32()).ToArray(), res[i].Times, tag + " times");
                }
            }
            Assert.AreEqual(40, n);
        }

        /// <summary>走者ごとの檄の効き f_k(04 §9.2): 式が参照モデルと一致(やる気 1〜5 × 絆平均の格子と乱数の組)。境界(0.3・1.2)に当たる組も含む。</summary>
        [Test]
        public void GekiFactor_MatchesReference_AndIsClamped()
        {
            int n = 0, atLo = 0, atHi = 0;
            foreach (JsonElement c in _data.GetProperty("geki_f").EnumerateArray())
            {
                double got = RaceMath.GekiFactor(c.GetProperty("mot").GetInt32(), c.GetProperty("bond").GetDouble()), want = c.GetProperty("f").GetDouble();
                Assert.AreEqual(want, got, 0.0, "mot=" + c.GetProperty("mot") + " bond=" + c.GetProperty("bond"));      // 同じ式・同じ順の double なのでビットまで一致
                Assert.That(got, Is.InRange(0.3, 1.2));
                if (got == 0.3) atLo++; if (got == 1.2) atHi++;
                n++;
            }
            Assert.Greater(n, 300);
            Assert.AreEqual(0.6, RaceMath.GekiFactor(1, 0.0), 1e-12, "やる気 1・絆 0");
            Assert.AreEqual(1.2, RaceMath.GekiFactor(5, 1000.0), 0.0, "やる気 5・絆 1000 は上限");
            Assert.AreEqual(1.0, RaceMath.GekiFactor(5, 0.0), 1e-12, "やる気 5・絆 0");
            Assert.AreEqual(0.3, RaceMath.GekiFactor(-9, 0.0), 0.0, "下限");
            Assert.Greater(atHi, 0, "試験の前提: 上限に当たる組がある");
        }

        [Test]
        public void Pace50Table_MatchesFormula()
        {
            foreach (double d in Pace50.Distances)
                Assert.AreEqual(Pace50.Formula(d), Pace50.Of(d), 1e-9, "pace50(" + d + ")");
            Assert.Throws<ArgumentException>(() => Pace50.Of(7.77));
        }
    }
}
