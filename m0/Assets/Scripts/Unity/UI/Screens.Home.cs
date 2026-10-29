using System.Linq;
using Ekiden.Domain.Game;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Text;
using Ekiden.Domain.Views;

namespace Ekiden.Unity.UI
{
    /// <summary>SC-08 ホーム。進行の『続ける』はここ(C-FLOW-01)。</summary>
    public sealed class HomeScreen : ScreenBase
    {
        public override string Id { get { return "scr.home"; } }
        public override bool CanGoBack { get { return false; } }

        public override void Build(Page p)
        {
            HomeView h = S.Home();
            p.NewRow();
            Region status = p.Region("status", scroll: true, title: C.T("scr.home.status"));
            status.Text(C.School(h.SchoolNameKey) + "  " + C.T("scr.home.manager", "name", h.ManagerName), "bold");
            status.Text(C.T("ui.date", "year", h.Year.ToString(), "month", h.CalendarMonth.ToString(), "week", h.WeekOfMonth.ToString()));
            status.Text(C.T("scr.home.rank", "rank", h.CoachRank.ToString(), "points", h.CumPoints.ToString("F0"), "next", h.NextRankPoints.HasValue ? h.NextRankPoints.Value.ToString() : "—"));
            status.Text(C.T("scr.home.numbers", "prestige", h.Prestige.ToString("F1"), "funds", Fmt.Money(h.Funds), "morale", h.Morale.ToString("F0"), "power", h.PowerRank.ToString()));
            status.Text(C.T(h.Cleared ? "scr.home.cleared" : "scr.home.not_cleared", "year", h.ClearYear.HasValue ? h.ClearYear.Value.ToString() : ""), h.Cleared ? "good" : "sub");
            status.Text(C.T("scr.home.won", "fall", Mark(h.WonEver[0]), "national", Mark(h.WonEver[1]), "newyear", Mark(h.WonEver[2])), "sub");

            Region next = p.Region("next", scroll: true, title: C.T("scr.home.next"));
            if (h.Next != null)
            {
                string race = C.RaceName(h.Next.Kind);
                next.Text(h.Next.WeeksUntil == 0 ? C.T("scr.home.race_this_week", "race", race) : C.T("scr.home.race_in", "race", race, "weeks", h.Next.WeeksUntil.ToString()), "bold");
                if (h.Next.CanEnter == false) next.Text(C.T("scr.home.not_qualified"), "warn");
                if (h.Next.EntryDeadlineWeeksLeft >= 0) next.Text(C.T("scr.home.deadline", "weeks", h.Next.EntryDeadlineWeeksLeft.ToString()), h.Next.EntryDeadlineWeeksLeft == 0 ? "warn" : null);
            }
            if (h.QualifierWeeksUntil.HasValue) next.Text(C.T("scr.home.qualifier", "weeks", h.QualifierWeeksUntil.Value.ToString()));
            next.Text(C.T("scr.home.target", "rank", h.ExpectedTargetRank.ToString(), "bonus", Fmt.Money(h.ExpectedBonusFunds)));
            if (h.Declarable) next.Text(C.T("scr.home.declarable"), "good");
            next.Text(C.T("scr.home.guidance", "n", h.GuidanceLeft.ToString()), "sub");

            p.NewRow(false);
            Region menu = p.Region("menu", horizontal: true);
            menu.Button(C.T("scr.home.menu.plan"), () => R.Show(new PlanScreen(), true), "btn-tile");
            menu.Button(C.T("scr.home.menu.roster"), () => R.Show(new RosterScreen(), true), "btn-tile");
            menu.Button(C.T("scr.home.menu.annual"), () => R.Show(new AnnualScreen(), true), "btn-tile");
            menu.Button(C.T("scr.home.menu.scout"), () => R.Show(new ScoutMapScreen(), true), "btn-tile");
            menu.Button(C.T("scr.home.menu.ops"), () => R.Show(new OpsScreen(), true), "btn-tile");
            menu.Button(C.T("scr.home.menu.history"), () => R.Show(new HistoryScreen(), true), "btn-tile");
            menu.Button(C.T("scr.home.menu.notify", "n", h.UnreadNotifications.ToString()), () => R.Show(new NotificationsScreen(), true), "btn-tile");
            if (h.Next != null && h.Next.EntryOpen) menu.Button(C.T("scr.home.menu.entry"), () => R.Show(new EntryScreen { Kind = h.Next.Kind }, true), "btn-tile");
            if (h.Next != null && h.Next.OrderOpen) menu.Button(C.T("scr.home.menu.order"), () => R.Show(new OrderScreen { Kind = h.Next.Kind }, true), "btn-tile");

            p.Primary(C.T("ui.continue"), R.Continue);
        }

        private string Mark(bool b) { return C.T(b ? "ui.yes" : "ui.no"); }
    }

    /// <summary>SC-10 年間方針(重点・注目大会)。</summary>
    public sealed class AnnualScreen : ScreenBase
    {
        public bool Flow;
        public override string Id { get { return "scr.annual"; } }
        public override bool CanGoBack { get { return !Flow; } }

