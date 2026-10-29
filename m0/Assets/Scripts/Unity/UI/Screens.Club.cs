using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Text;
using Ekiden.Domain.Views;

namespace Ekiden.Unity.UI
{
    /// <summary>SC-12 年度の結果と精算。強化は SC-32(運営)で。</summary>
    public sealed class YearSummaryScreen : ScreenBase
    {
        public override string Id { get { return "scr.year.summary"; } }
        public override bool CanGoBack { get { return false; } }

        public override void Build(Page p)
        {
            YearSummaryView v = S.YearSummary();
            p.NewRow();
            Region results = p.Region("results", title: C.T("scr.year.summary.results", "year", v.Year.ToString()));
            foreach (YearRaceView r in v.Races.Where(x => x != null))
                results.Text(C.RaceName(r.Kind) + "  " + (r.Place >= 99 ? C.T("scr.year.summary.no_race") : C.T("scr.year.summary.place", "n", r.Place.ToString(), "points", r.EvalPoints.ToString())) + (r.Won ? " " + C.T("scr.year.summary.won") : ""), r.Won ? "good" : null);
            if (v.TripleCrown) results.Text(C.T("scr.year.summary.triple", "bonus", v.TripleCrownBonus.ToString()), "good bold");
            results.Text(C.T("scr.year.summary.points", "points", v.EvalPoints.ToString(), "fame", v.FamePoints.ToString("F1"), "before", v.PrestigeBefore.ToString("F1"), "after", v.PrestigeAfter.ToString("F1")));
            results.Text(C.T(v.TargetMet ? "scr.year.summary.target_met" : "scr.year.summary.target_missed", "rank", v.ExpectedTargetRank.ToString(), "bonus", Fmt.Money(v.TargetBonusFunds)), v.TargetMet ? "good" : "sub");
            if (v.RankUp) results.Text(C.T("scr.year.summary.rank_up", "before", v.CoachRankBefore.ToString(), "after", v.CoachRankAfter.ToString(), "cap", v.FacilityCapAfter.ToString()), "good");
            foreach (TicketMilestoneView t in v.TicketsEarned.Concat(v.TicketsAtYearEnd)) results.Text(C.T("scr.year.summary.ticket", "name", C.T(t.NameKey, "years", t.Years.ToString())), "good");
            Region funds = p.Region("funds", width: 230, title: C.T("scr.year.summary.funds"));
            YearFinanceView f = v.Finance;
            if (f != null)
            {
                funds.Text(C.T("scr.year.summary.income", "v", Fmt.Money((long)f.Income)));
                funds.Text(C.T("scr.year.summary.expense", "v", Fmt.Money((long)f.Expense)));
                funds.Text(C.T("scr.year.summary.funds_line", "before", Fmt.Money(f.FundsBefore), "after", Fmt.Money(f.FundsNow)), "bold");
            }
            p.Actions.Button(C.T("scr.year.summary.ops"), () => R.Show(new OpsScreen(), true));
            p.Primary(C.T("ui.continue"), R.Continue);
        }
    }

    /// <summary>SC-13 卒業・進路 → SC-14 入学者発表。</summary>
    public sealed class GraduationScreen : ScreenBase
    {
        public override string Id { get { return "scr.year.grad"; } }
        public override bool CanGoBack { get { return false; } }

        public override void Build(Page p)
        {
            GraduationView v = S.Graduation();
            p.DefaultFocus = "list";
            p.NewRow();
            Region list = p.Region("list", scroll: true, title: C.T("scr.year.grad.list", "n", v.Graduates.Count.ToString()));
            foreach (GraduateRowView g in v.Graduates)
                list.Add(Ui.Line(g.HallOfFame, ((g.HallOfFame ? "★ " : "") + g.Name, 0), (g.PrefName + (g.CountryName.Length > 0 ? g.CountryName : ""), 70), ("P" + g.Power.ToString("F1") + "(" + g.Rank + ")", 70),
                    (C.T("career." + g.Career.ToString().ToLowerInvariant() + ".name"), 64), (C.T("scr.year.grad.wins", "n", g.SectionWins.ToString()), 56)));
            if (v.Quit.Count > 0)
            {
                Region quit = p.Region("detail", width: 160, title: C.T("scr.year.grad.quit"));
                foreach (RunnerRowView q in v.Quit) quit.Text(UiContext.Name(q), "sub");
            }
            p.Primary(C.T("ui.next"), () => R.Show(new EnrollmentScreen()));
        }

