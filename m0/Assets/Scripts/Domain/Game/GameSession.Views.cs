using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Race;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.Training;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;

namespace Ekiden.Domain.Game
{
    /// <summary>画面に出す値を、規則(08 §8)で組み立てた『読み取りの API』(状態は変えない)。画面の層はこれを呼ぶだけで、表示に要る値がそろう(08b)。</summary>
    public sealed partial class GameSession
    {
        // ------------------------------------------------------------ ホーム(SC-08)
        public HomeView Home()
        {
            TeamState s = State;
            int yw = Yw, month = yw / 4;
            var h = new HomeView
            {
                SchoolId = School.Id, SchoolNameKey = "school." + School.Id + ".name", ManagerName = s.ManagerName,
                Year = Year, YearWeek = yw, CalendarMonth = (3 + month) % 12 + 1, WeekOfMonth = yw % 4 + 1,
                CoachRank = School.Rank(), CumPoints = School.CumPoints, Prestige = s.Prestige, Funds = s.Funds, Morale = s.Morale,
                PowerRank = CurrentPowerRank(), Cleared = s.Cleared != null, ClearYear = s.Cleared != null ? (int?)s.Cleared.Year : null,
                GuidanceLeft = s.GuidanceLeft, YearSettled = IsYearSettled,
            };
            int rank = h.CoachRank;
            h.NextRankPoints = rank < WorldK.RankCum.Length ? (int?)WorldK.RankCum[rank] : null;
            for (int k = 0; k < 3; k++) h.WonEver[k] = Stats.Any(x => x.Wins[k]) || (_yearWins & (1 << k)) != 0;

            int weeks, kind;
            Calendar.NextRace(yw, out weeks, out kind);
            int ki = kind - 1;
            YearState ys = PrepareYear();
            var next = new NextRaceView { Kind = (RaceKind)ki, WeeksUntil = weeks };
            next.CanEnter = yw <= Calendar.RaceWeeks[ki] ? (bool?)ys.Entrants((RaceKind)ki).Contains(School.Id) : null;
            int toDeadline = TeamWeek.EntryDeadlineWeeks[ki] - yw;
            next.EntryDeadlineWeeksLeft = toDeadline >= 0 ? toDeadline : -1;
            bool inWindow = yw >= TeamWeek.EntryDeadlineWeeks[ki] && yw <= Calendar.RaceWeeks[ki];
            next.EntryOpen = inWindow && next.CanEnter == true;                                           // SetEntries の期間(締切の週から大会の週まで)
            next.OrderOpen = inWindow && !_weekResolved && next.CanEnter == true && s.Entries[ki].Count > 0;   // SetOrder の期間(大会の週の初めまで。登録が済んでいること)
            h.Next = next;
            if (yw <= QualifierWeek && !ys.Seeds.Contains(School.Id)) h.QualifierWeeksUntil = QualifierWeek - yw;

            h.ExpectedTargetRank = Math.Max(1, s.LastRank[2] - 1);
            h.ExpectedBonusFunds = TargetBonusFunds();

            int active = s.Runners.Count(r => r.RecruitSource == RecruitSource.Ticket);
            h.Declarable = s.Tickets.CanDeclare(Year, month, active);
            h.TicketStock = s.Tickets.Stock;
            h.TicketPending = s.Tickets.Pending != null;
            return h;
        }

        /// <summary>期待目標を達成したときの予算ボーナス(万円。イベント <c>ev.team.univ_bonus</c> の既定の選択肢の資金)。</summary>
        private int TargetBonusFunds()
        {
            EventDef bonus = Catalog.Events.FirstOrDefault(e => e.Id == "ev.team.univ_bonus");
            return bonus != null ? bonus.Choices[0].Fx.Where(f => f.Op == EffectOp.Funds).Sum(f => f.D) : 0;
        }

