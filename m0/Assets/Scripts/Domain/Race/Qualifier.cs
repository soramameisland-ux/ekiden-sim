using System;
using System.Collections.Generic;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Rng;

namespace Ekiden.Domain.Race
{
    public sealed class QualifierTeam
    {
        public int Id;
        public RaceRunner[] Runners;
    }

    public sealed class QualifierResult
    {
        public int Total;       // 上位 10 名の合計(ds)
        public int Id;
        public int[] Times;     // 個人(ds。小さい順)
    }

    public static class Qualifier
    {
        /// <summary>予選会: 各校 12 名が 20km を走り、上位 10 名の合計タイムの小さい順(04 §3.2)。rngFor(チーム ID, 選手の番号)。</summary>
        public static List<QualifierResult> Run(IReadOnlyList<QualifierTeam> teams, Func<int, int, Pcg32> rngFor, Env env)
        {
            var outList = new List<QualifierResult>();
            double d = RaceConstants.QualKm;
            foreach (QualifierTeam t in teams)
            {
                var times = new List<int>();
                for (int ri = 0; ri < t.Runners.Length; ri++)
                {
                    RaceRunner r = t.Runners[ri];
                    Pcg32 rng = rngFor(t.Id, ri);
                    LegDraw dr = RaceMath.DrawLeg(rng, r, 1.0, Instruction.Keep, 1.0, d, env.Temp);
                    double p = RaceTables.Rating(r.Stats, SectionType.Long);
                    double bt = RaceMath.BaseTime(d, 1.0, p);
                    double tt = bt * (1.0 + RaceMath.FormAdj(r) + RaceMath.EnvAdj(env, r) + dr.Noise + dr.Boost + dr.Stall) + bt * dr.Brake;
                    times.Add(Rounding.HalfUp(tt * 10.0));
                }
                times.Sort();
                int total = 0;
                for (int i = 0; i < Math.Min(RaceConstants.QualCount, times.Count); i++) total += times[i];
                outList.Add(new QualifierResult { Total = total, Id = t.Id, Times = times.ToArray() });
            }
            RaceSimulator.StableSort(outList, (a, b) => a.Total != b.Total ? a.Total.CompareTo(b.Total) : a.Id.CompareTo(b.Id));
            return outList;
        }
    }
}
