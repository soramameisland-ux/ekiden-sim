using System;
using Ekiden.Persistence.Sync;

namespace Ekiden.SimCli
{
    /// <summary>クラウド同期の乱数シミュレーション(09 §9.7。TV-9)。端末 2〜3 台が勝手にプレイ・同期・削除・新規を繰り返す。
    /// 書き込みに条件(CAS)がある方式では『失われた更新』が 0・全端末が収束し、条件なし(最後の書き込みが勝つ)では失われる。</summary>
    internal static class SyncSimCmd
    {
        public static int Run(string[] args)
        {
            var pos = Common.Positional(args);
            int n = Common.IntArg(pos, 0, 400);
            int steps = Common.IntArg(pos, 1, 300);
            int bad = 0;
            Console.WriteLine("== クラウド同期の乱数シミュレーション(" + n + " 回 × " + steps + " 手)");
            foreach (bool cas in new[] { true, false })
            {
                int lostRuns = 0, lostEnd = 0, nonconv = 0, maxb = 0;
                long plays = 0, syncs = 0, uploads = 0, downloads = 0, conflicts = 0, casFail = 0;
                for (int k = 0; k < n; k++)
                {
                    SyncWorld.Outcome r = SyncWorld.Simulate(2 + k % 2, steps, (ulong)(1000 + k), cas);
                    if (r.LostDuring.HasValue || r.LostEnd > 0) lostRuns++;
                    lostEnd += r.LostEnd;
                    if (!r.Converged) nonconv++;
                    maxb = Math.Max(maxb, r.Backups);
                    plays += r.Stat.Play; syncs += r.Stat.Sync; uploads += r.Stat.Upload; downloads += r.Stat.Download; conflicts += r.Stat.Conflict; casFail += r.Stat.CasFail;
                }
                bool ok = cas ? (lostRuns == 0 && lostEnd == 0 && nonconv == 0) : (lostRuns > 0);
                if (!ok) bad++;
                Console.WriteLine("  " + Common.Mark(ok) + " " + (cas ? "書き込みに条件あり(CAS)" : "条件なし(最後の書き込みが勝つ)") + ": 更新 " + plays + "・同期 " + syncs + "(アップロード " + uploads + "・ダウンロード " + downloads + "・競合 " + conflicts + "・CAS 失敗 " + casFail + ")"
                                  + " | 更新が失われた回 " + lostRuns + "・終了時に失われた葉 " + lostEnd + "・収束しなかった回 " + nonconv + "・バックアップ最大 " + maxb + " 件");
            }
            foreach (var (pOff, pFail) in new[] { (0.15, 0.10), (0.05, 0.03), (0.02, 0.01) })
            {
                SyncWorld.Usage(300, 120, 5000, pOff, pFail, out int ss, out int cf, out int lo);
                bool ok = lo == 0;
                if (!ok) bad++;
                Console.WriteLine("  " + Common.Mark(ok) + " ふつうの使い方(起動時に同期できない " + pOff.ToString("P0") + "・終了時に同期できない " + pFail.ToString("P0") + "): " + ss + " セッションで競合 " + cf + " 回(" + ((double)cf / ss).ToString("P1") + ")・失われた更新 " + lo);
            }
            Console.WriteLine(bad == 0 ? "同期: 失われた更新 0・収束" : "同期: 問題 " + bad + " 件");
            return bad == 0 ? 0 : 1;
        }
    }
}
