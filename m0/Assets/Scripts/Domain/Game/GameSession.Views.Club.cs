using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;
using Ekiden.Domain.Training;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;

namespace Ekiden.Domain.Game
{
    /// <summary>運営・全国の大学・OB 名簿・歴史の読み取り API(SC-32・35・33・34)。状態は変えない。</summary>
    public sealed partial class GameSession
    {
        private static bool IsStaffKey(string key) { return key == "coach" || key == "trainer" || key == "nutri" || key == "scout"; }

        private static FacilityEffectView Fx(string code, double perLevel, int lv)
        {
            return new FacilityEffectView { Code = code, Value = perLevel * (lv - 1), NextValue = perLevel * Math.Min(4, lv) };               // 次のレベルの効果(Lv5 は上限なので同じ値)
        }

        /// <summary>施設・スタッフの効果(03 §4.6)。コード → 文字列テーブルの <c>fx.{コード}</c>。</summary>
        private static List<FacilityEffectView> FacilityEffects(string key, int lv)
        {
            var list = new List<FacilityEffectView>();
            switch (key)
            {
                case "coach": list.Add(Fx("xp_all", TrainingMaster.FcCoach, lv)); break;
                case "trainer":
                    list.Add(Fx("injury_prob", -TrainingMaster.FcTrainerInj, lv));
                    list.Add(Fx("injury_dur", -TrainingMaster.FcTrainerDur, lv));
                    list.Add(Fx("recovery_r", TrainingMaster.FcTrainerR, lv));
                    break;
                case "nutri": list.Add(Fx("recovery_r", TrainingMaster.FcNutriR, lv)); break;
                case "dorm":
                    list.Add(Fx("recovery_r", TrainingMaster.FcDormR, lv));
                    list.Add(Fx("xp_rec", TrainingMaster.FcDormXp, lv));
                    break;
                case "track": list.Add(Fx("xp_spd_kck", TrainingMaster.FcTrack, lv)); break;
                case "hill": list.Add(Fx("xp_clb_dwn", TrainingMaster.FcHill, lv)); break;
                case "gym":
                    list.Add(Fx("xp_dur", TrainingMaster.FcGymXp, lv));
                    list.Add(Fx("injury_prob", -TrainingMaster.FcGymInj, lv));
                    break;
                case "medical": list.Add(Fx("injury_dur", -TrainingMaster.FcMedicalDur, lv)); break;
                case "scout":
                    list.Add(new FacilityEffectView { Code = "scout_sd", Value = Scouting.InfoSd(lv), NextValue = Scouting.InfoSd(Math.Min(5, lv + 1)) });
                    list.Add(new FacilityEffectView { Code = "gift_kind", Value = lv >= 3 ? 1 : 0, NextValue = Math.Min(5, lv + 1) >= 3 ? 1 : 0 });
                    break;
            }
            return list;
        }

        // ------------------------------------------------------------ 運営(SC-32)
        public OpsView Ops()
        {
            TeamState s = State;
            FacilityLevels f = s.Fac;
            int rank = School.Rank(), cap = WorldMath.FacilityCap(rank);
            FacilityLevels after = f;
            double funds2 = s.Funds;
            FacilityPolicy.Upgrade(ref after, ref funds2, rank);                       // 『推奨をすべて実行』のあとのレベル(状態は変えない)
            var v = new OpsView { Funds = s.Funds, Reserve = (int)WorldK.Reserve, CoachRank = rank, Cap = cap, Members = s.Runners.Count };
            for (int i = 0; i < FacilityPolicy.Order.Length; i++)
            {
                string key = FacilityPolicy.Order[i];
                int lv = FacilityPolicy.Level(f, key);
                var row = new FacilityRowView
                {
                    Key = key, NameKey = "fac." + key + ".name", IsStaff = IsStaffKey(key), Level = lv, Cap = cap, RecommendedOrder = i + 1,
                    RecommendedLevel = FacilityPolicy.Level(after, key), Effects = FacilityEffects(key, lv),
                };
                if (lv < cap) row.NextCost = WorldMath.UpgradeCost(lv);
                row.BlockedBy = lv >= cap ? "cap" : (s.Funds < row.NextCost.Value ? "funds" : null);
                row.CanUpgrade = row.BlockedBy == null;
                v.RecommendedCount += row.RecommendedLevel - lv;
                v.Rows.Add(row);
            }
            v.IncomeFormula = WorldMath.Income(School.Prestige, School.PrevPoints);
            v.IncomeMod = s.ModSum(ModKind.FundsYear);
            v.Income = v.IncomeFormula + v.IncomeMod;
            v.Staff = WorldMath.StaffExpense(f.Coach, f.Trainer, f.Nutri, f.Scout);
            v.Upkeep = WorldMath.FacilityUpkeep(f.Dorm, f.Track, f.Hill, f.Gym, f.Medical);
            v.MembersExpense = WorldMath.RunnerExpense(s.Runners.Count);
            v.Expense = v.Staff + v.Upkeep + v.MembersExpense;
            v.Balance = v.Income - v.Expense;
            foreach (PlayerYearStats st in Stats) v.History.Add(new FundsPointView { Year = st.Year, Funds = st.Funds });
            return v;
        }

