using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Game;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Text;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>選手の名前(02 §4.1・09 §8.2 label 8)。表(Assets/Master/names_ja.json)から生成した C# と一致し、look_seed から決まり、重複を避け、すべての生成箇所で付く。</summary>
    public class NameTests
    {
        private static readonly IRngFactory Rngs = new KeyedRngFactory(20261030UL);

        private static JsonElement Master()
        {
            return JsonDocument.Parse(File.ReadAllText(DocPaths.Find("Assets/Master/names_ja.json"), Encoding.UTF8)).RootElement;
        }

        [Test]
        public void GeneratedTable_MatchesTheJsonMaster()
        {
            JsonElement m = Master();
            string[] ft = m.GetProperty("family").EnumerateArray().Select(e => e.GetProperty("text").GetString()).ToArray();
            string[] fk = m.GetProperty("family").EnumerateArray().Select(e => e.GetProperty("kana").GetString()).ToArray();
            string[] gt = m.GetProperty("given").EnumerateArray().Select(e => e.GetProperty("text").GetString()).ToArray();
            string[] gk = m.GetProperty("given").EnumerateArray().Select(e => e.GetProperty("kana").GetString()).ToArray();
            CollectionAssert.AreEqual(ft, NameTableData.FamilyText, "姓。verify/gen_names_cs.py で作り直す");
            CollectionAssert.AreEqual(fk, NameTableData.FamilyKana);
            CollectionAssert.AreEqual(gt, NameTableData.GivenText, "名");
            CollectionAssert.AreEqual(gk, NameTableData.GivenKana);
            JsonElement intl = m.GetProperty("intl");
            string[][] fp = intl.GetProperty("family_parts").EnumerateArray().Select(c => c.EnumerateArray().Select(x => x.GetString()).ToArray()).ToArray();
            string[][] gp = intl.GetProperty("given_parts").EnumerateArray().Select(c => c.EnumerateArray().Select(x => x.GetString()).ToArray()).ToArray();
            Assert.AreEqual(fp.Length, NameTableData.IntlFamilyParts.Length);
            for (int i = 0; i < fp.Length; i++) CollectionAssert.AreEqual(fp[i], NameTableData.IntlFamilyParts[i]);
            Assert.AreEqual(gp.Length, NameTableData.IntlGivenParts.Length);
            for (int i = 0; i < gp.Length; i++) CollectionAssert.AreEqual(gp[i], NameTableData.IntlGivenParts[i]);
        }

        [Test]
        public void Table_IsSane()
        {
            Assert.AreEqual(NameTableData.FamilyText.Length, NameTableData.FamilyKana.Length);
            Assert.AreEqual(NameTableData.GivenText.Length, NameTableData.GivenKana.Length);
            Assert.GreaterOrEqual(NameTableData.FamilyText.Length, 100);
            Assert.GreaterOrEqual(NameTableData.GivenText.Length, 100);
            Assert.AreEqual(NameTableData.FamilyText.Length, NameTableData.FamilyText.Distinct().Count(), "姓の重複なし");
            Assert.AreEqual(NameTableData.GivenText.Length, NameTableData.GivenText.Distinct().Count(), "名の重複なし");
            var hira = new Regex("^[ぁ-ゖー]+$");
            foreach (string k in NameTableData.FamilyKana.Concat(NameTableData.GivenKana)) Assert.IsTrue(hira.IsMatch(k), "かなは平仮名: " + k);
            foreach (string t in NameTableData.FamilyText.Concat(NameTableData.GivenText)) Assert.IsFalse(string.IsNullOrWhiteSpace(t));
        }

        [Test]
        public void Generate_IsDeterministic_AndTheSameBeforeAndAfterEnrollment()
        {
            for (uint seed = 1; seed < 300; seed += 7)
            {
                PersonName a = NameGenerator.Generate(Rngs, seed), b = NameGenerator.Generate(new KeyedRngFactory(20261030UL), seed);
                Assert.AreEqual(a.Full, b.Full);
                Assert.AreEqual(a.FamilyKana, b.FamilyKana);
                Assert.IsNotEmpty(a.Family);
                Assert.IsNotEmpty(a.GivenKana);
            }
            var distinct = new HashSet<string>(Enumerable.Range(1, 50).Select(i => NameGenerator.Generate(Rngs, (uint)i).Full));
            Assert.Greater(distinct.Count, 45, "look_seed が違えば別の名前");
        }

        [Test]
        public void Generate_ReachesEveryEntry_AndKeepsTheKanaPairedWithTheText()
        {
            var fam = new HashSet<string>();
            var giv = new HashSet<string>();
            for (uint seed = 1; seed <= 20000; seed++)
            {
                PersonName n = NameGenerator.Generate(Rngs, seed * 2654435761u);
                fam.Add(n.Family);
                giv.Add(n.Given);
                int i = Array.IndexOf(NameTableData.FamilyText, n.Family), j = Array.IndexOf(NameTableData.GivenText, n.Given);
                Assert.AreEqual(NameTableData.FamilyKana[i], n.FamilyKana);
                Assert.AreEqual(NameTableData.GivenKana[j], n.GivenKana);
            }
            Assert.AreEqual(NameTableData.FamilyText.Length, fam.Count, "すべての姓が出る");
            Assert.AreEqual(NameTableData.GivenText.Length, giv.Count, "すべての名が出る");
        }

        [Test]
        public void Redraw_AvoidsTakenNames_Reproducibly_AndGivesUp()
        {
            for (uint seed = 1; seed <= 200; seed++)
            {
                string first = NameGenerator.Generate(Rngs, seed).Full;
                PersonName again = NameGenerator.Generate(Rngs, seed, false, f => f == first);
                Assert.AreNotEqual(first, again.Full, "重複したら引き直す");
                Assert.AreEqual(again.Full, NameGenerator.Generate(Rngs, seed, false, f => f == first).Full, "引き直しも再現する");
            }
            int calls = 0;
            PersonName last = NameGenerator.Generate(Rngs, 9, false, f => { calls++; return true; });
            Assert.AreEqual(NameGenerator.MaxRedraw, calls, "重複の判定は、最初の名前と 9 回目までの引き直し(10 回目の引き直しは判定せずそのまま使う)");
            Assert.IsNotEmpty(last.Full);
        }

        [Test]
        public void Overseas_NamesAreMadeOfKatakanaParts_WithHiraganaKana()
        {
            var kata = new Regex("^[ァ-ヶー]+$");
            var seen = new HashSet<string>();
            for (uint seed = 1; seed <= 500; seed++)
            {
                PersonName n = NameGenerator.Generate(Rngs, seed * 40503u, true);
                Assert.IsTrue(kata.IsMatch(n.Family) && kata.IsMatch(n.Given), n.Full);
                Assert.AreEqual(NameGenerator.ToHiragana(n.Family), n.FamilyKana);
                Assert.AreEqual(NameGenerator.ToHiragana(n.Given), n.GivenKana);
                seen.Add(n.Full);
            }
            Assert.Greater(seen.Count, 400, "名前の種類が十分ある");
        }

        [Test]
        public void EveryRunnerOfAPlaythrough_HasAName_AndNoTwoMembersShareOne()
        {
            foreach (Difficulty d in new[] { Difficulty.Mid, Difficulty.Club })
            {
                GameSession gs = GameSession.New(1234UL, d);
                gs.AutoDeclareTickets = true;
                for (int w = 0; w < 48 * 6; w++)
                {
                    gs.StepWeek();
                    if (w % 48 == 47 || w == 0)
                    {
                        var full = new HashSet<string>();
                        foreach (Runner r in gs.State.Runners)
                        {
                            Assert.IsNotEmpty(r.FamilyName, d + " 週 " + w + " 選手 " + r.Id);
                            Assert.IsNotEmpty(r.GivenName);
                            Assert.IsNotEmpty(r.FamilyKana);
                            Assert.IsTrue(full.Add(r.FamilyName + r.GivenName), "部員の姓名の重複: " + r.FamilyName + r.GivenName);
                        }
                    }
                }
                Assert.IsTrue(gs.State.Obs.Count > 0 && gs.State.Obs.All(o => o.Name.Length > 0), "卒業生の記録にも名前がある");
            }
        }

        [Test]
        public void SpecialRecruits_AreNamed_TheRookieWithAJapaneseName_TheOverseasOneWithKatakana()
        {
            GameSession gs = GameSession.New(99UL, Difficulty.Mid);
            gs.State.Tickets.Earn(Ekiden.Domain.Tickets.TicketMilestones.WinFall, 0);
            gs.State.Tickets.Earn(Ekiden.Domain.Tickets.TicketMilestones.WinNational, 0);
            int declared = 0, checkedRecruits = 0;
            var kata = new Regex("^[ァ-ヶー]+$");
            for (int w = 0; w < 48 * 4 && checkedRecruits < 2; w++)
            {
                if (declared < 2 && Ekiden.Domain.Team.TeamWeek.YearWeek(gs.State) / 4 >= 6 && Ekiden.Domain.Team.TeamWeek.YearWeek(gs.State) / 4 <= 8 && gs.DeclareTicket(declared == 0 ? Ekiden.Domain.Tickets.TicketKind.Rookie : Ekiden.Domain.Tickets.TicketKind.Intl).Ok) declared++;
                gs.StepWeek();
                foreach (Runner r in gs.State.Runners.Where(x => x.RecruitSource == RecruitSource.Ticket && x.EnrollWeek == gs.State.AbsWeek).ToList())
                {
                    Assert.IsNotEmpty(r.FamilyName + r.GivenName);
                    Assert.AreEqual(r.Overseas, kata.IsMatch(r.FamilyName), "留学生はカタカナの仮の名前: " + r.FamilyName);
                    checkedRecruits++;
                }
            }
            Assert.GreaterOrEqual(declared, 1, "招聘を宣言できた");
            Assert.GreaterOrEqual(checkedRecruits, 1, "招聘選手が入学した");
        }

        [Test]
        public void IntakeNames_AvoidTheNamesOfCurrentMembers()
        {
            // 姓・名の表を 1 組に絞った世界では、重複は必ず起きる。重複を避ける再抽選が働かなければ、部員の姓名が重なる。
            // (表は読み取り専用の静的な値なので、ここでは 2 人の look_seed を、わざと同じ名前になるものにして確かめる)
            uint a = 1, b = 0;
            string first = NameGenerator.Generate(Rngs, a).Full;
            for (uint s = 2; s < 2000000 && b == 0; s++) if (NameGenerator.Generate(Rngs, s).Full == first) b = s;       // 同じ名前になる別の look_seed(姓 150 × 名 110 = 16,500 通りなので見つかる)
            Assert.AreNotEqual(0u, b, "同じ名前になる look_seed が見つかる");
            var members = new List<Runner> { new Runner { Id = 1, LookSeed = a } };
            NameGenerator.Assign(members[0], Rngs, f => false);
            var second = new Runner { Id = 2, LookSeed = b };
            NameGenerator.Assign(second, Rngs, f => members.Any(m => m.FamilyName + m.GivenName == f));
            Assert.AreNotEqual(members[0].FamilyName + members[0].GivenName, second.FamilyName + second.GivenName, "いまの部員と同じ姓名は避ける");
            var third = new Runner { Id = 3, LookSeed = b };
            NameGenerator.Assign(third, Rngs, null);
            Assert.AreEqual(members[0].FamilyName + members[0].GivenName, third.FamilyName + third.GivenName, "判定がなければ同じ名前になる(重複を避けているのは再抽選)");
        }

        [Test]
        public void Names_DoNotChangeAnyOtherRandomStream()
        {
            // 名前の生成は label 8 の別の b だけを使う。名前の有無で、能力・個性・性格タグは変わらない。
            WorldRunner w = new WorldRunner { LookSeed = 777, A0 = 60, Gap = 10 };
            var before = Ekiden.Domain.Screens.Presentation.Traits(Rngs, 777).Select(t => t.ToString()).ToArray();
            NameGenerator.Generate(Rngs, 777);
            NameGenerator.Generate(Rngs, 777, false, f => true);
            var after = Ekiden.Domain.Screens.Presentation.Traits(Rngs, 777).Select(t => t.ToString()).ToArray();
            CollectionAssert.AreEqual(before, after);
            Assert.AreEqual(w.N10(Rngs).Sum(), w.N10(Rngs).Sum());
        }
    }
}
