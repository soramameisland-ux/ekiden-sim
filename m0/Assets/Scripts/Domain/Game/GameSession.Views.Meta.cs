using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Race;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.Training;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using Calendar = Ekiden.Domain.Team.Calendar;

namespace Ekiden.Domain.Game
{
    /// <summary>設定(SC-04)・年間予定(SC-09)・クリア(SC-15)・個別指導(SC-20)の読み取りの API(08b)。状態は変えない。</summary>
    public sealed partial class GameSession
    {
        // ------------------------------------------------------------ 設定(SC-04。ゲームのタブ)
        public SettingsView Settings()
        {
            TeamSettings t = State.Settings;
            return new SettingsView
            {
                SpectateMode = t.SpectateMode, SpectateModes = (string[])SpectateModes.Clone(), AutoSafetyF = t.AutoSafetyF, AutoSafetyChoices = (int[])AutoSafetyChoices.Clone(),
                EventAuto = t.EventAuto, PlanAuto = t.PlanAuto, ScoutAuto = t.ScoutAuto, UniformPattern = t.UniformPattern, CheerSong = t.CheerSong, BgmSet = t.BgmSet,
            };
        }

        // ------------------------------------------------------------ 年間予定(SC-09)
        public CalendarView YearCalendar()
        {
            TeamState s = State;
            int yw = Yw, curMonth = yw / 4;
            YearState ys = PrepareYear();
            bool seed = ys.Seeds.Contains(School.Id);
            var v = new CalendarView { Year = Year, YearWeek = yw, CurrentMonthIndex = curMonth };

            // 行事(年度内の週の順)。第一〜第三戦・予選会・エントリーの締切・記録会(5・6 月の第 3 週 = 06 の ind.pb_meet)
            var items = new List<CalendarItemView>();
            Action<CalendarItemKind, RaceKind?, int> add = (kind, race, week) =>
            {
                var it = new CalendarItemView { Kind = kind, Race = race, YearWeek = week, WeekOfMonth = week % 4 + 1, Past = week < yw, Current = week == yw };
                if (race.HasValue && (kind == CalendarItemKind.Race || kind == CalendarItemKind.EntryDeadline))
                {
                    RaceKind rk = race.Value;
                    bool thirdBeforeQualifier = rk == RaceKind.NewYear && !seed && yw <= QualifierWeek;       // 予選会の結果で決まる(まだ分からない)
                    if (thirdBeforeQualifier) it.NoEntryCode = "qualifier";
                    else
                    {
                        it.Participates = ys.Entrants(rk).Contains(School.Id);
                        if (it.Participates == false) it.NoEntryCode = rk == RaceKind.NewYear ? "qualifier" : "rank";
                    }
                    if (kind == CalendarItemKind.Race) { it.Peaking = s.Plan.Peaking[(int)rk]; it.PeakingFromWeek = Calendar.RaceWeeks[(int)rk] - 3; }
                }
                else if (kind == CalendarItemKind.Qualifier || kind == CalendarItemKind.QualifierDeadline)
                {
                    it.Participates = !seed;                                       // シードは予選会を免除される
                    if (seed) it.NoEntryCode = "seed";
                }
                items.Add(it);
            };
            add(CalendarItemKind.Meet, null, 4 * 1 + 2);
            add(CalendarItemKind.Meet, null, 4 * 2 + 2);
            for (int k = 0; k < 3; k++)
            {
                add(CalendarItemKind.EntryDeadline, (RaceKind)k, TeamWeek.EntryDeadlineWeeks[k]);
                add(CalendarItemKind.Race, (RaceKind)k, Calendar.RaceWeeks[k]);
            }
            if (!seed) add(CalendarItemKind.QualifierDeadline, null, QualifierDeadlineWeek);
            add(CalendarItemKind.Qualifier, null, QualifierWeek);
            items = items.OrderBy(i => i.YearWeek).ThenBy(i => (int)i.Kind).ToList();

            for (int m = 0; m < 12; m++)
            {
                var mv = new CalendarMonthView
                {
                    MonthIndex = m, CalendarMonth = (3 + m) % 12 + 1, IsCurrent = m == curMonth, IsPast = m < curMonth,
                    Camp = m == 4, ScoutSurvey = m >= 2 && m <= 7, ScoutPursue = m >= 8 && m <= 10,
                    TicketWindow = m >= TicketK.K_TICKET_DECLARE_FROM && m <= TicketK.K_TICKET_DECLARE_TO, YearEnd = m == 11,
                };
                if (m == curMonth) mv.Themes = (string[])s.MonthlyPlan.Clone();
                else if (m > curMonth) { string t = PlanRules.AutoPlan(s.Plan.Focus, m); mv.Themes = new[] { t, t, t }; mv.ThemesAreRecommended = true; }
                foreach (CalendarItemView it in items) if (it.YearWeek / 4 == m) mv.Items.Add(it);
                v.Months.Add(mv);
            }
            return v;
        }

