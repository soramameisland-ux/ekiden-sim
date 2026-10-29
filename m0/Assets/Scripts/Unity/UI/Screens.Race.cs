using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Text;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;

namespace Ekiden.Unity.UI
{
    internal static class RaceText
    {
        public static string Type(UiContext c, SectionType t) { return c.T("scr.annual.type." + t.ToString().ToLowerInvariant()); }

        public static string Problems(UiContext c, List<ViewProblem> ps) { return string.Join("\n", ps.Select(x => c.T("problem." + x.Code, "n", x.Value.ToString()))); }

        public static string Highlight(UiContext c, HighlightView h)
        {
            return c.T(h.Key, "school", c.School(h.SchoolNameKey), "other", c.School(h.OtherSchoolNameKey), "section", h.Section.ToString(), "sec", (h.Ds / 10.0).ToString("F0"),
                "pct", h.Percent.ToString("F1"), "n", System.Math.Abs(h.RankDelta).ToString());
        }

        public static string Form(UiContext c, int f) { return c.T("form." + f + ".name"); }

        public static string Instr(UiContext c, Instruction i) { return c.T("instr." + i.ToString().ToLowerInvariant() + ".name"); }
    }

    /// <summary>SC-21 エントリー。行を選ぶと登録 / 外す(<c>SetEntries</c>)。おまかせ = <c>AutoEntries</c>。</summary>
    public sealed class EntryScreen : ScreenBase
    {
        public RaceKind Kind;
        public bool Flow;
        public override string Id { get { return "scr.entry"; } }
        public override bool CanGoBack { get { return !Flow; } }

        public override void Build(Page p)
        {
            EntryView v = S.Entry(Kind);
            p.DefaultFocus = "roster";
            p.NewRow(false);
            Region summary = p.Region("summary");
            summary.Text(C.RaceName(Kind) + "  " + C.T("scr.entry.summary", "reg", v.Registered.ToString(), "cap", v.Capacity.ToString(), "req", v.Required.ToString(),
                "intl", v.IntlRegistered.ToString(), "intlmax", v.IntlRegisterMax.ToString(), "deadline", v.DeadlineWeeksLeft.ToString()), "bold");
            if (!v.Qualified) summary.Text(C.T("scr.entry.not_qualified"), "warn");
            if (v.Problems.Count > 0) summary.Text(RaceText.Problems(C, v.Problems), "warn");
            p.NewRow();
            Region roster = p.Region("roster", scroll: true, title: C.T("scr.entry.roster"));
            foreach (EntryRowView row in v.Roster.OrderByDescending(r => r.Row.P))
            {
                int id = row.Row.Id;
                string text = (row.Entered ? "✔ " : "　") + UiContext.Name(row.Row) + "  " + Fmt.Grade(row.Row.Grade) + "  P" + row.Row.P.ToString("F1") + "(" + row.Row.Rank + ")  "
                    + C.T("scr.entry.fit", "type", RaceText.Type(C, row.BestType), "mark", row.BestMark.ToString()) + "  " + RaceText.Form(C, row.Row.Form)
                    + (row.Outlook != null ? "  " + C.T("scr.entry.good", "pct", Fmt.Pct(row.Outlook.PGood)) : "");
                if (!row.CanEnter) { roster.Blocked(text, C.T("scr.entry.blocked." + (row.BlockedReason ?? "injured")), R.Toast, "btn-row"); continue; }
                roster.Button(text, () => Toggle(v, id), "btn-row", v.Open);
            }
            Region entered = p.Region("entered", width: 170, scroll: true, title: C.T("scr.entry.entered"));
            foreach (int id in v.Entered)
            {
                EntryRowView r = v.Roster.FirstOrDefault(x => x.Row.Id == id);
                if (r != null) entered.Text(UiContext.Name(r.Row), "nowrap");
            }
            p.Actions.Button(Flow ? C.T("ui.home") : C.T("ui.back"), Flow ? (System.Action)R.Home : R.Back);
            p.Actions.Button(C.T("ui.omakase"), Omakase, null);
            if (Flow) p.Primary(C.T("ui.decide_continue"), R.Continue);
            else p.Primary(C.T("scr.entry.to_order"), () => R.Show(new OrderScreen { Kind = Kind }, true));
        }