        public override void Build(Page p)
        {
            AnnualPlanView v = S.AnnualPlanView();
            p.DefaultFocus = "focus";
            p.NewRow();
            Region focus = p.Region("focus", scroll: true, title: C.T("scr.annual.focus"));
            foreach (FocusOptionView o in v.Options)
            {
                PlanFocus f = o.Focus;
                string dp = string.Join(" ", new[] { "fast", "mid", "long", "climb", "descent" }.Select((k, i) => C.T("scr.annual.type." + k) + (o.DeltaP[i] >= 0 ? "+" : "") + o.DeltaP[i].ToString("F1")));
                var b = focus.Button(C.T("scr.annual.focus." + f.ToString().ToLowerInvariant()) + "\n" + dp + "  " + C.T("scr.annual.injury", "n", o.InjuryPerYear.ToString("F2")),
                    () => { if (C.Check(S.SetAnnualPlan(f, v.TargetRace, v.Peaking))) R.Rebuild(); }, "btn-row");
                if (o.Selected) b.AddToClassList("selected");
            }
            Region target = p.Region("target", width: 190, title: C.T("scr.annual.target"));
            for (int i = 0; i < 3; i++)
            {
                int t = i;
                var b = target.Button(C.RaceName((Domain.Race.RaceKind)i), () => { if (C.Check(S.SetAnnualPlan(v.Focus, t, v.Peaking))) R.Rebuild(); }, "btn-row");
                if (v.TargetRace == i) b.AddToClassList("selected");
            }
            target.Text(C.T("scr.home.target", "rank", v.ExpectedTargetRank.ToString(), "bonus", Fmt.Money(v.ExpectedBonusFunds)), "sub");
            p.NewRow(false);
            Region peaking = p.Region("peaking", horizontal: true);
            peaking.Text(C.T("scr.annual.peaking") + " " + string.Join(" / ", v.Peaking.Select(x => C.T("scr.annual.peaking." + x.ToString().ToLowerInvariant()))), "sub");
            if (Flow) p.Actions.Button(C.T("ui.home"), R.Home);
            else p.Actions.Button(C.T("ui.back"), R.Back);
            if (Flow) p.Primary(C.T("ui.decide_continue"), R.Continue);
        }
    }

    /// <summary>SC-16 月間計画(おまかせ / 手動。グループごとのテーマ)。</summary>
    public sealed class PlanScreen : ScreenBase
    {
        public bool Flow;
        private int _group;
        public override string Id { get { return "scr.plan"; } }
        public override bool CanGoBack { get { return !Flow; } }

        public override void Build(Page p)
        {
            PlanView v = S.Plan();
            p.DefaultFocus = "groups";
            p.NewRow(false);
            Region groups = p.Region("groups", horizontal: true);
            foreach (GroupPlanView g in v.Groups)
            {
                int gi = g.Group;
                var b = groups.Button(C.T("scr.plan.group", "g", "ABC"[gi].ToString(), "n", g.Count.ToString(), "theme", C.T(g.SelectedTheme + ".name"), "fatigue", g.AvgFatigue.ToString("F0")), () => { _group = gi; R.Rebuild(); }, "btn-row");
                if (gi == _group) b.AddToClassList("selected");
            }
            p.NewRow();
            GroupPlanView cur = v.Groups.FirstOrDefault(g => g.Group == _group) ?? v.Groups[0];
            Region forecast = p.Region("forecast", scroll: true, title: C.T("scr.plan.themes"));
            foreach (ThemeOptionView o in cur.Options)
            {
                string th = o.ThemeId;
                string text = C.T(o.ThemeId + ".name") + (o.Recommended ? " " + C.T("scr.plan.recommended") : "") + "\n"
                    + C.T("scr.plan.option", "good", o.FitGood.ToString(), "ok", o.FitOk.ToString(), "low", o.FitLow.ToString(), "fatigue", o.FatigueEndAvg.ToString("F0"), "max", o.FatigueEndMax.ToString(), "inj", o.ExpectedInjuries.ToString("F2"));
                var b = forecast.Button(text, () => { if (C.Check(S.SetMonthlyPlan(_group, th))) R.Rebuild(); }, "btn-row");
                if (o.Selected) b.AddToClassList("selected");
            }
            Region detail = p.Region("detail", width: 200, title: C.T("scr.plan.detail"));
            detail.Text(C.T("scr.plan.month", "month", (((v.MonthIndex + 3) % 12) + 1).ToString()));
            detail.Text(C.T(v.PlanAuto ? "scr.plan.auto_on" : "scr.plan.auto_off"), v.PlanAuto ? "good" : null);
            if (v.CampMonth) detail.Text(C.T("scr.plan.camp"), "warn");
            detail.Text(C.T("scr.home.guidance", "n", v.GuidanceLeft.ToString()), "sub");
            if (v.OutlookRace.HasValue) detail.Text(C.T("scr.plan.outlook", "race", C.RaceName(v.OutlookRace.Value), "weeks", v.OutlookWeeks.ToString()), "sub");
            detail.Button(C.T(v.PlanAuto ? "scr.plan.to_manual" : "scr.plan.to_auto"), () => { S.State.Settings.PlanAuto = !v.PlanAuto; if (S.State.Settings.PlanAuto) C.Check(S.ApplyRecommendedPlan()); R.Rebuild(); });
            p.Actions.Button(Flow ? C.T("ui.home") : C.T("ui.back"), Flow ? (System.Action)R.Home : R.Back);
            p.Actions.Button(C.T("scr.plan.apply_recommended"), () => { if (C.Check(S.ApplyRecommendedPlan())) R.Rebuild(); });
            if (Flow) p.Primary(C.T("ui.decide_continue"), R.Continue);
        }

