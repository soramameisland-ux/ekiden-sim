using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Tickets;
using Ekiden.Persistence.Store;
using NUnit.Framework;

namespace Ekiden.Persistence.Tests
{
    /// <summary>購入状態の判定(07 §8.2・TV-3)と時計対策(09 §10・TV-10)。</summary>
    public class EntitlementTests
    {
        [TestCase(StoreAnswer.Owned, false, false, 0.0, 0.0, 2, "オンラインで所有")]
        [TestCase(StoreAnswer.Unknown, true, true, 0.0, 10.0, 2, "オフライン・10日前に確認")]
        [TestCase(StoreAnswer.Unknown, true, true, 0.0, 40.0, 0, "オフライン・40日前に確認")]
        [TestCase(StoreAnswer.Unknown, false, false, 0.0, 5.0, 0, "オフライン・確認なし")]
        [TestCase(StoreAnswer.NotOwned, true, true, 0.0, 5.0, 0, "返金")]
        public void TV3_EntitlementAndGrant(StoreAnswer prov, bool hasCache, bool cacheOwned, double cacheDay, double nowDay, int grant, string label)
        {
            bool? state = Entitlement.State(prov, hasCache, cacheOwned, cacheDay, nowDay);
            var t = new TicketState();
            Assert.AreEqual(grant, t.TryPlanGrant(1, state), label);
        }


        [Test]
        public void TV10_ClockGuard_13Cases()
        {
            const double T = 1_800_000_000, D = 86400;
            var c = new EntitlementCache { Owned = true, VerifiedAt = T, VerifiedRun = 0 };
            var rows = new (string, StoreAnswer, EntitlementCache, double, double, double, bool?)[]
            {
                ("オンラインで所有", StoreAnswer.Owned, null, T, T, 0, true),
                ("返金", StoreAnswer.NotOwned, c, T + 5 * D, T + 5 * D, 0, false),
                ("オフライン・10日前に確認", StoreAnswer.Unknown, c, T + 10 * D, T + 10 * D, 20 * 3600, true),
                ("オフライン・30日ちょうど", StoreAnswer.Unknown, c, T + 30 * D, T + 30 * D, 60 * 3600, true),
                ("オフライン・30日と1秒", StoreAnswer.Unknown, c, T + 30 * D + 1, T + 30 * D + 1, 60 * 3600, null),
                ("オフライン・確認なし", StoreAnswer.Unknown, null, T, T, 0, null),
                ("オフライン・以前に not_owned", StoreAnswer.Unknown, new EntitlementCache { Owned = false, VerifiedAt = T }, T + D, T + D, 0, null),
                ("時計を 5 日戻した", StoreAnswer.Unknown, c, T + 5 * D, T + 10 * D, 20 * 3600, null),
                ("時計を 24 時間ちょうど戻した", StoreAnswer.Unknown, c, T + 9 * D, T + 10 * D, 20 * 3600, true),
                ("時計を 24 時間と1秒戻した", StoreAnswer.Unknown, c, T + 9 * D - 1, T + 10 * D, 20 * 3600, null),
                ("時計を止めたまま 120 時間を超えて遊んだ", StoreAnswer.Unknown, c, T + 5 * D, T + 5 * D, 120 * 3600 + 1, null),
                ("時計を止めたまま 120 時間ちょうど", StoreAnswer.Unknown, c, T + 5 * D, T + 5 * D, 120 * 3600, true),
                ("巻き戻したまま、オンラインで所有を確認", StoreAnswer.Owned, c, T + 5 * D, T + 10 * D, 20 * 3600, true),
            };
            foreach (var r in rows)
            {
                EntitlementResult res = Entitlement.Guarded(r.Item2, r.Item3, r.Item4, r.Item5, r.Item6);
                Assert.AreEqual(r.Item7, res.State, r.Item1);
            }
            EntitlementResult back = Entitlement.Guarded(StoreAnswer.Owned, c, T + 5 * D, T + 10 * D, 20 * 3600);
            Assert.AreEqual(T + 5 * D, back.Hw, "確認できたら高水位を取り直す");
            Assert.AreEqual(20 * 3600, back.Cache.VerifiedRun);
        }