        public override void AutoDefault() { R.Show(new EnrollmentScreen()); }
    }

    public sealed class EnrollmentScreen : ScreenBase
    {
        public override string Id { get { return "scr.enroll"; } }
        public override bool CanGoBack { get { return false; } }

        public override void Build(Page p)
        {
            EnrollmentView v = S.Enrollment();
            p.DefaultFocus = "list";
            p.NewRow();
            Region list = p.Region("list", scroll: true, title: C.T("scr.enroll.list", "n", (v.Targeted.Count + v.Natural.Count).ToString()));
            foreach (EnrolleeRowView e in v.Targeted.Concat(v.Natural))
            {
                int id = e.Row.Id;
                list.Button(new string('★', e.Stars) + "  " + UiContext.Name(e.Row) + "  " + e.Row.PrefName + "  P" + e.Row.P.ToString("F1") + (e.Targeted ? "  " + C.T("scr.enroll.targeted") : ""), () => R.Show(new RunnerScreen { RunnerId = id }, true), "btn-row");
            }
            Region missed = p.Region("detail", scroll: true, width: 200, title: C.T("scr.enroll.missed"));
            foreach (MissedCandidateView m in v.Missed.Take(8)) missed.Text(m.Candidate.FamilyName + " " + m.Candidate.GivenName + "  " + C.T(m.ReasonKey), "sub");
            p.Primary(C.T("ui.continue"), R.Continue);
        }
    }

    /// <summary>SC-32 運営(施設・スタッフ。新設・追加の欄 ExtraRows を含む。ランク 7 未満は『ランク 7 から』)。</summary>
    public sealed class OpsScreen : ScreenBase
    {
        public override string Id { get { return "scr.ops"; } }

        public override void Build(Page p)
        {
            OpsView v = S.Ops();
            p.DefaultFocus = "list";
            p.NewRow();
            Region list = p.Region("list", scroll: true, title: C.T("scr.ops.head", "funds", Fmt.Money(v.Funds), "rank", v.CoachRank.ToString(), "cap", v.Cap.ToString()));
            foreach (FacilityRowView row in v.Rows) AddRow(list, row, v);
            list.Text(C.T("scr.ops.extra", "rank", v.NewMinRank.ToString()), "region-title");
            foreach (FacilityRowView row in v.ExtraRows) AddRow(list, row, v);
            Region detail = p.Region("detail", width: 210, title: C.T("scr.ops.balance"));
            detail.Text(C.T("scr.year.summary.income", "v", Fmt.Money((long)v.Income)));
            detail.Text(C.T("scr.year.summary.expense", "v", Fmt.Money((long)v.Expense)));
            detail.Text(C.T("scr.ops.balance_line", "v", Fmt.Money((long)v.Balance)), v.Balance >= 0 ? "good" : "bad");
            detail.Text(C.T("scr.ops.reserve", "v", Fmt.Money(v.Reserve)), "sub");
            detail.Text(C.T("scr.ops.guidance", "n", v.GuidancePoints.ToString()), "sub");
            p.Actions.Button(C.T("ui.back"), R.Back);
            p.Primary(C.T("scr.ops.recommended", "n", v.RecommendedCount.ToString()), () => { S.UpgradeRecommended(); R.Rebuild(); }, v.RecommendedCount > 0);
        }

        private void AddRow(Region list, FacilityRowView row, OpsView v)
        {
            string key = row.Key;
            string fx = string.Join(" / ", row.Effects.Select(e => C.T("fx." + e.Code, "value", e.Value.ToString("0.##"))));
            string text = C.T(row.NameKey) + "  Lv" + row.Level + "/" + row.Cap + (row.NextCost.HasValue ? "  " + C.T("scr.ops.cost", "v", Fmt.Money((long)row.NextCost.Value)) : "") + "\n" + fx;
            if (row.CanUpgrade) list.Button(text, () => { if (C.Check(S.UpgradeFacility(key))) R.Rebuild(); }, "btn-row");
            else
            {
                string why = row.BlockedBy == "rank" ? C.T("scr.ops.blocked.rank", "rank", v.NewMinRank.ToString()) : C.T("scr.ops.blocked." + (row.BlockedBy ?? "cap"));
                list.Blocked(text + "  (" + why + ")", why, R.Toast, "btn-row");
            }
        }
    }

