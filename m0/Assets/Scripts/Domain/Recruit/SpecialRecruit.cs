using System;
using System.Collections.Generic;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.World;

namespace Ekiden.Domain.Recruit
{
    /// <summary>生成した招聘選手(07 §6)。入学時の姿・名前の元になる look_seed を含む。</summary>
    public sealed class SpecialRecruit
    {
        public TicketKind Kind;
        public double A0, Gap, Q, V;
        public GrowthType Growth;
        public Profile Profile;
        public int[] Stats = new int[StatInfo.Count];
        public int[] Potential = new int[StatInfo.Count];       // 素養の加算のあと
        public int[] PotentialBase = new int[StatInfo.Count];   // 素養の加算の前(07 §6.2 の式のとおり)
        public string Skill;                    // 特殊能力の ID(skill. なし)。なければ null
        public int Shift;                       // 上限を超えたため全能力から引いた値
        public uint LookSeed;
        public int CountryIndex = -1;           // 留学生
        public int Pref;                        // スーパールーキー(指定がなければ抽選した県)
        public double HighSchoolU;              // 県内の高校を strength に比例して選ぶための乱数
        public Style Style;
        public Gift[] Gifts = new Gift[0];

        public string Country { get { return CountryIndex >= 0 ? Geo.Countries[CountryIndex] : null; } }
    }

    /// <summary>招聘選手の生成(07 §6)。種別と(スーパールーキーの)県の指定だけを入力に、年度ごとに決まった鍵つきの乱数で作る(購入状態・他の乱数の消費に影響されない)。</summary>
    public static class SpecialRecruitGenerator
    {
        // 帯(07 §6.1): A0 の平均・標準偏差・範囲 / ギャップの平均・標準偏差・範囲
        private struct Band
        {
            public double A0Mean, A0Sd, A0Lo, A0Hi, GapMean, GapSd, GapLo, GapHi;
        }

        private static readonly Band Rookie = new Band { A0Mean = 61.0, A0Sd = 2.0, A0Lo = 58.0, A0Hi = 64.0, GapMean = 25.0, GapSd = 3.0, GapLo = 18.0, GapHi = 32.0 };
        private static readonly Band Intl = new Band { A0Mean = 66.0, A0Sd = 2.0, A0Lo = 64.0, A0Hi = 68.0, GapMean = 8.0, GapSd = 2.0, GapLo = 4.0, GapHi = 12.0 };

        // 型ごとの能力の偏り(05 §6.2。SPD STA PAC KCK CLB DWN)
        private static readonly int[][] Offsets =
        {
            new[] { 0, 0, 0, 0, 0, 0 },
            new[] { -8, 6, 4, -6, -2, -3 },
            new[] { 8, -6, -4, 6, -2, -2 },
            new[] { -4, 2, 0, -2, 10, 6 },
        };

        private static readonly double[] GrowthShare = { 0.25, 0.55, 0.20 };     // Early / Normal / Late
        private static readonly double[] ProfileShare = { 0.35, 0.25, 0.25, 0.15 };

        /// <summary>入学時に持てる特殊能力の候補(Blue のうち不自然でないもの。Gold・Red・リベンジは付けない。07 §6.5)。</summary>
        public static readonly string[] SkillPool =
        {
            "downhill_master", "ace_section", "anchor", "overtake", "hold_lead", "pack_runner", "peaking_master", "summer_boy", "big_stage", "recovery_plus",
        };

        private static int Pick(Pcg32 g, double[] w)
        {
            double u = g.Uniform(), acc = 0.0;
            for (int i = 0; i < w.Length; i++)
            {
                acc += w[i];
                if (u < acc) return i;
            }
            return w.Length - 1;
        }

        private static double Clamp(double v, double lo, double hi) { return Math.Max(lo, Math.Min(hi, v)); }

