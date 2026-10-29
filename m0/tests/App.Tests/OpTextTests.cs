using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ekiden.App;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;
using Ekiden.Domain.World;
using Ekiden.Persistence.Strings;
using Ekiden.Platform;
using Ekiden.Verification;
using NUnit.Framework;

namespace Ekiden.App.Tests
{
    /// <summary>操作の理由・警告(L-63 ③): Domain はキーと値だけを持ち、App が表から日本語の文にする。</summary>
    public class OpTextTests
    {
        private static string Find(string relative)
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                string p = Path.Combine(dir, relative);
                if (File.Exists(p)) return p;
                dir = Path.GetDirectoryName(dir);
            }
            throw new FileNotFoundException(relative);
        }

        private static Dictionary<string, string> Table() { return StringTable.Parse(File.ReadAllText(Find("Assets/Strings/ja.json"), new UTF8Encoding(false))); }

        private static ILocalization Loc() { return new TableLocalization(Table(), true, null); }

        [Test]
        public void EveryDefinition_HasATextWhosePlaceholdersAreItsArguments()
        {
            Dictionary<string, string> table = Table();
            IReadOnlyList<ErrDef> all = OpErr.All;
            Assert.GreaterOrEqual(all.Count, 80);
            Assert.AreEqual(all.Count, all.Select(d => d.Code).Distinct().Count(), "キーの重複なし");
            var calls = all.Where(d => !d.Internal).Select(d => new CallSite { Key = d.Code, ArgNames = d.ArgNames, Where = "OpErr" }).ToList();
            List<LintIssue> issues = StringsLint.CheckCalls(table, calls);
            Assert.IsEmpty(issues, string.Join("\n", issues.Take(10)));
            foreach (ErrDef d in all) StringAssert.StartsWith(d.Code.Split('.')[0] == "hint" ? "hint." : (d.Code.StartsWith("warn.") ? "warn." : "err."), d.Code);
        }

        [Test]
        public void TheTable_HasNoOrphanErrWarnHintKeys()
        {
            var defined = new HashSet<string>(OpErr.All.Where(d => !d.Internal).Select(d => d.Code));
            string[] orphans = Table().Keys.Where(k => (k.StartsWith("err.") || k.StartsWith("warn.") || k.StartsWith("hint.")) && !defined.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            Assert.IsEmpty(orphans, "OpErr にない: " + string.Join(", ", orphans));
        }

        [Test]
        public void Make_RejectsAWrongNumberOfValues_AndKeepsTheNestedReason()
        {
            Assert.Throws<ArgumentException>(() => OpErr.EntryMax.Make());
            Assert.Throws<ArgumentException>(() => OpErr.NotifyNone.Make(1));
            Msg m = OpErr.CustomWeek.Make(2, OpErr.MenuMileage.Make());
            Assert.AreEqual("err.plan.custom_week", m.Code);
            CollectionAssert.AreEqual(new[] { "n", "2" }, m.Args);
            Assert.AreEqual("err.menu.mileage", m.Why.Code);
            Assert.AreEqual("2 週目: 走行量は必須", OpText.Of(m, Loc()));
            Assert.AreEqual("err.entry.max(n=10)", OpErr.EntryMax.Make(10).ToString(), "診断用の形");
        }

        [Test]
        public void TheTextsAreTheOnesTheDomainUsedToWrite()
        {
            ILocalization loc = Loc();
            GameSession gs = GameSession.New(20260601UL, Difficulty.Mid);
            for (int i = 0; i < 4; i++) gs.StepWeek();
            OpResult r = gs.ApplyRecommendedGroups();
            Assert.AreEqual("推奨の編成を適用できるのは 4 月(年度初め)だけ", OpText.Reason(r, loc));

            r = gs.SetEntries(RaceKind.Fall, gs.State.Runners.Take(10).Select(x => x.Id).ToList());
            Assert.AreEqual("登録できるのは大会の 4 週前から大会の週まで", OpText.Reason(r, loc));

            r = gs.SetQualifierEntries(gs.State.Runners.Take(12).Select(x => x.Id).ToList());
            Assert.AreEqual("予選会の登録は、週 " + GameSession.QualifierDeadlineWeek + " から予選会の週の初めまで", OpText.Reason(r, loc));

            r = gs.SetCustomWeeks(1, new[] { new Ekiden.Domain.Team.MenuSpec("ks.hill", "ks.hill", "mil.low"), new Ekiden.Domain.Team.MenuSpec("ks.hill", "ks.core", "mil.low"), new Ekiden.Domain.Team.MenuSpec("ks.hill", "ks.core", "mil.low"), new Ekiden.Domain.Team.MenuSpec("ks.hill", "ks.core", "mil.low") });
            Assert.AreEqual("1 週目: ポイント練習の2枠は異なるIDにする", OpText.Reason(r, loc));

            gs.State.Funds = 100;
            r = gs.UpgradeFacility("scout");
            Assert.AreEqual("資金が足りない(" + (300 + 250 * gs.State.Fac.Scout).ToString() + " 万円)", OpText.Reason(r, loc));

            r = gs.Talk(999);
            Assert.AreEqual("その選手はいない", OpText.Reason(r, loc));
            Assert.AreEqual("", OpText.Reason(OpResult.Success, loc));
        }

        [Test]
        public void Warnings_AreTexts_AndAConfirmCarriesTheFirstAsItsReason()
        {
            ILocalization loc = Loc();
            GameSession gs = GameSession.New(20260601UL, Difficulty.Mid);
            Ekiden.Domain.Entities.Runner r = gs.State.Runners[0];
            r.Fatigue = 90;
            OpResult o = gs.GuideMenu(r.Id, new Ekiden.Domain.Team.MenuSpec("ks.core", "ks.rest", "mil.low"));
            Assert.IsTrue(o.NeedsConfirm);
            CollectionAssert.AreEqual(new[] { "安全網が働きません" }, OpText.Warnings(o, loc).ToArray());
            Assert.AreEqual("安全網が働きません", OpText.Reason(o, loc));
            Assert.AreEqual("warn.guide.no_safety", o.Code);
        }
    }
}