        // ------------------------------------------------------------ 全国の大学(SC-35)
        public WorldView WorldOverview()
        {
            var v = new WorldView { Year = Year };
            var power = new Dictionary<int, double>();
            foreach (School sc in World.Schools) power[sc.Id] = sc.IsPlayer ? OwnPower() : World.TeamPower(sc);
            var rows = new List<WorldSchoolRowView>();
            foreach (School sc in World.Schools)
            {
                double p = power[sc.Id];
                rows.Add(new WorldSchoolRowView
                {
                    SchoolId = sc.Id, NameKey = "school." + sc.Id + ".name", IsOwn = sc.IsPlayer, Power = p, PowerRank = 1 + World.Schools.Count(o => power[o.Id] > p),
                    Prestige = sc.Prestige, Tier = sc.Tier, Region = sc.Region, PrevPoints = sc.PrevPoints,
                    LastNewYearPlace = sc.LastNewYearRank.HasValue ? (int?)(sc.LastNewYearRank.Value + 1) : null,
                });
            }
            v.Schools.AddRange(rows.OrderBy(r => r.PowerRank).ThenBy(r => r.SchoolId));
            int me = School.Id;
            int fall = World.FallOrder().FindIndex(x => x.Id == me) + 1, national = World.PrestigeOrder().FindIndex(x => x.Id == me) + 1;
            v.Eligibility[0] = new EligibilityView { Kind = RaceKind.Fall, Cutoff = 20, Position = fall, InRange = fall <= 20, Short = Math.Max(0, fall - 20) };
            v.Eligibility[1] = new EligibilityView { Kind = RaceKind.National, Cutoff = 30, Position = national, InRange = national <= 30, Short = Math.Max(0, national - 30) };
            bool seed = World.SeedIds().Contains(me);
            v.Eligibility[2] = new EligibilityView
            {
                Kind = RaceKind.NewYear, Cutoff = 10, InRange = seed, NeedsQualifier = !seed,
                Position = seed && School.LastNewYearRank.HasValue ? School.LastNewYearRank.Value + 1 : 0,
            };
            return v;
        }

        // ------------------------------------------------------------ OB 名簿(SC-33)
        private ObRowView ObRow(OBRecord ob)
        {
            return new ObRowView
            {
                Grad = GraduateRow(ob), EnrollYear = ob.EnrollYear, GradYear = ob.GradYear, Role = ob.Role,
                RoleWeeksLeft = ob.Role != ObRole.None ? Math.Max(0, ob.Until - State.AbsWeek) : 0,
            };
        }

        private IEnumerable<OBRecord> ObsNewestFirst() { return State.Obs.OrderByDescending(o => o.GradYear).ThenBy(o => o.RunnerId); }

        public ObView Obs()
        {
            var v = new ObView { Total = State.Obs.Count };
            foreach (OBRecord ob in ObsNewestFirst())
            {
                ObRowView row = ObRow(ob);
                v.Rows.Add(row);
                if (ob.Role != ObRole.None) v.Roles.Add(row);
            }
            foreach (var g in State.Obs.GroupBy(o => o.Pref).OrderBy(g => g.Key == 0 ? 1000 : g.Key))
            {
                int n = g.Count(), lv = Math.Min(5, (int)Math.Sqrt(n));
                v.Network.Add(new ObNetworkRowView
                {
                    Pref = g.Key, PrefName = g.Key >= 1 && g.Key <= 47 ? Geo.ById(g.Key).Name : "", Count = n, Level = lv, ToNextLevel = lv >= 5 ? 0 : (lv + 1) * (lv + 1) - n,
                });
            }
            return v;
        }

