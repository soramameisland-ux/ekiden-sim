using System;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Team;

namespace Ekiden.Domain.Events
{
    /// <summary>その週に開催された大会(04 §10)。大会がない週は null で表す(race.kind = 0、順位 = 99)。</summary>
    public struct RaceInfo
    {
        public int Kind;                // 1〜3
        public int TeamRank;
        public int ExpectedRank;

        public RaceInfo(int kind, int teamRank, int expectedRank)
        {
            Kind = kind; TeamRank = teamRank; ExpectedRank = expectedRank;
        }
    }

    /// <summary>条件式が世界(他校)に尋ねること。</summary>
    public interface IEventWorld
    {
        /// <summary>その週に出場する他校の先発に、同じ出身高校の選手がいるか(06 §3.3 rival_same_hs)。</summary>
        bool RivalSameHs(Runner r, int absWeek);
    }

    /// <summary>検算用の近似(参照モデルと同じ): 確率 0.40 を、選手と週から決まる鍵つきの値で決める。本番は世界の他校の選手の出身から判定する。</summary>
    public sealed class ApproxEventWorld : IEventWorld
    {
        private readonly ulong _seed;

        public ApproxEventWorld(ulong masterSeed) { _seed = masterSeed; }

        public bool RivalSameHs(Runner r, int absWeek)
        {
            ulong h = Mix.Derive(_seed, RngRegistry.LabelWorldGen, r.Id, Calendar.FloorDiv(absWeek, 4));
            return (h % 1000UL) / 1000.0 < 0.40;
        }
    }

    /// <summary>条件式の評価の文脈(06 §3.2)。1 回の EventPhase で作り、<see cref="Bind"/> で当事者を差し替えて使い回す。</summary>
    public sealed class EventContext
    {
        public TeamState State { get; }
        public IEventWorld World { get; }
        public Runner R { get; private set; }
        public Runner P { get; private set; }
        public RaceInfo? Race { get; }

        private readonly int _yearWeek, _nextRace, _nextRaceKind;

        public EventContext(TeamState state, IEventWorld world, RaceInfo? race)
        {
            State = state;
            World = world;
            Race = race;
            _yearWeek = Calendar.FloorMod(state.AbsWeek, Calendar.WeeksPerYear);
            Calendar.NextRace(_yearWeek, out _nextRace, out _nextRaceKind);
        }

        public EventContext Bind(Runner r, Runner p)
        {
            R = r;
            P = p;
            return this;
        }

        internal int SamePrefN(Runner r)
        {
            int n = 0;
            if (r.Pref == 0) return 0;
            foreach (Runner o in State.Runners)
                if (!ReferenceEquals(o, r) && o.Pref == r.Pref) n++;
            return n;
        }

        internal bool SeniorSamePref(Runner r)
        {
            if (r.Pref == 0) return false;
            foreach (Runner o in State.Runners)
                if (!ReferenceEquals(o, r) && o.Pref == r.Pref && o.Grade >= r.Grade + 1) return true;
            return false;
        }

        private static double B(bool v) { return v ? 1.0 : 0.0; }

