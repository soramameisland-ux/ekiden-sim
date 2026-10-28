using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Race;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using Calendar = Ekiden.Domain.Team.Calendar;

namespace Ekiden.Domain.Game
{
    /// <summary>年度末の読み取り API(SC-12 年度の結果・SC-13 卒業・SC-14 入学者)。状態は変えない。</summary>
    public sealed partial class GameSession
    {
        private SkillRank SkillRankOf(string id)
        {
            SkillDef sd = Catalog.Skills.FirstOrDefault(x => "skill." + x.Id == id);
            return sd != null ? sd.Rank : SkillRank.Blue;
        }

        // ------------------------------------------------------------ 年度の結果と精算(SC-12)
        /// <summary>年度の結果と精算。年度末の精算(第 48 週の終わり)が済んでから、次の年度に入るまで。それ以外は null。</summary>
        public YearSummaryView YearSummary()
        {
            PlayerYearStats st = Stats.Count > 0 ? Stats[Stats.Count - 1] : null;
            if (st == null || st.Year != Year) return null;
            var v = new YearSummaryView
            {
                Year = Year, EvalPoints = st.Points, FamePoints = School.FamePoints, PrestigeBefore = st.Prestige, PrestigeAfter = School.Prestige,
                CoachRankBefore = st.CoachRank, CoachRankAfter = School.Rank(), CumPoints = School.CumPoints, TurnoverDone = !IsYearSettled,
            };
            int raceSum = 0;
            for (int k = 0; k < 3; k++)
            {
                int place = st.Place[k];
                int pts = place >= 1 && place <= 10 ? WorldK.YearPoints[place - 1] : 0;
                raceSum += pts;
                v.Races[k] = new YearRaceView { Kind = (RaceKind)k, Place = place, Won = st.Wins[k], EvalPoints = pts };
            }
            v.TripleCrown = st.Wins[0] && st.Wins[1] && st.Wins[2];
            v.TripleCrownBonus = v.EvalPoints - raceSum;
            v.RankUp = v.CoachRankAfter > v.CoachRankBefore;
            v.FacilityCapBefore = WorldMath.FacilityCap(v.CoachRankBefore);
            v.FacilityCapAfter = WorldMath.FacilityCap(v.CoachRankAfter);
            v.NextRankPoints = v.CoachRankAfter < WorldK.RankCum.Length ? (int?)WorldK.RankCum[v.CoachRankAfter] : null;
            v.ExpectedTargetRank = System.Math.Max(1, State.LastRank[2] - 1);
            v.TargetMet = st.Place[2] <= v.ExpectedTargetRank;
            v.TargetBonusFunds = v.TargetMet ? TargetBonusFunds() : 0;
            foreach (TicketLogEntry e in State.Tickets.Log)
                if (e.Year == Year && e.Delta > 0 && e.Source.StartsWith("Earned:")) v.TicketsEarned.Add(TicketMilestoneView.Of(e.Source.Substring("Earned:".Length)));
            if (IsYearSettled) v.TicketsAtYearEnd = TicketMilestones.PeekAtYearEnd(State.Tickets, v.CoachRankAfter, Year).Select(TicketMilestoneView.Of).ToList();
            if (LastSettlement != null && _settlementYear == Year)
            {
                Settlement f = LastSettlement;
                var fv = new YearFinanceView
                {
                    FundsBefore = f.FundsBefore, IncomeFormula = f.IncomeFormula, IncomeMod = f.IncomeMod, Income = f.Income, Staff = f.Staff, Upkeep = f.Upkeep,
                    MembersExpense = f.MembersExpense, Expense = f.Expense, Members = f.Members, FundsAfterOps = f.FundsAfterOps, FundsNow = State.Funds,
                };
                fv.FacBefore = YearSummaryView.FacilityOrder.Select(k => FacilityPolicy.Level(f.FacBefore, k)).ToArray();
                fv.FacAfter = YearSummaryView.FacilityOrder.Select(k => FacilityPolicy.Level(f.FacAfter, k)).ToArray();
                v.Finance = fv;
            }
            return v;
        }

        /// <summary>卒業生・OB の行(SC-13・SC-33 で共通)。</summary>
        private GraduateRowView GraduateRow(OBRecord ob)
        {
            var g = new GraduateRowView
            {
                RunnerId = ob.RunnerId, Name = ob.Name, Kana = ob.Kana, Pref = ob.Pref, Overseas = ob.Pref == 0, Power = ob.Power, Rank = DisplayRules.RankLetter(ob.Power),
                HighSchoolNameKey = ob.HighSchoolName.Length > 0 ? ob.HighSchoolName : null, SectionWins = ob.SectionWins, RelayWins = ob.RaceWins,
                Pb5000 = ob.T5000, Pb10000 = ob.T10000, PbHalf = ob.THalf, Career = ob.CareerPath, HallOfFame = ob.HallOfFame, Ticket = ob.RecruitSource == RecruitSource.Ticket,
            };
            if (ob.Pref >= 1 && ob.Pref <= 47) g.PrefName = Geo.ById(ob.Pref).Name;
            g.StatRanks = ob.FinalStats.Select(x => DisplayRules.RankLetter(x)).ToArray();
            foreach (string id in ob.Skills) g.Skills.Add(new SkillView { Id = id, Rank = SkillRankOf(id) });
            return g;
        }

        // ------------------------------------------------------------ 卒業・進路(SC-13)
        /// <summary>直近の年度末の卒業と退部。まだ一度もなければ null。</summary>
        public GraduationView Graduation()
        {
            if (LastTurnover == null) return null;
            var v = new GraduationView { Year = _turnoverYear };
            foreach (OBRecord ob in LastTurnover.Graduates.OrderByDescending(o => o.HallOfFame))      // 殿堂入りを先頭に(安定な整列。残りは ID の昇順のまま)
                v.Graduates.Add(GraduateRow(ob));
            foreach (Runner r in LastTurnover.Quit) v.Quit.Add(RowOf(r, null));
            return v;
        }

        // ------------------------------------------------------------ 入学者発表・入部確認(SC-14)
        /// <summary>直近の入学者(在籍している選手)。まだ一度もなければ null。</summary>
        public EnrollmentView Enrollment()
        {
            if (_intakeYear < 0) return null;
            var v = new EnrollmentView { Year = _intakeYear + 1 };
            foreach (int id in _lastIntake)
            {
                Runner r = State.FindRunner(id);
                if (r == null) continue;
                var e = new EnrolleeRowView
                {
                    Row = RowOf(r, null), Targeted = _lastIntakeTargeted.Contains(id), Far = r.Far, Near = r.Near,
                    Stars = _lastIntakeStars.TryGetValue(id, out int stars) ? stars : 0,
                    Stats = (int[])r.Stats.Clone(), StatRanks = r.Stats.Select(x => DisplayRules.RankLetter(x)).ToArray(),
                };
                (e.Targeted ? v.Targeted : v.Natural).Add(e);
            }
            return v;
        }
    }
}
