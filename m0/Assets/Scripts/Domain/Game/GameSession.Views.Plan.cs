using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Race;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Text;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;

namespace Ekiden.Domain.Game
{
    /// <summary>年間方針(SC-10)と他校情報・予報(SC-23)の読み取り API。状態は変えない。</summary>
    public sealed partial class GameSession
    {
        // ------------------------------------------------------------ 年間方針(SC-10)
        public AnnualPlanView AnnualPlanView()
        {
            AnnualPlan p = State.Plan;
            var v = new AnnualPlanView
            {
                Focus = p.Focus, TargetRace = p.TargetRace, PlanAuto = State.Settings.PlanAuto,
                ExpectedTargetRank = Math.Max(1, State.LastRank[2] - 1), ExpectedBonusFunds = TargetBonusFunds(),
            };
            for (int i = 0; i < AnnualPlan.PeakingKinds; i++) v.Peaking[i] = p.Peaking[i];
            foreach (PlanFocus f in Enum.GetValues(typeof(PlanFocus)))
            {
                var o = new FocusOptionView { Focus = f, Selected = f == p.Focus, InjuryPerYear = PlanRules.FocusInjuryPerYear[(int)f] };
                for (int m = 0; m < 12; m++) o.Months[m] = PlanRules.AutoPlan(f, m);
                Array.Copy(PlanRules.FocusDeltaP[(int)f], o.DeltaP, 5);
                v.Options.Add(o);
            }
            return v;
        }

        // ------------------------------------------------------------ 他校情報・予報(SC-23)
        private RivalRunnerView RivalRow(WorldRunner w, double[] stats)
        {
            PersonName n = NameGenerator.Generate(Rngs, w.LookSeed, w.Overseas);
            return new RivalRunnerView
            {
                FamilyName = n.Family, GivenName = n.Given, FamilyKana = n.FamilyKana, GivenKana = n.GivenKana, Grade = w.Grade,
                FitLong = RaceTables.Rating(stats, SectionType.Long),
                Pb5000 = DisplayRules.DerivePbFromP(DisplayRules.PbEvent.M5000, RaceTables.Rating(stats, SectionType.Fast)),
                Pb10000 = DisplayRules.DerivePbFromP(DisplayRules.PbEvent.M10000, RaceTables.Rating(stats, SectionType.Mid)),
            };
        }

        /// <summary>他校情報・予報(オーダー提出の前後で見られる)。前評判は <see cref="RacePreview"/>、上位選手は区間適性 Long の上位 3 人(他校の能力は 04 §11 の復元)。</summary>
        public PreviewView Preview(RaceKind kind)
        {
            YearState ys = PrepareYear();
            var v = new PreviewView { Kind = kind };
            var power = new Dictionary<int, double>();
            foreach (School s in World.Schools) power[s.Id] = s.IsPlayer ? OwnPower() : World.TeamPower(s);
            foreach (PreviewRow row in RacePreview(kind))
            {
                var sv = new PreviewSchoolView
                {
                    SchoolId = row.SchoolId, NameKey = "school." + row.SchoolId + ".name", Rank = row.Rank, Value = row.Value, Mark = row.Mark, IsOwn = row.IsOwn,
                    PowerRank = 1 + World.Schools.Count(o => power[o.Id] > power[row.SchoolId]),
                };
                if (row.IsOwn)
                {
                    var own = State.Entries[(int)kind].Select(State.FindRunner).Where(r => r != null && TeamRacing.CanRun(State, r)).ToList();
                    foreach (Runner r in own.OrderByDescending(x => RaceTables.Rating(RaceRunner.From(x).Stats, SectionType.Long)).ThenBy(x => x.Id).Take(3))
                        sv.Top.Add(new RivalRunnerView
                        {
                            FamilyName = r.FamilyName, GivenName = r.GivenName, FamilyKana = r.FamilyKana, GivenKana = r.GivenKana, Grade = r.Grade,
                            FitLong = RaceTables.Rating(RaceRunner.From(r).Stats, SectionType.Long),
                            Pb5000 = DisplayRules.DerivePb(r.Stats, DisplayRules.PbEvent.M5000), Pb10000 = DisplayRules.DerivePb(r.Stats, DisplayRules.PbEvent.M10000),
                        });
                }
                else
                {
                    School sc = World.Schools.Single(s => s.Id == row.SchoolId);
                    List<RaceRunner> roster = ys.Rosters[row.SchoolId];
                    var idx = Enumerable.Range(0, roster.Count).OrderByDescending(i => RaceTables.Rating(roster[i].Stats, SectionType.Long)).ThenBy(i => i).Take(3);
                    foreach (int i in idx) sv.Top.Add(RivalRow(sc.Roster[i], roster[i].Stats));
                }
                v.Schools.Add(sv);
            }
            foreach (Env e in Forecast(kind)) v.Forecast.Add(new EnvForecastView { Temp = e.Temp, Wind = e.Wind, Rain = e.Rain });
            return v;
        }
    }
}