    /// <summary>SC-18 部員一覧。行を選ぶと選手詳細(SC-19)。</summary>
    public sealed class RosterScreen : ScreenBase
    {
        public override string Id { get { return "scr.roster"; } }

        public override void Build(Page p)
        {
            var rows = S.RosterRows().OrderBy(r => r.PRank).ToList();
            p.DefaultFocus = "list";
            p.NewRow();
            Region list = p.Region("list", scroll: true, title: C.T("scr.roster.head", "n", rows.Count.ToString()));
            foreach (RunnerRowView r in rows)
            {
                int id = r.Id;
                string inj = r.Injury == InjurySeverity.None ? "" : C.T("injury." + r.Injury.ToString().ToLowerInvariant() + ".name") + r.InjuryWeeksLeft + C.T("ui.weeks");
                list.Button(r.PRank + ". " + UiContext.Name(r) + (r.Captain ? " " + C.T("scr.roster.captain") : "") + "  " + Fmt.Grade(r.Grade) + "  " + "ABC"[r.Group] + "  P" + r.P.ToString("F1") + "(" + r.Rank + ")  "
                    + C.T("scr.roster.fatigue", "n", r.Fatigue.ToString()) + "  " + RaceText.Form(C, r.Form) + "  " + C.T("scr.roster.mot", "n", r.Motivation.ToString()) + "  " + inj + "  " + (r.Overseas ? r.CountryName : r.PrefName),
                    () => R.Show(new RunnerScreen { RunnerId = id }, true), "btn-row");
            }
            Region side = p.Region("side", width: 150, title: C.T("scr.roster.summary"));
            side.Text(C.T("scr.roster.regulars", "n", rows.Count(r => r.Regular).ToString()));
            side.Text(C.T("scr.roster.injured", "n", rows.Count(r => r.Injury != InjurySeverity.None).ToString()));
            for (int g = 1; g <= 4; g++) side.Text(C.T("scr.roster.grade", "g", g.ToString(), "n", rows.Count(r => r.Grade == g).ToString(), "p", rows.Where(r => r.Grade == g).Select(r => r.P).DefaultIfEmpty(0).Average().ToString("F1")), "sub");
            p.Actions.Button(C.T("ui.back"), R.Back);
        }
    }

    /// <summary>SC-19 選手詳細(隠す情報 = 素質・体質・成長タイプ・未判明の素養は View に入っていない)。</summary>
    public sealed class RunnerScreen : ScreenBase
    {
        public int RunnerId;
        public override string Id { get { return "scr.runner"; } }

