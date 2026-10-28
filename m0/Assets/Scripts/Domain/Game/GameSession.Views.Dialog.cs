using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Race;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Training;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;

namespace Ekiden.Domain.Game
{
    /// <summary>ダイアログの読み取り API(SC-36 イベント・SC-41 コンディション警告・SC-42 故障の知らせ)。状態は変えない。</summary>
    public sealed partial class GameSession
    {
        public const int EffectSummaryMax = 3;                      // K_UI_FX_MAX(R-15: 要約は 3 項目まで)

        private EffectView EffectOf(Effect e)
        {
            var v = new EffectView { Op = e.Op, To = e.To, Amount = e.D, Probability = e.P, StatMode = e.StatMode, Xu = e.Xu, Weeks = e.Weeks };
            switch (e.Op)
            {
                case EffectOp.Xp: if (e.StatMode == XpStatMode.Fixed) v.Stat = (Stat)e.Stat; break;
                case EffectOp.Skill:
                    v.SkillId = "skill." + e.SkillId;
                    v.SkillRank = SkillRankOf(v.SkillId);
                    break;
                case EffectOp.Inj: v.Severity = e.Sev; break;
                case EffectOp.Mod: v.Mod = e.Kind; v.ModValue = e.Value; break;
                case EffectOp.ObRole: v.Role = e.Role; break;
            }
            return v;
        }

        // ------------------------------------------------------------ イベント(SC-36)
        /// <summary>発生したイベント 1 件(<see cref="Stop.Event"/>)。選べる選択肢は <paramref name="own"/>(その週の自校の大会の結果)で決まる。</summary>
        public EventDialogView EventDialog(FiredEvent f, RaceInfo? own = null)
        {
            EventDef def = f.Def;
            List<ChoiceDef> valid = ValidChoices(f, own);
            var v = new EventDialogView
            {
                EventId = def.Id, TitleKey = def.Id + ".title", Category = def.Category, Interactive = def.Interactive, Awakening = def.Category == EventCategory.Awakening,
                Runner = f.R != null ? RowOf(f.R, null) : null, Partner = f.P != null ? RowOf(f.P, null) : null,
            };
            if (v.Awakening)
            {
                Effect skill = def.Choices.SelectMany(c => c.Fx).FirstOrDefault(x => x.Op == EffectOp.Skill);
                string id = def.SkillId ?? (skill != null ? skill.SkillId : null);
                if (id != null) { v.SkillId = "skill." + id; v.SkillRank = SkillRankOf(v.SkillId); }
            }
            ChoiceDef dflt = valid.Count > 0 ? new DefaultChoicePolicy().Choose(def, valid, null) : null;
            foreach (ChoiceDef c in def.Choices)
            {
                var cv = new ChoiceView { Id = c.Id, LabelKey = def.Id + ".choice." + c.Id, Enabled = valid.Any(x => x.Id == c.Id), IsDefault = dflt != null && dflt.Id == c.Id };
                foreach (Effect e in c.Fx.Take(EffectSummaryMax)) cv.Effects.Add(EffectOf(e));
                cv.MoreEffects = Math.Max(0, c.Fx.Length - EffectSummaryMax);
                v.Choices.Add(cv);
            }
            return v;
        }

        // ------------------------------------------------------------ コンディション警告(SC-41)
        /// <summary>疲労が警告の値(70。合宿の月は 80)以上で、安全網(85)にはまだ届いていない選手。次の週の疲労は、いまの計画のとおりの正確な予測。</summary>
        public ConditionWarnView ConditionWarning()
        {
            TeamState s = State;
            int yw = Yw, month = yw / 4;
            bool camp = month == 4;
            var v = new ConditionWarnView { Threshold = camp ? GameFlow.WarnFatigueCamp : GameFlow.WarnFatigue, Camp = camp };
            int next = Math.Min(yw + 1, Calendar.WeeksPerYear - 1);
            foreach (Runner r in s.Runners.OrderBy(x => x.Id))
            {
                if (r.IsInjured || r.Fatigue < v.Threshold || r.Fatigue >= GameFlow.WarnFatigueMax) continue;
                bool c, individual;
                Menu menu = _week.MenuFor(s, r, next, out c, out individual);
                var planned = new[] { new PlannedWeek { Menu = menu, MonthIndex = next / 4, Camp = c, Individual = individual } };
                ForecastResult fr = Forecaster.Run(Master, r, planned, s.Fac, s.Settings.AutoSafetyF >= 0, r.BondAvg, s.ModsFor(r));        // Run は複製で予測する(選手の状態は変えない)
                v.Rows.Add(new WarnRowView
                {
                    Row = RowOf(r, null), Fatigue = r.Fatigue, NextFatigue = fr.F[0], HasIndividualMenu = individual && s.IndividualMenus.ContainsKey(r.Id),
                    SafetyNext = fr.F[0] >= GameFlow.WarnFatigueMax, InjuryNext = fr.Weeks[0].InjuryP,
                });
            }
            return v;
        }

        // ------------------------------------------------------------ 故障の知らせ(SC-42)
        public InjuryDialogView InjuryDialog(int runnerId)
        {
            Runner r = State.FindRunner(runnerId);
            if (r == null) throw new ArgumentException("その選手はいない: " + runnerId);
            var v = new InjuryDialogView { Row = RowOf(r, null), Severity = r.InjurySeverity, WeeksLeft = r.InjuryWeeksLeft };
            int weeks, kind;
            Calendar.NextRace(Yw, out weeks, out kind);
            RaceKind race = (RaceKind)(kind - 1);
            bool qualified = PrepareYear().Entrants(race).Contains(School.Id);
            if (!qualified) return v;
            v.NextRace = race;
            v.WeeksUntilRace = weeks;
            v.MissesRace = r.InjuryWeeksLeft > weeks;
            v.Registered = State.Entries[(int)race].Contains(runnerId);
            if (!v.Registered) return v;
            OrderView ov = Order(race);
            OrderSectionView sec = ov.Sections.FirstOrDefault(x => x.RunnerId == runnerId);
            if (sec == null) return v;
            v.InOrder = true;
            v.Section = sec.Section;
            var placed = new HashSet<int>(ov.Sections.Where(x => x.RunnerId.HasValue).Select(x => x.RunnerId.Value));
            foreach (OrderCandidateView c in OrderCandidates(race, sec.Section))
                if (c.Row.Injury == InjurySeverity.None && !placed.Contains(c.Row.Id)) v.Substitutes.Add(c);
            if (v.Substitutes.Count > 3) v.Substitutes.RemoveRange(3, v.Substitutes.Count - 3);
            return v;
        }
    }
}