        /// <summary>いまの戦力順位(自校の先発 10 人の力の平均を、世界の他校の戦力と比べる。1 = 最強)。</summary>
        private int CurrentPowerRank()
        {
            double mine = OwnPower();
            int better = 0;
            foreach (School o in World.Schools)
                if (!o.IsPlayer && World.TeamPower(o) > mine) better++;
            return 1 + better;
        }

        // ------------------------------------------------------------ 選手の行・選手詳細(SC-18・19ほか)
        /// <summary>部員全員の行(ID の昇順)。<paramref name="selectedId"/> があれば、その選手と同郷・同じ高校の印を付ける(08 §7.1)。</summary>
        public List<RunnerRowView> RosterRows(int? selectedId = null)
        {
            Runner sel = selectedId.HasValue ? State.FindRunner(selectedId.Value) : null;
            return State.Runners.OrderBy(r => r.Id).Select(r => RowOf(r, sel)).ToList();
        }

        public RunnerRowView RunnerRow(int runnerId, int? selectedId = null)
        {
            Runner r = State.FindRunner(runnerId);
            if (r == null) throw new ArgumentException("その選手はいない: " + runnerId);
            return RowOf(r, selectedId.HasValue ? State.FindRunner(selectedId.Value) : null);
        }

        private RunnerRowView RowOf(Runner r, Runner sel)
        {
            double p = TeamState.LongP(r);
            var v = new RunnerRowView
            {
                Id = r.Id, FamilyName = r.FamilyName, GivenName = r.GivenName, FamilyKana = r.FamilyKana, GivenKana = r.GivenKana,
                Grade = r.Grade, Captain = r.Captain, Group = r.Group, P = p, Rank = DisplayRules.RankLetter(p), PRank = r.PRank, Regular = r.Regular,
                Fatigue = r.Fatigue, Form = r.Form, Motivation = r.Motivation, Injury = r.InjurySeverity, InjuryWeeksLeft = r.InjuryWeeksLeft,
                Pref = r.Pref, HighSchool = r.HighSchool, Overseas = r.Overseas, Country = r.Country,
                Ticket = r.RecruitSource == RecruitSource.Ticket, TicketKind = r.TicketKind,
            };
            if (r.Pref >= 1 && r.Pref <= 47) v.PrefName = Geo.ById(r.Pref).Name;
            if (r.Overseas && r.Country >= 1 && r.Country <= Geo.Countries.Length) v.CountryName = Geo.Countries[r.Country - 1];
            if (r.HighSchool > 0) v.HighSchoolNameKey = "hs." + r.HighSchool + ".name";
            foreach (string id in r.Skills)
            {
                v.SkillCount++;
                SkillDef sd = Catalog.Skills.FirstOrDefault(x => "skill." + x.Id == id);
                SkillRank rank = sd != null ? sd.Rank : SkillRank.Blue;
                if (rank == SkillRank.Gold) v.GoldSkills++; else if (rank == SkillRank.Red) v.RedSkills++; else v.BlueSkills++;
            }
            if (r.Style.HasValue) v.StyleChips = Presentation.StyleChips(r.Style.Value);
            v.MountainAdvice = r.GiftRevealed && r.Gifts.Any(g => g == Gift.Climber || g == Gift.Descender);
            v.Traits = r.Traits.Select(t => t.ToLowerInvariant()).ToList();
            if (sel != null && sel.Id != r.Id)
            {
                v.SameHometownAsSelected = r.Pref != 0 && r.Pref == sel.Pref;
                v.SameHighSchoolAsSelected = r.HighSchool != 0 && r.HighSchool == sel.HighSchool;
            }
            return v;
        }

