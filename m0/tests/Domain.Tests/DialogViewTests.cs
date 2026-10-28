using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;
using Ekiden.Domain.Training;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>ダイアログの読み取り API(SC-36 イベント・SC-41 コンディション警告・SC-42 故障の知らせ。08b)。</summary>
    public class DialogViewTests
    {
        private static GameSession Game(ulong seed = 20260403UL, Difficulty d = Difficulty.Mid) { return GameSession.New(seed, d); }

        // ------------------------------------------------------------ イベント
        [Test]
        public void EventDialog_ForEveryEventInTheCatalog_MirrorsTheDefinition()
        {
            GameSession gs = Game();
            Runner a = gs.State.Runners[0], b = gs.State.Runners[1];
            int checkedEvents = 0, interactive = 0, withDisabled = 0, awakenings = 0;
            foreach (EventDef def in gs.Catalog.Events)
            {
                var f = new FiredEvent { Def = def, R = def.Roles >= 1 ? a : null, P = def.Roles >= 2 ? b : null };
                EventDialogView v = gs.EventDialog(f);
                List<ChoiceDef> valid = gs.ValidChoices(f, null);
                Assert.AreEqual(def.Id, v.EventId);
                Assert.AreEqual(def.Id + ".title", v.TitleKey);
                Assert.AreEqual(def.Category, v.Category);
                Assert.AreEqual(def.Choices.Length >= 2, v.Interactive);
                Assert.AreEqual(def.Choices.Length, v.Choices.Count);
                Assert.AreEqual(f.R != null, v.Runner != null);
                Assert.AreEqual(f.P != null, v.Partner != null);
                if (v.Runner != null) Assert.AreEqual(a.Id, v.Runner.Id);
                for (int i = 0; i < def.Choices.Length; i++)
                {
                    ChoiceDef c = def.Choices[i];
                    ChoiceView cv = v.Choices[i];
                    Assert.AreEqual(c.Id, cv.Id);
                    Assert.AreEqual(def.Id + ".choice." + c.Id, cv.LabelKey);
                    Assert.AreEqual(valid.Any(x => x.Id == c.Id), cv.Enabled, def.Id + " " + c.Id);
                    Assert.AreEqual(Math.Min(3, c.Fx.Length), cv.Effects.Count, "要約は 3 項目まで(R-15)");
                    Assert.AreEqual(Math.Max(0, c.Fx.Length - 3), cv.MoreEffects);
                    for (int k = 0; k < cv.Effects.Count; k++)
                    {
                        Effect e = c.Fx[k];
                        EffectView ev = cv.Effects[k];
                        Assert.AreEqual(e.Op, ev.Op);
                        Assert.AreEqual(e.To, ev.To);
                        Assert.AreEqual(e.D, ev.Amount);
                        Assert.AreEqual(e.P, ev.Probability);
                        if (e.Op == EffectOp.Xp) { Assert.AreEqual(e.Xu, ev.Xu); Assert.AreEqual(e.StatMode, ev.StatMode); Assert.AreEqual(e.StatMode == XpStatMode.Fixed ? (Stat?)(Stat)e.Stat : null, ev.Stat); }
                        if (e.Op == EffectOp.Skill) { Assert.AreEqual("skill." + e.SkillId, ev.SkillId); Assert.IsNotNull(ev.SkillRank); }
                        if (e.Op == EffectOp.Mod) { Assert.AreEqual(e.Kind, ev.Mod); Assert.AreEqual(e.Value, ev.ModValue); Assert.AreEqual(e.Weeks, ev.Weeks); }
                        if (e.Op == EffectOp.Inj) Assert.AreEqual(e.Sev, ev.Severity);
                        if (e.Op == EffectOp.ObRole) Assert.AreEqual(e.Role, ev.Role);
                    }
                }
                if (valid.Count > 0)
                {
                    ChoiceDef expectedDefault = new DefaultChoicePolicy().Choose(def, valid, null);
                    Assert.AreEqual(1, v.Choices.Count(c => c.IsDefault), "既定の選択肢はちょうど 1 つ");
                    Assert.AreEqual(expectedDefault.Id, v.Choices.Single(c => c.IsDefault).Id);
                    Assert.IsTrue(v.Choices.Single(c => c.IsDefault).Enabled);
                }
                Assert.AreEqual(def.Category == EventCategory.Awakening, v.Awakening);
                if (v.Awakening) { awakenings++; Assert.IsNotNull(v.SkillId); StringAssert.StartsWith("skill.", v.SkillId); Assert.IsNotNull(v.SkillRank); }
                else Assert.IsNull(v.SkillId);
                checkedEvents++;
                if (v.Interactive) interactive++;
                if (v.Choices.Any(c => !c.Enabled)) withDisabled++;
            }
            Assert.Greater(checkedEvents, 40);
            Assert.Greater(interactive, 10);
            Assert.Greater(awakenings, 5);
            Assert.GreaterOrEqual(withDisabled, 0);
        }

        [Test]
        public void EventDialog_DisabledChoices_FollowTheRequirement()
        {
            GameSession gs = Game();
            // 指導ポイントが要る選択肢(req を持つもの)を探して、ポイントの有無で有効・無効が変わることを確かめる
            EventDef def = gs.Catalog.Events.FirstOrDefault(e => e.Choices.Any(c => !string.IsNullOrEmpty(c.Req)));
            Assert.IsNotNull(def, "req を持つ選択肢がある");
            Runner a = gs.State.Runners[0], b = gs.State.Runners[1];
            var f = new FiredEvent { Def = def, R = def.Roles >= 1 ? a : null, P = def.Roles >= 2 ? b : null };
            EventDialogView v = gs.EventDialog(f);
            foreach (ChoiceDef c in def.Choices.Where(c => !string.IsNullOrEmpty(c.Req)))
                Assert.AreEqual(gs.ValidChoices(f, null).Any(x => x.Id == c.Id), v.Choices.Single(x => x.Id == c.Id).Enabled);
        }

        // ------------------------------------------------------------ コンディション警告
        [Test]
        public void Warning_ListsOnlyTheTiredButNotYetSafetyNet_AndPredictsNextWeekExactly()
        {
            GameSession gs = Game(51UL, Difficulty.Elite);
            int verified = 0;
            for (int month = 0; month < 12; month++)
            {
                while (gs.YearWeek != month * 4 + 2) gs.StepWeek();
                // この週(月の第 3 週)を解決し、警告を出す時点(週の終わり。次の週へはまだ進まない)に置く
                gs.ResolveTraining();
                RaceInfo? o;
                gs.PlayWeekRaces(out o);
                gs.ResolveWeekEvents(gs.SelectWeekEvents(o), o, new DefaultChoicePolicy());
                gs.FinishWeek();
                bool camp = month == 4;
                int thr = camp ? 80 : 70;
                List<Runner> rs = gs.State.Runners.OrderBy(r => r.Id).ToList();
                foreach (Runner r in rs) { r.Fatigue = 30; r.InjurySeverity = InjurySeverity.None; r.InjuryWeeksLeft = 0; }
                rs[0].Fatigue = thr;                 // 境界ちょうど(含む)
                rs[1].Fatigue = thr - 1;             // 届かない
                rs[2].Fatigue = 85;                  // 安全網の値(含まない)
                rs[3].Fatigue = thr + 3;
                rs[4].Fatigue = thr + 4; rs[4].InjurySeverity = InjurySeverity.Minor; rs[4].InjuryWeeksLeft = 2;       // 故障中は含まない
                ConditionWarnView w = gs.ConditionWarning();
                Assert.AreEqual(thr, w.Threshold);
                Assert.AreEqual(camp, w.Camp);
                CollectionAssert.AreEqual(new[] { rs[0].Id, rs[3].Id }, w.Rows.Select(x => x.Row.Id).ToList(), "月 " + month);
                var predicted = w.Rows.ToDictionary(x => x.Row.Id, x => x);
                Assert.AreEqual(thr, predicted[rs[0].Id].Fatigue);
                // 次の週を実際に解決して、予測と突き合わせる(故障が起きなかった選手は正確に一致する)
                gs.AdvanceWeek();
                List<WeekReport> reps = gs.ResolveTraining(out List<int> ids);
                foreach (var kv in predicted)
                {
                    WeekReport rep = reps[ids.IndexOf(kv.Key)];
                    if (rep.NewInjury.HasValue) continue;
                    Assert.AreEqual(rep.FEnd, kv.Value.NextFatigue, "次の週の疲労の予測 月 " + month + " 選手 " + kv.Key);
                    Assert.AreEqual(rep.FEnd >= 85, kv.Value.SafetyNext);
                    verified++;
                }
                RaceInfo? o2;
                gs.PlayWeekRaces(out o2);
                gs.ResolveWeekEvents(gs.SelectWeekEvents(o2), o2, new DefaultChoicePolicy());
                gs.FinishWeek();
                gs.AdvanceWeek();
            }
            Assert.GreaterOrEqual(verified, 6, "予測と実際の突き合わせが十分にできた");
        }

        [Test]
        public void Warning_SafetyNextAndInjuryChanceFollowTheForecast_ForAnyMenuAndFatigue()
        {
            GameSession gs = Game(57UL);
            while (gs.YearWeek != 10) gs.StepWeek();
            Runner r = gs.State.Runners[0];
            foreach (Runner x in gs.State.Runners) { x.Fatigue = 30; x.InjurySeverity = InjurySeverity.None; x.InjuryWeeksLeft = 0; }
            string[] mileages = { "mil.low", "mil.std", "mil.high", "mil.max" };
            string[] sessions = { "ks.pace_run", "ks.threshold", "ks.long_run", "ks.interval", "ks.hill", "ks.core", "ks.rest" };
            int rows = 0;
            foreach (string mil in mileages)
                foreach (string k1 in sessions)
                {
                    gs.CancelGuide(r.Id);
                    if (!gs.GuideMenu(r.Id, new MenuSpec { Key1 = k1, Key2 = "ks.core", Mileage = mil }, confirmed: true).Ok) continue;
                    double prevP = -1;
                    for (int f = 70; f <= 84; f++)
                    {
                        r.Fatigue = f;
                        WarnRowView row = gs.ConditionWarning().Rows.Single(x => x.Row.Id == r.Id);
                        Assert.AreEqual(f, row.Fatigue);
                        Assert.AreEqual(row.NextFatigue >= 85, row.SafetyNext, k1 + " " + mil + " 疲労 " + f + " → " + row.NextFatigue);
                        Assert.That(row.InjuryNext, Is.InRange(0.0, 1.0));
                        Assert.GreaterOrEqual(row.InjuryNext, prevP, "疲労が高いほど故障しやすい(" + k1 + " " + mil + ")");
                        prevP = row.InjuryNext;
                        Assert.IsTrue(row.HasIndividualMenu);
                        rows++;
                    }
                }
            Assert.Greater(rows, 100);
        }

        [Test]
        public void Warning_MarksRunnersWithAnIndividualMenu()
        {
            GameSession gs = Game(53UL);
            while (gs.YearWeek != 10) gs.StepWeek();
            Runner r = gs.State.Runners[0];
            r.Fatigue = 75;
            Assert.IsTrue(gs.GuideMenu(r.Id, new MenuSpec { Key1 = "ks.pace_run", Key2 = "ks.core", Mileage = "mil.std" }).Ok);
            ConditionWarnView w = gs.ConditionWarning();
            Assert.IsTrue(w.Rows.Single(x => x.Row.Id == r.Id).HasIndividualMenu);
            foreach (WarnRowView x in w.Rows.Where(x => x.Row.Id != r.Id)) Assert.IsFalse(x.HasIndividualMenu);
        }

        // ------------------------------------------------------------ 故障の知らせ
        [Test]
        public void InjuryDialog_ShowsTheImpactOnTheNextRace_AndTheSubstitutes()
        {
            GameSession gs = Game(55UL, Difficulty.Elite);
            while (gs.YearWeek != TeamWeek.EntryDeadlineWeeks[0] + 1) gs.StepWeek();
            Assert.IsTrue(gs.PrepareYear().Entrants(RaceKind.Fall).Contains(gs.School.Id), "第一戦に出られる(試験の前提)");
            Assert.IsTrue(gs.SetOrder(RaceKind.Fall, gs.SuggestOrder(RaceKind.Fall)).Ok, "オーダーを提出する");
            OrderView ov = gs.Order(RaceKind.Fall);
            OrderSectionView sec = ov.Sections[3];
            int hurtId = sec.RunnerId.Value;
            Runner hurt = gs.State.FindRunner(hurtId);
            hurt.InjurySeverity = InjurySeverity.Moderate;
            hurt.InjuryWeeksLeft = 3;
            InjuryDialogView v = gs.InjuryDialog(hurtId);
            int weeks, kind;
            Calendar.NextRace(gs.YearWeek, out weeks, out kind);
            Assert.AreEqual(hurtId, v.Row.Id);
            Assert.AreEqual(InjurySeverity.Moderate, v.Severity);
            Assert.AreEqual(3, v.WeeksLeft);
            Assert.AreEqual(RaceKind.Fall, v.NextRace);
            Assert.AreEqual(weeks, v.WeeksUntilRace);
            Assert.AreEqual(3 > weeks, v.MissesRace);
            Assert.IsTrue(v.Registered);
            Assert.IsTrue(v.InOrder);
            Assert.AreEqual(3, v.Section);
            var placed = new HashSet<int>(gs.Order(RaceKind.Fall).Sections.Select(s => s.RunnerId.Value));
            int available = gs.State.Entries[0].Select(gs.State.FindRunner).Count(r => r.InjurySeverity == InjurySeverity.None && !placed.Contains(r.Id));
            Assert.Greater(available, 3, "控えが 4 人以上いる(試験の前提)");
            Assert.AreEqual(3, v.Substitutes.Count, "代役の候補は 3 人まで");
            foreach (OrderCandidateView c in v.Substitutes)
            {
                Assert.AreEqual(InjurySeverity.None, c.Row.Injury);
                Assert.IsFalse(placed.Contains(c.Row.Id), "すでに先発の選手は代役にしない");
                Assert.IsTrue(gs.State.Entries[0].Contains(c.Row.Id), "登録メンバーから");
            }
            for (int i = 1; i < v.Substitutes.Count; i++) Assert.GreaterOrEqual(v.Substitutes[i - 1].Fit, v.Substitutes[i].Fit, "適性の高い順");
            var best = gs.State.Entries[0].Select(gs.State.FindRunner).Where(r => r.InjurySeverity == InjurySeverity.None && !placed.Contains(r.Id))
                         .Select(r => gs.SectionFit(RaceKind.Fall, r.Id, 3).Key).OrderByDescending(x => x).First();
            Assert.AreEqual(best, v.Substitutes[0].Fit, 1e-12, "先頭は、その区間にいちばん合う控え");

            // オーダーを出していなければ、おまかせの配置が故障者を外して組み直されるので先発ではない
            GameSession auto = Game(55UL, Difficulty.Elite);
            while (auto.YearWeek != TeamWeek.EntryDeadlineWeeks[0] + 1) auto.StepWeek();
            int autoStarter = auto.SuggestOrder(RaceKind.Fall)[3];
            auto.State.FindRunner(autoStarter).InjurySeverity = InjurySeverity.Moderate;
            auto.State.FindRunner(autoStarter).InjuryWeeksLeft = 3;
            Assert.IsFalse(auto.InjuryDialog(autoStarter).InOrder);
            Assert.IsTrue(auto.InjuryDialog(autoStarter).Registered);

            // 控え(出走しない登録メンバー)・登録していない選手
            Runner bench = gs.State.Entries[0].Select(gs.State.FindRunner).First(r => !placed.Contains(r.Id) && r.Id != hurtId);
            bench.InjurySeverity = InjurySeverity.Minor;
            bench.InjuryWeeksLeft = 1;
            InjuryDialogView b = gs.InjuryDialog(bench.Id);
            Assert.IsTrue(b.Registered);
            Assert.IsFalse(b.InOrder);
            Assert.IsNull(b.Section);
            Assert.IsEmpty(b.Substitutes);
            Assert.IsFalse(b.MissesRace == (1 <= weeks));
            Runner outside = gs.State.Runners.First(r => !gs.State.Entries[0].Contains(r.Id));
            outside.InjurySeverity = InjurySeverity.Severe;
            outside.InjuryWeeksLeft = 10;
            InjuryDialogView o = gs.InjuryDialog(outside.Id);
            Assert.IsFalse(o.Registered);
            Assert.IsFalse(o.InOrder);
            Assert.IsTrue(o.MissesRace);
            Assert.Throws<ArgumentException>(() => gs.InjuryDialog(999999));
        }
    }
}
