using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ekiden.Persistence.Json;
using Ekiden.Platform;

namespace Ekiden.Persistence.Container
{
    /// <summary>スロットから読んだ結果。</summary>
    public sealed class SlotLoad
    {
        public HeaderInfo Header;
        public JsonValue Payload;
        public char Generation;          // 'a' / 'b'
        public int BrokenGenerations;    // 壊れていた世代の数(0〜1)
    }

    public enum RecoverySource { Auto, Manual, PreMigrate, Conflict }

    public sealed class SlotRecovery
    {
        public RecoverySource Source;
        public HeaderInfo Header;
        public JsonValue Payload;
        public string Path;
        /// <summary>最新の世代が壊れていて、ひとつ前から再開した(利用者に知らせる。§4.5)。</summary>
        public bool NewestWasBroken;
    }

    /// <summary>A / B の 2 世代と原子的な書き込み、回復の順(09 §4.4・§4.5)、中断セーブの再開の条件(§4.6)。</summary>
    public sealed class SlotStore
    {
        private readonly IFileStore _fs;

        public SlotStore(IFileStore fs) { _fs = fs; }

        public static string AutoPath(int slot, char gen) { return "saves/slot" + slot + "." + gen + ".eksv"; }

        public static string ManualPath(int slot) { return "saves/slot" + slot + ".m.eksv"; }

        public const string SuspendPath = "saves/suspend.eksv";

        public static string PreMigratePath(int slot, int fromVersion) { return "backups/premigrate_slot" + slot + "_v" + fromVersion + ".eksv"; }

        public static string ConflictPath(int slot, long utc) { return "backups/conflict_slot" + slot + "_" + utc.ToString(CultureInfo.InvariantCulture) + ".eksv"; }

        private long CounterOf(string path)
        {
            try
            {
                if (!_fs.Exists(path)) return -1;
                return Eksv.DecodeHeader(_fs.ReadAllBytes(path)).SaveCounter;
            }
            catch (Exception e) when (e is LoadException || e is System.IO.IOException)
            {
                return -1;
            }
        }

        /// <summary>書き込み先: A / B のうち、壊れている・無い・save_counter が小さいほう(同じなら A)。</summary>
        public char PickTarget(int slot)
        {
            return CounterOf(AutoPath(slot, 'a')) <= CounterOf(AutoPath(slot, 'b')) ? 'a' : 'b';
        }

        /// <summary>次の保存番号 = そのスロットの {a, b, m} の最大 + 1(1 から。戻らない)。</summary>
        public long NextCounter(int slot)
        {
            long m = Math.Max(Math.Max(CounterOf(AutoPath(slot, 'a')), CounterOf(AutoPath(slot, 'b'))), CounterOf(ManualPath(slot)));
            return m < 0 ? 1 : m + 1;
        }

        private static bool VerifyAll(byte[] b)
        {
            HeaderInfo h;
            JsonValue p;
            string code;
            return Eksv.TryDecode(b, out h, out p, out code);
        }

        /// <summary>自動セーブを書く(§4.4)。書き込み先の世代を返す。失敗は <see cref="System.IO.IOException"/>(呼ぶ側が 1 回だけ再試行する。§4.6)。</summary>
        public char WriteAuto(int slot, JsonObject header, JsonValue payload)
        {
            char g = PickTarget(slot);
            _fs.WriteAtomic(AutoPath(slot, g), Eksv.Encode(header, payload), VerifyAll);
            return g;
        }

        public void WriteManual(int slot, JsonObject header, JsonValue payload)
        {
            _fs.WriteAtomic(ManualPath(slot), Eksv.Encode(header, payload), VerifyAll);
        }

        public void WriteSuspend(JsonObject header, JsonValue payload)
        {
            _fs.WriteAtomic(SuspendPath, Eksv.Encode(header, payload, fast: true), VerifyAll);          // 同期で書く(200 ms 以内。§14.2)ので最速の圧縮
        }

        private bool TryLoad(string path, out HeaderInfo h, out JsonValue p)
        {
            h = null;
            p = null;
            string code;
            return _fs.Exists(path) && Eksv.TryDecode(_fs.ReadAllBytes(path), out h, out p, out code);
        }

        /// <summary>スロットを読む: A / B のうち、全体の検証に通るもので save_counter が最大のもの。両方だめなら <c>no_valid_generation</c>。</summary>
        public SlotLoad LoadAuto(int slot)
        {
            SlotLoad best = null;
            int bad = 0;
            foreach (char g in new[] { 'a', 'b' })
            {
                string path = AutoPath(slot, g);
                if (!_fs.Exists(path)) continue;
                HeaderInfo h;
                JsonValue p;
                if (!TryLoad(path, out h, out p)) { bad++; continue; }
                if (best == null || h.SaveCounter > best.Header.SaveCounter) best = new SlotLoad { Header = h, Payload = p, Generation = g };
            }
            if (best == null) throw new LoadException("no_valid_generation");
            best.BrokenGenerations = bad;
            return best;
        }

        /// <summary>回復の順(§4.5): ① A/B の最新の有効な世代 ② もう一方 ③ 手動セーブ(confirmManual が true のとき)④ premigrate(最新)⑤ conflict(saved_at の新しい順)。すべて失敗なら null(ファイルは消さない)。</summary>
        public SlotRecovery Recover(int slot, Func<bool> confirmManual = null)
        {
            return RecoveryCandidates(slot, confirmManual).FirstOrDefault();
        }