        // ------------------------------------------------------------ クリア(SC-15)
        public ClearView Clear()
        {
            var v = new ClearView { ScenarioId = State.Scenario.Id, Difficulty = State.Scenario.Difficulty };
            if (State.Cleared != null) { v.Cleared = true; v.ClearYear = State.Cleared.Year; v.NoInvite = State.Cleared.NoInvite; }
            foreach (PlayerYearStats st in Stats)
            {
                for (int k = 0; k < 3; k++)
                    if (st.Wins[k]) { v.Titles++; if (v.FirstWinYear[k] == 0) v.FirstWinYear[k] = st.Year; }
                if (st.Wins[0] && st.Wins[1] && st.Wins[2]) v.TripleCrowns++;
            }
            bool currentCounted = Stats.Count > 0 && Stats[Stats.Count - 1].Year == Year;
            if (!currentCounted)                                                       // 今年度の優勝(年度末の精算の前。まだ戦績には入っていない。History と同じ)
                for (int k = 0; k < 3; k++)
                    if ((_yearWins & (1 << k)) != 0) { v.Titles++; if (v.FirstWinYear[k] == 0) v.FirstWinYear[k] = Year; }
            return v;
        }

        // ------------------------------------------------------------ 個別指導(SC-20。R-06)
        public GuidanceView Guidance(int runnerId)
        {
            Runner r = State.FindRunner(runnerId);
            if (r == null) throw new ArgumentException("その選手はいない: " + runnerId);
            bool hasMenu = State.IndividualMenus.ContainsKey(runnerId);
            bool again = _guidedThisWeek.Contains(runnerId) && hasMenu;
            var v = new GuidanceView
            {
                Row = RowOf(r, null), PointsLeft = State.GuidanceLeft, PointsMax = WorldMath.GuidancePoints(State.Fac.Manager),
                Talked = State.Talked.Contains(runnerId), SoloBonus = r.IndMult > 1.0, CanCancel = hasMenu, CancelRefunds = again,
            };
            KeyValuePair<bool, Msg> talk = PlanRules.CanGuide(Master, State.GuidanceLeft, "talk", r.IsInjured, v.Talked);
            v.CanTalk = talk.Key; v.TalkWhy = talk.Value;
            // 指定の可否(R-06)。GuideMenu と同じ順で調べる: メニューの検証(故障中は err.menu.injured)→ ポイント。今週すでに指定していれば、前の指定を取り消してポイントが戻る分を足して数える
            List<Msg> verr = PlanRules.ValidateMenu(Master, Master.Safe.Key1, Master.Safe.Key2, Master.Safe.Mileage, r.IsInjured);
            if (verr.Count > 0) { v.CanSetMenu = false; v.MenuWhy = verr[0]; }
            else
            {
                KeyValuePair<bool, Msg> menu = PlanRules.CanGuide(Master, State.GuidanceLeft + (again ? 1 : 0), "menu", r.IsInjured, false, Tuple.Create(Master.Safe.Key1, Master.Safe.Key2, Master.Safe.Mileage));
                v.CanSetMenu = menu.Key; v.MenuWhy = menu.Value;
            }
            IndividualMenu im;
            if (State.IndividualMenus.TryGetValue(runnerId, out im)) { v.CurrentMenu = im.Menu; v.CurrentUntilAbsWeek = im.UntilAbsWeek; }
            v.Presets.Add(new GuidancePresetView { Id = "care", Menu = new MenuSpec(Master.Safe.Key1, Master.Safe.Key2, Master.Safe.Mileage) });
            v.Presets.Add(new GuidancePresetView { Id = "adjust", Menu = new MenuSpec("ks.sharpen", "ks.pace_run", "mil.low") });
            if (TeamWeek.TaperIndex(State, r, Yw) >= 0) v.Warnings.Add(OpErr.WarnOverrideTaper.Make());
            if (State.Settings.AutoSafetyF > 0 && r.Fatigue >= State.Settings.AutoSafetyF) v.Warnings.Add(OpErr.WarnNoSafety.Make());
            return v;
        }

