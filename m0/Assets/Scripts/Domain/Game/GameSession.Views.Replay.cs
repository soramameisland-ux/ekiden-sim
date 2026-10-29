using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Events;
using Ekiden.Domain.Race;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;

namespace Ekiden.Domain.Game
{
    /// <summary>ダイジェストの再生(SC-25。08 R-14.4)の読み取りの API。大会の結果(<see cref="RaceOutcome"/>)から、中継所ごとの順位表・見どころ・自校の走者の行を作る。状態は変えず、乱数も引かない。</summary>
    public sealed partial class GameSession
    {
        public RaceReplayView RaceReplay(RaceOutcome race, RaceInfo? own = null)
        {
            if (race == null) throw new ArgumentNullException("race");
            RaceResultView rv = RaceResult(race, own);
            int n = rv.Sections;
            var v = new RaceReplayView { Kind = race.Kind, Sections = n, OwnEntered = rv.OwnEntered, OwnRank = rv.OwnRank, HalfSection = race.Kind == RaceKind.NewYear ? FirstDayLast(race.Kind, n) : -1 };
            List<TeamResult> res = race.Results;
            RaceTeam team = race.Teams.FirstOrDefault(t => t.Id == School.Id);
            List<Highlight> digest = RaceHighlights.Digest(RaceHighlights.Detect(res, School.Id, -1, race.Records));
            for (int s = 0; s < n; s++)
            {
                var step = new ReplayStepView { Section = s, IsFinal = s == n - 1, IsHalfEnd = s == v.HalfSection, Prize = rv.Prizes[s], OwnLeg = rv.OwnEntered ? rv.OwnLegs[s] : null };
                var rows = res.Select(r => new StandingRow { TeamId = r.Id, CumDs = r.Cum[s], SectionDs = r.Sections[s], IsOwn = r.Id == School.Id }).ToList();
                RaceSimulator.StableSort(rows, (a, b) => a.CumDs != b.CumDs ? a.CumDs.CompareTo(b.CumDs) : a.TeamId.CompareTo(b.TeamId));
                for (int i = 0; i < rows.Count; i++) { rows[i].Rank = i + 1; rows[i].GapDs = rows[i].CumDs - rows[0].CumDs; }
                step.Standings.AddRange(CondenseStandings(rows, id => "school." + id + ".name"));
                foreach (Highlight h in digest.Where(x => x.Section == s))
                {
                    step.Highlights.Add(HighlightOf(h, sec => team != null ? team.Lineup[sec].RunnerId : 0));
                    if (h.Kind == HighlightKind.LeadChange || h.Kind == HighlightKind.CourseRecord || h.Kind == HighlightKind.TeamRecord) step.Peak = true;
                }
                v.Steps.Add(step);
            }
            v.Finish.AddRange(rv.Overall.Where(r => r.Rank <= StandingsTop || r.IsOwn));
            return v;
        }

        /// <summary>1 日目の最後の区間(0 始まり)。</summary>
        private static int FirstDayLast(RaceKind kind, int sections)
        {
            int last = -1;
            for (int s = 0; s < sections; s++) if (RaceTables.DayOf(kind, s) == 1) last = s;
            return last;
        }
    }
}
