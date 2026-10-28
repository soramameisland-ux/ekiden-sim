using System;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Screens;
using Ekiden.Domain.World;

namespace Ekiden.Domain.Recruit
{
    /// <summary>入学する候補・途中の選手を、自校の選手(<see cref="Runner"/>)に展開する(05 §6.2・§10.1)。他校の選手は展開しない(抽象のまま)。
    /// 能力: <c>stat_i = clamp(A0 + 型の偏り_i + n10_i, 20, 95)</c>(n10 は look_seed から決まる固定の偏り。N(0,3)、CON・MEN・REC・DUR は N(0,4))、
    /// 素質: <c>pot_i = min(100, stat_i + max(2, round(gap + N(0,3))))</c>(label 8・b = 12)に、素養の加算(10 §4)。個性・素養・性格タグは look_seed から決まる。</summary>
    public static class RunnerFactory
    {
        public const int StatMax = 95, StatMin = 20;
        public const int PotentialNoiseKey = 12;                  // label 8 の b。素質の雑音(i = 0〜9 の順に N(0,3) を 1 つずつ)

        private static int[] ToInts(double[] v)
        {
            var r = new int[StatInfo.Count];
            for (int i = 0; i < r.Length; i++) r[i] = Math.Max(StatMin, Math.Min(StatMax, Rounding.HalfUp(v[i])));
            return r;
        }

        /// <summary>入学時の素質(素養の加算の前)。</summary>
        public static int[] PotentialAtEntry(WorldRunner c, int[] entryStats, IRngFactory rngs)
        {
            Pcg32 g = rngs.Stream(RngRegistry.LabelLook, c.LookSeed, PotentialNoiseKey);
            var pot = new int[StatInfo.Count];
            for (int i = 0; i < pot.Length; i++)
                pot[i] = Math.Min(100, entryStats[i] + Math.Max(2, Rounding.HalfUp(c.Gap + g.Normal(0, 3))));
            return pot;
        }

        /// <summary>入学が確定した候補を、1 年生として展開する。enrollWeek = 入学する年度の最初の通算週。</summary>
        public static Runner FromCandidate(WorldRunner c, int id, int enrollWeek, IRngFactory rngs, int schoolPref, bool invited = false)
        {
            int[] stats = ToInts(AiRacing.ExpandStats(c.A0, c.Profile, c.N10(rngs)));
            return Build(c, id, 1, enrollWeek, stats, PotentialAtEntry(c, stats, rngs), rngs, schoolPref, invited ? 4 : 3);
        }

        /// <summary>途中の選手(新規ゲームで開始校の名簿にいる 1〜4 年生)を展開する。入学時の能力・素質を求めてから、いまの力(A0 + gap × 進捗率)の能力にする(素質を超えない)。
        /// 進捗の近似(05 §9.3)と 03 の詳細モデルは別のモデルなので、ここは開始の足場にすぎない。</summary>
        public static Runner FromInFlight(WorldRunner w, int id, int currentYearIndex, IRngFactory rngs, int schoolPref)
        {
            int[] entry = ToInts(AiRacing.ExpandStats(w.A0, w.Profile, w.N10(rngs)));
            int[] pot = PotentialAtEntry(w, entry, rngs);
            double now = w.A0 + w.Gap * w.Prog;
            int[] stats = ToInts(AiRacing.ExpandStats(now, w.Profile, w.N10(rngs)));
            return Build(w, id, w.Grade, (currentYearIndex - w.Grade) * Team.Calendar.WeeksPerYear, stats, pot, rngs, schoolPref, 3, capAtPotential: true);
        }

        private static Runner Build(WorldRunner c, int id, int grade, int enrollWeek, int[] stats, int[] potential, IRngFactory rngs, int schoolPref, int motivation, bool capAtPotential = false)
        {
            Style style = Individuality.GenerateStyle(rngs, c.LookSeed, c.Profile);
            Gift[] gifts = Individuality.GenerateGifts(rngs, c.LookSeed);
            Individuality.AddGiftPotential(potential, gifts);
            var r = new Runner
            {
                Id = id, Grade = grade, EnrollWeek = enrollWeek, GrowthType = (GrowthType)(int)c.Growth, Fatigue = 20, Motivation = motivation, Form = 0, LookSeed = c.LookSeed,
                Overseas = c.Overseas, Country = c.Overseas ? c.CountryIndex + 1 : 0, Pref = c.Overseas ? 0 : c.PrefectureId, HighSchool = c.Overseas ? 0 : Geo.HighSchoolId(c.PrefectureId, c.HighSchoolU),
                RecruitSource = c.Ticket ? RecruitSource.Ticket : RecruitSource.Normal,
            };
            for (int i = 0; i < StatInfo.Count; i++)
            {
                r.Potential[i] = potential[i];
                r.Stats[i] = capAtPotential ? Math.Min(stats[i], potential[i]) : stats[i];
            }
            r.SetIndividuality(style, gifts);
            foreach (Trait t in Presentation.Traits(rngs, c.LookSeed)) r.Traits.Add(t.ToString());
            r.DeriveDistance(schoolPref);
            return r;
        }
    }
}