        public override void Build(Page p)
        {
            RunnerDetailView v = S.RunnerDetail(RunnerId);
            p.DefaultFocus = "actions";
            p.NewRow(false);
            Region header = p.Region("header");
            if (v == null || v.Row == null) { header.Text(C.T("scr.runner.gone")); p.Actions.Button(C.T("ui.back"), R.Back); return; }
            RunnerRowView r = v.Row;
            header.Text(UiContext.Name(r) + "(" + r.FamilyKana + " " + r.GivenKana + ")  " + Fmt.Grade(r.Grade) + "  P" + r.P.ToString("F1") + "(" + r.Rank + ")  " + (r.Overseas ? r.CountryName : r.PrefName), "bold");
            p.NewRow();
            Region stats = p.Region("stats", scroll: true);
            foreach (StatView s in v.Stats)
                stats.Add(Ui.Line(false, (C.T("stat." + s.Stat + ".name"), 90), (s.Value.ToString(), 32), (s.Rank.ToString(), 20), (C.T("scr.runner.hint." + s.Hint.ToString().ToLowerInvariant()), 70), ((s.DeltaThisMonth >= 0 ? "+" : "") + s.DeltaThisMonth, 36)));
            Region side = p.Region("side", width: 220, scroll: true);
            side.Text(string.Join(" ", v.Fits.Select(f => RaceText.Type(C, f.Type) + f.Mark)));
            side.Text(C.T("scr.runner.pb", "t5", Fmt.Time(v.Pb5000), "t10", Fmt.Time(v.Pb10000), "half", Fmt.Time(v.PbHalf)), "sub");
            side.Text(C.T("scr.runner.career", "races", v.Races.ToString(), "wins", v.SectionWins.ToString(), "brakes", v.Brakes.ToString()), "sub");
            foreach (SkillView sk in v.Skills) side.Text(C.T(sk.Id + ".name") + "(" + C.T("scr.runner.skill." + sk.Rank.ToString().ToLowerInvariant()) + ")");
            if (v.GiftRevealed) foreach (var g in v.Gifts) side.Text(C.T("gift." + g.ToString().ToLowerInvariant() + ".name"), "good");
            foreach (BondView b in v.Bonds.Take(4)) side.Text(C.T("scr.runner.bond", "name", b.Name, "v", b.Value.ToString()), "sub");
            p.Actions.Button(C.T("ui.back"), R.Back);
            p.Actions.Button(C.T("scr.runner.talk"), () => { if (C.Check(S.Talk(RunnerId))) { R.Toast(C.T("scr.runner.talked")); R.Rebuild(); } }, null, !v.Talked);
        }
    }

    /// <summary>SC-27 スカウト(地図)。地方 → 県の 2 段(08 §4.3)。M1 は一覧で、視察・おまかせができる。</summary>
    public sealed class ScoutMapScreen : ScreenBase
    {
        private int _region = -1;
        public override string Id { get { return "scr.scout.map"; } }

        public override void Build(Page p)
        {
            ScoutMapView v = S.ScoutMap();
            p.DefaultFocus = "map";
            p.NewRow(false);
            Region points = p.Region("points");
            points.Text(C.T("scr.scout.points", "phase", C.T("scr.scout.phase." + v.Phase.ToString().ToLowerInvariant()), "points", v.PointsAvailable.ToString(), "total", v.PointsYearTotal.ToString(),
                "obs", v.Observed.ToString(), "talk", v.Talked.ToString(), "inv", v.Invited.ToString()), "bold");
            p.NewRow();
            Region map = p.Region("map", horizontal: true, title: C.T("scr.scout.regions"));
            foreach (ScoutRegionView r in v.Regions)
            {
                int ri = (int)r.Region;
                var b = map.Button(C.T("region." + r.Region.ToString().ToLowerInvariant() + ".name") + "\n" + C.T("scr.scout.region_line", "n", r.Candidates.ToString(), "s5", r.Stars5.ToString()), () => { _region = ri; R.Rebuild(); }, "btn-tile");
                if (ri == _region) b.AddToClassList("selected");
            }
            Region side = p.Region("side", scroll: true, width: 230, title: C.T("scr.scout.prefs"));
            foreach (ScoutPrefView pr in v.Prefs.Where(x => _region < 0 || (int)x.Region == _region))
                side.Add(Ui.Line(false, (pr.PrefName, 0), (C.T("scr.scout.pref_line", "n", pr.Candidates.ToString(), "obs", pr.Observed.ToString(), "s5", pr.Stars5.ToString(), "ob", pr.ObLevel.ToString()), 150)));
            p.Actions.Button(C.T("ui.back"), R.Back);
            p.Actions.Button(C.T("scr.scout.list"), () => R.Show(new ScoutListScreen(), true));
            p.Primary(C.T("scr.scout.auto"), () => { int n = S.AutoScoutObserve() + S.AutoScoutRecruit(); R.Toast(C.T("scr.scout.auto_done", "n", n.ToString())); R.Rebuild(); });
        }
    }

    /// <summary>SC-28 候補リスト(視察・声かけ・練習会)。</summary>
    public sealed class ScoutListScreen : ScreenBase
    {
        public override string Id { get { return "scr.scout.list"; } }

