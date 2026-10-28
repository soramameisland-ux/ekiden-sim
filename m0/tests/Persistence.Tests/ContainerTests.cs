using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Ekiden.Persistence.Container;
using Ekiden.Persistence.Json;
using Ekiden.Platform;
using NUnit.Framework;

namespace Ekiden.Persistence.Tests
{
    /// <summary>コンテナ形式 EKSV と A / B の世代(09 §4)・09 TV-1 / TV-2 / TV-3 の性質。</summary>
    public class ContainerTests
    {
        private static JsonObject Header(long counter, int slot = 1, string kind = "auto", string lineage = "3f2c9a6e-0000-4000-8000-000000000001")
        {
            return new HeaderBuilder { Kind = kind, Slot = slot, LineageId = lineage, SaveCounter = counter, AppVersion = "1.0.0", MasterHash = "a1b2c3d4", SavedAt = 1800000000 + counter, Year = 2, MonthIndex = 6, SchoolName = "青嵐大学", ScenarioId = "sc.mid", PlaySeconds = 1234 + counter }.Build();
        }

        private static JsonValue SmallPayload(int n = 20)
        {
            var o = new JsonObject();
            o["schema_version"] = new JsonNumber(1L);
            var runners = new JsonObject();
            for (int i = 1; i <= n; i++)
            {
                var r = new JsonObject();
                r["id"] = new JsonNumber((long)i);
                r["name"] = new JsonString("選手" + i);
                r["stats"] = new JsonArray(Enumerable.Range(0, 10).Select(k => (JsonValue)new JsonNumber((long)(30 + (i * 7 + k * 3) % 50))));
                r["prog"] = new JsonNumber(0.125 * (i % 7));
                runners[i.ToString()] = r;
            }
            o["runners"] = runners;
            return o;
        }

        [Test]
        public void TV1_RoundTrip_AndHeaderOnly()
        {
            JsonObject hdr = Header(5);
            JsonValue pay = SmallPayload();
            byte[] b = Eksv.Encode(hdr, pay);
            JsonValue p2;
            HeaderInfo h = Eksv.Decode(b, out p2);
            Assert.IsTrue(hdr.StructEquals(h.Header));
            Assert.IsTrue(pay.StructEquals(p2));
            HeaderInfo h2 = Eksv.DecodeHeader(b);
            Assert.IsTrue(hdr.StructEquals(h2.Header));
            Assert.AreEqual(5, h2.SaveCounter);
            Assert.AreEqual(h.PayloadOffset, h2.PayloadOffset);
            CollectionAssert.AreEqual(new byte[] { 0x45, 0x4B, 0x53, 0x56 }, b.Take(4).ToArray());
            Assert.AreEqual(1, b[4]);
            Assert.AreEqual(0, b[5]);
            Assert.AreEqual(1, b[6], "flags bit0 = gzip");
            Assert.Less(h2.PayloadOffset, 600, "ヘッダ部は約 260 バイト(§4.1)");
        }

        [Test]
        public void FastCompression_IsTheSameFormat_AndSmallerWorkThanDefault()
        {
            // 中断セーブは同期で書くので最速の圧縮(zlib level 1)。形式は同じで、通常の読み込みで読め、CRC も通る
            JsonValue pay = SmallPayload(400);
            byte[] normal = Eksv.Encode(Header(5), pay);
            byte[] fast = Eksv.Encode(Header(5), pay, fast: true);
            JsonValue p1, p2;
            Eksv.Decode(normal, out p1);
            HeaderInfo h = Eksv.Decode(fast, out p2);
            Assert.IsTrue(p1.StructEquals(p2));
            Assert.IsTrue(pay.StructEquals(p2));
            Assert.AreEqual(5, h.SaveCounter);
            Assert.GreaterOrEqual(fast.Length, normal.Length * 0.9, "最速の圧縮は同じかやや大きい");
            Assert.AreEqual(1, fast[6], "flags bit0 = gzip のまま");
        }

        [Test]
        public void SuspendSave_UsesTheFastPath_AndLoadsBack()
        {
            var fs = new MemoryFileStore();
            var store = new SlotStore(fs);
            JsonValue pay = SmallPayload(300);
            store.WriteSuspend(Header(9, kind: "suspend"), pay);
            JsonValue back;
            HeaderInfo h = Eksv.Decode(fs.ReadAllBytes(SlotStore.SuspendPath), out back);
            Assert.AreEqual("suspend", h.Header.Get("kind") is JsonString k ? k.Value : null);
            Assert.IsTrue(pay.StructEquals(back));
        }