        private void Toggle(EntryView v, int id)
        {
            var ids = v.Entered.ToList();
            if (ids.Contains(id)) ids.Remove(id); else ids.Add(id);
            R.Run(ok => S.SetEntries(Kind, ids, ok), R.Rebuild);
        }

        public override void Omakase() { if (C.Check(S.AutoEntries(Kind))) R.Rebuild(); }

        public override void AutoDefault() { R.Continue(); }
    }

    /// <summary>予選会のエントリー(SC-21 の予選会)。M1 は おまかせ(力の上位 12 名)で出る。</summary>
    public sealed class QualifierEntryScreen : ScreenBase
    {
        public override string Id { get { return "scr.entry.qualifier"; } }
        public override bool CanGoBack { get { return false; } }

        public override void Build(Page p)
        {
            p.NewRow();
            Region body = p.Region("summary");
            body.Text(C.T("scr.entry.qualifier.body"), "bold");
            foreach (RunnerRowView r in S.RosterRows().Where(x => x.Injury == Domain.Entities.InjurySeverity.None).OrderByDescending(x => x.P).Take(12))
                body.Text(UiContext.Name(r) + "  P" + r.P.ToString("F1"), "sub");
            p.Actions.Button(C.T("ui.home"), R.Home);
            p.Primary(C.T("ui.decide_continue"), R.Continue);
        }
    }

    /// <summary>SC-22 区間オーダー。区間を選び、候補を選ぶと配置(入れ替え)。おまかせ = <c>ClearOrder</c>(最適な配置)。</summary>
    public sealed class OrderScreen : ScreenBase
    {
        public RaceKind Kind;
        public bool Flow;
        private int _section;
        public override string Id { get { return "scr.order"; } }
        public override bool CanGoBack { get { return !Flow; } }

        public override void Build(Page p)
        {
            OrderView v = S.Order(Kind);
            p.DefaultFocus = "sections";
            p.NewRow();
            Region sections = p.Region("sections", scroll: true, title: C.RaceName(Kind) + "  " + C.T(v.Submitted ? "scr.order.submitted" : "scr.order.auto") + "  " + C.T("scr.order.total", "time", Fmt.Total((long)(v.TotalExpectedSec * 10))));
            foreach (OrderSectionView s in v.Sections)
            {
                int sec = s.Section;
                string text = C.T("scr.order.section", "n", (s.Section + 1).ToString(), "km", Fmt.Km(s.Distance), "type", RaceText.Type(C, s.Type)) + (s.Mountain ? " ⛰" : "") + "  "
                    + (s.Runner != null ? UiContext.Name(s.Runner) + "  " + C.T("scr.entry.fit", "type", "", "mark", s.Mark.ToString()) + "  " + Fmt.Time((long)(s.ExpectedSec * 10)) : C.T("scr.order.empty"));
                var b = sections.Button(text, () => { _section = sec; R.Rebuild(); }, "btn-row");
                if (sec == _section) b.AddToClassList("selected");
            }
            Region cands = p.Region("candidates", scroll: true, width: 250, title: C.T("scr.order.candidates", "n", (_section + 1).ToString()));
            foreach (OrderCandidateView c in S.OrderCandidates(Kind, _section).Take(16))
            {
                int id = c.Row.Id;
                string text = UiContext.Name(c.Row) + "  " + c.Mark + "  " + Fmt.Time((long)(c.ExpectedSec * 10)) + (c.AssignedSection.HasValue ? "  " + C.T("scr.order.assigned", "n", (c.AssignedSection.Value + 1).ToString()) : "");
                cands.Button(text, () => Assign(v, id), "btn-row", v.Open);
            }
            if (v.Problems.Count > 0) cands.Text(RaceText.Problems(C, v.Problems), "warn");
            p.Actions.Button(Flow ? C.T("ui.home") : C.T("ui.back"), Flow ? (System.Action)R.Home : R.Back);
            p.Actions.Button(C.T("ui.omakase"), Omakase);
            if (Flow) p.Primary(C.T("ui.decide_continue"), R.Continue);
        }

