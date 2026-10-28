using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Ekiden.Domain.Rng;
using Ekiden.Domain.World;
using Ekiden.Persistence.Container;
using Ekiden.Persistence.Json;
using Ekiden.Persistence.Snapshot;
using Ekiden.Persistence.Validation;
using Ekiden.Platform;

namespace Ekiden.SimCli
{
    /// <summary>保存まわりのファズ(09 §4・§7。T2)。
    ///  ① コンテナ: 切り詰め・ビット反転・バイトの書き換えを、見逃しなく断る(小さいコンテナと、世界の状態を入れた実物大のコンテナ)
    ///  ② A / B の世代: 書き込みのどの手順で電源が切れても、直前か直後の世代が読める
    ///  ③ 検証と修復: 正常なセーブは変わらない・壊したセーブは致命にするか、修復して不変条件を満たす(冪等)。</summary>
    internal static class FuzzSaveCmd
    {
        public static int Run(string[] args)
        {
            var pos = Common.Positional(args);
            int n = Common.IntArg(pos, 0, 20000);
            var sw = Stopwatch.StartNew();
            int bad = 0;
            Console.WriteLine("== 保存のファズ(回数 " + n + ")");
            bad += Containers(n);
            bad += PowerCuts();
            bad += Validation(Math.Max(300, n / 5));
            Console.WriteLine(bad == 0 ? "ファズ: 見逃し 0・読めなくなる例 0・不変条件の違反 0(所要 " + sw.Elapsed.TotalSeconds.ToString("F1") + " 秒)" : "ファズ: 問題 " + bad + " 件");
            return bad == 0 ? 0 : 1;
        }

        private static JsonObject Header(long counter, string kind = "auto")
        {
            return new HeaderBuilder { Kind = kind, Slot = 1, LineageId = "3f2c9a6e-0000-4000-8000-000000000001", SaveCounter = counter, AppVersion = "0.0.0", MasterHash = "a1b2c3d4", SavedAt = 1800000000L + counter, Year = 2, MonthIndex = 6, SchoolName = "青嵐大学", ScenarioId = "sc.mid", PlaySeconds = 1234 + counter }.Build();
        }

        private static JsonObject RealPayload(int years)
        {
            WorldSim w = WorldSim.Create(new KeyedRngFactory(Common.WorldSeed(7)), 15);
            PlayerAgent agent = w.StartPlayer(Difficulty.Mid);
            for (int y = 1; y <= years; y++) w.PlayYear(y, agent);
            return WorldSnapshot.Capture(w, agent, years + 1);
        }

        private static bool Detected(byte[] c)
        {
            return !Eksv.TryDecode(c, out HeaderInfo h, out JsonValue p, out string code);
        }

        private static int Containers(int n)
        {
            int missed = 0, total = 0;
            var rnd = new Random(1);
            var cases = new[] { (name: "小(約 3 KB)", payload: (JsonValue)Small(), full: true, count: n), (name: "実物大(世界の状態 36 校)", payload: (JsonValue)RealPayload(6), full: false, count: Math.Min(n, 1500)) };
            foreach (var c in cases)
            {
                byte[] b = Eksv.Encode(Header(5), c.payload);
                int m = 0, t = 0;
                IEnumerable<int> cuts = c.full ? Enumerable.Range(0, b.Length) : Enumerable.Range(0, 1500).Select(_ => rnd.Next(b.Length));
                foreach (int len in cuts) { t++; if (!Detected(b.Take(len).ToArray())) m++; }
                for (int i = 0; i < c.count; i++)
                {
                    byte[] f = (byte[])b.Clone();
                    f[rnd.Next(f.Length)] ^= (byte)(1 << rnd.Next(8));
                    t++;
                    if (!Detected(f)) m++;
                }
                for (int i = 0; i < c.count; i++)
                {
                    byte[] f = (byte[])b.Clone();
                    for (int k = 0; k < 8; k++) f[rnd.Next(f.Length)] = (byte)rnd.Next(256);
                    if (f.SequenceEqual(b)) continue;
                    t++;
                    if (!Detected(f)) m++;
                }
                Console.WriteLine("  " + Common.Mark(m == 0) + " コンテナ " + c.name + " " + (b.Length / 1024.0).ToString("F1") + " KB: 切り詰め・ビット反転・8 バイト書き換え " + t + " 回で見逃し " + m);
                missed += m;
                total += t;
            }
            return missed == 0 ? 0 : 1;
        }

