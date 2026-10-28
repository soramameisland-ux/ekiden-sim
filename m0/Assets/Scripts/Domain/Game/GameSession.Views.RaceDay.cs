using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Race;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Views;

namespace Ekiden.Domain.Game
{
    /// <summary>大会の当日の読み取り API(SC-24 当日・SC-25 レース進行)。状態は変えない(<see cref="RaceDay"/> の再計算 <c>Preview</c> は何度呼んでも同じ)。</summary>
    public sealed partial class GameSession
    {
        public const int StandingsTop = 3;                         // 順位表: 先頭 3 校 + 自校の前後 1 校

        private DayLegView LegOf(RaceDay rd, int section)
        {
            LegView l = rd.View(section);
            IReadOnlyList<SectionDef> secs = RaceTables.Course(rd.Kind);
            Runner own = l.Runner.RunnerId != 0 ? State.FindRunner(l.Runner.RunnerId) : null;
            return new DayLegView
            {
                Section = section, Runner = own != null ? RowOf(own, null) : null, Name = l.Runner.Name ?? (own != null ? own.FamilyName + own.GivenName : ""), Form = l.Runner.Form,
                Distance = l.Distance, Type = l.Type, Mountain = l.Type == SectionType.Climb || l.Type == SectionType.Descent, Fit = l.Fit, FitMark = l.FitMark, PaceMark = l.PaceMark,
                Recommended = l.Recommended, Instr = l.Instr, ExpectedSec = l.ExpectedSec, Swapped = l.Swapped, Passed = section < rd.Resolved,
            };
        }

        public RaceDayView RaceDayView(RaceDay rd)
        {
            var v = new RaceDayView
            {
                Kind = rd.Kind, Sections = rd.Sections, SwapLimit = rd.SwapLimit, SwapsLeft = rd.SwapsLeft, GekiLimit = rd.GekiLimit, GekiLeft = rd.GekiLeft,
                Started = rd.Started, Touched = rd.Touched, Resolved = rd.Resolved,
            };
            foreach (Env e in rd.Environment) v.Env.Add(new EnvForecastView { Temp = e.Temp, Wind = e.Wind, Rain = e.Rain });
            for (int i = 0; i < rd.Sections; i++) v.Legs.Add(LegOf(rd, i));
            IReadOnlyList<RaceRunner> bench = rd.Bench;
            for (int b = 0; b < bench.Count; b++)
            {
                RaceRunner r = bench[b];
                Runner own = r.RunnerId != 0 ? State.FindRunner(r.RunnerId) : null;
                var bv = new BenchRunnerView
                {
                    BenchIndex = b, Runner = own != null ? RowOf(own, null) : null, Name = r.Name ?? (own != null ? own.FamilyName + own.GivenName : ""), Form = r.Form, Fatigue = r.Fatigue,
                    HeatRank = DisplayRules.RankLetter(r.Stats[(int)Stat.STA]), WindRank = DisplayRules.RankLetter(r.Stats[(int)Stat.PAC]), RainRank = DisplayRules.RankLetter(r.Stats[(int)Stat.CON]),
                    DeltaSec = new double[rd.Sections],
                };
                for (int i = 0; i < rd.Sections; i++) bv.DeltaSec[i] = rd.BenchExpectedSec(i, b) - v.Legs[i].ExpectedSec;
                v.Bench.Add(bv);
            }
            List<TeamResult> res = rd.Preview();
            TeamResult mine = rd.OwnResult(res);
            v.ExpectedRank = res.IndexOf(mine) + 1;
            v.ExpectedTotalDs = mine.TotalDs;
            return v;
        }

        /// <summary>順位表を『先頭 3 校 + 自校の前後 1 校』に絞る(間が空くところに区切りの印)。</summary>
        public static List<StandingLineView> CondenseStandings(IReadOnlyList<StandingRow> rows, Func<int, string> nameKey)
        {
            int own = rows.ToList().FindIndex(r => r.IsOwn);
            var keep = new SortedSet<int>();
            for (int i = 0; i < Math.Min(StandingsTop, rows.Count); i++) keep.Add(i);
            if (own >= 0) for (int i = Math.Max(0, own - 1); i <= Math.Min(rows.Count - 1, own + 1); i++) keep.Add(i);
            var list = new List<StandingLineView>();
            int prev = -1;
            foreach (int i in keep)
            {
                StandingRow r = rows[i];
                list.Add(new StandingLineView
                {
                    Rank = r.Rank, SchoolId = r.TeamId, NameKey = nameKey(r.TeamId), IsOwn = r.IsOwn, CumDs = r.CumDs, GapDs = r.GapDs, SectionDs = r.SectionDs, SeparatorBefore = prev >= 0 && i != prev + 1,
                });
                prev = i;
            }
            return list;
        }

        /// <summary>見どころの判定(<see cref="RaceHighlights"/>)を画面の形に。<paramref name="runnerOf"/>(区間)は自校のその区間の走者。</summary>
        private HighlightView HighlightOf(Highlight h, Func<int, int> runnerOf)
        {
            var v = new HighlightView
            {
                Section = h.Section, Kind = h.Kind, Key = HighlightView.KeyOf(h.Kind, h.Delta), SchoolId = h.TeamId, SchoolNameKey = "school." + h.TeamId + ".name", IsOwn = h.IsOwn,
                RunnerId = h.IsOwn ? runnerOf(h.Section) : 0,
            };
            switch (h.Kind)
            {
                case HighlightKind.LeadChange:
                case HighlightKind.CourseRecord:
                case HighlightKind.TeamRecord:
                    v.OtherSchoolId = h.OtherTeamId; v.OtherSchoolNameKey = "school." + h.OtherTeamId + ".name"; v.Ds = (int)h.Magnitude; break;
                case HighlightKind.GapMove:
                case HighlightKind.SectionPrize:
                    v.Ds = (int)h.Magnitude; break;
                case HighlightKind.RankMove:
                    v.RankDelta = h.Delta; break;
                default:
                    v.Percent = h.Magnitude * 100.0; break;
            }
            return v;
        }

        public RaceLiveView RaceLive(RaceDay rd)
        {
            var v = new RaceLiveView { Kind = rd.Kind, Sections = rd.Sections, Resolved = rd.Resolved, Finished = rd.Resolved >= rd.Sections, Started = rd.Started, GekiLeft = rd.GekiLeft };
            if (!v.Finished) v.Next = LegOf(rd, rd.Resolved);
            Incident inc = rd.NextIncident();
            if (inc != null) { v.IncidentKind = inc.Kind; v.IncidentLoss = inc.Loss; v.IncidentPos = inc.Pos; }
            if (rd.Resolved > 0)
            {
                List<TeamResult> results = rd.Preview();
                v.Standings.AddRange(CondenseStandings(rd.Standings(rd.Resolved - 1), id => "school." + id + ".name"));
                foreach (Highlight h in RaceHighlights.Digest(RaceHighlights.Detect(results, School.Id, rd.Resolved, World.History.Peek(Year, rd.Kind, results, null))))
                    v.Highlights.Add(HighlightOf(h, s => rd.View(s).Runner.RunnerId));
            }
            return v;
        }
    }
}
