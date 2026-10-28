using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Recruit;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Text;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;

namespace Ekiden.Domain.Game
{
    /// <summary>スカウトの読み取り API(SC-27・28・29・30)。状態は変えない。</summary>
    public sealed partial class GameSession
    {
        public const int ScoutNaturalIntake = 3;                       // K_UI_SCOUT_NATURAL(自然に入る目安)

        public ScoutPhase ScoutPhaseNow()
        {
            int m = MonthIndex;
            if (Scouting.CanObserve(m)) return ScoutPhase.Observe;
            if (Scouting.CanTalk(m)) return ScoutPhase.Recruit;
            return m == 11 ? ScoutPhase.Confirm : ScoutPhase.Closed;
        }

        private ScoutCandidateView CandidateOf(WorldRunner c)
        {
            ScoutStage st = StageOf(c.Index);
            int month = MonthIndex;
            PersonName name = NameGenerator.Generate(Rngs, c.LookSeed, c.Overseas);
            var v = new ScoutCandidateView
            {
                Index = c.Index, FamilyName = name.Family, GivenName = name.Given, FamilyKana = name.FamilyKana, GivenKana = name.GivenKana,
                Stars = DisplayRules.Stars(c.V), V = c.V, TopTier = c.V >= WorldK.K_V_TOP, Region = c.Region, Overseas = c.Overseas, Stage = st, Sigma = Scouting.InfoSd(School.ScoutLv),
            };
            v.Rivalry = DisplayRules.Rivalry(c.V, out double win);
            v.RivalryWin = win;
            if (c.Overseas)
            {
                v.Band = Geo.Overseas;
                if (c.CountryIndex >= 0 && c.CountryIndex < Geo.Countries.Length) v.CountryName = Geo.Countries[c.CountryIndex];
            }
            else
            {
                v.Pref = c.PrefectureId;
                v.PrefName = Geo.ById(c.PrefectureId).Name;
                v.Band = Geo.Band(Geo.ById(c.PrefectureId).Region, School.Region);
                v.HighSchoolNameKey = "hs." + Geo.HighSchoolId(c.PrefectureId, c.HighSchoolU) + ".name";
            }
            int[] entry = RunnerFactory.EntryStats(c, Rngs);
            v.Pb5000 = DisplayRules.DerivePb(entry, DisplayRules.PbEvent.M5000);
            foreach (Trait t in Presentation.Traits(Rngs, c.LookSeed)) v.Traits.Add(t.ToString().ToLowerInvariant());
            bool points = ScoutPointsAvailable >= Scouting.ObserveCost;
            v.CanObserve = points && Scouting.CanAct(st, ScoutStage.Observed, month);
            v.CanTalk = points && Scouting.CanAct(st, ScoutStage.Talked, month);
            v.CanInvite = points && Scouting.CanAct(st, ScoutStage.Invited, month);
            v.StyleVisible = Scouting.StyleVisible(st);
            v.GiftPresenceVisible = Scouting.GiftPresenceVisible(st);
            v.GiftKindVisible = Scouting.GiftKindVisible(st, School.ScoutLv);
            v.GrowthTypeVisible = Scouting.GrowthTypeVisible(st);
            if (st >= ScoutStage.Observed) v.QObs = Scouting.ObserveQ(c, School.ScoutLv, Rngs);
            if (v.StyleVisible) v.StyleChips = Presentation.StyleChips(Individuality.GenerateStyle(Rngs, c.LookSeed, c.Profile));
            if (v.GrowthTypeVisible) v.Growth = c.Growth;
            if (v.GiftPresenceVisible)
            {
                Gift[] gifts = Individuality.GenerateGifts(Rngs, c.LookSeed);
                v.HasGift = gifts.Length > 0;
                if (v.GiftKindVisible) v.GiftKinds.AddRange(gifts);
            }
            return v;
        }

        /// <summary>候補の 1 人(SC-29)。番号が範囲外なら null。</summary>
        public ScoutCandidateView ScoutCandidate(int index)
        {
            IReadOnlyList<WorldRunner> pool = ScoutPool;
            return index < 0 || index >= pool.Count ? null : CandidateOf(pool[index]);
        }

        /// <summary>候補の一覧(SC-28)。V の高い順(= 確定の順番。同じなら番号の昇順)。絞り込み・並べ替えは画面側。</summary>
        public List<ScoutCandidateView> ScoutCandidates()
        {
            return ScoutPool.OrderByDescending(c => c.V).ThenBy(c => c.Index).Select(CandidateOf).ToList();
        }