        public override void Build(Page p)
        {
            p.DefaultFocus = "list";
            p.NewRow();
            Region list = p.Region("list", scroll: true);
            foreach (ScoutCandidateView c in S.ScoutCandidates().Take(60))
            {
                int i = c.Index;
                string text = new string('★', c.Stars) + " " + c.FamilyName + " " + c.GivenName + "  " + c.PrefName + "  V" + c.V.ToString("F1") + "  " + C.T("scr.scout.stage." + c.Stage.ToString().ToLowerInvariant())
                    + (c.QObs.HasValue ? "  " + C.T("scr.scout.qobs", "q", c.QObs.Value.ToString("F1"), "sd", c.Sigma.ToString("F1")) : "");
                if (c.CanObserve) list.Button(text + "  → " + C.T("scr.scout.observe"), () => { if (C.Check(S.ScoutObserve(i))) R.Rebuild(); }, "btn-row");
                else if (c.CanTalk) list.Button(text + "  → " + C.T("scr.scout.talk"), () => { if (C.Check(S.ScoutTalk(i))) R.Rebuild(); }, "btn-row");
                else if (c.CanInvite) list.Button(text + "  → " + C.T("scr.scout.invite"), () => { if (C.Check(S.ScoutInvite(i))) R.Rebuild(); }, "btn-row");
                else list.Blocked(text, C.T("scr.scout.cannot"), R.Toast, "btn-row");
            }
            p.Actions.Button(C.T("ui.back"), R.Back);
        }
    }

    /// <summary>SC-34 歴史・殿堂。</summary>
    public sealed class HistoryScreen : ScreenBase
    {
        private int _tab;
        public override string Id { get { return "scr.history"; } }

        public override void Build(Page p)
        {
            HistoryView v = S.History();
            p.DefaultFocus = "body";
            p.NewRow(false);
            Region tabs = p.Region("tabs", horizontal: true);
            for (int i = 0; i < 3; i++) { int t = i; var b = tabs.Button(C.T("scr.history.tab" + i), () => { _tab = t; R.Rebuild(); }, "btn-small"); if (t == _tab) b.AddToClassList("selected"); }
            p.NewRow();
            Region body = p.Region("body", scroll: true);
            if (_tab == 0)
                foreach (YearRecordView y in v.Years)
                    body.Add(Ui.Line(false, (C.T("scr.history.year", "n", y.Year.ToString()), 64), ("P" + y.P.ToString("F1"), 56), (string.Join(" / ", y.Place.Select(x => x >= 99 ? "—" : x.ToString())), 0), (y.TripleCrown ? C.T("scr.history.triple") : "", 50)));
            else if (_tab == 1)
                foreach (ChampionRowView c in v.Champions.Skip(System.Math.Max(0, v.Champions.Count - 40)))
                    body.Add(Ui.Line(c.IsOwn.Any(x => x), (C.T("scr.history.year", "n", c.Year.ToString()), 64), (C.School(c.WinnerNameKey[0]), 0), (C.School(c.WinnerNameKey[1]), 0), (C.School(c.WinnerNameKey[2]), 0)));
            else
                foreach (CourseRecordView r in v.CourseRecords)
                    body.Add(Ui.Line(r.IsOwn, (C.RaceName(r.Kind), 100), ((r.Section + 1) + C.T("scr.result.ku"), 36), (r.IsLegacy ? C.T("record.legacy_holder") : C.School(r.SchoolNameKey), 0), (Fmt.Time(r.TimeDs), 60)));
            Region side = p.Region("side", width: 180);
            side.Text(C.T("scr.history.titles", "n", v.Titles.ToString(), "tc", v.TripleCrowns.ToString()));
            for (int i = 0; i < 3; i++) side.Text(C.RaceName((Domain.Race.RaceKind)i) + " " + (v.FirstWinYear[i] > 0 ? C.T("scr.history.first", "n", v.FirstWinYear[i].ToString()) : C.T("scr.history.never")), "sub");
            side.Text(C.T("scr.history.prefs", "n", v.PrefecturesCovered.ToString()), "sub");
            side.Text(C.T("scr.history.hof", "n", v.HallOfFame.Count.ToString()), "sub");
            p.Actions.Button(C.T("ui.back"), R.Back);
        }
    }
}