        public RunnerDetailView RunnerDetail(int runnerId)
        {
            Runner r = State.FindRunner(runnerId);
            if (r == null) throw new ArgumentException("その選手はいない: " + runnerId);
            var d = new RunnerDetailView
            {
                Row = RowOf(r, null), Races = r.Races, SectionWins = r.SectionWins, Brakes = r.Brakes, RelayWins = r.RelayWins, EnrollWeek = r.EnrollWeek,
                Growth = r.GrowthType, Style = r.Style, GiftRevealed = r.GiftRevealed, Talked = State.Talked.Contains(r.Id), ThemeId = State.MonthlyPlan[r.Group],
            };
            for (int i = 0; i < StatInfo.Count; i++)
                d.Stats.Add(new StatView { Stat = (Stat)i, Value = r.Stats[i], Rank = DisplayRules.RankLetter(r.Stats[i]), Hint = DisplayRules.Hint(r.Stats[i], r.Potential[i]) });
            var dv = new double[StatInfo.Count];
            for (int i = 0; i < dv.Length; i++) dv[i] = r.Stats[i];
            foreach (SectionType t in Enum.GetValues(typeof(SectionType)))
            {
                double p = RaceTables.Rating(dv, t);
                d.Fits.Add(new FitView { Type = t, Value = p, Mark = p >= 70.0 ? '◎' : (p >= 60.0 ? '〇' : '△') });
            }
            d.Pb5000 = DisplayRules.DerivePb(r.Stats, DisplayRules.PbEvent.M5000);
            d.Pb10000 = DisplayRules.DerivePb(r.Stats, DisplayRules.PbEvent.M10000);
            d.PbHalf = DisplayRules.DerivePb(r.Stats, DisplayRules.PbEvent.Half);
            foreach (string id in r.Skills.OrderBy(x => x, StringComparer.Ordinal))
            {
                SkillDef sd = Catalog.Skills.FirstOrDefault(x => "skill." + x.Id == id);
                d.Skills.Add(new SkillView { Id = id, Rank = sd != null ? sd.Rank : SkillRank.Blue });
            }
            if (r.GiftRevealed) d.Gifts.AddRange(r.Gifts);                      // 素養は判明するまで出さない(10 §8)
            foreach (Runner o in State.Runners.Where(x => x.Id != r.Id).OrderBy(x => x.Id))
            {
                int b = State.Bond(r, o);
                if (b <= 0) continue;
                d.Bonds.Add(new BondView
                {
                    RunnerId = o.Id, Name = o.FamilyName + o.GivenName, Value = b, SameGroup = o.Group == r.Group,
                    SameHometown = r.Pref != 0 && r.Pref == o.Pref, SameHighSchool = r.HighSchool != 0 && r.HighSchool == o.HighSchool,
                });
            }
            d.Bonds = d.Bonds.OrderByDescending(x => x.Value).ThenBy(x => x.RunnerId).Take(8).ToList();
            IndividualMenu im;
            if (State.IndividualMenus.TryGetValue(r.Id, out im)) { d.IndividualMenu = im.Menu; d.IndividualUntilAbsWeek = im.UntilAbsWeek; }
            d.ThemeFitMark = Presentation.FitMark(ThemeFit(r, ThemeMenus(r.Group, d.ThemeId)));
            return d;
        }

        // ------------------------------------------------------------ 月間計画(SC-16)
        /// <summary>グループのテーマの 4 週のメニュー(自由編集 <c>th.custom</c> は、決めた 4 週)。</summary>
        private Menu[] ThemeMenus(int group, string theme)
        {
            if (theme == "th.custom") return State.CustomWeeks[group].Select(w => new Menu(w.Key1, w.Key2, w.Mileage)).ToArray();
            return Master.Themes[theme];
        }

        /// <summary>選手とテーマの相性の値(R-17)。素養は判明したものだけ反映する。</summary>
        private double ThemeFit(Runner r, Menu[] weeks)
        {
            return r.Style.HasValue ? Presentation.FitView(Master, r.Style.Value, r.Gifts, r.GiftRevealed, weeks) : Presentation.FitValue(Master, null, weeks);
        }

        private static readonly string[] ThemeOrder = { "th.base_build", "th.speed", "th.mountain", "th.camp", "th.recovery" };

