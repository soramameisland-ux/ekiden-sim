using System;
using System.Collections.Generic;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;

namespace Ekiden.Domain.World
{
    /// <summary>自校(プレイヤー)が候補に出す提示スコアの追加分(好感度・奨学金・OB・乱数を除く部分は <see cref="WorldMath.OfferBase"/>)。</summary>
    public delegate double PlayerOffer(WorldRunner candidate);

    /// <summary>自校が狙った候補が入れなかった理由(08a SC-14『惜しかった候補』)。</summary>
    public enum MissReason { Quota, Scholarship, Outbid }

    /// <summary>自校が狙ったのに、自校に入らなかった候補 1 人。</summary>
    public sealed class AllocationMiss
    {
        public WorldRunner Candidate;
        public MissReason Reason;
        public int WinnerSchoolId = -1;      // 入った学校(Outbid のとき。それ以外も入った学校がわかれば入れる。どこにも入らなければ −1)
        public double Margin;                // Outbid: 入った学校の提示スコア − 自校の提示スコア(小さいほど惜しい)
    }

    /// <summary>勧誘の確定(3 月の割り当て。05 §8)と他校の狙い撃ち(§9.2)。</summary>
    public static class Allocator
    {
        /// <summary>学校 s の来年度の名簿の力の 10 番目(P10)。名簿が 10 人に満たなければ最後の人、空なら 40。</summary>
        public static double P10(School s, double lv)
        {
            var ps = new List<double>();
            foreach (WorldRunner r in s.Roster) ps.Add(WorldMath.SeasonPower(r, lv));
            ps.Sort((a, b) => b.CompareTo(a));
            return ps.Count >= 10 ? ps[9] : (ps.Count > 0 ? ps[ps.Count - 1] : 40.0);
        }

        /// <summary>他校の狙い撃ち: 奨学金枠の 1.5 倍の人数の上位級候補に、重み exp((V + N(0,6) − V_TOP)/6) で好感度を積む。戻り値: 学校 ID → 狙った候補の番号の集合。</summary>
        public static Dictionary<int, HashSet<int>> RivalTargets(IReadOnlyList<School> schools, IReadOnlyList<WorldRunner> pool, IRngFactory rngs, int year)
        {
            var top = new List<WorldRunner>();
            foreach (WorldRunner r in pool) if (r.V >= WorldK.K_V_TOP) top.Add(r);
            var res = new Dictionary<int, HashSet<int>>();
            foreach (School s in schools)
            {
                if (s.IsPlayer || top.Count == 0) continue;
                Pcg32 g = rngs.Stream(RngRegistry.LabelScout, year, 100 + s.Id);
                int nT = (int)Math.Round(WorldK.RivalTargetMult * s.ScholSlots(), MidpointRounding.ToEven);
                var cum = new double[top.Count];
                double acc = 0;
                for (int i = 0; i < top.Count; i++)
                {
                    acc += Math.Exp((top[i].V + g.Normal(0, WorldK.RivalPercSd) - WorldK.K_V_TOP) / WorldK.RivalTargetTemp);
                    cum[i] = acc;
                }
                var picked = new HashSet<int>();
                for (int k = 0; k < nT * 3; k++)
                {
                    if (picked.Count >= nT) break;
                    double x = g.Uniform() * acc;
                    int idx = 0;
                    while (idx < cum.Length - 1 && !(x < cum[idx])) idx++;
                    picked.Add(top[idx].Index);
                }
                res[s.Id] = picked;
            }
            return res;
        }

        /// <summary>候補を V の高い順に処理し、各候補は空き定員(上位級は奨学金枠も)のある学校のうち提示スコア最大の学校に入る。戻り値: 学校 ID → 入学する候補。</summary>
        public static Dictionary<int, List<WorldRunner>> Allocate(IReadOnlyList<School> schools, IReadOnlyList<WorldRunner> pool, IRngFactory rngs, int year,
                                                                  School player, PlayerOffer playerOffer, double? playerP10 = null, int? playerRosterCount = null,
                                                                  Func<WorldRunner, bool> playerTarget = null, List<AllocationMiss> misses = null)
        {
            var p10 = new Dictionary<int, double>();
            var slots = new Dictionary<int, int>();
            var scholLeft = new Dictionary<int, int>();
            var intake = new Dictionary<int, List<WorldRunner>>();
            foreach (School s in schools)
            {
                bool own = player != null && ReferenceEquals(s, player);             // 実体の選手を持つ自校は、抽象の名簿が空なので、外から渡された値を使う
                p10[s.Id] = own && playerP10.HasValue ? playerP10.Value : P10(s, s.Lv());
                slots[s.Id] = Math.Max(0, Math.Min(s.Quota(), WorldK.RosterMax - (own && playerRosterCount.HasValue ? playerRosterCount.Value : s.Roster.Count)));
                scholLeft[s.Id] = s.ScholSlots();
                intake[s.Id] = new List<WorldRunner>();
            }
            double chase = player != null ? WorldK.K_RIVAL_CHASE * School.Clamp((player.Prestige - WorldK.ChaseFrom) / WorldK.ChaseSpan, 0.0, 1.0) : 0.0;
            Dictionary<int, HashSet<int>> rivalInt = RivalTargets(schools, pool, rngs, year);
            var order = new List<WorldRunner>(pool);
            RaceSimulator.StableSort(order, (a, b) => b.V.CompareTo(a.V));
            foreach (WorldRunner r in order)
            {
                School best = null;
                double bestS = -1e9, playerS = double.NaN;
                bool topTier = r.V >= WorldK.K_V_TOP;
                foreach (School s in schools)
                {
                    if (slots[s.Id] <= 0) continue;
                    if (topTier && scholLeft[s.Id] <= 0) continue;
                    double sc = WorldMath.OfferBase(s, r, p10[s.Id]);
                    Pcg32 g = rngs.Stream(RngRegistry.LabelScout, year, 10000 + (long)r.Index * 64 + s.Id);
                    double uBase = g.Uniform();
                    double noise = g.Normal(0, s.IsPlayer ? WorldK.PlayerNoise : WorldK.RivalNoise);
                    if (s.IsPlayer)
                    {
                        sc += (playerOffer != null ? playerOffer(r) : 0.0) + noise;
                        playerS = sc;
                    }
                    else
                    {
                        bool hot = rivalInt.TryGetValue(s.Id, out HashSet<int> set) && set.Contains(r.Index);
                        sc += (hot ? WorldK.RivalTargetInterest + chase : uBase * WorldK.RivalBaseInterest) + noise;
                    }
                    if (sc > bestS) { best = s; bestS = sc; }
                }
                if (misses != null && player != null && playerTarget != null && !ReferenceEquals(best, player) && playerTarget(r))         // 自校が狙ったのに入らない: 理由は処理の時点の空きで決まる
                {
                    var miss = new AllocationMiss { Candidate = r, WinnerSchoolId = best != null ? best.Id : -1 };
                    if (slots[player.Id] <= 0) miss.Reason = MissReason.Quota;
                    else if (topTier && scholLeft[player.Id] <= 0) miss.Reason = MissReason.Scholarship;
                    else { miss.Reason = MissReason.Outbid; miss.Margin = bestS - playerS; }
                    misses.Add(miss);
                }
                if (best == null) continue;
                slots[best.Id] -= 1;
                if (topTier) scholLeft[best.Id] -= 1;
                intake[best.Id].Add(r);
            }
            return intake;
        }
    }
}
