using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Race;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Views;

namespace Ekiden.Domain.Game
{
    /// <summary>月末レポートの読み取り API(SC-11)。</summary>
    public sealed partial class GameSession
    {
        public const int WarnFatigueDisplay = 70;                // K_UI_WARN_F(疲労の警告。GameFlow.WarnFatigue と同じ値)

        public MonthReportView MonthReportView(MonthReport rep)
        {
            TeamState s = State;
            var v = new MonthReportView
            {
                MonthIndex = rep.MonthIndex, CalendarMonth = (3 + rep.MonthIndex) % 12 + 1, Morale = s.Morale, More = rep.More,
                ChangeKnown = rep.StartKnown, Next = Home().Next,
            };
            v.AvgFatigue = s.Runners.Count == 0 ? 0.0 : s.Runners.Sum(r => (double)r.Fatigue) / s.Runners.Count;
            v.AvgForm = s.Runners.Count == 0 ? 0.0 : s.Runners.Sum(r => (double)r.Form) / s.Runners.Count;
            if (rep.StartKnown)
            {
                v.AvgFatigueChange = v.AvgFatigue - rep.StartFatigue;
                v.AvgFormChange = v.AvgForm - rep.StartForm;
                v.MoraleChange = v.Morale - rep.StartMorale;
            }
            for (int g = 0; g < 3; g++) v.ThemeIds[g] = rep.Themes[g];
            foreach (FlowRules.ReportItem it in rep.Items)
            {
                Runner r = it.RunnerId != 0 ? s.FindRunner(it.RunnerId) : null;
                var iv = new ReportItemView { Kind = it.Kind, RunnerId = it.RunnerId, RunnerName = r != null ? r.FamilyName + r.GivenName : "", EventId = it.EventId, Value = it.Value };
                if (it.Kind == "awaken" && it.SkillId != null)
                {
                    iv.SkillId = "skill." + it.SkillId;
                    iv.SkillRank = SkillRankOf(iv.SkillId);
                }
                if (it.Kind == "rank_up") { iv.Stat = (Stat)it.Value; iv.Letter = it.Letter; }
                if (it.Kind == "gift_reveal" && r != null && r.GiftRevealed) iv.Gifts.AddRange(r.Gifts);
                v.Items.Add(iv);
            }
            v.Notices.AddRange(rep.Deferred);
            foreach (WeekOutcome w in rep.Weeks)
            {
                if (w.Race == null) continue;
                var mr = new MonthRaceView { Kind = w.Race.Kind, WinnerSchoolId = w.Race.Results[0].Id };
                mr.WinnerNameKey = "school." + mr.WinnerSchoolId + ".name";
                int idx = w.Race.Results.FindIndex(t => t.Id == School.Id);
                mr.OwnRank = idx >= 0 ? idx + 1 : 0;
                mr.OwnWon = idx == 0;
                v.Races.Add(mr);
            }
            foreach (Runner r in s.Runners.OrderBy(x => x.Id))
            {
                if (r.IsInjured) v.Caution.Injured.Add(r.Id);
                else if (r.Fatigue >= WarnFatigueDisplay) v.Caution.HighFatigue.Add(r.Id);
            }
            return v;
        }
    }
}
