using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Team;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>06 のイベントを、参照モデル(verify/ref_model_06.py)のゴールデンデータと突き合わせる(`verify/gen_golden_06.py`)。</summary>
    public class EventGoldenTests
    {
        private static JsonElement _root;
        private static readonly EventCatalog Catalog = EventCatalogData.Create();

        [OneTimeSetUp]
        public void Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Golden", "events_cases.json.gz");
            using (var fs = File.OpenRead(path)) using (var gz = new GZipStream(fs, CompressionMode.Decompress)) using (var doc = JsonDocument.Parse(gz)) _root = doc.RootElement.Clone();
        }

        private static readonly string[] TargetNames = { "", "r", "p", "all", "g1", "g2", "g3", "g4", "top10", "same_pref", "same_group", "far", "near", "overseas", "captain", "team" };
        private static readonly string[] CaptainNames = { "top_bond", "top_p", "top_men" };

        /// <summary>効果を、参照モデルの辞書と同じ形(使わない項目は書かない)の JSON にする。</summary>
        private static JsonNode FxJson(Effect fx)
        {
            var o = new JsonObject();
            string[] ops = { "mot", "fat", "xp", "bond", "bondg", "inj", "skill", "funds", "scout", "guide", "mod", "captain", "bench", "entry", "ob_role" };
            o["op"] = ops[(int)fx.Op];
            if (fx.To != Target.None) o["to"] = TargetNames[(int)fx.To];
            switch (fx.Op)
            {
                case EffectOp.Mot: case EffectOp.Fat: case EffectOp.Bond: case EffectOp.Bondg: case EffectOp.Funds: case EffectOp.Scout: case EffectOp.Guide:
                    o["d"] = fx.D;
                    break;
                case EffectOp.Xp:
                    o["stat"] = fx.StatMode == XpStatMode.Weakest ? "weakest" : fx.StatMode == XpStatMode.Random ? "random" : ((Stat)fx.Stat).ToString();
                    o["xu"] = fx.Xu;
                    break;
                case EffectOp.Inj: o["sev"] = fx.Sev.ToString(); break;
                case EffectOp.Skill: o["id"] = fx.SkillId; break;
                case EffectOp.Mod: o["kind"] = StateJson.ModName(fx.Kind); o["value"] = fx.Value; o["weeks"] = fx.Weeks; break;
                case EffectOp.Captain: o["select"] = CaptainNames[(int)fx.Select]; break;
                case EffectOp.ObRole: o["role"] = fx.Role.ToString(); o["weeks"] = fx.Weeks; break;
            }
            if (fx.P.HasValue) o["p"] = fx.P.Value;
            return o;
        }

        private static JsonElement ToElement(JsonNode n)
        {
            using (JsonDocument d = JsonDocument.Parse(n == null ? "null" : n.ToJsonString())) return d.RootElement.Clone();
        }

        private static JsonNode DropNulls(JsonElement e)
        {
            var o = new JsonObject();
            foreach (JsonProperty p in e.EnumerateObject())
                if (p.Value.ValueKind != JsonValueKind.Null) o[p.Name] = JsonNode.Parse(p.Value.GetRawText());
            return o;
        }

        private static JsonNode IntsOrNull(int[] v)
        {
            return v == null ? null : new JsonArray(v.Select(x => (JsonNode)x).ToArray());
        }

        [Test]
        public void Catalogue_MatchesReference()
        {
            JsonElement cat = _root.GetProperty("catalogue");
            var expected = cat.GetProperty("events").EnumerateArray().ToDictionary(e => e.GetProperty("id").GetString());
            Assert.AreEqual(expected.Count, Catalog.Events.Count);
            Assert.AreEqual(0, cat.GetProperty("violations").GetArrayLength());
            string[] scopes = { "team", "runner", "pair" }, phases = { "normal", "race" }, onces = { null, "runner", "save" };
            foreach (EventDef e in Catalog.Events)
            {
                JsonElement x;
                Assert.IsTrue(expected.TryGetValue(e.Id, out x), "カタログにないイベント " + e.Id);
                Assert.AreEqual(x.GetProperty("cat").GetString(), e.Category.ToString(), e.Id + " cat");
                Assert.AreEqual(x.GetProperty("name").GetString(), e.Name, e.Id + " name");
                Assert.AreEqual(scopes[(int)e.Scope], x.GetProperty("scope").GetString(), e.Id + " scope");
                Assert.AreEqual(x.GetProperty("cond").GetString(), e.Cond, e.Id + " cond");
                Assert.AreEqual(x.GetProperty("p").GetDouble(), e.P, 0.0, e.Id + " p");
                Assert.AreEqual(phases[(int)e.Phase], x.GetProperty("phase").GetString(), e.Id + " phase");
                Assert.AreEqual(x.GetProperty("cooldown").GetInt32(), e.Cooldown, e.Id + " cooldown");
                Assert.AreEqual(onces[(int)e.Once], x.GetProperty("once").ValueKind == JsonValueKind.Null ? null : x.GetProperty("once").GetString(), e.Id + " once");
                JsonElement my = x.GetProperty("max_year");
                Assert.AreEqual(my.ValueKind == JsonValueKind.Null ? (int?)null : my.GetInt32(), e.MaxYear, e.Id + " max_year");
                Assert.AreEqual(x.GetProperty("priority").GetInt32(), e.Priority, e.Id + " priority");
                Assert.AreEqual(x.GetProperty("exempt").GetBoolean(), e.Exempt, e.Id + " exempt");
                Assert.IsNull(StateJson.Diff(x.GetProperty("months"), ToElement(IntsOrNull(e.Months)), e.Id + ".months"), e.Id + " months");
                Assert.IsNull(StateJson.Diff(x.GetProperty("weeks"), ToElement(IntsOrNull(e.Weeks)), e.Id + ".weeks"), e.Id + " weeks");
                CollectionAssert.AreEqual(x.GetProperty("default").EnumerateArray().Select(d => d.GetString()).ToArray(), e.Default, e.Id + " default");
                Assert.AreEqual(x.GetProperty("choices").GetArrayLength(), e.Choices.Length, e.Id + " choices");
                for (int i = 0; i < e.Choices.Length; i++)
                {
                    JsonElement cj = x.GetProperty("choices")[i];
                    ChoiceDef c = e.Choices[i];
                    Assert.AreEqual(cj.GetProperty("id").GetString(), c.Id, e.Id);
                    Assert.AreEqual(cj.GetProperty("label").GetString(), c.Label, e.Id + "/" + c.Id + " label");
                    Assert.AreEqual(cj.GetProperty("req").ValueKind == JsonValueKind.Null ? null : cj.GetProperty("req").GetString(), string.IsNullOrEmpty(c.Req) ? null : c.Req, e.Id + "/" + c.Id + " req");
                    JsonElement fj = cj.GetProperty("fx");
                    Assert.AreEqual(fj.GetArrayLength(), c.Fx.Length, e.Id + "/" + c.Id + " fx");
                    for (int k = 0; k < c.Fx.Length; k++)
                    {
                        string d = StateJson.Diff(ToElement(DropNulls(fj[k])), ToElement(FxJson(c.Fx[k])), e.Id + "/" + c.Id + ".fx[" + k + "]");
                        Assert.IsNull(d, d);
                    }
                }
            }
            JsonElement sk = cat.GetProperty("skills");
            Assert.AreEqual(sk.GetArrayLength(), Catalog.Skills.Count);
            for (int i = 0; i < Catalog.Skills.Count; i++)
            {
                Assert.AreEqual(sk[i][0].GetString(), Catalog.Skills[i].Id);
                Assert.AreEqual(sk[i][2].GetString(), Catalog.Skills[i].Rank.ToString());
                Assert.AreEqual(sk[i][3].GetString(), Catalog.Skills[i].Acquire);
            }
            foreach (JsonElement ex in cat.GetProperty("exclusive").EnumerateArray())
            {
                CollectionAssert.Contains(Catalog.ExclusiveWith(ex[0].GetString()).ToList(), ex[1].GetString());
                CollectionAssert.Contains(Catalog.ExclusiveWith(ex[1].GetString()).ToList(), ex[0].GetString());
            }
        }

        private static CompiledExpr[] CompileAll(out string[] sources, out int[] roles)
        {
            JsonElement table = _root.GetProperty("exprs");
            int n = table.GetArrayLength();
            var res = new CompiledExpr[n];
            sources = new string[n];
            roles = new int[n];
            for (int i = 0; i < n; i++)
            {
                JsonElement x = table[i];
                int mask = 0;
                foreach (JsonElement r in x.GetProperty("roles").EnumerateArray()) mask |= r.GetString() == "r" ? DslCompiler.RoleR : DslCompiler.RoleP;
                sources[i] = x.GetProperty("src").GetString();
                roles[i] = mask;
                Assert.AreEqual(x.GetProperty("tree").GetString(), Parser.Parse(sources[i]).ToString(), "構文木 " + sources[i]);
                res[i] = DslCompiler.Compile(sources[i], mask);
            }
            return res;
        }

        [Test]
        public void Expressions_ParseTrees_AndCompile()
        {
            string[] src;
            int[] roles;
            CompiledExpr[] all = CompileAll(out src, out roles);
            Assert.GreaterOrEqual(all.Length, 800);
            TestContext.Out.WriteLine("条件式: " + all.Length + " 件の構文木が参照モデルと一致");
        }

        private static RaceInfo? RaceOf(JsonElement j)
        {
            if (j.ValueKind == JsonValueKind.Null) return null;
            return new RaceInfo(j.GetProperty("kind").GetInt32(), j.GetProperty("team_rank").GetInt32(), j.GetProperty("expected_rank").GetInt32());
        }

        [Test]
        public void Expressions_Evaluate_MatchReference()
        {
            string[] src;
            int[] roles;
            CompiledExpr[] all = CompileAll(out src, out roles);
            int n = 0, states = 0;
            foreach (JsonElement c in _root.GetProperty("cases").EnumerateArray())
            {
                if (c.GetProperty("evals").ValueKind == JsonValueKind.Null) continue;
                states++;
                TeamState s = StateJson.Load(c.GetProperty("pre"));
                var ctx = new EventContext(s, new ApproxEventWorld(s.Seed), RaceOf(c.GetProperty("race")));
                foreach (JsonElement e in c.GetProperty("evals").EnumerateArray())
                {
                    int i = e[0].GetInt32();
                    Runner r = e[1].ValueKind == JsonValueKind.Null ? null : s.FindRunner(e[1].GetInt32());
                    Runner p = e[2].ValueKind == JsonValueKind.Null ? null : s.FindRunner(e[2].GetInt32());
                    double actual = all[i].Eval(ctx.Bind(r, p));
                    double expected = e[3].GetDouble();
                    Assert.AreEqual(expected, actual, 1e-12 * Math.Max(1.0, Math.Abs(expected)), "状態 " + states + " 式 " + src[i] + " r=" + e[1] + " p=" + e[2]);
                    n++;
                }
            }
            Assert.Greater(n, 10000);
            TestContext.Out.WriteLine("条件式の評価: " + states + " 状態 × " + n + " 件が参照モデルと一致");
        }

        [Test]
        public void Refresh_MatchesReference()
        {
            int n = 0;
            foreach (JsonElement c in _root.GetProperty("cases").EnumerateArray())
            {
                if (c.GetProperty("refreshed").ValueKind == JsonValueKind.Null) continue;
                TeamState s = StateJson.Load(c.GetProperty("pre"));
                s.Refresh();
                foreach (JsonElement e in c.GetProperty("refreshed").EnumerateArray())
                {
                    Runner r = s.FindRunner(e[0].GetInt32());
                    string tag = "状態 " + n + " 選手 " + r.Id;
                    Assert.AreEqual(e[1].GetInt32(), r.PRank, tag + " p_rank");
                    Assert.AreEqual(e[2].GetInt32(), r.Group, tag + " group");
                    Assert.AreEqual(e[3].GetInt32() != 0, r.Regular, tag + " regular");
                    Assert.AreEqual(e[4].GetDouble(), r.GapAvg, 1e-12, tag + " gap_avg");
                    Assert.AreEqual(e[5].GetDouble(), r.BondAvg, 1e-9, tag + " bond_avg");
                }
                n++;
            }
            Assert.Greater(n, 50);
        }

        [Test]
        public void EventPhase_MatchesReference()
        {
            int n = 0, fired = 0;
            var kinds = new HashSet<string>();
            foreach (JsonElement c in _root.GetProperty("cases").EnumerateArray())
            {
                n++;
                JsonElement pre = c.GetProperty("pre");
                TeamState s = StateJson.Load(pre);
                double scale = c.GetProperty("ev_scale").GetDouble();
                var phase = new EventPhase(Catalog, new KeyedRngFactory(s.Seed), new ApproxEventWorld(s.Seed), scale);
                IChoicePolicy pol = c.GetProperty("policy").GetString() == "uniform" ? (IChoicePolicy)new UniformChoicePolicy() : new DefaultChoicePolicy();
                List<FiredEvent> got = phase.Run(s, RaceOf(c.GetProperty("race")), pol);
                JsonElement exp = c.GetProperty("fired");
                string tag = "状態 " + n + "(週 " + pre.GetProperty("abs_week") + ")";
                Assert.AreEqual(exp.GetArrayLength(), got.Count, tag + ": 発生した件数\n期待 " + exp.GetRawText() + "\n実際 " + string.Join(", ", got.Select(f => f.Def.Id)));
                for (int i = 0; i < got.Count; i++)
                {
                    Assert.AreEqual(exp[i][0].GetString(), got[i].Def.Id, tag + " #" + i + " イベント");
                    Assert.AreEqual(exp[i][1].ValueKind == JsonValueKind.Null ? 0 : exp[i][1].GetInt32(), got[i].R == null ? 0 : got[i].R.Id, tag + " #" + i + " 当事者 r");
                    Assert.AreEqual(exp[i][2].ValueKind == JsonValueKind.Null ? 0 : exp[i][2].GetInt32(), got[i].P == null ? 0 : got[i].P.Id, tag + " #" + i + " 当事者 p");
                    Assert.AreEqual(exp[i][3].GetString(), got[i].Choice.Id, tag + " #" + i + " 選択肢");
                    kinds.Add(got[i].Def.Id);
                    fired++;
                }
                string diff = StateJson.Diff(c.GetProperty("post"), StateJson.Dump(s));
                Assert.IsNull(diff, tag + ": 効果を適用したあとの状態 " + diff);
            }
            Assert.Greater(fired, 150);
            Assert.GreaterOrEqual(kinds.Count, 45);
            TestContext.Out.WriteLine("イベントの発生と効果: " + n + " 状態・" + fired + " 件(" + kinds.Count + " 種類)が参照モデルと一致");
        }

        // ---- 検証に違反するイベント ----

        private static EventDef ToDef(JsonElement j)
        {
            Func<string, string> norm = x => x.Replace("_", "");
            Func<JsonElement, int[]> ints = a => a.ValueKind == JsonValueKind.Null ? null : a.EnumerateArray().Select(v => v.GetInt32()).ToArray();
            var e = new EventDef
            {
                Id = j.GetProperty("id").GetString(),
                Category = (EventCategory)Enum.Parse(typeof(EventCategory), j.GetProperty("cat").GetString()),
                Name = j.GetProperty("name").GetString(),
                Scope = (EventScope)Enum.Parse(typeof(EventScope), j.GetProperty("scope").GetString(), true),
                Months = ints(j.GetProperty("months")), Weeks = ints(j.GetProperty("weeks")),
                Phase = (EventPhaseKind)Enum.Parse(typeof(EventPhaseKind), j.GetProperty("phase").GetString(), true),
                Cond = j.GetProperty("cond").GetString(), P = j.GetProperty("p").GetDouble(), Cooldown = j.GetProperty("cooldown").GetInt32(),
                Once = j.GetProperty("once").ValueKind == JsonValueKind.Null ? OnceKind.None : (OnceKind)Enum.Parse(typeof(OnceKind), j.GetProperty("once").GetString(), true),
                MaxYear = j.GetProperty("max_year").ValueKind == JsonValueKind.Null ? (int?)null : j.GetProperty("max_year").GetInt32(),
                Priority = j.GetProperty("priority").GetInt32(), Exempt = j.GetProperty("exempt").GetBoolean(),
                Default = j.GetProperty("default").EnumerateArray().Select(v => v.GetString()).ToArray(),
            };
            var choices = new List<ChoiceDef>();
            foreach (JsonElement c in j.GetProperty("choices").EnumerateArray())
            {
                var cd = new ChoiceDef { Id = c.GetProperty("id").GetString(), Label = c.GetProperty("label").GetString(), Req = c.GetProperty("req").ValueKind == JsonValueKind.Null ? null : c.GetProperty("req").GetString() };
                var fx = new List<Effect>();
                foreach (JsonElement f in c.GetProperty("fx").EnumerateArray())
                {
                    var ef = new Effect { Op = (EffectOp)Enum.Parse(typeof(EffectOp), norm(f.GetProperty("op").GetString()), true) };
                    JsonElement v;
                    if (f.TryGetProperty("to", out v) && v.ValueKind == JsonValueKind.String) ef.To = (Target)Enum.Parse(typeof(Target), norm(v.GetString()), true);
                    if (f.TryGetProperty("d", out v)) ef.D = v.GetInt32();
                    if (f.TryGetProperty("p", out v) && v.ValueKind == JsonValueKind.Number) ef.P = v.GetDouble();
                    if (f.TryGetProperty("xu", out v)) ef.Xu = v.GetInt32();
                    if (f.TryGetProperty("kind", out v)) ef.Kind = StateJson.ModOf(v.GetString());
                    if (f.TryGetProperty("value", out v)) ef.Value = v.GetDouble();
                    if (f.TryGetProperty("weeks", out v)) ef.Weeks = v.GetInt32();
                    fx.Add(ef);
                }
                cd.Fx = fx.ToArray();
                choices.Add(cd);
            }
            e.Choices = choices.ToArray();
            return e;
        }

        private static string Head(string error)
        {
            int i = error.IndexOf(':');
            return i < 0 ? error : error.Substring(0, i);
        }

        [Test]
        public void Validator_ViolationCases_MatchReference()
        {
            int n = 0;
            foreach (JsonElement c in _root.GetProperty("neg").EnumerateArray())
            {
                string label = c.GetProperty("label").GetString();
                EventDef e = ToDef(c.GetProperty("event"));
                var list = new List<EventDef> { e };
                if (label == "V-01") list.Add(ToDef(c.GetProperty("event")));
                string[] expected = c.GetProperty("errors").EnumerateArray().Select(x => Head(x.GetString())).ToArray();
                string[] actual = EventValidator.Validate(list).Select(Head).ToArray();
                CollectionAssert.AreEqual(expected, actual, label);
                Assert.IsNotEmpty(expected, label + ": 違反が見つからない例は、例として意味がない");
                n++;
            }
            Assert.GreaterOrEqual(n, 25);
        }
    }
}