        private void CountStages(out int observed, out int talked, out int invited)
        {
            observed = _scout.Count(kv => kv.Value >= ScoutStage.Observed);
            talked = _scout.Count(kv => kv.Value >= ScoutStage.Talked);
            invited = _scout.Count(kv => kv.Value == ScoutStage.Invited);
        }

        // ------------------------------------------------------------ 地図(SC-27)
        public ScoutMapView ScoutMap()
        {
            var v = new ScoutMapView { MonthIndex = MonthIndex, Phase = ScoutPhaseNow(), PointsAvailable = ScoutPointsAvailable, PointsYearTotal = Scouting.AnnualPoints(State.CoachRank) };
            CountStages(out v.Observed, out v.Talked, out v.Invited);
            var regions = Enum.GetValues(typeof(Region)).Cast<Region>().ToDictionary(r => r, r => new ScoutRegionView { Region = r });
            var prefs = Geo.Prefectures.ToDictionary(p => p.Id, p => new ScoutPrefView { Pref = p.Id, PrefName = p.Name, Region = p.Region, Band = Geo.Band(p.Region, School.Region) });
            var ob = new Dictionary<int, int>();
            foreach (OBRecord o in State.Obs) { ob.TryGetValue(o.Pref, out int n); ob[o.Pref] = n + 1; }
            foreach (WorldRunner c in ScoutPool)
            {
                ScoutStage st = StageOf(c.Index);
                bool star5 = DisplayRules.Stars(c.V) == 5;
                if (c.Overseas)
                {
                    v.OverseasCandidates++;
                    if (star5) v.OverseasStars5++;
                    continue;
                }
                foreach (var t in new object[] { regions[c.Region], prefs[c.PrefectureId] })
                {
                    var r = t as ScoutRegionView;
                    if (r != null) { r.Candidates++; if (star5) r.Stars5++; if (st >= ScoutStage.Observed) r.Observed++; if (st >= ScoutStage.Talked) r.Targets++; }
                    else
                    {
                        var p = (ScoutPrefView)t;
                        p.Candidates++; if (star5) p.Stars5++; if (st >= ScoutStage.Observed) p.Observed++; if (st >= ScoutStage.Talked) p.Targets++;
                    }
                }
            }
            foreach (ScoutPrefView p in prefs.Values)
            {
                ob.TryGetValue(p.Pref, out int n);
                p.ObCount = n;
                p.ObLevel = Math.Min(5, (int)Math.Sqrt(n));
            }
            v.Regions.AddRange(regions.Values.OrderBy(r => (int)r.Region));
            v.Prefs.AddRange(prefs.Values.OrderBy(p => p.Pref));
            return v;
        }

        // ------------------------------------------------------------ 目標とポイント(SC-30)
        public ScoutTargetsView ScoutTargets()
        {
            var v = new ScoutTargetsView { PointsAvailable = ScoutPointsAvailable };
            CountStages(out v.Observed, out v.Talked, out v.Invited);
            IReadOnlyList<WorldRunner> pool = ScoutPool;
            var targets = _scout.Where(kv => kv.Value >= ScoutStage.Talked).Select(kv => pool[kv.Key]).OrderByDescending(c => c.V).ThenBy(c => c.Index).ToList();
            for (int i = 0; i < targets.Count; i++)
                v.Targets.Add(new ScoutTargetRowView { Candidate = CandidateOf(targets[i]), ConfirmOrder = i + 1, Unlikely = i + 1 >= 9 });
            int capacity = School.Quota(), reserved = State.Tickets.Pending != null ? 1 : 0;
            var m = new ScoutSlotMeterView
            {
                Capacity = capacity, TicketReserved = reserved, Usable = capacity - reserved, NaturalIntake = ScoutNaturalIntake, Recommended = ScoutTargetRecommended,
                TargetCount = targets.Count, TopTierTargets = targets.Count(c => c.V >= WorldK.K_V_TOP), ScholarshipSlots = School.ScholSlots(),
            };
            m.OverRecommended = m.TargetCount > m.Recommended;
            m.OverCapacity = m.TargetCount > m.Usable - m.NaturalIntake;
            v.Meter = m;
            return v;
        }
    }
}