        [Test]
        public void ClockGuard_Property_HonestUsersAreNeverCut_AndTamperingNeverExtends()
        {
            const double D = 86400;
            var rngs = new KeyedRngFactory(21UL);
            int falseNeg = 0;
            for (int k = 0; k < 2000; k++)
            {
                Pcg32 g = rngs.Stream(RngRegistry.LabelWorldGen, 5000, k);
                double T = 1_800_000_000 + (int)(g.Uniform() * 1e6);
                EntitlementResult r0 = Entitlement.Guarded(StoreAnswer.Owned, null, T, T, 0);
                EntitlementCache cache = r0.Cache;
                double hw = r0.Hw, run = 0, t = 0;
                double drift = (g.Uniform() * 24 - 12) * 3600;
                double horizon = (1 + g.Uniform() * 28) * D;
                while (t < horizon)
                {
                    double dt = (0.2 + g.Uniform() * 1.8) * D;
                    t += dt;
                    if (t > horizon) break;                      // 確認から 1〜29 日の間だけを調べる(30 日の猶予の内側)
                    run += Math.Min(dt, g.Uniform() * 4 * 3600);
                    drift = Math.Max(-12 * 3600, Math.Min(12 * 3600, drift + (g.Uniform() * 6 - 3) * 3600));
                    if (run > 100 * 3600) break;
                    EntitlementResult r = Entitlement.Guarded(StoreAnswer.Unknown, cache, T + t + drift, hw, run);
                    hw = r.Hw;
                    cache = r.Cache;
                    if (r.State != true) { falseNeg++; break; }
                }
            }
            Assert.AreEqual(0, falseNeg, "正直な利用者(時計の揺れ ±12 時間・確認から 1〜29 日・動かした時間 100 時間以内)は、猶予の間に『未確認』にならない");

            int attackOk = 0, valid = 0;
            for (int k = 0; k < 2000; k++)
            {
                Pcg32 g = rngs.Stream(RngRegistry.LabelWorldGen, 5001, k);
                const double T = 1_800_000_000;
                var cache = new EntitlementCache { Owned = true, VerifiedAt = T, VerifiedRun = 0 };
                double hw = T, run = 0, t = 0;
                int n = 5 + (int)(g.Uniform() * 35);
                for (int i = 0; i < n; i++)
                {
                    t += (0.5 + g.Uniform() * 19.5) * D;
                    run += (0.5 + g.Uniform() * 5.5) * 3600;
                    double shown = g.Uniform() < 0.7 ? T + new[] { 0, 3, 12, 25 }[(int)(g.Uniform() * 4)] * D : T + t;
                    EntitlementResult r = Entitlement.Guarded(StoreAnswer.Unknown, cache, shown, hw, run);
                    hw = r.Hw;
                    if (r.State == true)
                    {
                        valid++;
                        if (run > SaveConstants.K_GRACE_RUN_H * 3600.0) attackOk++;
                    }
                }
            }
            Assert.AreEqual(0, attackOk, "時計を操作しても、動かした時間の上限を超えて有効にならない");
            Assert.Greater(valid, 1000);
        }


        [Test]
        public void Golden_MatchesReference()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Golden", "special_cases.json.gz");
            JsonElement root;
            using (var fs = File.OpenRead(path)) using (var gz = new GZipStream(fs, CompressionMode.Decompress)) using (var doc = JsonDocument.Parse(gz)) root = doc.RootElement.Clone();
            int n = 0, trues = 0, nulls = 0, falses = 0;
            foreach (JsonElement c in root.GetProperty("entitle").EnumerateArray())
            {
                StoreAnswer prov = c.GetProperty("provider").GetString() == "owned" ? StoreAnswer.Owned : c.GetProperty("provider").GetString() == "not_owned" ? StoreAnswer.NotOwned : StoreAnswer.Unknown;
                EntitlementCache cache = null;
                JsonElement cj = c.GetProperty("cache");
                if (cj.ValueKind == JsonValueKind.Object)
                    cache = new EntitlementCache { Owned = cj.GetProperty("owned").GetBoolean(), VerifiedAt = cj.GetProperty("verified_at").GetDouble(), VerifiedRun = cj.GetProperty("verified_run").GetDouble() };
                EntitlementResult r = Entitlement.Guarded(prov, cache, c.GetProperty("now").GetDouble(), c.GetProperty("hw").GetDouble(), c.GetProperty("run").GetDouble());
                string tag = "case " + n;
                JsonElement st = c.GetProperty("state");
                Assert.AreEqual(st.ValueKind == JsonValueKind.Null ? (bool?)null : st.GetBoolean(), r.State, tag + " state");
                Assert.AreEqual(c.GetProperty("hw_out").GetDouble(), r.Hw, tag + " hw");
                JsonElement co = c.GetProperty("cache_out");
                if (co.ValueKind == JsonValueKind.Null) Assert.IsNull(r.Cache, tag);
                else
                {
                    Assert.AreEqual(co.GetProperty("owned").GetBoolean(), r.Cache.Owned, tag);
                    Assert.AreEqual(co.GetProperty("verified_at").GetDouble(), r.Cache.VerifiedAt, tag);
                    Assert.AreEqual(co.GetProperty("verified_run").GetDouble(), r.Cache.VerifiedRun, tag);
                }
                // 07 §8.2 の判定(日数)
                JsonElement s07 = c.GetProperty("s07");
                bool hasCache = c.GetProperty("cache_day").ValueKind == JsonValueKind.Number;
                bool? got07 = Entitlement.State(prov, hasCache, hasCache && cache.Owned, hasCache ? c.GetProperty("cache_day").GetDouble() : 0.0, c.GetProperty("now_day").GetDouble());
                Assert.AreEqual(s07.ValueKind == JsonValueKind.Null ? (bool?)null : s07.GetBoolean(), got07, tag + " 07 §8.2");
                if (r.State == true) trues++; else if (r.State == false) falses++; else nulls++;
                n++;
            }
            Assert.AreEqual(4000, n);
            Assert.Greater(trues, 300);
            Assert.Greater(nulls, 300);
            Assert.Greater(falses, 300);
            TestContext.Out.WriteLine("購入状態の判定: " + n + " 件が参照モデルと一致(有効 " + trues + " / 未確認 " + nulls + " / 返金 " + falses + ")");
        }
    }
}