        private static JsonObject Small()
        {
            var o = new JsonObject();
            o["schema_version"] = new JsonNumber(1L);
            var runners = new JsonObject();
            for (int i = 1; i <= 20; i++)
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

        private static int PowerCuts()
        {
            JsonObject payload = RealPayload(2);
            int cases = 0, lost = 0;
            for (int writes = 1; writes <= 4; writes++)
            {
                var fs = new MemoryFileStore();
                var store = new SlotStore(fs);
                for (int c = 1; c <= writes; c++) store.WriteAuto(1, Header(c), payload);
                for (int crash = 1; crash <= MemoryFileStore.StepsPerWrite + 1; crash++)
                {
                    MemoryFileStore f2 = fs.Clone();
                    var st = new SlotStore(f2);
                    f2.CrashAt(crash);
                    try { st.WriteAuto(1, Header(writes + 1), payload); }
                    catch (PowerCutException) { }
                    f2.ClearCrash();
                    cases++;
                    try
                    {
                        long c = st.LoadAuto(1).Header.SaveCounter;
                        if (c != writes && c != writes + 1) lost++;
                    }
                    catch (LoadException) { lost++; }
                }
            }
            Console.WriteLine("  " + Common.Mark(lost == 0) + " 電源断 " + cases + " 通り(書き込みの手順 × 既存の世代の数): 直前か直後の世代が読めず失った例 " + lost);
            return lost == 0 ? 0 : 1;
        }

        private static int Validation(int n)
        {
            var rng = new Rnd(5);
            int cleanViol = 0;
            for (int i = 0; i < 300; i++)
            {
                JsonObject s = MiniSave.Build(rng);
                ValidationResult v = SaveValidator.ValidateAndRepair(s);
                if (v.Fatal != null || v.Fixes.Count > 0 || SaveInvariants.Check(s).Count > 0 || !v.Repaired.StructEquals(s)) cleanViol++;
            }
            int left = 0, idem = 0, fatalMiss = 0, fatalFalse = 0, fatalHit = 0, repCases = 0;
            var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++)
            {
                var used = new List<string>();
                JsonObject s = MiniSave.Corrupt(MiniSave.Build(rng), rng, used);
                bool wantFatal = used.Any(o => MiniSave.FatalOps.Contains(o));
                ValidationResult r = SaveValidator.ValidateAndRepair(s);
                if (r.Repaired == null)
                {
                    if (wantFatal) fatalHit++; else fatalFalse++;
                    continue;
                }
                if (wantFatal) { fatalMiss++; continue; }
                repCases++;
                foreach (string f in r.Fixes) { string k = f.Split(' ')[0]; kinds.TryGetValue(k, out int c); kinds[k] = c + 1; }
                if (SaveInvariants.Check(r.Repaired).Count > 0) left++;
                ValidationResult r2 = SaveValidator.ValidateAndRepair(r.Repaired);
                if (r2.Repaired == null || !r2.Repaired.StructEquals(r.Repaired) || r2.Fixes.Count > 0) idem++;
            }
            int fatKeys = 0;
            foreach (string k in SaveValidator.FatalKeys)
            {
                JsonObject s = MiniSave.Build(rng, 6);
                s.Remove(k);
                ValidationResult r = SaveValidator.ValidateAndRepair(s);
                if (r.Repaired == null && r.Fatal != null && r.Fatal.StartsWith("fatal", StringComparison.Ordinal)) fatKeys++;
            }
            bool ok = cleanViol == 0 && left == 0 && idem == 0 && fatalMiss == 0 && fatalFalse == 0 && fatKeys == SaveValidator.FatalKeys.Length;
            Console.WriteLine("  " + Common.Mark(cleanViol == 0) + " 正常なセーブ 300 件: 修復が入った・不変条件に違反 = " + cleanViol);
            Console.WriteLine("  " + Common.Mark(left == 0 && idem == 0) + " 破損 " + n + " 件のうち修復で足りる " + repCases + " 件: 修復後も違反 = " + left + " / 2 度目の修復で変化(冪等でない)= " + idem + "(" + (sw.Elapsed.TotalMilliseconds / n).ToString("F2") + " ms/件)");
            Console.WriteLine("  " + Common.Mark(fatalMiss == 0 && fatalFalse == 0) + " 致命にすべき " + (fatalHit + fatalMiss) + " 件: 致命にした " + fatalHit + "・通してしまった " + fatalMiss + " / 修復で足りるのに致命にした " + fatalFalse);
            Console.WriteLine("  " + Common.Mark(fatKeys == SaveValidator.FatalKeys.Length) + " 必須項目の欠落 " + SaveValidator.FatalKeys.Length + " 種: 致命として拒否 " + fatKeys + " / " + SaveValidator.FatalKeys.Length + "   修復の内訳: " + string.Join(", ", kinds.Select(kv => kv.Key + " " + kv.Value)));
            return ok ? 0 : 1;
        }
    }
}