        internal double Get(VarKind kind, Runner who, int arg)
        {
            TeamState t = State;
            switch (kind)
            {
                case VarKind.CalYear: return Calendar.FloorDiv(t.AbsWeek, Calendar.WeeksPerYear);
                case VarKind.CalMonth: return _yearWeek / 4;
                case VarKind.CalWeek: return _yearWeek % 4;
                case VarKind.CalNextRace: return _nextRace;
                case VarKind.CalNextRaceKind: return _nextRaceKind;
                case VarKind.RaceKind: return Race.HasValue ? Race.Value.Kind : 0;
                case VarKind.RaceTeamRank: return Race.HasValue ? Race.Value.TeamRank : 99;
                case VarKind.RaceExpectedRank: return Race.HasValue ? Race.Value.ExpectedRank : 99;
                case VarKind.Fac:
                    switch (arg)
                    {
                        case 0: return t.Fac.Dorm;
                        case 1: return t.Fac.Track;
                        case 2: return t.Fac.Hill;
                        case 3: return t.Fac.Gym;
                        case 4: return t.Fac.Medical;
                        case 5: return t.Fac.Coach;
                        case 6: return t.Fac.Trainer;
                        case 7: return t.Fac.Nutri;
                        default: return t.Fac.Scout;
                    }
                case VarKind.TFunds: return t.Funds;
                case VarKind.TPrestige: return t.Prestige;
                case VarKind.TMorale: return t.Morale;
                case VarKind.TSize: return t.Runners.Count;
                case VarKind.TCoachRank: return t.CoachRank;
                case VarKind.TScoutPts: return t.ScoutPts + t.ModSum(ModKind.ScoutPts);
                case VarKind.TGuidance: return t.GuidanceLeft;
                case VarKind.TGrade:
                    {
                        int n = 0;
                        foreach (Runner r in t.Runners) if (r.Grade == arg) n++;
                        return n;
                    }
                case VarKind.TGrade3Plus:
                    {
                        int n = 0;
                        foreach (Runner r in t.Runners) if (r.Grade >= 3) n++;
                        return n;
                    }
                case VarKind.ObTotal: return t.ObCount;
                case VarKind.ObCorp: return t.ObCorpCount;
                case VarKind.TRank: return t.Rank[arg];
                case VarKind.TLastRank: return t.LastRank[arg];
                case VarKind.RId: return who.Id;
                case VarKind.RStat: return who.Stats[arg];
                case VarKind.RInjury: return (int)who.InjurySeverity;
                case VarKind.RInjuryWeeks: return who.InjuryWeeksLeft;
                case VarKind.RInjuryAge: return who.InjuryAge;
                case VarKind.RWeeksIn: return t.AbsWeek - who.EnrollWeek;
                case VarKind.RGift: return B(Array.IndexOf(who.Gifts, (Gift)arg) >= 0);
                case VarKind.RStyle:
                    {
                        if (who.Style == null) return 0;
                        Style s = who.Style.Value;
                        switch (arg)
                        {
                            case 0: return B(s.Form == StyleForm.Speed);
                            case 1: return B(s.Form == StyleForm.Long);
                            case 2: return B(s.Run == StyleRun.Pack);
                            case 3: return B(s.Run == StyleRun.Solo);
                            case 4: return B(s.Flow == StyleFlow.Chase);
                            default: return B(s.Flow == StyleFlow.Lead);
                        }
                    }
                case VarKind.RSkills: return who.Skills.Count;
                case VarKind.RTaper: return who.TaperWeeks;
                case VarKind.RGrade: return who.Grade;
                case VarKind.RFatigue: return who.Fatigue;
                case VarKind.RForm: return who.Form;
                case VarKind.RMotivation: return who.Motivation;
                case VarKind.RGroup: return who.Group;
                case VarKind.RCaptain: return B(who.Captain);
                case VarKind.ROverseas: return B(who.Overseas);
                case VarKind.RFar: return B(who.Far);
                case VarKind.RNear: return B(who.Near);
                case VarKind.RPref: return who.Pref;
                case VarKind.RBondAvg: return who.BondAvg;
                case VarKind.RPRank: return who.PRank;
                case VarKind.RRegular: return B(who.Regular);
                case VarKind.RRaces: return who.Races;
                case VarKind.RSectionWins: return who.SectionWins;
                case VarKind.RBrakes: return who.Brakes;
                case VarKind.RGapAvg: return who.GapAvg;
                default:
                    {
                        RaceFlags f = who.RaceFlags;
                        switch (arg)
                        {
                            case 0: return f.InLineup;
                            case 1: return f.Brake;
                            case 2: return f.SectionRank;
                            case 3: return f.SectionWin;
                            default: return f.Record;
                        }
                    }
            }
        }
    }
}