        public PlanView Plan()
        {
            TeamState s = State;
            int yw = Yw, month = yw / 4, wk = yw % 4;
            var pv = new PlanView
            {
                MonthIndex = month, WeekOfMonth = wk, Focus = s.Plan.Focus, TargetRace = s.Plan.TargetRace, RecommendedTheme = PlanRules.AutoPlan(s.Plan.Focus, month),
                CampMonth = month == 4, GuidanceLeft = s.GuidanceLeft, PlanAuto = s.Settings.PlanAuto,
            };
            for (int g = 0; g < 3; g++)
            {
                List<Runner> members = s.Runners.Where(r => r.Group == g).OrderBy(r => r.Id).ToList();
                var gp = new GroupPlanView { Group = g, Count = members.Count, Injured = members.Count(r => r.IsInjured), SelectedTheme = s.MonthlyPlan[g] };
                gp.AvgFatigue = members.Count > 0 ? members.Average(r => (double)r.Fatigue) : 0.0;
                var themes = new List<string>(ThemeOrder.Where(t => Master.Themes.ContainsKey(t)));
                themes.AddRange(Master.Themes.Keys.Where(t => !themes.Contains(t)).OrderBy(t => t, StringComparer.Ordinal));
                if (s.CustomWeeks != null && s.CustomWeeks[g] != null) themes.Add("th.custom");
                foreach (string th in themes) gp.Options.Add(ThemeOption(members, g, th, pv.RecommendedTheme, gp.SelectedTheme));
                pv.Groups.Add(gp);
            }
            return pv;
        }

        private ThemeOptionView ThemeOption(List<Runner> members, int group, string theme, string recommended, string selected)
        {
            TeamState s = State;
            int yw = Yw, month = yw / 4, wk = yw % 4;
            var o = new ThemeOptionView { ThemeId = theme, Recommended = theme == recommended, Selected = theme == selected };
            Menu[] themeMenus = ThemeMenus(group, theme);
            foreach (Menu m in themeMenus) o.Weeks.Add(new MenuSpec(m.Key1, m.Key2, m.Mileage));
            double fEndSum = 0;
            var gain = new double[5];
            foreach (Runner r in members)
            {
                char mark = Presentation.FitMark(ThemeFit(r, themeMenus));
                if (mark == '◎') o.FitGood++; else if (mark == '〇') o.FitOk++; else o.FitLow++;
                var planned = new List<PlannedWeek>();
                for (int i = wk; i < 4; i++)
                {
                    int off = i - wk;
                    Menu menu = themeMenus[i];
                    bool individual = false;
                    IndividualMenu im;
                    if (r.IsInjured) menu = Master.Injured;
                    else if (s.IndividualMenus.TryGetValue(r.Id, out im) && im.UntilAbsWeek >= s.AbsWeek + off) { menu = new Menu(im.Menu.Key1, im.Menu.Key2, im.Menu.Mileage); individual = true; }
                    else
                    {
                        int taper = TeamWeek.TaperIndex(s, r, yw + off);
                        if (taper >= 0) menu = Master.Taper[taper];
                    }
                    planned.Add(new PlannedWeek { Menu = menu, MonthIndex = month, Camp = theme == "th.camp", Individual = individual });
                }
                ForecastResult f = Forecaster.Run(Master, r, planned, s.Fac, true, r.BondAvg, s.ModsFor(r));
                fEndSum += f.End.Fatigue;
                o.FatigueEndMax = Math.Max(o.FatigueEndMax, f.End.Fatigue);
                if (f.FMax >= GameFlow.WarnFatigue) o.Attention++;
                o.ExpectedInjuries += f.PInjury;
                o.SafetyWeeks += f.SafetyWeeks;
                double[] before = FractionalStats(r), after = FractionalStats(f.End);
                for (int t = 0; t < 5; t++) gain[t] += RaceTables.Rating(after, (SectionType)t) - RaceTables.Rating(before, (SectionType)t);
            }
            if (members.Count > 0)
            {
                o.FatigueEndAvg = fEndSum / members.Count;
                for (int t = 0; t < 5; t++) o.GainP[t] = gain[t] / members.Count;
            }
            return o;
        }