        /// <summary>招聘選手を 1 人生成する。乱数は label 3、a = 入学年度、b = 9000 + index。引く順序は固定(§6.2)。個性・素養は通常の入学生と同じ関数で付ける(§6.5)。</summary>
        public static SpecialRecruit Generate(IRngFactory rngs, int enrollYear, TicketKind kind, int pref = 0, int index = 0)
        {
            Pcg32 g = rngs.Stream(RngRegistry.LabelScout, enrollYear, 9000 + index);
            Band b = kind == TicketKind.Rookie ? Rookie : Intl;
            double a0 = Clamp(g.Normal(b.A0Mean, b.A0Sd), b.A0Lo, b.A0Hi);                  // ① A0
            double gap = Clamp(g.Normal(b.GapMean, b.GapSd), b.GapLo, b.GapHi);             // ② ギャップ
            int growth = Pick(g, GrowthShare);                                              // ③ 成長タイプ
            int ptype = Pick(g, ProfileShare);                                              // ④ 型
            var n10 = new double[10];
            for (int i = 0; i < 6; i++) n10[i] = g.Normal(0.0, 3.0);                        // ⑤ 能力の偏り
            for (int i = 6; i < 10; i++) n10[i] = g.Normal(0.0, 4.0);
            var pn = new double[10];
            for (int i = 0; i < 10; i++) pn[i] = g.Normal(0.0, 3.0);                        // ⑥ 素質の揺れ
            double uSkill = g.Uniform(), uSkillI = g.Uniform();                             // ⑦ 特殊能力
            double uOrigin = g.Uniform(), uHs = g.Uniform();                                // ⑧ 出身と高校(指定があっても引く)
            uint lookSeed = g.U32();                                                        // ⑨ 外見・名前

            var r = new SpecialRecruit { Kind = kind, A0 = a0, Gap = gap, Growth = (GrowthType)growth, Profile = (Profile)ptype, LookSeed = lookSeed, Q = a0 + 0.6 * gap, V = a0 + 0.3 * gap };
            int[] off = Offsets[ptype];
            int max = 0;
            double sum = 0;
            for (int i = 0; i < 10; i++)
            {
                int v = Rounding.HalfUp(a0 + (i < 6 ? off[i] : 0) + n10[i]);
                r.Stats[i] = Math.Max(20, Math.Min(95, v));
                if (r.Stats[i] > max) max = r.Stats[i];
                sum += r.Stats[i];
            }
            double mean = sum / 10.0;
            int shift = Math.Max(0, Math.Max(max - TicketK.K_SPECIAL_MAX_STAT, (int)Math.Ceiling(mean - TicketK.K_SPECIAL_MAX_MEAN)));   // 上限を超えたら、全能力を同じだけ下げる(型の形は保つ)
            r.Shift = shift;
            for (int i = 0; i < 10; i++)
            {
                r.Stats[i] -= shift;
                r.Potential[i] = Math.Min(100, r.Stats[i] + Math.Max(2, Rounding.HalfUp(gap + pn[i])));
            }
            if (uSkill < TicketK.K_SPECIAL_SKILL_P) r.Skill = SkillPool[Math.Min(SkillPool.Length - 1, (int)(uSkillI * SkillPool.Length))];
            if (kind == TicketKind.Intl)
            {
                r.CountryIndex = Math.Min(Geo.Countries.Length - 1, (int)(uOrigin * Geo.Countries.Length));
            }
            else
            {
                r.Pref = pref != 0 ? pref : 1 + Math.Min(46, (int)(uOrigin * 47));
                r.HighSchoolU = uHs;
            }
            r.Style = Individuality.GenerateStyle(rngs, lookSeed, r.Profile);
            r.Gifts = Individuality.GenerateGifts(rngs, lookSeed);
            Array.Copy(r.Potential, r.PotentialBase, StatInfo.Count);
            Individuality.AddGiftPotential(r.Potential, r.Gifts);
            return r;
        }

        /// <summary>検証ルール S-1〜S-4(§6.6。S-5 は選手への変換で付ける)。違反の文言のリスト(空なら適合)。</summary>
        public static List<string> Validate(SpecialRecruit r, bool giftsAdded = true)
        {
            var errs = new List<string>();
            int max = 0, sum = 0;
            foreach (int s in r.Stats) { max = Math.Max(max, s); sum += s; }
            if (max > TicketK.K_SPECIAL_MAX_STAT) errs.Add("S-1 各能力 ≤ 80");
            if (sum / 10.0 > TicketK.K_SPECIAL_MAX_MEAN + 1e-9) errs.Add("S-1 10能力の平均 ≤ 68");
            for (int i = 0; i < 10; i++)
                if (!(r.Stats[i] <= r.Potential[i] && r.Potential[i] <= 100)) { errs.Add("S-2 stats ≤ potential ≤ 100"); break; }
            if (r.Skill != null && Array.IndexOf(SkillPool, r.Skill) < 0) errs.Add("S-3 特殊能力は候補だけ");
            Band b = r.Kind == TicketKind.Rookie ? Rookie : Intl;
            if (r.A0 < b.A0Lo || r.A0 > b.A0Hi || r.Gap < b.GapLo || r.Gap > b.GapHi) errs.Add("S-4 A0・gap が帯の範囲内");
            return errs;
        }

        /// <summary>入学する選手にする(§3.4): 疲労20・やる気3・調子0・特殊能力・個性と素養。呼ぶ側が id・入学週・出身(県・高校・距離帯)を付ける。</summary>
        public static Runner ToRunner(SpecialRecruit r, int id)
        {
            var run = new Runner { Id = id, Fatigue = 20, Motivation = 3, Form = 0, Grade = 1, GrowthType = r.Growth };
            Array.Copy(r.Stats, run.Stats, StatInfo.Count);
            Array.Copy(r.Potential, run.Potential, StatInfo.Count);
            if (r.Skill != null) run.Skills.Add("skill." + r.Skill);
            run.SetIndividuality(r.Style, r.Gifts);
            run.RecruitSource = RecruitSource.Ticket;
            run.TicketKind = r.Kind;
            if (r.Kind == TicketKind.Intl)
            {
                run.Overseas = true;
                run.Far = true;
                run.Pref = 0;
            }
            else
            {
                run.Pref = r.Pref;
            }
            return run;
        }
    }
}