        // ------------------------------------------------------------ 歴史・殿堂(SC-34 の自校の部分)
        /// <summary>自校の在籍の選手・OB の名前(わからなければ空)。</summary>
        private string RunnerNameOf(int runnerId)
        {
            if (runnerId == 0) return "";
            Runner r = State.FindRunner(runnerId);
            if (r != null) return r.FamilyName + r.GivenName;
            foreach (OBRecord ob in State.Obs) if (ob.RunnerId == runnerId) return ob.Name;
            return "";
        }

        public HistoryView History()
        {
            var v = new HistoryView();
            ChampionRowView row = null;
            foreach (RaceEntry e in World.History.Races)
            {
                if (row == null || row.Year != e.Year) { row = new ChampionRowView { Year = e.Year, PreGame = e.Year <= 0 }; v.Champions.Add(row); }
                int k = (int)e.Kind;
                row.WinnerId[k] = e.WinnerId;
                row.WinnerNameKey[k] = "school." + e.WinnerId + ".name";
                row.IsOwn[k] = e.WinnerId == School.Id;
            }
            for (int k = 0; k < 3; k++)
            {
                CourseRecord[] cr = World.History.Course[k];
                if (cr != null)
                    for (int si = 0; si < cr.Length; si++)
                    {
                        CourseRecord c = cr[si];
                        if (c == null) continue;
                        bool own = c.SchoolId == School.Id;
                        v.CourseRecords.Add(new CourseRecordView
                        {
                            Kind = (RaceKind)k, Section = si, SchoolId = c.SchoolId, SchoolNameKey = "school." + c.SchoolId + ".name", IsOwn = own,
                            RunnerId = own ? c.RunnerId : 0, RunnerName = own ? RunnerNameOf(c.RunnerId) : "", TimeDs = c.TimeDs, Year = c.Year,
                        });
                    }
                TeamRecord t = World.History.Team[k];
                if (t != null) v.TeamRecords.Add(new TeamRecordView { Kind = (RaceKind)k, SchoolId = t.SchoolId, SchoolNameKey = "school." + t.SchoolId + ".name", IsOwn = t.SchoolId == School.Id, TotalDs = t.TotalDs, Year = t.Year });
            }
            foreach (PlayerYearStats st in Stats)
            {
                var y = new YearRecordView
                {
                    Year = st.Year, P = st.P, PowerRank = st.PowerRank, EvalPoints = st.Points, Prestige = st.Prestige, CoachRank = st.CoachRank,
                    TripleCrown = st.Wins[0] && st.Wins[1] && st.Wins[2],
                };
                for (int k = 0; k < 3; k++)
                {
                    y.Place[k] = st.Place[k];
                    y.Won[k] = st.Wins[k];
                    if (st.Wins[k]) { v.Titles++; if (v.FirstWinYear[k] == 0) v.FirstWinYear[k] = st.Year; }
                }
                if (y.TripleCrown) v.TripleCrowns++;
                v.Years.Add(y);
            }
            bool currentCounted = Stats.Count > 0 && Stats[Stats.Count - 1].Year == Year;
            if (!currentCounted)                                                       // 今年度の優勝(年度末の精算の前。まだ戦績には入っていない)
                for (int k = 0; k < 3; k++)
                    if ((_yearWins & (1 << k)) != 0) { v.Titles++; if (v.FirstWinYear[k] == 0) v.FirstWinYear[k] = Year; }
            if (State.Cleared != null) { v.ClearYear = State.Cleared.Year; v.NoInvite = State.Cleared.NoInvite; }
            foreach (OBRecord ob in ObsNewestFirst()) if (ob.HallOfFame) v.HallOfFame.Add(ObRow(ob));
            var counts = new SortedDictionary<int, int>();
            foreach (Runner r in State.Runners) if (!r.Overseas && r.Pref >= 1 && r.Pref <= 47) { counts.TryGetValue(r.Pref, out int n); counts[r.Pref] = n + 1; }
            foreach (OBRecord ob in State.Obs) if (ob.Pref >= 1 && ob.Pref <= 47) { counts.TryGetValue(ob.Pref, out int n); counts[ob.Pref] = n + 1; }
            foreach (var kv in counts) v.Prefectures.Add(new PrefCountView { Pref = kv.Key, PrefName = Geo.ById(kv.Key).Name, Count = kv.Value });
            v.PrefecturesCovered = counts.Count;
            return v;
        }
    }
}
