using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Team;
using Ekiden.Domain.Training;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>06 §15 のテストベクタ(TV-1〜TV-7)と、イベントの手順(§7)の性質。</summary>
    public class EventVectorTests
    {
        private static readonly EventCatalog Catalog = EventCatalogData.Create();

        [TestCase("r.grade == 1 and r.far == 1 or r.overseas == 1", "(or (and (== r.grade 1) (== r.far 1)) (== r.overseas 1))")]
        [TestCase("not r.captain == 1 and r.fatigue >= 60", "(and (not (== r.captain 1)) (>= r.fatigue 60))")]
        [TestCase("max(1, t.last_rank.newyear - 1) + 2 * 3", "(+ (max 1 (- t.last_rank.newyear 1)) (* 2 3))")]
        [TestCase("-r.form < 1", "(< (neg r.form) 1)")]
        [TestCase("a - b - c", "(- (- a b) c)")]
        [TestCase("8 / 2 / 2 * 3", "(* (/ (/ 8 2) 2) 3)")]
        [TestCase("not not true or false and true", "(or (not (not true)) (and false true))")]
        public void TV1_Parse_Precedence(string src, string tree)
        {
            Assert.AreEqual(tree, Parser.Parse(src).ToString());
        }

        [TestCase("r.grade == 1 and q.x", DslCompiler.RoleR, "未知の変数 q.x")]
        [TestCase("p.grade >= 2", DslCompiler.RoleR, "束縛されていない役割 p")]
        [TestCase("max(1)", DslCompiler.RoleR, "未知の関数または引数の数 max")]
        [TestCase("r.stat.XXX > 1", DslCompiler.RoleR, "未知の変数 r.stat.XXX")]
        [TestCase("r.grade == ", DslCompiler.RoleR, "構文エラー")]
        [TestCase("r.grade == 1", 0, "束縛されていない役割 r")]
        [TestCase("has_skill(r, 1)", DslCompiler.RoleR, "第2引数は文字列")]
        [TestCase("ctr(r, 'nothing')", DslCompiler.RoleR, "未知のカウンター nothing")]
        [TestCase("r == 1", DslCompiler.RoleR, "役割 r は関数の引数にだけ使える")]
        [TestCase("r.grade # 1", DslCompiler.RoleR, "不正な文字")]
        public void TV2_Validation_Failures(string src, int roles, string expected)
        {
            var x = Assert.Throws<DslException>(() => DslCompiler.Compile(src, roles));
            StringAssert.Contains(expected, x.Message);
        }

        /// <summary>TV-3 の状態: 選手 r(学年1・遠方・疲労65・調子−1・やる気2・出身県13)と p(学年3・同県)。絆 450。abs_week = 18。</summary>
        private static TeamState Tv3State(out Runner r, out Runner p)
        {
            var s = new TeamState { Seed = 20260101UL, AbsWeek = 18 };
            r = new Runner { Id = 1, Grade = 1, Far = true, Overseas = false, Fatigue = 65, Form = -1, Motivation = 2, Captain = false, Pref = 13, HighSchool = 66, EnrollWeek = 0 };
            p = new Runner { Id = 2, Grade = 3, Pref = 13, HighSchool = 66 };
            s.Runners.Add(r);
            s.Runners.Add(p);
            s.Bonds.Set(1, 2, 450);
            s.LastRank[2] = 8;
            return s;
        }

        private static double Eval(TeamState s, Runner r, Runner p, string src, int roles)
        {
            return DslCompiler.Compile(src, roles).Eval(new EventContext(s, new ApproxEventWorld(s.Seed), null).Bind(r, p));
        }

        [TestCase("r.grade == 1 and r.far == 1 and r.overseas == 0 and r.weeks_in <= 16", 0.0)]
        [TestCase("r.motivation <= 2 and (r.fatigue >= 60 or r.form <= -1) and r.injury == 0", 1.0)]
        [TestCase("bond(r, p) >= 400 and p.grade >= r.grade + 2", 1.0)]
        [TestCase("cal.month == 4 and cal.week == 2", 1.0)]
        [TestCase("same_pref_n(r) >= 1", 1.0)]
        [TestCase("max(1, t.last_rank.newyear - 1)", 7.0)]
        [TestCase("1 / 0 + 2", 2.0)]
        [TestCase("r.fatigue / (r.grade - 1)", 0.0)]
        [TestCase("clamp(r.fatigue, 0, 50) + abs(-3) + min(4, 9)", 57.0)]
        [TestCase("senior_same_pref(r)", 1.0)]
        [TestCase("not senior_same_pref(p)", 1.0)]
        [TestCase("race.kind == 0 and race.team_rank == 99 and r.race.brake == 0", 1.0)]
        public void TV3_Evaluate(string src, double expected)
        {
            Runner r, p;
            TeamState s = Tv3State(out r, out p);
            Assert.AreEqual(expected, Eval(s, r, p, src, DslCompiler.RoleR | DslCompiler.RoleP), 1e-12, src);
        }

        [Test]
        public void TV4_Scheduler_Stream_FirstSixUniforms()
        {
            Pcg32 st = new KeyedRngFactory(20260101UL).Stream(RngRegistry.LabelEvent, 100, 0);
            double[] expect = { 0.428493, 0.560746, 0.236736, 0.974129, 0.569393, 0.713001 };
            for (int i = 0; i < expect.Length; i++) Assert.AreEqual(expect[i], st.Uniform(), 5e-7, "u" + i);
        }

        [Test]
        public void TV4_Selection_ByTheDocumentedExample()
        {
            // イベント A(p = 0.25、候補3人)は発生しない(u1 = 0.4285)/ イベント B(p = 1.0、候補2人)は発生し、当事者は floor(0.9741 × 2) = 1 番目(0 始まり)
            var s = new TeamState { Seed = 20260101UL, AbsWeek = 100 };
            for (int i = 1; i <= 3; i++) s.Runners.Add(new Runner { Id = i, Grade = 2, Pref = 1, EnrollWeek = 0 });
            var a = Authored("ev.test.a", EventScope.Runner, "r.grade == 2 and r.id <= 3", 0.25, priority: 60);
            var b = Authored("ev.test.b", EventScope.Runner, "r.id >= 2", 1.0, priority: 50);
            var cat = new EventCatalog(new[] { a, b }, new SkillDef[0], new KeyValuePair<string, string>[0]).Prepare();
            var phase = new EventPhase(cat, new KeyedRngFactory(20260101UL), new ApproxEventWorld(20260101UL));
            EventSelection sel = phase.Select(s, null);
            Assert.AreEqual(1, sel.Fired.Count);
            Assert.AreEqual("ev.test.b", sel.Fired[0].Def.Id);
            Assert.AreEqual(3, sel.Fired[0].R.Id, "候補は id 2・3 の2人、u2 = 0.9741 → 1 番目 = id 3(A の u2 は引いたが使わない)");
        }

        private static EventDef Authored(string id, EventScope scope, string cond, double p, int priority = 50, bool exempt = false, int cooldown = 0, OnceKind once = OnceKind.None,
            ChoiceDef[] choices = null, int[] months = null, int[] weeks = null, int? maxYear = null)
        {
            return new EventDef
            {
                Id = id, Category = EventCategory.Individual, Name = id, Scope = scope, Cond = cond, P = p, Priority = priority, Exempt = exempt, Cooldown = cooldown, Once = once,
                Months = months, Weeks = weeks, MaxYear = maxYear,
                Choices = choices ?? new[] { new ChoiceDef { Id = "ok", Label = "ok", Fx = new Effect[0] } }, Default = new[] { "ok" },
            };
        }

        [Test]
        public void TV5_Xp_LevelUp()
        {
            var r = new Runner { Id = 1 };
            for (int i = 0; i < StatInfo.Count; i++) { r.Stats[i] = 60; r.Potential[i] = 90; }
            r.Xp[(int)Stat.STA] = 500;
            int up = Progression.ApplyXp(r, (int)Stat.STA, 200);
            Assert.AreEqual(1, up);
            Assert.AreEqual(61, r.Stats[(int)Stat.STA]);
            Assert.AreEqual(140, r.Xp[(int)Stat.STA]);
        }

        [Test]
        public void TV5_Xp_StopsAtPotential_AndClearsXp()
        {
            var r = new Runner { Id = 1 };
            for (int i = 0; i < StatInfo.Count; i++) { r.Stats[i] = 60; r.Potential[i] = 61; }
            Progression.ApplyXp(r, 0, 5000);
            Assert.AreEqual(61, r.Stats[0]);
            Assert.AreEqual(0, r.Xp[0]);
        }

        [Test]
        public void TV5_ModifierComposition_AndClamp()
        {
            Assert.AreEqual(0.9826, 1.03 * 1.06 * 0.9, 1e-4);
            Assert.AreEqual(1.2, EventConstants.ClampMod(ModKind.EMult, 1.2 * 1.15), 1e-12);
            Assert.AreEqual(0.8, EventConstants.ClampMod(ModKind.EMult, 0.5), 1e-12);
            Assert.AreEqual(0.06, EventConstants.ClampMod(ModKind.RAdd, 0.2), 1e-12);
            Assert.AreEqual(8, EventConstants.ClampMod(ModKind.ScoutPts, 12), 1e-12);
        }

        private static void ApplySkill(TeamState s, Runner r, string id)
        {
            var choice = new ChoiceDef { Id = "ok", Fx = new[] { new Effect { Op = EffectOp.Skill, To = Target.R, SkillId = id } } };
            EffectApplier.Apply(s, Catalog, Catalog.Events.First(e => e.Id == "ev.ind.slump"), choice, r, null, new Pcg32(1));
        }

        [Test]
        public void TV5_Skill_Exclusive_And_Cap()
        {
            var s = new TeamState();
            var r = new Runner { Id = 1 };
            r.Skills.Add("skill.pack_runner");
            s.Runners.Add(r);
            ApplySkill(s, r, "solo_weak");
            Assert.IsFalse(r.Skills.Contains("skill.solo_weak"), "集団走○を持つ選手に単独走×は付かない");
            ApplySkill(s, r, "anchor");
            Assert.IsTrue(r.Skills.Contains("skill.anchor"));
            ApplySkill(s, r, "anchor");
            Assert.AreEqual(2, r.Skills.Count, "すでに持つ能力は増えない");
            var q = new Runner { Id = 2 };
            for (int i = 0; i < 8; i++) q.Skills.Add("skill.x" + i);
            ApplySkill(s, q, "anchor");
            Assert.AreEqual(8, q.Skills.Count, "能力は8個まで");
            Assert.IsFalse(q.Skills.Contains("skill.anchor"));
        }

        [Test]
        public void TV6_AwakeningProbabilities()
        {
            Assert.AreEqual(0.10, EventConstants.AwakenP(SkillRank.Blue));
            Assert.AreEqual(0.05, EventConstants.AwakenP(SkillRank.Gold));
            Assert.AreEqual(0.08, EventConstants.AwakenP(SkillRank.Red));
            Assert.AreEqual(0.344, 1 - Math.Pow(1 - 0.10, 4), 5e-4);
            Assert.AreEqual(0.185, 1 - Math.Pow(1 - 0.05, 4), 5e-4);
            Assert.AreEqual(0.284, 1 - Math.Pow(1 - 0.08, 4), 5e-4);
            foreach (EventDef e in Catalog.Events.Where(x => x.Category == EventCategory.Awakening))
            {
                SkillDef sk = Catalog.Skills.First(k => k.Id == e.SkillId);
                Assert.AreEqual(EventConstants.AwakenP(sk.Rank), e.P, e.Id);
                Assert.AreEqual(OnceKind.Runner, e.Once, e.Id);
                Assert.IsTrue(e.Exempt, e.Id);
                Assert.AreEqual(95, e.Priority, e.Id);
            }
        }

        [Test]
        public void TV7_Catalogue_Is_52_And_Valid()
        {
            Assert.AreEqual(34, Catalog.Events.Count(e => e.Category != EventCategory.Awakening));
            Assert.AreEqual(18, Catalog.Events.Count(e => e.Category == EventCategory.Awakening));
            Assert.AreEqual(52, Catalog.Events.Count);
            Assert.AreEqual(19, Catalog.Skills.Count);
            CollectionAssert.IsEmpty(EventValidator.Validate(Catalog.Events));
        }

        [Test]
        public void Catalogue_OrderIsPriorityDescThenId()
        {
            var list = Catalog.ByPriority;
            for (int i = 1; i < list.Count; i++)
            {
                bool ok = list[i - 1].Priority > list[i].Priority || (list[i - 1].Priority == list[i].Priority && string.CompareOrdinal(list[i - 1].Id, list[i].Id) < 0);
                Assert.IsTrue(ok, list[i - 1].Id + " → " + list[i].Id);
            }
        }

        // ---- 手順(06 §7)の性質 ----

        private static EventPhase PhaseOf(IEnumerable<EventDef> defs, ulong seed = 7UL)
        {
            var cat = new EventCatalog(defs, new SkillDef[0], new KeyValuePair<string, string>[0]).Prepare();
            return new EventPhase(cat, new KeyedRngFactory(seed), new ApproxEventWorld(seed));
        }

        private static TeamState Team(int n, int absWeek = 100, ulong seed = 7UL)
        {
            var s = new TeamState { Seed = seed, AbsWeek = absWeek };
            for (int i = 1; i <= n; i++) s.Runners.Add(new Runner { Id = i, Grade = 1 + i % 4, Pref = 1 + i % 3, EnrollWeek = 0 });
            return s;
        }

        [Test]
        public void Select_CapsAtTwoPerWeek_InPriorityOrder()
        {
            var phase = PhaseOf(new[]
            {
                Authored("ev.t.c", EventScope.Team, "t.size >= 1", 1.0, priority: 10),
                Authored("ev.t.a", EventScope.Team, "t.size >= 1", 1.0, priority: 90),
                Authored("ev.t.b", EventScope.Team, "t.size >= 1", 1.0, priority: 50),
            });
            EventSelection sel = phase.Select(Team(5), null);
            CollectionAssert.AreEqual(new[] { "ev.t.a", "ev.t.b" }, sel.Fired.Select(f => f.Def.Id).ToArray());
            Assert.IsTrue(sel.CappedByWeek);
        }

        [Test]
        public void Select_OneRunnerIsSubjectOnlyOncePerWeek()
        {
            var phase = PhaseOf(new[]
            {
                Authored("ev.t.a", EventScope.Runner, "r.id >= 1", 1.0, priority: 90),
                Authored("ev.t.b", EventScope.Runner, "r.id >= 1", 1.0, priority: 50),
            });
            EventSelection one = phase.Select(Team(1), null);
            Assert.AreEqual(1, one.Fired.Count, "選手が1人なら2件目の候補がない");
            EventSelection two = phase.Select(Team(2), null);
            Assert.AreEqual(2, two.Fired.Count);
            Assert.AreNotEqual(two.Fired[0].R.Id, two.Fired[1].R.Id);
        }

        [Test]
        public void Select_DoesNotChangeState_AndIsRepeatable()
        {
            var phase = PhaseOf(Catalog.Events);
            TeamState s = Team(12);
            foreach (Runner r in s.Runners) { r.Fatigue = 70; r.Motivation = 2; }
            string before = StateJson.Dump(s).GetRawText();
            EventSelection a = phase.Select(s, null);
            EventSelection b = phase.Select(s, null);
            Assert.AreEqual(before, StateJson.Dump(s).GetRawText());
            CollectionAssert.AreEqual(a.Fired.Select(f => f.Def.Id + ":" + (f.R == null ? 0 : f.R.Id)).ToArray(), b.Fired.Select(f => f.Def.Id + ":" + (f.R == null ? 0 : f.R.Id)).ToArray());
        }

        [Test]
        public void Resolve_Records_CooldownOnceAndYearCounts()
        {
            var phase = PhaseOf(new[]
            {
                Authored("ev.t.once", EventScope.Runner, "r.id == 1", 1.0, cooldown: 10, once: OnceKind.Runner),
                Authored("ev.t.team", EventScope.Team, "t.size >= 1", 1.0, cooldown: 5, exempt: true),
            });
            TeamState s = Team(3, 100);
            var pol = new DefaultChoicePolicy();
            List<FiredEvent> w1 = phase.Run(s, null, pol);
            Assert.AreEqual(2, w1.Count);
            Assert.AreEqual(1, s.YearCounts["ev.t.once"]);
            Assert.AreEqual(1, s.YearTotal, "exempt のイベントは年度の上限を数えない");
            Assert.IsTrue(s.FindRunner(1).EventOnce.Contains("ev.t.once"));
            Assert.AreEqual(100, s.Fired["ev.t.team"].Last);
            s.AbsWeek = 102;
            Assert.AreEqual(0, phase.Select(s, null).Fired.Count, "once のイベントは再び起こらず、team のイベントはクールダウン中");
            s.AbsWeek = 105;
            Assert.AreEqual(1, phase.Select(s, null).Fired.Count, "5週あけば team のイベントがまた起こる");
        }

        [Test]
        public void Resolve_RunnerMonthLimit_AppliesToNonExempt()
        {
            var phase = PhaseOf(new[]
            {
                Authored("ev.t.a", EventScope.Runner, "r.id == 1", 1.0, priority: 60),
                Authored("ev.t.b", EventScope.Runner, "r.id == 1", 1.0, priority: 50),
                Authored("ev.t.x", EventScope.Runner, "r.id == 1", 1.0, priority: 40, exempt: true),
            });
            TeamState s = Team(2, 100);                 // abs_week 100 → 年内 4 週 = 2 年度目の 5 月第 1 週(month 1)
            phase.Run(s, null, new DefaultChoicePolicy());
            Assert.AreEqual(1 + 100 / 48 * 12, s.FindRunner(1).LastEventMonth);
            s.AbsWeek = 101;                            // 同じ月
            EventSelection sel = phase.Select(s, null);
            CollectionAssert.AreEqual(new[] { "ev.t.x" }, sel.Fired.Select(f => f.Def.Id).ToArray(), "同じ月は exempt でないイベントの候補から外れる");
        }

        [Test]
        public void Choice_DoesNotChangeTheSelection()
        {
            var cat = Catalog;
            var rngs = new KeyedRngFactory(11UL);
            for (int w = 0; w < 200; w++)
            {
                TeamState s1 = Team(14, 48 * 3 + w, 11UL), s2 = Team(14, 48 * 3 + w, 11UL);
                foreach (Runner r in s1.Runners.Concat(s2.Runners)) { r.Fatigue = 65; r.Motivation = 2; r.Form = -1; r.Far = r.Id % 2 == 0; }
                var ph = new EventPhase(cat, rngs, new ApproxEventWorld(11UL), 5.0);
                var a = ph.Run(s1, null, new DefaultChoicePolicy()).Select(f => f.Def.Id + ":" + (f.R == null ? 0 : f.R.Id)).ToArray();
                var b = ph.Run(s2, null, new UniformChoicePolicy()).Select(f => f.Def.Id + ":" + (f.R == null ? 0 : f.R.Id)).ToArray();
                CollectionAssert.AreEqual(a, b, "週 " + w + ": 選択肢の選び方は発生の判定を変えない(06 §7.5)");
            }
        }

        [Test]
        public void Performance_Select_36Runners_UnderBudget()
        {
            var ph = new EventPhase(Catalog, new KeyedRngFactory(5UL), new ApproxEventWorld(5UL));
            TeamState s = Team(36, 48 * 2);
            foreach (Runner r in s.Runners) { r.Fatigue = 70; r.Motivation = 2; r.Pref = 1 + r.Id % 5; }
            for (int i = 0; i < 20; i++) { s.AbsWeek = 96 + i; ph.Select(s, null); }       // 暖機
            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int N = 500;
            for (int i = 0; i < N; i++) { s.AbsWeek = 96 + i; ph.Select(s, null); }
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds / N;
            TestContext.Out.WriteLine("EventPhase.Select(36 人・52 イベント): 1 週あたり " + ms.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " ms");
            Assert.Less(ms, 5.0, "1 回の Domain 呼び出しの予算(100 ms)に対して、週のイベント判定は 5 ms 未満であること");
        }

        [Test]
        public void Effects_Funds_Guidance_Motivation_AreClamped()
        {
            var s = new TeamState { Funds = 100, GuidanceLeft = 1 };
            var r = new Runner { Id = 1, Motivation = 5, Fatigue = 5 };
            s.Runners.Add(r);
            var choice = new ChoiceDef
            {
                Id = "x",
                Fx = new[]
                {
                    new Effect { Op = EffectOp.Funds, D = -500 }, new Effect { Op = EffectOp.Guide, D = -3 },
                    new Effect { Op = EffectOp.Mot, To = Target.R, D = 1 }, new Effect { Op = EffectOp.Fat, To = Target.R, D = -20 },
                },
            };
            EffectApplier.Apply(s, Catalog, Catalog.Events[0], choice, r, null, new Pcg32(1));
            Assert.AreEqual(0, s.Funds);
            Assert.AreEqual(0, s.GuidanceLeft);
            Assert.AreEqual(5, r.Motivation);
            Assert.AreEqual(0, r.Fatigue);
        }

        [Test]
        public void Effect_Mod_ReplacesSameKindTargetAndSource()
        {
            var s = new TeamState { AbsWeek = 10 };
            var r = new Runner { Id = 4 };
            s.Runners.Add(r);
            EventDef ev = Catalog.Events.First(e => e.Id == "ev.ind.slump");
            var c1 = new ChoiceDef { Id = "x", Fx = new[] { new Effect { Op = EffectOp.Mod, To = Target.R, Kind = ModKind.InjMult, Value = 1.5, Weeks = 4 } } };
            var c2 = new ChoiceDef { Id = "y", Fx = new[] { new Effect { Op = EffectOp.Mod, To = Target.R, Kind = ModKind.InjMult, Value = 0.8, Weeks = 8 } } };
            EffectApplier.Apply(s, Catalog, ev, c1, r, null, new Pcg32(1));
            EffectApplier.Apply(s, Catalog, ev, c2, r, null, new Pcg32(1));
            Assert.AreEqual(1, s.Mods.Count);
            Assert.AreEqual(0.8, s.Mods[0].Value);
            Assert.AreEqual(18, s.Mods[0].Until);
            Assert.AreEqual(4, s.Mods[0].To);
        }

        [Test]
        public void Effect_ObRole_PicksStrongestFreeOb_CoachNeedsCorp()
        {
            var s = new TeamState { AbsWeek = 50 };
            s.Obs.Add(new OBRecord { Pref = 1, Corp = false, Power = 70 });
            s.Obs.Add(new OBRecord { Pref = 2, Corp = true, Power = 60 });
            s.Obs.Add(new OBRecord { Pref = 3, Corp = true, Power = 65 });
            EventDef ev = Catalog.Events.First(e => e.Id == "ev.team.ob_coach");
            var coach = new ChoiceDef { Id = "a", Fx = new[] { new Effect { Op = EffectOp.ObRole, Role = ObRole.Coach, Weeks = 48 } } };
            EffectApplier.Apply(s, Catalog, ev, coach, null, null, new Pcg32(1));
            Assert.AreEqual(ObRole.Coach, s.Obs[2].Role, "コーチは実業団に進んだOBの中で最も力が高い人");
            Assert.AreEqual(98, s.Obs[2].Until);
            Assert.AreEqual(1.06, s.ModSum(ModKind.EMult), 1e-12);
            var help = new ChoiceDef { Id = "b", Fx = new[] { new Effect { Op = EffectOp.ObRole, Role = ObRole.ScoutHelper, Weeks = 48 } } };
            EffectApplier.Apply(s, Catalog, ev, help, null, null, new Pcg32(1));
            Assert.AreEqual(ObRole.ScoutHelper, s.Obs[0].Role, "コーチ以外は実業団でなくてもよい(役割のない中で力が最も高い人)");
            Assert.AreEqual(4, s.ModSum(ModKind.ScoutPts), 1e-12);
            s.AbsWeek = 98;
            s.ExpireTimed();
            Assert.AreEqual(ObRole.None, s.Obs[2].Role);
            Assert.AreEqual(0, s.Mods.Count);
        }

        [Test]
        public void Effect_Captain_ChoosesAmongSeniors_FirstOnTies()
        {
            var s = new TeamState();
            for (int i = 1; i <= 4; i++) s.Runners.Add(new Runner { Id = i, Grade = i, BondAvg = 100 });
            s.Runners[3].Captain = true;
            s.Runners[0].BondAvg = 900;                       // 1年生は候補外
            s.Runners[2].BondAvg = 300;
            s.Runners[3].BondAvg = 300;                       // 同点 → 先に並んでいる 3 年生
            EventDef ev = Catalog.Events.First(e => e.Id == "ev.team.captain");
            var c = new ChoiceDef { Id = "a", Fx = new[] { new Effect { Op = EffectOp.Captain, Select = CaptainSelect.TopBond } } };
            EffectApplier.Apply(s, Catalog, ev, c, null, null, new Pcg32(1));
            CollectionAssert.AreEqual(new[] { false, false, true, false }, s.Runners.Select(r => r.Captain).ToArray());
        }

        [Test]
        public void Refresh_RanksByLongP_StableOnTies_AndGroups()
        {
            var s = new TeamState();
            for (int i = 1; i <= 24; i++)
            {
                var r = new Runner { Id = i };
                for (int k = 0; k < StatInfo.Count; k++) { r.Stats[k] = 50; r.Potential[k] = 60; }
                if (i == 7) r.Stats[(int)Stat.STA] = 70;
                if (i == 3) r.InjurySeverity = InjurySeverity.Minor;
                s.Runners.Add(r);
            }
            s.Refresh();
            Assert.AreEqual(1, s.FindRunner(7).PRank);
            Assert.AreEqual(2, s.FindRunner(1).PRank, "同点は並びの順");
            Assert.AreEqual(3, s.FindRunner(2).PRank);
            Assert.IsFalse(s.FindRunner(3).Regular, "故障中はレギュラーに入れない");
            Assert.IsTrue(s.FindRunner(1).Regular);
            Assert.AreEqual(0, s.FindRunner(1).Group);
            Assert.AreEqual(2, s.FindRunner(24).Group);
            Assert.AreEqual(10.0, s.FindRunner(1).GapAvg, 1e-12);
        }

        [Test]
        public void Calendar_NextRace()
        {
            int w, k;
            Calendar.NextRace(25, out w, out k); Assert.AreEqual((0, 1), (w, k));
            Calendar.NextRace(26, out w, out k); Assert.AreEqual((2, 2), (w, k));
            Calendar.NextRace(36, out w, out k); Assert.AreEqual((0, 3), (w, k));
            Calendar.NextRace(37, out w, out k); Assert.AreEqual((36, 1), (w, k), "次が来年度なら翌年の値");
            Calendar.NextRace(0, out w, out k); Assert.AreEqual((25, 1), (w, k));
            Assert.AreEqual(-1, Calendar.FloorDiv(-1, 48));
            Assert.AreEqual(47, Calendar.FloorMod(-1, 48));
        }

        [Test]
        public void Bonds_ClampAndDropZero()
        {
            var b = new BondTable();
            b.Add(2, 1, 600);
            b.Add(1, 2, 600);
            Assert.AreEqual(1000, b.Get(1, 2));
            b.Add(1, 2, -2000);
            Assert.AreEqual(0, b.Get(2, 1));
            Assert.AreEqual(0, b.Count);
        }
    }
}