        [Test]
        public void TV1_MinimalContainer_IsSmall()
        {
            var empty = new JsonObject();
            byte[] b = Eksv.Encode(Header(1), empty);
            Assert.Less(b.Length, 400, "最小のコンテナは約 300 バイト(gzip の実装で数バイトずれる)");
            JsonValue p;
            Eksv.Decode(b, out p);
            Assert.AreEqual("{}", p.ToJson());
        }

        [Test]
        public void Tamper_AllTruncations_AreDetected()
        {
            byte[] b = Eksv.Encode(Header(5), SmallPayload());
            for (int n = 0; n < b.Length; n++)
            {
                HeaderInfo h;
                JsonValue p;
                string code;
                Assert.IsFalse(Eksv.TryDecode(b.Take(n).ToArray(), out h, out p, out code), "長さ " + n);
            }
        }

        [Test]
        public void Tamper_BitFlips_AndRewrites_AreDetected()
        {
            byte[] b = Eksv.Encode(Header(5), SmallPayload());
            var rnd = new Random(1);
            for (int i = 0; i < 20000; i++)
            {
                byte[] c = (byte[])b.Clone();
                c[rnd.Next(c.Length)] ^= (byte)(1 << rnd.Next(8));
                HeaderInfo h;
                JsonValue p;
                string code;
                Assert.IsFalse(Eksv.TryDecode(c, out h, out p, out code), "ビット反転 " + i);
            }
            for (int i = 0; i < 20000; i++)
            {
                byte[] c = (byte[])b.Clone();
                for (int k = 0; k < 8; k++) c[rnd.Next(c.Length)] = (byte)rnd.Next(256);
                if (c.SequenceEqual(b)) continue;
                HeaderInfo h;
                JsonValue p;
                string code;
                Assert.IsFalse(Eksv.TryDecode(c, out h, out p, out code), "書き換え " + i);
            }
        }

        private static string CodeOf(byte[] b)
        {
            HeaderInfo h;
            JsonValue p;
            string code;
            return Eksv.TryDecode(b, out h, out p, out code) ? "ok" : code;
        }

        [Test]
        public void Rejects_NewContainerVersion_AndLimits()
        {
            JsonObject hdr = Header(5);
            JsonValue pay = SmallPayload(3);
            Assert.AreEqual("container_too_new", CodeOf(Eksv.Encode(hdr, pay, containerVer: 2)));
            Assert.AreEqual("ok", CodeOf(Eksv.Encode(hdr, pay, compress: false)));
            byte[] good = Eksv.Encode(hdr, pay);
            byte[] longer = good.Concat(new byte[] { 0 }).ToArray();
            Assert.AreEqual("length", CodeOf(longer));
            byte[] hl = new byte[] { 0x45, 0x4B, 0x53, 0x56, 1, 0, 1, 0, 0x01, 0x00, 0x01, 0x00 };           // header_len = K_HEADER_MAX + 1
            Assert.AreEqual("header_len", CodeOf(hl.Concat(new byte[64]).ToArray()));
            byte[] bad = (byte[])good.Clone();
            bad[0] = 0x58;
            Assert.AreEqual("magic", CodeOf(bad));
            Assert.AreEqual("truncated", CodeOf(new byte[10]));
        }

        [Test]
        public void Header_MustBeAnObject_AndValidUtf8()
        {
            foreach (string body in new[] { "[1,2,3]", "\"x\"", "7", "{\"kind\":" })
            {
                byte[] hdr = System.Text.Encoding.UTF8.GetBytes(body);
                using (var o = new MemoryStream())
                {
                    o.Write(new byte[] { 0x45, 0x4B, 0x53, 0x56, 1, 0, 1, 0 }, 0, 8);
                    o.Write(BitConverter.GetBytes((uint)hdr.Length), 0, 4);
                    o.Write(hdr, 0, hdr.Length);
                    o.Write(BitConverter.GetBytes(Crc32.Compute(o.ToArray())), 0, 4);
                    o.Write(BitConverter.GetBytes(0u), 0, 4);
                    o.Write(BitConverter.GetBytes(Crc32.Compute(o.ToArray())), 0, 4);
                    Assert.AreEqual("header_json", CodeOf(o.ToArray()), body);
                }
            }
        }