        /// <summary>能力 + 経験値の端数(次のレベルまでの進み)。月ごとの伸びの比較に使う(ThemeTable と同じ)。</summary>
        private static double[] FractionalStats(Runner r)
        {
            var v = new double[StatInfo.Count];
            for (int i = 0; i < v.Length; i++) v[i] = r.Stats[i] + r.Xp[i] / (10.0 * (20 + 0.6 * r.Stats[i]));
            return v;
        }

        // ------------------------------------------------------------ エントリー(SC-21)
        public EntryView Entry(RaceKind kind)
        {
            TeamState s = State;
            int k = (int)kind, yw = Yw;
            RaceRule rule = RaceTables.Rule(kind);
            bool qualified = PrepareYear().Entrants(kind).Contains(School.Id);
            var v = new EntryView
            {
                Kind = kind, Capacity = rule.Entry, Required = rule.Run, IntlRegisterMax = rule.IntlReg, IntlRunMax = rule.IntlRun, Qualified = qualified,
                DeadlineWeeksLeft = TeamWeek.EntryDeadlineWeeks[k] - yw >= 0 ? TeamWeek.EntryDeadlineWeeks[k] - yw : -1,
                Open = qualified && yw >= TeamWeek.EntryDeadlineWeeks[k] && yw <= Calendar.RaceWeeks[k],
            };
            List<int> entered = s.Entries[k];
            v.Registered = entered.Count;
            foreach (Runner r in s.Runners.OrderBy(x => x.Id))
            {
                var dv = r.Stats.Select(x => (double)x).ToArray();
                SectionType best = SectionType.Fast;
                double bestP = double.MinValue;
                foreach (SectionType t in Enum.GetValues(typeof(SectionType)))
                {
                    double p = RaceTables.Rating(dv, t);
                    if (p > bestP) { bestP = p; best = t; }
                }
                v.Roster.Add(new EntryRowView
                {
                    Row = RowOf(r, null), BestType = best, BestFit = bestP, BestMark = bestP >= 70.0 ? '◎' : (bestP >= 60.0 ? '〇' : '△'),
                    Entered = entered.Contains(r.Id), CanEnter = !r.IsInjured, BlockedReason = r.IsInjured ? "injured" : null,
                });
            }
            v.Entered = entered.OrderByDescending(id => TeamState.LongP(s.FindRunner(id))).ThenBy(id => id).ToList();
            int intl = entered.Count(id => s.FindRunner(id) != null && s.FindRunner(id).Overseas);
            v.IntlRegistered = intl;
            if (entered.Count < rule.Run) v.Problems.Add(new ViewProblem { Code = "entry.short", Value = rule.Run });
            if (intl > rule.IntlReg) v.Problems.Add(new ViewProblem { Code = "entry.intl", Value = rule.IntlReg });
            foreach (int id in entered.OrderBy(x => x)) { Runner r = s.FindRunner(id); if (r != null && r.IsInjured) v.Problems.Add(new ViewProblem { Code = "entry.injured", RunnerId = id }); }
            return v;
        }

