using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Text;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>年間方針(SC-10)と他校情報・予報(SC-23)の読み取り API(08b)。</summary>
    public class PlanOverviewViewTests
    {
        private static GameSession Game(ulong seed = 20260403UL, Difficulty d = Difficulty.Mid) { return GameSession.New(seed, d); }

        [Test]
        public void FocusTable_MatchesTheSpecTableOfR04()
        {
            string text = File.ReadAllText(DocPaths.Find("08_screen_spec.md"), Encoding.UTF8);
            int start = text.IndexOf("**重点ごとの違い**", StringComparison.Ordinal);
            string section = text.Substring(start, text.IndexOf("- **ロング寄り", start, StringComparison.Ordinal) - start);
            string[] names = { "バランス(基準)", "スピード寄り", "ロング寄り" };
            for (int f = 0; f < 3; f++)
            {
                Match m = Regex.Match(section, @"^\|\s*" + Regex.Escape(names[f]) + @"\s*\|(.+)\|\s*$", RegexOptions.Multiline);
                Assert.IsTrue(m.Success, names[f]);
                string[] cells = m.Groups[1].Value.Split('|').Select(c => c.Trim().Replace('−', '-').Replace('+', ' ').Trim()).ToArray();
                Assert.AreEqual(6, cells.Length);
                for (int k = 0; k < 5; k++)
                {
                    double expected = cells[k] == "—" ? 0.0 : double.Parse(cells[k], CultureInfo.InvariantCulture);
                    Assert.AreEqual(expected, PlanRules.FocusDeltaP[f][k], 1e-12, names[f] + " " + (SectionType)k);
                }
                Assert.AreEqual(double.Parse(cells[5], CultureInfo.InvariantCulture), PlanRules.FocusInjuryPerYear[f], 1e-12);
            }
        }

        [Test]
        public void AnnualPlan_ShowsTheStateAndEachFocusTemplate()
        {
            GameSession gs = Game();
            AnnualPlanView v = gs.AnnualPlanView();
            Assert.AreEqual(PlanFocus.Balanced, v.Focus);
            Assert.AreEqual(2, v.TargetRace);
            Assert.AreEqual(3, v.Options.Count);
            Assert.AreEqual(1, v.Options.Count(o => o.Selected));
            foreach (FocusOptionView o in v.Options)
            {
                for (int m = 0; m < 12; m++) Assert.AreEqual(PlanRules.AutoPlan(o.Focus, m), o.Months[m]);
                CollectionAssert.AreEqual(PlanRules.FocusDeltaP[(int)o.Focus], o.DeltaP);
                Assert.AreEqual(PlanRules.FocusInjuryPerYear[(int)o.Focus], o.InjuryPerYear);
            }
            Assert.AreEqual(Math.Max(1, gs.State.LastRank[2] - 1), v.ExpectedTargetRank);
            Assert.AreEqual(gs.Home().ExpectedBonusFunds, v.ExpectedBonusFunds);

            var peaking = new[] { PeakingMode.Short, PeakingMode.None, PeakingMode.Standard, PeakingMode.Standard, PeakingMode.None };
            Assert.IsTrue(gs.SetAnnualPlan(PlanFocus.Long, 0, peaking).Ok);
            gs.State.Settings.PlanAuto = false;
            AnnualPlanView w = gs.AnnualPlanView();
            Assert.AreEqual(PlanFocus.Long, w.Focus);
            Assert.AreEqual(0, w.TargetRace);
            CollectionAssert.AreEqual(peaking, w.Peaking);
            Assert.IsFalse(w.PlanAuto);
            Assert.AreEqual(PlanFocus.Long, w.Options.Single(o => o.Selected).Focus);
            Assert.AreEqual("th.base_build", w.Options[2].Months[3], "ロング寄りは 7 月も基礎");
            Assert.AreEqual("th.mountain", w.Options[0].Months[3], "標準は 7 月に山");
        }

        private static void ToDeadline(GameSession gs, RaceKind kind)
        {
            while (gs.YearWeek != TeamWeek.EntryDeadlineWeeks[(int)kind] + 1) gs.StepWeek();
        }

        [Test]
        public void Preview_ListsTheEntrantsWithTheirTopThree_AndTheForecast()
        {
            GameSession gs = Game(41UL, Difficulty.Elite);
            foreach (RaceKind kind in new[] { RaceKind.Fall, RaceKind.National, RaceKind.NewYear })
            {
                ToDeadline(gs, kind);
                YearState ys = gs.PrepareYear();
                PreviewView v = gs.Preview(kind);
                Assert.AreEqual(kind, v.Kind);
                CollectionAssert.AreEquivalent(ys.Entrants(kind), v.Schools.Select(s => s.SchoolId).ToList(), "出場校");
                for (int i = 0; i < v.Schools.Count; i++)
                {
                    PreviewSchoolView s = v.Schools[i];
                    Assert.AreEqual(i + 1, s.Rank);
                    Assert.AreEqual(DisplayRules.PreviewMark(i + 1), s.Mark);
                    if (i > 0) Assert.GreaterOrEqual(v.Schools[i - 1].Value, s.Value);
                    Assert.AreEqual("school." + s.SchoolId + ".name", s.NameKey);
                    Assert.AreEqual(s.SchoolId == gs.School.Id, s.IsOwn);
                    Assert.LessOrEqual(s.Top.Count, 3);
                    for (int k = 1; k < s.Top.Count; k++) Assert.GreaterOrEqual(s.Top[k - 1].FitLong, s.Top[k].FitLong, "区間適性 Long の高い順");
                    if (s.IsOwn) continue;
                    // 他校の上位 3 人を、独立に(名簿の全員を復元して)求める
                    School sc = gs.World.Schools.Single(x => x.Id == s.SchoolId);
                    double lv = sc.Lv();
                    var all = sc.Roster.Select(w => new { w, rr = AiRacing.ToRaceRunner(w, lv, gs.Rngs) }).Select(x => new { x.w, fit = RaceTables.Rating(x.rr.Stats, SectionType.Long), x.rr }).ToList();
                    var top = all.Select((x, idx) => new { x, idx }).OrderByDescending(x => x.x.fit).ThenBy(x => x.idx).Take(3).Select(x => x.x).ToList();
                    Assert.AreEqual(top.Count, s.Top.Count);
                    for (int k = 0; k < top.Count; k++)
                    {
                        PersonName n = NameGenerator.Generate(gs.Rngs, top[k].w.LookSeed, top[k].w.Overseas);
                        Assert.AreEqual(n.Family + n.Given, s.Top[k].FamilyName + s.Top[k].GivenName);
                        Assert.AreEqual(top[k].w.Grade, s.Top[k].Grade);
                        Assert.AreEqual(top[k].fit, s.Top[k].FitLong, 1e-12);
                        Assert.AreEqual(DisplayRules.DerivePbFromP(DisplayRules.PbEvent.M5000, RaceTables.Rating(top[k].rr.Stats, SectionType.Fast)), s.Top[k].Pb5000);
                        Assert.AreEqual(DisplayRules.DerivePbFromP(DisplayRules.PbEvent.M10000, RaceTables.Rating(top[k].rr.Stats, SectionType.Mid)), s.Top[k].Pb10000);
                        Assert.Greater(s.Top[k].Pb10000, s.Top[k].Pb5000);
                    }
                }
                PreviewSchoolView mine = v.Schools.SingleOrDefault(s => s.IsOwn);
                if (mine != null)
                {
                    var entered = gs.State.Entries[(int)kind].Select(gs.State.FindRunner).ToList();
                    Assert.AreEqual(Math.Min(3, entered.Count), mine.Top.Count);
                    foreach (RivalRunnerView r in mine.Top)
                    {
                        Runner e = entered.Single(x => x.FamilyName + x.GivenName == r.FamilyName + r.GivenName && x.Grade == r.Grade);          // 自校は登録メンバーから
                        var dv = e.Stats.Select(x => (double)x).ToArray();
                        Assert.AreEqual(RaceTables.Rating(dv, SectionType.Long), r.FitLong, 1e-12);
                        Assert.AreEqual(DisplayRules.DerivePb(e.Stats, DisplayRules.PbEvent.M5000), r.Pb5000);
                        Assert.AreEqual(DisplayRules.DerivePb(e.Stats, DisplayRules.PbEvent.M10000), r.Pb10000);
                    }
                    var best = entered.Where(x => TeamRacing.CanRun(gs.State, x)).Select(x => RaceTables.Rating(x.Stats.Select(y => (double)y).ToArray(), SectionType.Long)).OrderByDescending(x => x).Take(3).ToList();
                    CollectionAssert.AreEqual(best, mine.Top.Select(r => r.FitLong).ToList(), "登録メンバーの区間適性 Long の上位 3 人");
                }
                Env[] env = gs.Forecast(kind);
                Assert.AreEqual(env.Length, v.Forecast.Count);
                for (int i = 0; i < env.Length; i++)
                {
                    Assert.AreEqual(env[i].Temp, v.Forecast[i].Temp);
                    Assert.AreEqual(env[i].Wind, v.Forecast[i].Wind);
                    Assert.AreEqual(env[i].Rain, v.Forecast[i].Rain);
                }
                Assert.AreEqual(kind == RaceKind.NewYear ? 2 : 1, v.Forecast.Count, "第三戦は往路と復路");
                Assert.AreEqual(0.55, v.TopMarkWinRate);
                Assert.IsTrue(v.Schools.Any(s => s.IsOwn) || !ys.Entrants(kind).Contains(gs.School.Id));
            }
        }

        [Test]
        public void Preview_PowerRankMatchesTheWorldView()
        {
            GameSession gs = Game(43UL, Difficulty.Mid);
            ToDeadline(gs, RaceKind.Fall);
            PreviewView v = gs.Preview(RaceKind.Fall);
            WorldView w = gs.WorldOverview();
            foreach (PreviewSchoolView s in v.Schools) Assert.AreEqual(w.Schools.Single(x => x.SchoolId == s.SchoolId).PowerRank, s.PowerRank);
        }
    }
}