        private void Assign(OrderView v, int id)
        {
            List<int> ids = v.Sections.Select(s => s.RunnerId ?? 0).ToList();
            int from = ids.IndexOf(id);
            int prev = ids[_section];
            ids[_section] = id;
            if (from >= 0 && from != _section) ids[from] = prev;               // 別の区間にいたら入れ替え
            if (ids.Any(x => x == 0)) { C.Router.Toast(C.T("scr.order.fill_all")); return; }
            if (C.Check(S.SetOrder(Kind, ids))) R.Rebuild();
        }

        public override void Omakase() { if (C.Check(S.ClearOrder(Kind))) R.Rebuild(); }
    }

    /// <summary>SC-24 当日(調子・環境・当日変更・指示)。『スタート』でレース進行(SC-25)へ。</summary>
    public sealed class RaceDayScreen : ScreenBase
    {
        public RaceDay Day;
        private int _leg;
        public override string Id { get { return "scr.raceday"; } }
        public override bool CanGoBack { get { return false; } }

        public override void Build(Page p)
        {
            RaceDayView v = S.RaceDayView(Day);
            p.DefaultFocus = "lineup";
            p.NewRow(false);
            Region env = p.Region("env");
            env.Text(C.RaceName(v.Kind) + "  " + string.Join(" / ", v.Env.Select(e => C.T("scr.raceday.env", "temp", e.Temp.ToString("F1"), "wind", e.Wind.ToString("F1"), "rain", C.T(e.Rain ? "ui.yes" : "ui.no")))), "bold");
            env.Text(C.T("scr.raceday.left", "swap", v.SwapsLeft.ToString(), "geki", v.GekiLeft.ToString(), "rank", v.ExpectedRank.ToString(), "time", Fmt.Total(v.ExpectedTotalDs)), "sub");
            p.NewRow();
            Region lineup = p.Region("lineup", scroll: true, title: C.T("scr.raceday.lineup"));
            foreach (DayLegView l in v.Legs)
            {
                int sec = l.Section;
                string text = C.T("scr.order.section", "n", (l.Section + 1).ToString(), "km", Fmt.Km(l.Distance), "type", RaceText.Type(C, l.Type)) + "  " + l.Name + (l.Swapped ? " ⇄" : "") + "  "
                    + RaceText.Form(C, l.Form) + "  " + C.T("scr.raceday.fit", "mark", l.FitMark.ToString()) + "  " + C.T("scr.raceday.pace", "mark", l.PaceMark.ToString()) + "  " + RaceText.Instr(C, l.Instr) + "  " + Fmt.Time((long)(l.ExpectedSec * 10));
                var b = lineup.Button(text, () => { _leg = sec; R.Rebuild(); }, "btn-row", !l.Passed);
                if (sec == _leg) b.AddToClassList("selected");
            }
            Region bench = p.Region("bench", scroll: true, width: 210, title: C.T("scr.raceday.bench", "n", (_leg + 1).ToString()));
            DayLegView cur = v.Legs.FirstOrDefault(l => l.Section == _leg);
            if (cur != null)
                bench.Button(C.T("scr.raceday.instr", "instr", RaceText.Instr(C, cur.Instr)), () => { if (C.Check(Day.SetInstruction(_leg, (Instruction)(((int)cur.Instr + 1) % 3)))) R.Rebuild(); }, "btn-row");
            foreach (BenchRunnerView b in v.Bench)
            {
                int bi = b.BenchIndex;
                double d = _leg < b.DeltaSec.Length ? b.DeltaSec[_leg] : 0;
                bench.Button(b.Name + "  " + RaceText.Form(C, b.Form) + "  " + (d >= 0 ? "+" : "") + d.ToString("F0") + C.T("ui.sec"), () => { if (C.Check(Day.Swap(_leg, bi))) R.Rebuild(); }, "btn-row", v.SwapsLeft > 0);
            }
            p.Actions.Button(C.T("scr.raceday.reset"), () => { if (C.Check(Day.ResetSwaps())) R.Rebuild(); });
            p.Actions.Button(C.T("ui.omakase"), Omakase);
            p.Primary(C.T("scr.raceday.start"), StartRace);
        }

