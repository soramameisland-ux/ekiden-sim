using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>中断からの再開(09 §4.6・TV-3): レースの途中の区間 k の前で中断 → 再開後に k 以降の指示を変えても、完了した区間の ds・累積は変わらない(乱数は大会・チーム・区間の鍵で決まり、指示や処理順で引き方が変わらない。04 §12)。</summary>
    public class SuspendReplayTests
    {
        private static List<RaceTeam> Teams(Pcg32 g, int nsec, Instruction[][] instr, int nTeams)
        {
            var teams = new List<RaceTeam>();
            for (int t = 1; t <= nTeams; t++)
            {
                double mean = 58 + g.Uniform() * 14;
                var lineup = new RaceRunner[nsec];
                for (int i = 0; i < nsec; i++)
                {
                    var r = new RaceRunner { Form = (int)(g.Uniform() * 5) - 2, Fatigue = (int)(g.Uniform() * 40), Name = "t" + t + "s" + i };
                    for (int k = 0; k < StatInfo.Count; k++) r.Stats[k] = Math.Max(30, Math.Min(90, Math.Round(g.Normal(mean, 3.5))));
                    lineup[i] = r;
                }
                teams.Add(new RaceTeam { Id = t, Lineup = lineup, Geki = 2, GekiMin = 0.0, Instr = instr[t - 1] });
            }
            return teams;
        }

        [Test]
        public void CompletedSections_DoNotDependOnLaterInstructions()
        {
            int compared = 0;
            for (int k = 0; k < 300; k++)
            {
                RaceKind race = (RaceKind)(k % 3);
                int nsec = RaceTables.Course(race).Count;
                var g = new KeyedRngFactory((ulong)(2000 + k)).Stream(RngRegistry.LabelWorldGen, 9000, k);
                Instruction[][] instr = Enumerable.Range(0, 12).Select(_ => Enumerable.Range(0, nsec).Select(__ => (Instruction)(int)(g.Uniform() * 3)).ToArray()).ToArray();
                int cut = 1 + (int)(g.Uniform() * (nsec - 1));                       // 区間 cut の指示の前で中断した
                Instruction[][] alt = instr.Select(a => a.Take(cut).Concat(Enumerable.Range(0, nsec - cut).Select(__ => (Instruction)(int)(g.Uniform() * 3))).ToArray()).ToArray();
                var envs = new[] { new Env { Temp = 5 + g.Uniform() * 20, Wind = g.Uniform() * 4, Rain = g.Uniform() < 0.2 }, new Env { Temp = 5 + g.Uniform() * 20, Wind = g.Uniform() * 4, Rain = false } };
                var master = new KeyedRngFactory((ulong)(4242 + k));
                Func<int, int, Pcg32> rngFor = (t, si) => master.Stream(RngRegistry.LabelRace, 20 + k % 3 + 1, t * 100 + si);
                // 同じ選手・同じ乱数で、指示の後半だけが違う 2 回のレース(選手の生成は同じ g の状態を使わないよう、別の鍵から作る)
                var gp1 = new KeyedRngFactory((ulong)(7000 + k)).Stream(RngRegistry.LabelWorldGen, 9001, k);
                var gp2 = new KeyedRngFactory((ulong)(7000 + k)).Stream(RngRegistry.LabelWorldGen, 9001, k);
                List<TeamResult> full = RaceSimulator.Simulate(race, Teams(gp1, nsec, instr, 12), envs, rngFor);
                List<TeamResult> again = RaceSimulator.Simulate(race, Teams(gp2, nsec, alt, 12), envs, rngFor);
                foreach (TeamResult a in full)                                          // 結果は順位順に返るので、チーム ID で組にする
                {
                    TeamResult b = again.First(x => x.Id == a.Id);
                    CollectionAssert.AreEqual(a.Sections.Take(cut).ToArray(), b.Sections.Take(cut).ToArray(), "レース " + k + " チーム " + a.Id + " 区間 ds");
                    CollectionAssert.AreEqual(a.Cum.Take(cut).ToArray(), b.Cum.Take(cut).ToArray(), "レース " + k + " チーム " + a.Id + " 累積");
                    compared += cut;
                }
            }
            Assert.Greater(compared, 10000);
            TestContext.Out.WriteLine("中断からの再開: 300 レース(3 大会 × 12 チーム)で、完了した区間 " + compared + " 件の ds・累積が、再開後の指示の変更で変わらない");
        }
    }
}