        /// <summary>選んだメニューを今週から月末まで指定したときの予測(R-05)と、指定しなかったとき(いまの月間計画)の予測。状態は変えない。</summary>
        public GuidancePreviewView GuidancePreview(int runnerId, MenuSpec menu)
        {
            Runner r = State.FindRunner(runnerId);
            if (r == null) throw new ArgumentException("その選手はいない: " + runnerId);
            var v = new GuidancePreviewView();
            List<Msg> errs = PlanRules.ValidateMenu(Master, menu.Key1, menu.Key2, menu.Mileage, r.IsInjured);
            if (errs.Count > 0) { v.Valid = false; v.Why = errs[0]; return v; }
            v.Valid = true;
            int yw = Yw, month = yw / 4, wk = yw % 4;
            string theme = State.MonthlyPlan[r.Group];
            Menu[] themeMenus = ThemeMenus(r.Group, theme);
            bool camp = theme == "th.camp";
            var chosen = new Menu(menu.Key1, menu.Key2, menu.Mileage);
            v.FitMark = Presentation.FitMark(ThemeFit(r, new[] { chosen }));
            v.WeeksCovered = 4 - wk;
            Fill(v, r, PlannedWeeks(r, themeMenus, camp, (Menu?)chosen), isBaseline: false);
            Fill(v, r, PlannedWeeks(r, themeMenus, camp, (Menu?)null), isBaseline: true);
            return v;
        }

        /// <summary>今週から月末までの週(<see cref="ThemeOption"/> と同じ決め方)。<paramref name="forced"/> があれば、故障していない限り全週をそのメニューにする(個別指導 = ピーキングより優先・安全網の対象外)。なければいまの月間計画(指定中の個別指導は使わない)。</summary>
        private List<PlannedWeek> PlannedWeeks(Runner r, Menu[] themeMenus, bool camp, Menu? forced)
        {
            TeamState s = State;
            int yw = Yw, month = yw / 4, wk = yw % 4;
            var planned = new List<PlannedWeek>();
            for (int i = wk; i < 4; i++)
            {
                Menu menu = themeMenus[i];
                bool individual = false;
                if (r.IsInjured) menu = Master.Injured;
                else if (forced.HasValue) { menu = forced.Value; individual = true; }
                else
                {
                    int taper = TeamWeek.TaperIndex(s, r, yw + (i - wk));
                    if (taper >= 0) menu = Master.Taper[taper];
                }
                planned.Add(new PlannedWeek { Menu = menu, MonthIndex = month, Camp = camp, Individual = individual });
            }
            return planned;
        }

        private void Fill(GuidancePreviewView v, Runner r, List<PlannedWeek> planned, bool isBaseline)
        {
            TeamState s = State;
            ForecastResult f = Forecaster.Run(Master, r, planned, s.Fac, s.Settings.AutoSafetyF > 0, r.BondAvg, s.ModsFor(r), SafetyF);
            int levelups = f.Levelups.Sum();
            double[] before = FractionalStats(r), after = FractionalStats(f.End);
            double[] gain = isBaseline ? v.BaselineGainP : v.GainP;
            for (int t = 0; t < 5; t++) gain[t] = RaceTables.Rating(after, (SectionType)t) - RaceTables.Rating(before, (SectionType)t);
            if (isBaseline) { v.BaselineFatigueEnd = f.End.Fatigue; v.BaselineInjuryProb = f.PInjury; v.BaselineLevelups = levelups; }
            else { v.FatigueEnd = f.End.Fatigue; v.FatigueMax = f.FMax; v.SafetyWeeks = f.SafetyWeeks; v.InjuryProb = f.PInjury; v.Levelups = levelups; }
        }
    }
}