        private void StartRace()
        {
            if (C.Check(Day.Start())) R.Show(new LiveScreen { Day = Day });
        }

        public override void Omakase() { if (C.Check(Day.UseRecommended())) R.Rebuild(); }

        public override void AutoDefault() { StartRace(); }
    }

    /// <summary>SC-25 レース進行。区間ごとに進め、異変(ブレーキ / 失速)に檄を使える。『最後まで』は決定のまま走る。</summary>
    public sealed class LiveScreen : ScreenBase
    {
        public RaceDay Day;
        public override string Id { get { return "scr.live"; } }
        public override bool CanGoBack { get { return false; } }

        public override void Build(Page p)
        {
            RaceLiveView v = S.RaceLive(Day);
            p.NewRow(false);
            Region course = p.Region("course");
            course.Text(C.RaceName(v.Kind) + "  " + C.T("scr.live.progress", "done", v.Resolved.ToString(), "all", v.Sections.ToString()) + "  " + C.T("scr.raceday.left", "swap", "-", "geki", v.GekiLeft.ToString(), "rank", "-", "time", "-"), "bold");
            if (v.Next != null) course.Text(C.T("scr.live.next", "n", (v.Next.Section + 1).ToString(), "runner", v.Next.Name, "instr", RaceText.Instr(C, v.Next.Instr)));
            if (v.IncidentKind != null) course.Text(C.T("scr.live.incident." + v.IncidentKind, "pct", (v.IncidentLoss * 100).ToString("F1")), "bad");
            p.NewRow();
            Region st = p.Region("standings", title: C.T("scr.live.standings"));
            foreach (StandingLineView s in v.Standings)
            {
                if (s.SeparatorBefore) st.Text("…", "sub");
                st.Add(Ui.Line(s.IsOwn, (s.Rank.ToString(), 28), (C.School(s.NameKey), 0), (Fmt.Total(s.CumDs), 72), (Fmt.Diff(s.GapDs), 60)));
            }
            Region lineup = p.Region("lineup", scroll: true, width: 230, title: C.T("scr.live.highlights"));
            foreach (HighlightView h in v.Highlights.Skip(System.Math.Max(0, v.Highlights.Count - 6))) lineup.Text(RaceText.Highlight(C, h), h.IsOwn ? "bold" : "sub");
            if (!v.Finished)
            {
                p.Actions.Button(C.T("scr.live.geki", "n", v.GekiLeft.ToString()), () => { if (C.Check(Day.UseGeki())) R.Rebuild(); }, null, v.IncidentKind != null && v.GekiLeft > 0);
                p.Actions.Button(C.T("scr.live.to_end"), R.Continue);
                p.Primary(C.T("scr.live.next_section"), () => { Day.Advance(); R.Rebuild(); });
            }
            else p.Primary(C.T("scr.live.to_result"), R.Continue);
        }

        public override void AutoDefault()
        {
            RaceLiveView v = S.RaceLive(Day);
            if (!v.Finished && v.Resolved < 2) { Day.Advance(); R.Rebuild(); return; }      // 少しだけ区間を進めて画面を見せ、残りは決定のまま
            R.Continue();
        }
    }

    /// <summary>SC-26 大会の結果(総合 / 自校 / 見どころ・記録)。</summary>
    public sealed class ResultScreen : ScreenBase
    {
        public RaceOutcome Outcome;
        public RaceInfo? Own;
        private int _tab;
        public override string Id { get { return "scr.result"; } }
        public override bool CanGoBack { get { return false; } }

