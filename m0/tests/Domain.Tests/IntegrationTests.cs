using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Rng;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>段階 10 の統合: 自校の抽象(PlayerAgent)・世界の不変条件・統計の受け入れの帯(09 §16.3)。</summary>
    public class IntegrationTests
    {
        private static WorldSim NewWorld(ulong seed = 20260101UL, double? bonus = null) { return WorldSim.Create(new KeyedRngFactory(seed), 15, bonus); }

        [Test]
        public void StartPlayer_PicksTheSchoolByPowerRank_AndSetsTheScenario()
        {
            foreach (var (d, rank) in new[] { (Difficulty.Elite, 3), (Difficulty.Mid, 7), (Difficulty.Low, 20) })
            {
                WorldSim w = NewWorld();
                var order = w.Schools.OrderByDescending(s => w.TeamPower(s)).ToList();
                PlayerAgent a = w.StartPlayer(d);
                Assert.AreEqual(1, w.Schools.Count(s => s.IsPlayer), d.ToString());
                Assert.AreSame(a.School, w.Schools.Single(s => s.IsPlayer));
                Assert.AreEqual(order[rank - 1].Id, a.School.Id, d + " は戦力順位 " + rank + " 位の学校");
                ScenarioDef sc = Scenarios.For(d);
                Assert.AreEqual(sc.Funds, a.School.Funds);
                Assert.AreEqual(sc.Facilities.Dorm, a.School.Fac.Dorm);
                Assert.AreEqual(a.School.Fac.Scout, a.School.ScoutLv);
            }
        }

        [Test]
        public void Agent_RunsYears_RecordsEachYear_AndKeepsTheInvariants()
        {
            WorldSim w = NewWorld();
            PlayerAgent a = w.StartPlayer(Difficulty.Mid);
            for (int y = 1; y <= 40; y++)
            {
                YearResult r = w.PlayYear(y, a);
                Assert.AreEqual(y, a.Stats[a.Stats.Count - 1].Year);
                Assert.AreEqual(3, r.Order.Count);
                CollectionAssert.IsEmpty(WorldInvariants.Check(w, a), "年度 " + y);
            }
            Assert.AreEqual(40, a.Stats.Count);
            Assert.GreaterOrEqual(a.Tickets.InvitesDeclared, 1, "40 年度のうちに券を使う(07 §4: 45 年度で 14 枚)");
            Assert.LessOrEqual(a.School.Roster.Count(r => r.Ticket), 2, "招聘選手の在籍は 2 人まで");
        }

        [Test]
        public void Agent_WithoutTickets_NeverInvites()
        {
            WorldSim w = NewWorld();
            PlayerAgent a = w.StartPlayer(Difficulty.Mid);
            a.TicketsEnabled = false;
            for (int y = 1; y <= 20; y++) w.PlayYear(y, a);
            Assert.AreEqual(0, a.Tickets.InvitesDeclared);
            Assert.AreEqual(0, a.School.Roster.Count(r => r.Ticket));
        }

        [Test]
        public void WorldWithoutPlayer_StillRuns_AndKeepsTheInvariants()
        {
            WorldSim w = NewWorld(7);
            for (int y = 1; y <= 30; y++)
            {
                w.PlayYear(y);
                CollectionAssert.IsEmpty(WorldInvariants.Check(w, null), "年度 " + y);
            }
        }

        [Test]
        public void Invariants_DetectBrokenStates()
        {
            WorldSim w = NewWorld();
            PlayerAgent a = w.StartPlayer(Difficulty.Mid);
            w.PlayYear(1, a);
            CollectionAssert.IsEmpty(WorldInvariants.Check(w, a));
            WorldRunner r = w.Schools[0].Roster[0];
            double prog = r.Prog;
            r.Prog = 1.2;
            Assert.That(WorldInvariants.Check(w, a).Any(e => e.StartsWith("W-4", StringComparison.Ordinal)));
            r.Prog = prog;
            int grade = r.Grade;
            r.Grade = 7;
            Assert.That(WorldInvariants.Check(w, a).Any(e => e.StartsWith("W-3", StringComparison.Ordinal)));
            r.Grade = grade;
            w.Schools[3].Prestige = 1500;
            Assert.That(WorldInvariants.Check(w, a).Any(e => e.StartsWith("W-2", StringComparison.Ordinal)));
            w.Schools[3].Prestige = 500;
            a.School.Funds = -1;
            Assert.That(WorldInvariants.Check(w, a).Any(e => e.StartsWith("W-5", StringComparison.Ordinal)));
            a.School.Funds = 100;
            a.Tickets.Stock = 3;
            Assert.That(WorldInvariants.Check(w, a).Any(e => e.StartsWith("W-6", StringComparison.Ordinal)));
        }

        [Test]
        public void SavedDoubles_AreAlwaysQ6Closed_AcrossAYear()
        {
            // 09 D-10: 保存する double は代入のときに Q6 を通す。1 年度ごとに、名簿・学校の double がすべて Q6 で閉じている
            WorldSim w = NewWorld(11);
            PlayerAgent a = w.StartPlayer(Difficulty.Low);
            for (int y = 1; y <= 25; y++)
            {
                w.PlayYear(y, a);
                foreach (School s in w.Schools)
                {
                    foreach (double x in new[] { s.Prestige, s.Hist, s.Base, s.CoachQ })
                        Assert.AreEqual(Q6.Round(x), x, "学校 " + s.Id + " 年度 " + y);
                    foreach (WorldRunner r in s.Roster)
                        foreach (double x in new[] { r.A0, r.Gap, r.Prog, r.HighSchoolU })
                            Assert.AreEqual(Q6.Round(x), x, "選手 年度 " + y);
                }
                Assert.AreEqual(Q6.Round(a.School.Funds), a.School.Funds);
            }
        }

        [Test]
        public void EliteBonusKnob_ChangesThePool_AndZeroEqualsNoBonus()
        {
            WorldSim spec = NewWorld(5);
            WorldSim none = NewWorld(5, 0.0);
            Assert.AreEqual(WorldK.ElitePrefBonus, spec.ElitePrefBonus);
            Assert.AreEqual(0.0, none.ElitePrefBonus);
            // 強豪県の候補の A0 の基準値(平均が変わらないように下げて足す。05 §6.2)
            Assert.AreEqual(PoolGenerator.BaseMean(true, 0.0), PoolGenerator.BaseMean(false, 0.0), 0, "加算なしなら強豪県も同じ基準値");
            Assert.AreEqual(3.0, PoolGenerator.BaseMean(true, 3.0) - PoolGenerator.BaseMean(false, 3.0), 1e-12);
            Assert.AreNotEqual(spec.TeamPower(spec.Schools[0]), none.TeamPower(none.Schools[0]));
        }

        // ---- 統計の受け入れ(T4)の仕組み。帯に入るかは SimCli accept(120 シード × 45 年度)。ここでは仕組みだけを確かめる ----

        [Test]
        public void Acceptance_RunSeed_IsDeterministic_AndSane()
        {
            SeedResult a = Acceptance.RunSeed(1000003UL + 17UL, 30, Difficulty.Mid);
            SeedResult b = Acceptance.RunSeed(1000003UL + 17UL, 30, Difficulty.Mid);
            Assert.AreEqual(a.WinRate, b.WinRate);
            Assert.AreEqual(a.TripleRate, b.TripleRate);
            Assert.AreEqual(a.Clear, b.Clear);
            Assert.AreEqual(a.P, b.P);
            Assert.That(a.P, Is.InRange(55.0, 80.0));
            Assert.That(a.WinRate, Is.InRange(0.0, 1.0));
            Assert.That(a.Clear, Is.InRange(1.0, 31.0));
            Assert.AreEqual(6, a.Years, "25〜30 年度の 6 年度ぶん");
        }

        [Test]
        public void Acceptance_BandLogic()
        {
            Acceptance.Band b = Acceptance.Bands[Difficulty.Elite];
            var inside = new SeedResult { WinRate = b.Win, TripleRate = b.Triple, Clear = b.Clear, P = b.P };
            Assert.IsTrue(Acceptance.EquivalentToReference(b, inside));
            Assert.IsFalse(Acceptance.EquivalentToReference(b, new SeedResult { WinRate = b.Win + b.WinTol * 1.1, TripleRate = b.Triple, Clear = b.Clear, P = b.P }));
            Assert.IsFalse(Acceptance.EquivalentToReference(b, new SeedResult { WinRate = b.Win, TripleRate = b.Triple, Clear = b.Clear + b.ClearTol * 1.1, P = b.P }));
            // 仕様の世界: P は +3 を外した世界との差で見る(L-44)
            var spec = new SeedResult { WinRate = b.Win, TripleRate = b.Triple, Clear = b.Clear, P = b.P + b.PTol + 0.1 };
            Assert.IsFalse(Acceptance.EquivalentToReference(b, spec), "素の帯では外れる");
            Assert.IsTrue(Acceptance.SpecWorldAccepted(b, spec, inside), "差 +0.5 は許す範囲(−0.3〜+1.0)");
            Assert.IsFalse(Acceptance.SpecWorldAccepted(b, new SeedResult { WinRate = b.Win, TripleRate = b.Triple, Clear = b.Clear, P = b.P + 1.5 }, inside), "差 +1.5 は実装の取りこぼしを疑う");
            Assert.IsFalse(Acceptance.SpecWorldAccepted(b, new SeedResult { WinRate = b.Win, TripleRate = b.Triple, Clear = b.Clear, P = b.P - 0.6 }, inside), "+3 で P が下がるのは異常");
            Assert.AreEqual(3, Acceptance.Bands.Count);
        }

        [Test]
        public void Acceptance_Mean_AveragesPerSeed()
        {
            var m = Acceptance.Mean(new[] { new SeedResult { WinRate = 0.4, TripleRate = 0.2, Clear = 10, P = 70, Years = 20 }, new SeedResult { WinRate = 0.6, TripleRate = 0.4, Clear = 20, P = 72, Years = 20 } });
            Assert.AreEqual(0.5, m.WinRate, 1e-12);
            Assert.AreEqual(0.3, m.TripleRate, 1e-12);
            Assert.AreEqual(15.0, m.Clear, 1e-12);
            Assert.AreEqual(71.0, m.P, 1e-12);
        }

        [Test]
        public void AcceptanceBands_MatchDoc09_Section16_3()
        {
            string text = File.ReadAllText(DocPaths.Find("09_save_tech_spec.md"), Encoding.UTF8);
            int start = text.IndexOf("### 16.3 統計の受け入れ", StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0);
            string section = text.Substring(start, text.IndexOf("\n## 17.", start, StringComparison.Ordinal) - start);
            var rows = new Dictionary<string, Difficulty> { { "強豪(Elite)", Difficulty.Elite }, { "中堅(Mid)", Difficulty.Mid }, { "弱小(Low)", Difficulty.Low } };
            int found = 0;
            foreach (Match row in Regex.Matches(section, @"^\|\s*(強豪\(Elite\)|中堅\(Mid\)|弱小\(Low\))\s*\|\s*([\d.]+) ± ([\d.]+)\s*\|\s*([\d.]+) ± ([\d.]+)\s*\|\s*([\d.]+) ± ([\d.]+)\s*\|\s*([\d.]+) ± ([\d.]+)\s*\|", RegexOptions.Multiline))
            {
                double[] v = Enumerable.Range(2, 8).Select(i => double.Parse(row.Groups[i].Value, CultureInfo.InvariantCulture)).ToArray();
                Acceptance.Band b = Acceptance.Bands[rows[row.Groups[1].Value]];
                CollectionAssert.AreEqual(new[] { b.Win, b.WinTol, b.Triple, b.TripleTol, b.Clear, b.ClearTol, b.P, b.PTol }, v, row.Groups[1].Value);
                found++;
            }
            Assert.AreEqual(3, found, "09 §16.3 の帯の表の 3 行");
            Assert.That(section, Does.Contain("L44").Or.Contain("L-44"), "L-44 の扱いが §16.3 に書かれている");
        }

        [Test]
        public void FameHistMax_IsTheTripleWinLimit()
        {
            // 履歴 = 0.6 × 履歴 + 0.4 × その年度の名声ポイント。1 年度の最大は 3 × (0.5 + 12) + 30 = 67.5(極限もそのまま)
            Assert.AreEqual(67.5, WorldInvariants.FameHistMax, 1e-12);
            double h = 0;
            for (int i = 0; i < 200; i++) h = (1 - WorldK.K_PRESTIGE_ALPHA) * h + WorldK.K_PRESTIGE_ALPHA * WorldInvariants.FameHistMax;
            Assert.AreEqual(WorldInvariants.FameHistMax, h, 1e-9);
        }
    }
}