        [Test]
        public void GzipBomb_IsStopped_AtTheDecompressedLimit()
        {
            byte[] zeros = new byte[SaveConstants.K_PAYLOAD_MAX + 4096];
            byte[] bomb;
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true)) gz.Write(zeros, 0, zeros.Length);
                bomb = ms.ToArray();
            }
            Assert.Less(bomb.Length, 1 << 20, "圧縮後は小さい");
            byte[] hdr = Header(5).ToUtf8();
            using (var o = new MemoryStream())
            {
                o.Write(new byte[] { 0x45, 0x4B, 0x53, 0x56, 1, 0, 1, 0 }, 0, 8);
                o.Write(BitConverter.GetBytes((uint)hdr.Length), 0, 4);
                o.Write(hdr, 0, hdr.Length);
                o.Write(BitConverter.GetBytes(Crc32.Compute(o.ToArray())), 0, 4);
                o.Write(BitConverter.GetBytes((uint)bomb.Length), 0, 4);
                o.Write(bomb, 0, bomb.Length);
                o.Write(BitConverter.GetBytes(Crc32.Compute(o.ToArray())), 0, 4);
                Assert.AreEqual("payload_len", CodeOf(o.ToArray()));
            }
        }

        // ---- A / B の世代・電源断(TV-2) ----

        private static MemoryFileStore ThreeWrites(out SlotStore store)
        {
            var fs = new MemoryFileStore();
            store = new SlotStore(fs);
            for (int c = 1; c <= 3; c++) store.WriteAuto(1, Header(c), SmallPayload(5));
            return fs;
        }

        [Test]
        public void TV2_PowerCut_AtEveryStep_LeavesOneReadableGeneration()
        {
            SlotStore store;
            MemoryFileStore baseFs = ThreeWrites(out store);
            Assert.AreEqual(3, store.LoadAuto(1).Header.SaveCounter);
            int cases = 0;
            for (int crash = 1; crash <= MemoryFileStore.StepsPerWrite + 1; crash++)
            {
                MemoryFileStore fs = baseFs.Clone();
                var st = new SlotStore(fs);
                fs.CrashAt(crash);
                try { st.WriteAuto(1, Header(4), SmallPayload(5)); }
                catch (PowerCutException) { }
                fs.ClearCrash();
                long c = st.LoadAuto(1).Header.SaveCounter;
                Assert.IsTrue(c == 3 || c == 4, "手順 " + crash + ": 読めた保存番号 " + c);
                cases++;
            }
            byte[] full = Eksv.Encode(Header(4), SmallPayload(5));
            byte[] existing = baseFs.ReadAllBytes(SlotStore.AutoPath(1, 'a'));
            for (int n = 0; n < existing.Length; n += 997)
            {
                MemoryFileStore fs = baseFs.Clone();
                var st = new SlotStore(fs);
                char target = st.PickTarget(1);
                fs.PutRaw(SlotStore.AutoPath(1, target), full.Take(n).ToArray());          // 非原子な置き換えが途中で切れた
                long c = st.LoadAuto(1).Header.SaveCounter;
                Assert.IsTrue(c >= 2 && c <= 4, "切れた長さ " + n);
                cases++;
            }
            Assert.GreaterOrEqual(cases, 6 + existing.Length / 997);
        }

        [Test]
        public void TV2_FiveWrites_LoadLatest_GenerationA()
        {
            var fs = new MemoryFileStore();
            var store = new SlotStore(fs);
            for (int c = 1; c <= 5; c++) store.WriteAuto(1, Header(c), SmallPayload(5));
            SlotLoad l = store.LoadAuto(1);
            Assert.AreEqual(5, l.Header.SaveCounter);
            Assert.AreEqual('a', l.Generation);
            Assert.AreEqual(0, l.BrokenGenerations);
        }

        [Test]
        public void WriteFailure_LeavesExistingGenerations_Untouched_AndNoTempFile()
        {
            var fs = new MemoryFileStore();
            var store = new SlotStore(fs);
            store.WriteAuto(1, Header(1), SmallPayload(5));
            string before = string.Join(",", fs.List("").ToArray());
            fs.Free = 0;                                                             // 空き容量が足りない
            Assert.Throws<IOException>(() => store.WriteAuto(1, Header(2), SmallPayload(5)));
            Assert.AreEqual(before, string.Join(",", fs.List("").ToArray()));
            Assert.AreEqual(1, store.LoadAuto(1).Header.SaveCounter);
            fs.Free = long.MaxValue;
            Assert.Throws<IOException>(() => fs.WriteAtomic("x", new byte[] { 1 }, b => false), "検証に失敗したら置き換えない");
            Assert.IsFalse(fs.Exists("x"));
            Assert.IsFalse(fs.Exists("x.tmp"));
        }

        [Test]
        public void Counter_NeverGoesBack_AcrossManualAndAuto()
        {
            var fs = new MemoryFileStore();
            var store = new SlotStore(fs);
            Assert.AreEqual(1, store.NextCounter(1));
            store.WriteAuto(1, Header(1), SmallPayload(3));
            store.WriteManual(1, Header(store.NextCounter(1), kind: "manual"), SmallPayload(3));
            Assert.AreEqual(3, store.NextCounter(1), "{a, b, m} の最大 + 1");
            fs.PutRaw(SlotStore.AutoPath(1, 'b'), new byte[] { 1, 2, 3 });
            Assert.AreEqual(3, store.NextCounter(1), "壊れた世代は数えない");
            Assert.AreEqual('b', store.PickTarget(1), "壊れている世代を先に使う");
        }

        [Test]
        public void Recovery_Order_FollowsSection45()
        {
            var fs = new MemoryFileStore();
            var store = new SlotStore(fs);
            store.WriteAuto(1, Header(1), SmallPayload(3));
            store.WriteAuto(1, Header(2), SmallPayload(3));
            store.WriteManual(1, Header(3, kind: "manual"), SmallPayload(3));
            SlotRecovery r = store.Recover(1);
            Assert.AreEqual(RecoverySource.Auto, r.Source);
            Assert.AreEqual(2, r.Header.SaveCounter);
            Assert.IsFalse(r.NewestWasBroken);
            fs.PutRaw(SlotStore.AutoPath(1, 'b'), new byte[] { 9, 9 });                 // 最新(b)が壊れた
            r = store.Recover(1);
            Assert.AreEqual(1, r.Header.SaveCounter);
            Assert.IsTrue(r.NewestWasBroken, "『最新のセーブが壊れていたため、ひとつ前から再開しました』");
            fs.PutRaw(SlotStore.AutoPath(1, 'a'), new byte[] { 9 });
            Assert.IsNull(store.Recover(1, () => false), "手動セーブは確認がなければ使わない");
            r = store.Recover(1, () => true);
            Assert.AreEqual(RecoverySource.Manual, r.Source);
            fs.PutRaw(SlotStore.ManualPath(1), new byte[] { 9 });
            fs.PutRaw(SlotStore.PreMigratePath(1, 0), Eksv.Encode(Header(7), SmallPayload(3)));
            Assert.AreEqual(RecoverySource.PreMigrate, store.Recover(1).Source);
            fs.Delete(SlotStore.PreMigratePath(1, 0));
            fs.PutRaw(SlotStore.ConflictPath(1, 100), Eksv.Encode(new HeaderBuilder { LineageId = "x", SaveCounter = 8, SavedAt = 100 }.Build(), SmallPayload(3)));
            fs.PutRaw(SlotStore.ConflictPath(1, 200), Eksv.Encode(new HeaderBuilder { LineageId = "x", SaveCounter = 9, SavedAt = 200 }.Build(), SmallPayload(3)));
            r = store.Recover(1);
            Assert.AreEqual(RecoverySource.Conflict, r.Source);
            Assert.AreEqual(9, r.Header.SaveCounter, "saved_at の新しい順");
            Assert.IsTrue(fs.Exists(SlotStore.AutoPath(1, 'a')), "すべて失敗しても、ファイルは消さない");
        }

        [Test]
        public void Suspend_ResumeConditions_AndSlotDelete()
        {
            var fs = new MemoryFileStore();
            var store = new SlotStore(fs);
            store.WriteAuto(1, Header(4), SmallPayload(3));
            JsonObject sh = Header(0, kind: "suspend");
            sh["base_counter"] = new JsonNumber(4L);
            store.WriteSuspend(sh, SmallPayload(3));
            Assert.IsTrue(store.CanResume(1), "有効・lineage 一致・base_counter = 先頭の保存番号");
            store.WriteAuto(1, Header(5), SmallPayload(3));
            Assert.IsFalse(store.CanResume(1), "自動セーブが先に進んだら破棄");
            JsonObject sh2 = Header(0, kind: "suspend", lineage: "other");
            sh2["base_counter"] = new JsonNumber(5L);
            store.WriteSuspend(sh2, SmallPayload(3));
            Assert.IsFalse(store.CanResume(1), "別の歴史の中断セーブは使わない");
            store.DeleteSlot(1);
            Assert.IsFalse(fs.Exists(SlotStore.AutoPath(1, 'a')) || fs.Exists(SlotStore.AutoPath(1, 'b')) || fs.Exists(SlotStore.SuspendPath));
        }

        [Test]
        public void CleanTemp_RemovesLeftovers_AndNeverReadsThem()
        {
            var fs = new MemoryFileStore();
            var store = new SlotStore(fs);
            store.WriteAuto(1, Header(1), SmallPayload(3));
            fs.PutRaw("saves/slot1.b.eksv.tmp", Eksv.Encode(Header(99), SmallPayload(3)));
            Assert.AreEqual(1, store.LoadAuto(1).Header.SaveCounter, ".tmp は読み込みに使わない");
            Assert.AreEqual(1, store.CleanTemp());
            Assert.IsFalse(fs.Exists("saves/slot1.b.eksv.tmp"));
        }
    }
}