        public override void Build(Page p)
        {
            p.NewRow(false);
            Region tabs = p.Region("tabs", horizontal: true);
            if (Outcome == null) { tabs.Text(C.T("scr.result.none")); p.Primary(C.T("ui.continue"), R.Continue); return; }
            RaceResultView v = S.RaceResult(Outcome, Own);
            for (int i = 0; i < 3; i++) { int t = i; var b = tabs.Button(C.T("scr.result.tab" + i), () => { _tab = t; R.Rebuild(); }, "btn-small"); if (t == _tab) b.AddToClassList("selected"); }
            tabs.Text(C.RaceName(v.Kind) + "  " + (v.OwnEntered ? C.T("scr.result.own", "rank", v.OwnRank.ToString(), "expected", v.ExpectedRank.ToString(), "points", v.EvalPoints.ToString()) : C.T("scr.result.not_entered")), "bold");
            p.NewRow();
            Region table = p.Region("table", scroll: true);
            p.DefaultFocus = "tabs";
            if (_tab == 0)
                foreach (ResultRowView r in v.Overall) table.Add(Ui.Line(r.IsOwn, (r.Rank.ToString(), 28), (C.School(r.SchoolNameKey), 0), (Fmt.Total(r.TotalDs), 72), (r.Rank == 1 ? "" : Fmt.Diff(r.GapDs), 64), (r.Kuriage ? C.T("scr.result.kuriage") : "", 40)));
            else if (_tab == 1)
                foreach (OwnLegView l in v.OwnLegs)
                    table.Add(Ui.Line(true, ((l.Section + 1) + C.T("scr.result.ku"), 36), (l.FamilyName + " " + l.GivenName, 0), (Fmt.Time(l.Ds), 60), (C.T("scr.result.sec_rank", "n", l.SectionRank.ToString()), 64), (C.T("scr.result.cum_rank", "n", l.CumRank.ToString()), 64),
                        ((l.Prize ? C.T("scr.result.prize") : "") + (l.Brake ? C.T("scr.result.brake") : "") + (l.Record ? C.T("scr.result.record") : ""), 80)));
            else
            {
                foreach (HighlightView h in v.Highlights) table.Text(RaceText.Highlight(C, h), h.IsOwn ? "bold" : null);
                foreach (RecordUpdateView rec in v.Records) table.Text(C.T("scr.result.record_line", "section", rec.Section < 0 ? C.T("scr.result.team") : (rec.Section + 1).ToString(), "school", C.School(rec.SchoolNameKey), "time", Fmt.Time(rec.TimeDs)), "good");
                foreach (SectionPrizeView pr in v.Prizes) table.Text(C.T("scr.result.prize_line", "section", (pr.Section + 1).ToString(), "school", C.School(pr.SchoolNameKey), "time", Fmt.Time(pr.Ds)), pr.IsOwn ? "bold" : "sub");
            }
            p.Primary(C.T("ui.continue"), R.Continue);
        }
    }

    /// <summary>予選会の結果(SC-26 の予選会)。</summary>
    public sealed class QualifierResultScreen : ScreenBase
    {
        public override string Id { get { return "scr.result.qualifier"; } }
        public override bool CanGoBack { get { return false; } }

        public override void Build(Page p)
        {
            QualifierResultView v = S.QualifierResult();
            p.NewRow(false);
            Region head = p.Region("tabs");
            head.Text(v.OwnEntered ? C.T(v.Passed ? "scr.result.qualifier.passed" : "scr.result.qualifier.failed", "rank", v.OwnRank.ToString(), "margin", (v.MarginDs / 10.0).ToString("F0")) : C.T("scr.result.not_entered"), v.Passed ? "good bold" : "bad bold");
            p.NewRow();
            Region table = p.Region("table", scroll: true);
            foreach (ResultRowView r in v.Overall) table.Add(Ui.Line(r.IsOwn, (r.Rank.ToString(), 28), (C.School(r.SchoolNameKey), 0), (Fmt.Total(r.TotalDs), 72), (r.Rank <= v.PassCount ? C.T("scr.result.qualifier.pass") : "", 40)));
            Region own = p.Region("detail", scroll: true, width: 200);
            foreach (QualifierRunnerView q in v.Own) own.Text(q.Rank + ". " + q.FamilyName + " " + q.GivenName + "  " + Fmt.Time(q.Ds) + (q.Counted ? "" : " " + C.T("scr.result.qualifier.not_counted")), q.Counted ? null : "sub");
            p.Primary(C.T("ui.continue"), R.Continue);
        }
    }
}