        // ------------------------------------------------------------ 区間オーダー(SC-22)
        public OrderView Order(RaceKind kind)
        {
            TeamState s = State;
            int k = (int)kind, yw = Yw;
            RaceRule rule = RaceTables.Rule(kind);
            IReadOnlyList<SectionDef> secs = RaceTables.Course(kind);
            bool qualified = PrepareYear().Entrants(kind).Contains(School.Id);
            IReadOnlyList<int> submitted = OrderOf(kind);
            int[] order = submitted != null ? submitted.ToArray() : SuggestOrder(kind);
            var v = new OrderView
            {
                Kind = kind, Submitted = submitted != null,
                Open = qualified && yw >= TeamWeek.EntryDeadlineWeeks[k] && yw <= Calendar.RaceWeeks[k] && !_weekResolved && s.Entries[k].Count > 0,
            };
            double total = 0;
            for (int i = 0; i < secs.Count; i++)
            {
                var c = new OrderSectionView { Section = i, Distance = secs[i].Distance, Type = secs[i].Type, Mountain = secs[i].Type == SectionType.Climb || secs[i].Type == SectionType.Descent };
                if (order != null && i < order.Length && s.FindRunner(order[i]) != null)
                {
                    KeyValuePair<double, char> fit = SectionFit(kind, order[i], i);
                    c.RunnerId = order[i];
                    c.Runner = RowOf(s.FindRunner(order[i]), null);
                    c.Fit = fit.Key;
                    c.Mark = fit.Value;
                    c.ExpectedSec = RaceMath.BaseTime(secs[i].Distance, secs[i].Terrain, fit.Key);
                    total += c.ExpectedSec;
                }
                v.Sections.Add(c);
            }
            v.TotalExpectedSec = total;
            HashSet<int> used = order != null ? new HashSet<int>(order) : new HashSet<int>();
            foreach (int id in s.Entries[k].OrderBy(x => x)) if (!used.Contains(id) && s.FindRunner(id) != null) v.Bench.Add(RowOf(s.FindRunner(id), null));
            if (qualified) v.Forecast.AddRange(Forecast(kind));
            // O1〜O3(いまの配置の検証)
            if (order != null)
            {
                if (order.Length != secs.Count) v.Problems.Add(new ViewProblem { Code = "order.count", Value = secs.Count });
                if (order.Distinct().Count() != order.Length) v.Problems.Add(new ViewProblem { Code = "order.dup" });
                int intl = 0;
                foreach (int id in order)
                {
                    Runner r = s.FindRunner(id);
                    if (r == null) continue;
                    if (r.Overseas) intl++;
                    if (r.IsInjured) v.Problems.Add(new ViewProblem { Code = "order.injured", RunnerId = id });
                    if (!s.Entries[k].Contains(id)) v.Problems.Add(new ViewProblem { Code = "order.not_entered", RunnerId = id });
                }
                if (intl > rule.IntlRun) v.Problems.Add(new ViewProblem { Code = "order.intl", Value = rule.IntlRun });
            }
            return v;
        }

        /// <summary>区間 <paramref name="section"/> の配置の候補(登録メンバーを、その区間の適性値の高い順に)。</summary>
        public List<OrderCandidateView> OrderCandidates(RaceKind kind, int section)
        {
            TeamState s = State;
            IReadOnlyList<SectionDef> secs = RaceTables.Course(kind);
            if (section < 0 || section >= secs.Count) throw new ArgumentException("その区間はない: " + section);
            IReadOnlyList<int> submitted = OrderOf(kind);
            int[] order = submitted != null ? submitted.ToArray() : SuggestOrder(kind);
            var list = new List<OrderCandidateView>();
            foreach (int id in s.Entries[(int)kind])
            {
                Runner r = s.FindRunner(id);
                if (r == null) continue;
                KeyValuePair<double, char> fit = SectionFit(kind, id, section);
                int at = order != null ? Array.IndexOf(order, id) : -1;
                list.Add(new OrderCandidateView
                {
                    Row = RowOf(r, null), Fit = fit.Key, Mark = fit.Value, ExpectedSec = RaceMath.BaseTime(secs[section].Distance, secs[section].Terrain, fit.Key),
                    AssignedSection = at >= 0 ? (int?)at : null,
                });
            }
            return list.OrderByDescending(c => c.Fit).ThenBy(c => c.Row.Id).ToList();
        }