        public override void Omakase() { if (C.Check(S.ApplyRecommendedPlan())) R.Rebuild(); }
    }

    /// <summary>SC-11 月末レポート。</summary>
    public sealed class MonthReportScreen : ScreenBase
    {
        public MonthReport Report;
        public override string Id { get { return "scr.report.month"; } }
        public override bool CanGoBack { get { return false; } }

        public override void Build(Page p)
        {
            MonthReportView v = S.MonthReportView(Report);
            p.NewRow(false);
            Region summary = p.Region("summary");
            summary.Text(C.T("scr.report.month.head", "month", v.CalendarMonth.ToString()), "title");
            string chg = v.ChangeKnown ? C.T("scr.report.month.change", "f", Sign(v.AvgFatigueChange), "c", Sign(v.AvgFormChange), "m", Sign(v.MoraleChange)) : "";
            summary.Text(C.T("scr.report.month.avg", "fatigue", v.AvgFatigue.ToString("F0"), "form", v.AvgForm.ToString("F1"), "morale", v.Morale.ToString("F0")) + "  " + chg);
            summary.Text(C.T("scr.report.month.themes", "a", C.T(v.ThemeIds[0] + ".name"), "b", C.T(v.ThemeIds[1] + ".name"), "c", C.T(v.ThemeIds[2] + ".name")), "sub");
            p.NewRow();
            Region hl = p.Region("highlights", scroll: true, title: C.T("scr.report.month.items"));
            foreach (ReportItemView it in v.Items)
            {
                int id = it.RunnerId;
                string text = C.T("scr.report.item." + it.Kind, "runner", it.RunnerName, "skill", it.SkillId != null ? C.T(it.SkillId + ".name") : "", "stat", it.Stat.HasValue ? C.T("stat." + it.Stat.Value + ".name") : "",
                    "letter", it.Letter.ToString(), "value", it.Value.ToString(), "event", it.EventId != null ? C.T(it.EventId + ".title") : "");
                if (id > 0 && S.RosterRows().Any(r => r.Id == id)) hl.Button(text, () => R.Show(new RunnerScreen { RunnerId = id }, true), "btn-row");
                else hl.Text(text);
            }
            if (v.More > 0) hl.Text(C.T("scr.report.month.more", "n", v.More.ToString()), "sub");
            foreach (MonthRaceView race in v.Races)
                hl.Text(C.T("scr.report.month.race", "race", C.RaceName(race.Kind), "winner", C.School(race.WinnerNameKey), "own", race.OwnRank > 0 ? race.OwnRank.ToString() : "—"));
            if (v.Caution.HighFatigue.Count > 0) hl.Text(C.T("scr.report.month.caution_fatigue", "n", v.Caution.HighFatigue.Count.ToString()), "warn");
            if (v.Caution.Injured.Count > 0) hl.Text(C.T("scr.report.month.caution_injured", "n", v.Caution.Injured.Count.ToString()), "warn");
            p.Actions.Button(C.T("ui.home"), R.Home);
            p.Primary(C.T("ui.continue"), R.Continue);
        }

        private static string Sign(double d) { return (d >= 0 ? "+" : "") + d.ToString("F1"); }
    }

    /// <summary>SC-38 通知(一覧)。</summary>
    public sealed class NotificationsScreen : ScreenBase
    {
        public override string Id { get { return "dlg.notify"; } }

        public override void Build(Page p)
        {
            NotificationsView v = S.Notifications();
            p.DefaultFocus = "list";
            p.NewRow();
            Region list = p.Region("list", scroll: true);
            foreach (NotificationView n in v.Items)
            {
                string text = (n.Unread ? "● " : "　") + C.T("scr.notify.when", "year", n.Year.ToString(), "month", (((n.MonthIndex + 3) % 12) + 1).ToString(), "week", n.WeekOfMonth.ToString()) + "  "
                    + C.T(n.TextKey, "runner", n.RunnerName, "n", n.N.ToString(), "arg", n.ArgKey != null ? C.T(n.ArgKey) : "");
                int idx = n.Index;
                list.Button(text, () => { S.MarkNotificationRead(idx); R.Rebuild(); }, "btn-row");
            }
            if (v.Items.Count == 0) list.Text(C.T("scr.notify.empty"), "sub");
            p.Actions.Button(C.T("ui.back"), R.Back);
            p.Primary(C.T("scr.notify.read_all"), () => { S.MarkAllNotificationsRead(); R.Rebuild(); });
        }
    }
}