        /// <summary>回復の候補を、試す順に遅延で返す(§4.5)。コンテナとして有効でも、中身(ゲームの状態)が復元できないことがあるので、呼ぶ側が先頭から順に試す。
        /// 手動セーブは <paramref name="confirmManual"/> が true を返したときだけ(利用者への確認は、そこまで来たときに 1 回)。</summary>
        public IEnumerable<SlotRecovery> RecoveryCandidates(int slot, Func<bool> confirmManual = null)
        {
            var autos = new List<SlotRecovery>();
            int bad = 0;
            foreach (char g in new[] { 'a', 'b' })
            {
                string path = AutoPath(slot, g);
                if (!_fs.Exists(path)) continue;
                HeaderInfo ah;
                JsonValue ap;
                if (TryLoad(path, out ah, out ap)) autos.Add(new SlotRecovery { Source = RecoverySource.Auto, Header = ah, Payload = ap, Path = path });
                else bad++;
            }
            autos = autos.OrderByDescending(a => a.Header.SaveCounter).ToList();
            for (int i = 0; i < autos.Count; i++)
            {
                autos[i].NewestWasBroken = bad > 0;
                yield return autos[i];
            }
            HeaderInfo h;
            JsonValue p;
            if (TryLoad(ManualPath(slot), out h, out p) && (confirmManual == null || confirmManual()))
                yield return new SlotRecovery { Source = RecoverySource.Manual, Header = h, Payload = p, Path = ManualPath(slot) };
            foreach (string path in _fs.List("backups/premigrate_slot" + slot + "_v").OrderByDescending(x => x, StringComparer.Ordinal))
                if (TryLoad(path, out h, out p)) yield return new SlotRecovery { Source = RecoverySource.PreMigrate, Header = h, Payload = p, Path = path };
            var conflicts = new List<SlotRecovery>();
            foreach (string path in _fs.List("backups/conflict_slot" + slot + "_"))
                if (TryLoad(path, out h, out p)) conflicts.Add(new SlotRecovery { Source = RecoverySource.Conflict, Header = h, Payload = p, Path = path });
            foreach (SlotRecovery c in conflicts.OrderByDescending(c => c.Header.Num("saved_at", 0))) yield return c;
        }

        /// <summary>中断セーブを読む(再開できるか確かめたあと。<see cref="CanResume"/>)。なければ・壊れていれば null。</summary>
        public SlotLoad ReadSuspend()
        {
            HeaderInfo h;
            JsonValue p;
            return TryLoad(SuspendPath, out h, out p) ? new SlotLoad { Header = h, Payload = p, Generation = 's' } : null;
        }

        /// <summary>手動セーブを読む。なければ・壊れていれば null。</summary>
        public SlotLoad ReadManual(int slot)
        {
            HeaderInfo h;
            JsonValue p;
            return TryLoad(ManualPath(slot), out h, out p) ? new SlotLoad { Header = h, Payload = p, Generation = 'm' } : null;
        }

        /// <summary>残っている中断セーブがどのスロットのものか(なければ・壊れていれば −1)。ほかのスロットのものは触らないために使う。</summary>
        public int SuspendSlot()
        {
            SlotLoad s = ReadSuspend();
            return s == null ? -1 : (int)s.Header.Num("slot", -1);
        }

        /// <summary>中断セーブを捨てる(再開の条件に合わなかったとき・使い切ったとき)。</summary>
        public void DeleteSuspend() { _fs.Delete(SuspendPath); }

        /// <summary>スロットの削除(確認つき・取り消せない): a・b・m と、そのスロットの中断セーブ(あれば)を消す。</summary>
        public void DeleteSlot(int slot)
        {
            _fs.Delete(AutoPath(slot, 'a'));
            _fs.Delete(AutoPath(slot, 'b'));
            _fs.Delete(ManualPath(slot));
            HeaderInfo h;
            JsonValue p;
            if (TryLoad(SuspendPath, out h, out p) && h.Num("slot", -1) == slot) _fs.Delete(SuspendPath);
        }

        /// <summary>中断セーブから再開してよいか(§4.6): 有効で、lineage_id が一致し、base_counter がスロットの現在の先頭の save_counter と等しい。</summary>
        public bool CanResume(int slot)
        {
            HeaderInfo sh;
            JsonValue sp;
            if (!TryLoad(SuspendPath, out sh, out sp) || sh.Kind != "suspend" || sh.Num("slot", -1) != slot) return false;
            try
            {
                SlotLoad head = LoadAuto(slot);
                return head.Header.LineageId == sh.LineageId && sh.Num("base_counter", -2) == head.Header.SaveCounter;
            }
            catch (LoadException)
            {
                return false;
            }
        }

        /// <summary>起動時に残っている .tmp を消す(読み込みには使わない。§3.1)。</summary>
        public int CleanTemp()
        {
            int n = 0;
            foreach (string path in _fs.List("").Where(x => x.EndsWith(".tmp", StringComparison.Ordinal)).ToList())
            {
                _fs.Delete(path);
                n++;
            }
            return n;
        }
    }
}