        // ------------------------------------------------------------ 大会の結果(SC-26)
        /// <summary>走った大会の結果。<paramref name="own"/> は <see cref="PlayWeekRaces"/> が返した自校の結果(予想順位に使う。なければ null)。</summary>
        public RaceResultView RaceResult(RaceOutcome race, RaceInfo? own = null)
        {
            if (race == null) throw new ArgumentNullException("race");
            int n = RaceTables.Course(race.Kind).Count;
            var v = new RaceResultView { Kind = race.Kind, RaceId = race.RaceId, Sections = n };
            List<TeamResult> res = race.Results;
            int first = res.Count > 0 ? res[0].TotalDs : 0;
            for (int i = 0; i < res.Count; i++)
            {
                TeamResult t = res[i];
                v.Overall.Add(new ResultRowView { Rank = i + 1, SchoolId = t.Id, SchoolNameKey = "school." + t.Id + ".name", TotalDs = t.TotalDs, GapDs = t.TotalDs - first, Kuriage = t.Kuriage, IsOwn = t.Id == School.Id });
            }
            v.WinnerSchoolId = res.Count > 0 ? res[0].Id : -1;
            TeamResult mine = res.FirstOrDefault(t => t.Id == School.Id);
            RaceTeam team = race.Teams.FirstOrDefault(t => t.Id == School.Id);
            v.OwnEntered = mine != null && team != null;
            for (int si = 0; si < n; si++)
            {
                int best = res.Min(t => t.Sections[si]);
                TeamResult win = res.First(t => t.Sections[si] == best);
                var prize = new SectionPrizeView { Section = si, SchoolId = win.Id, SchoolNameKey = "school." + win.Id + ".name", Ds = best, IsOwn = win.Id == School.Id };
                if (prize.IsOwn && team != null) prize.RunnerId = team.Lineup[si].RunnerId;
                v.Prizes.Add(prize);
            }
            if (v.OwnEntered)
            {
                v.OwnRank = res.IndexOf(mine) + 1;
                v.ExpectedRank = own.HasValue ? own.Value.ExpectedRank : 0;
                v.EvalPoints = v.OwnRank >= 1 && v.OwnRank <= WorldK.YearPoints.Length ? WorldK.YearPoints[v.OwnRank - 1] : 0;
                for (int si = 0; si < n; si++)
                {
                    Runner r = State.FindRunner(team.Lineup[si].RunnerId);
                    int secRank = 1 + res.Count(t => t.Sections[si] < mine.Sections[si]);
                    int cumRank = 1 + res.Count(t => t.Cum[si] < mine.Cum[si]);
                    v.OwnLegs.Add(new OwnLegView
                    {
                        Section = si, RunnerId = team.Lineup[si].RunnerId, FamilyName = r != null ? r.FamilyName : "", GivenName = r != null ? r.GivenName : "",
                        Ds = mine.Sections[si], SectionRank = secRank, CumDs = mine.Cum[si], CumRank = cumRank,
                        Instruction = team.Instr != null ? team.Instr[si] : Instruction.Keep, Prize = secRank == 1,
                        Brake = mine.Events.Any(e => e.Section == si && e.Kind == "brake"), Stall = mine.Events.Any(e => e.Section == si && e.Kind == "stall"),
                        Geki = mine.Events.Any(e => e.Section == si && e.Kind == "geki_brake"), Kuriage = mine.Events.Any(e => e.Section == si && e.Kind == "kuriage"),
                        Record = race.Records.Any(u => u.Section == si && u.SchoolId == School.Id),
                    });
                }
            }
            foreach (RecordUpdate u in race.Records)
                v.Records.Add(new RecordUpdateView
                {
                    Section = u.Section, SchoolId = u.SchoolId, SchoolNameKey = "school." + u.SchoolId + ".name", IsOwn = u.SchoolId == School.Id, RunnerId = u.SchoolId == School.Id ? u.RunnerId : 0,
                    TimeDs = u.TimeDs, PrevTimeDs = u.PrevTimeDs, PrevSchoolId = u.PrevSchoolId, PrevSchoolNameKey = "school." + u.PrevSchoolId + ".name", PrevYear = u.PrevYear, Year = u.Year,
                });
            foreach (Highlight h in RaceHighlights.Top(RaceHighlights.Detect(res, School.Id, -1, race.Records)))
                v.Highlights.Add(HighlightOf(h, s => team != null ? team.Lineup[s].RunnerId : 0));
            return v;
        }
    }
}
