using System;
using System.Globalization;
using System.IO;
using System.Text;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Recruit;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Text;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.World;
using Ekiden.Persistence.Container;
using Ekiden.Persistence.Sync;

namespace Ekiden.Verification
{
    /// <summary>T1 の主なベクタの値を『名前 値』の行で出す(09 §16.2)。<c>tools/compare_vectors.py</c> が同じ行を参照モデル(Python)から作って突き合わせる(浮動小数は桁を決めて書く)。
    /// 全ベクタの照合は dotnet test(文書の値と参照モデルの大量のケース)。この命令は、テストランナーのないビルド(Unity の実機・IL2CPP。T7)でも同じ値が出ることの確認に使う。</summary>
    public static class VectorsRunner
    {
        public static readonly double[] Q6Inputs = { 5e-07, 1e-07, 0.1 + 0.2, -0.0, 12.3456785, 1e-06, 0.0000015, 123456.7890125, -0.0000005, 0.9999995, 72.4999995, 0.123456789 };
        public static readonly long[] TimeDs = { 0, 9, 599, 600, 36000, 7561, 123456, 7577000 / 10 };
        public static readonly long[] DiffDds = { -35, -1, 0, 1, 12, 600, 3601 };

        private static TextWriter _w;
        private static void L(string k, string v) { _w.WriteLine(k + " " + v); }
        private static string F(double x, int digits) { return x.ToString("F" + digits, CultureInfo.InvariantCulture); }

        /// <summary>全ベクタの行を <paramref name="w"/> に書く(SimCli の `vectors` と、Unity の実機・IL2CPP の確認で共通)。</summary>
        public static void Run(TextWriter w)
        {
            _w = w;
            foreach (string s in new[] { "123456789", "EKSV", "" }) L("crc32[" + s + "]", Crc32.Compute(Encoding.ASCII.GetBytes(s)).ToString("x8"));
            for (int i = 0; i < Q6Inputs.Length; i++) L("q6[" + i + "]", Q6.Format(Q6.Round(Q6Inputs[i])));
            foreach (long ds in TimeDs) { L("fmt.time[" + ds + "]", Fmt.Time(ds)); L("fmt.total[" + ds + "]", Fmt.Total(ds)); }
            foreach (long d in DiffDds) L("fmt.diff[" + d + "]", Fmt.Diff(d));
            foreach (double km in new[] { 5.0, 10.5, 21.0975, 0.04 }) L("fmt.km[" + F(km, 4) + "]", Fmt.Km(km));
            foreach (long m in new long[] { 0, 999, 1500, 12345, 1234567 }) L("fmt.money[" + m + "]", Fmt.Money(m));
            L("fmt.year[3]", Fmt.Year(3)); L("fmt.year[12,5]", Fmt.Year(12, 5));
            foreach (int g in new[] { 1, 2, 3, 4 }) L("fmt.grade[" + g + "]", Fmt.Grade(g));
            foreach (double p in new[] { 0.0, 0.5, 0.123, 0.9995, 1.0 }) L("fmt.pct[" + F(p, 4) + "]", Fmt.Pct(p));
            foreach (int n in new[] { 1, 2, 11, 21, 36 }) { L("fmt.rank[" + n + "]", Fmt.Rank(n)); L("fmt.rank[" + n + ",promoted]", Fmt.Rank(n, true)); }

            foreach (var (master, label, a, b) in new[] { (20260101UL, 3, 2030L, 1007L), (1UL, 1, 0L, 0L), (ulong.MaxValue, 8, 4000000000L, 59L), (123456789012345UL, 4, 31L, 9999L) })
                L("rng.derive[" + master + "," + label + "," + a + "," + b + "]", Mix.Derive(master, label, a, b).ToString("x16"));
            foreach (ulong seed in new[] { 12345UL, 0UL, 0xFFFFFFFFFFFFFFFFUL })
            {
                var g = new Pcg32(seed);
                string s = "";
                for (int i = 0; i < 3; i++) s += g.U32() + " ";
                for (int i = 0; i < 2; i++) s += F(g.Uniform(), 12) + " ";
                for (int i = 0; i < 2; i++) s += F(g.Normal(0.0, 1.0), 9) + " ";
                s += g.RandInt(3, 9);
                L("rng.pcg[" + seed + "]", s);
            }

            foreach (GrowthKind gk in new[] { GrowthKind.Early, GrowthKind.Normal, GrowthKind.Late })
                for (int y = 0; y <= 4; y++)
                    foreach (double lv in new[] { 1.0, 3.5, 6.0 }) L("world.prog_base[" + gk + "," + y + "," + F(lv, 1) + "]", F(WorldMath.ProgBase(gk, y, lv), 9));
            foreach (double gap in new[] { 0.0, 10.0, 24.0, 40.0, 60.0 }) L("world.gap_adj[" + F(gap, 1) + "]", F(WorldMath.GapAdj(gap), 9));

            foreach (var (master, year, kind) in new[] { (20260101UL, 3, TicketKind.Rookie), (20260101UL, 12, TicketKind.Intl), (99UL, 1, TicketKind.Rookie), (987654321UL, 40, TicketKind.Intl) })
            {
                SpecialRecruit r = SpecialRecruitGenerator.Generate(new KeyedRngFactory(master), year, kind);
                L("special[" + master + "," + year + "," + kind + "]", F(r.A0, 9) + " " + F(r.Gap, 9) + " " + r.LookSeed);
            }

            var recs = new (SyncRecord l, SyncRecord c)[]
            {
                (null, null), (null, new SyncRecord { LineageId = "A", Counter = 3 }), (new SyncRecord { LineageId = "A", Counter = 3 }, null),
                (new SyncRecord { LineageId = "A", Counter = 5, LastSynced = 3 }, new SyncRecord { LineageId = "A", Counter = 3 }),
                (new SyncRecord { LineageId = "A", Counter = 3, LastSynced = 3 }, new SyncRecord { LineageId = "A", Counter = 6 }),
                (new SyncRecord { LineageId = "A", Counter = 5, LastSynced = 3 }, new SyncRecord { LineageId = "A", Counter = 6 }),
                (new SyncRecord { LineageId = "A", Counter = 3, LastSynced = 3, IsTombstone = true }, new SyncRecord { LineageId = "B", Counter = 1 }),
                (new SyncRecord { LineageId = "A", Counter = 3, LastSynced = 3 }, new SyncRecord { LineageId = "B", Counter = 1, IsTombstone = true }),
                (new SyncRecord { LineageId = "A", Counter = 3, LastSynced = 3 }, new SyncRecord { LineageId = "B", Counter = 1 }),
                (new SyncRecord { LineageId = "A", Counter = 4, LastSynced = 4 }, new SyncRecord { LineageId = "A", Counter = 4 }),
            };
            for (int i = 0; i < recs.Length; i++) L("sync.decide[" + i + "]", SyncDecider.Decide(recs[i].l, recs[i].c).ToString().ToLowerInvariant());
        }
    }
}
