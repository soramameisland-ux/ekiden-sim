using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Ekiden.Persistence;
using Ekiden.Persistence.Container;
using Ekiden.Persistence.Json;

namespace Ekiden.SimCli
{
    /// <summary>セーブの大きさと、保存・読み込みの時間(09 §5.4・§14.2)。02 §4.9 の構造に沿った合成セーブ(名前・能力はランダム = 圧縮に不利)を、刈り込み(§5.2)ありで作る。</summary>
    internal static class SaveCmd
    {
        // 09 §5.4 の表(Python 参照): 年度, JSON (KB), gzip (KB)
        private static readonly int[][] Reference = { new[] { 1, 598, 123 }, new[] { 10, 710, 138 }, new[] { 25, 899, 163 }, new[] { 50, 1224, 205 }, new[] { 100, 1852, 288 }, new[] { 200, 3145, 456 } };

        public static int Run(string[] args)
        {
            var pos = Common.Positional(args);
            int[][] list = pos.Count > 0 ? new[] { Reference.First(r => r[0] == int.Parse(pos[0])) } : Reference;
            int bad = 0;
            Console.WriteLine("== セーブの大きさ(09 §5.4。合成データ。目標 " + SaveConstants.K_SIZE_TARGET / 1024 + " KB・上限 " + SaveConstants.K_SIZE_HARD / 1024 + " KB は gzip 後)");
            foreach (int[] rf in list)
            {
                JsonObject payload = SyntheticSave.Build(rf[0]);
                JsonObject header = Header(rf[0]);
                string json = payload.ToJson();
                byte[] container = Eksv.Encode(header, payload);
                double encMs = Median(5, () => Eksv.Encode(header, payload));
                byte[] fastBytes = Eksv.Encode(header, payload, fast: true);
                double fastMs = Median(5, () => Eksv.Encode(header, payload, fast: true));
                double serMs = Median(5, () => payload.ToJson());
                JsonValue back;
                double decMs = Median(5, () => Eksv.Decode(container, out back));
                Eksv.Decode(container, out back);
                bool same = back.StructEquals(payload);
                int rawBytes = System.Text.Encoding.UTF8.GetByteCount(json);
                double gzKb = container.Length / 1024.0, jsonKb = rawBytes / 1024.0;
                bool within = Math.Abs(gzKb - rf[2]) <= 0.3 * rf[2];
                bool budget = (rf[0] > 100 || container.Length <= SaveConstants.K_SIZE_TARGET) && container.Length <= SaveConstants.K_SIZE_HARD;
                // 読み込み ≤ 500 ms。中断セーブ(同期。最速の圧縮)のエンコード ≤ 200 ms。自動セーブは背景の書き込みなので時間の予算なし
                bool time = decMs <= 500.0 / Common.PhoneSlowdown && fastMs <= 200.0 / Common.PhoneSlowdown;
                bool ok = same && within && budget && time;
                if (!ok) bad++;
                Console.WriteLine("  " + Common.Mark(ok) + " " + rf[0].ToString().PadLeft(3) + " 年度: JSON " + jsonKb.ToString("F0") + " KB(参照 " + rf[1] + ")→ gzip 込みコンテナ " + gzKb.ToString("F0") + " KB(参照 " + rf[2] + "・比 "
                                  + (jsonKb / gzKb).ToString("F1") + ")| 直列化 " + serMs.ToString("F0") + " ms・エンコード(直列化 + gzip + CRC)" + encMs.ToString("F0") + " ms・同 最速圧縮(中断セーブ)" + fastMs.ToString("F0") + " ms(" + (fastBytes.Length / 1024) + " KB)・読み込み(伸長 + 解析)" + decMs.ToString("F0") + " ms"
                                  + (same ? "" : " ×読み戻しが一致しない") + (within ? "" : " ×参照の ±30% の外") + (budget ? "" : " ×大きさの予算の外") + (time ? "" : " ×時間の予算の外"));
            }
            Console.WriteLine("  時間の判定: 読み込み ≤ 500 ms・中断セーブのエンコード ≤ 200 ms を、下位のスマホ(開発機の " + Common.PhoneSlowdown + " 倍遅い)で守れる = 開発機で 1/" + Common.PhoneSlowdown + " 以内");
            return bad == 0 ? 0 : 1;
        }

        internal static JsonObject Header(int year)
        {
            return new HeaderBuilder { Kind = "auto", Slot = 1, LineageId = "3f2c9a6e-0000-4000-8000-000000000001", SaveCounter = year, AppVersion = "0.0.0", MasterHash = "a1b2c3d4", SavedAt = 1800000000L + year, Year = year, MonthIndex = 6, SchoolName = "青嵐大学", ScenarioId = "sc.mid", Difficulty = "Mid", PlaySeconds = 3600L * year }.Build();
        }

        /// <summary>中央値(ms)。最初に 1 回ならす。</summary>
        internal static double Median(int n, Action a)
        {
            a();
            var t = new List<double>();
            for (int i = 0; i < n; i++)
            {
                var sw = Stopwatch.StartNew();
                a();
                t.Add(sw.Elapsed.TotalMilliseconds);
            }
            t.Sort();
            return t[t.Count / 2];
        }
    }
}
