using System;

namespace Ekiden.Persistence.Sync
{
    public enum SyncAction { Noop, Upload, Download, Conflict }

    /// <summary>同期の記録 1 つ(端末の記録 L またはクラウドの記録 C。09 §9.1)。IsTombstone = 削除した記録(墓石)。LastSynced は端末の記録だけが持つ『最後にクラウドと一致させた時点』の counter。</summary>
    public sealed class SyncRecord
    {
        public string LineageId;
        public long Counter;
        public bool IsTombstone;
        public long LastSynced;
    }

    /// <summary>同期の判定 <c>SyncDecide(L, C)</c>(09 §9.2)。どちらが新しいかではなく、どちらが変わったかで決める(時計に頼らない)。プラットフォームに依存しない純粋な関数。</summary>
    public static class SyncDecider
    {
        /// <param name="local">端末の記録(なければ null。墓石を含む)</param>
        /// <param name="cloud">クラウドの記録(同)</param>
        public static SyncAction Decide(SyncRecord local, SyncRecord cloud)
        {
            if (local == null && cloud == null) return SyncAction.Noop;
            if (local == null) return cloud.IsTombstone ? SyncAction.Noop : SyncAction.Download;      // 初めての端末
            if (cloud == null) return local.IsTombstone ? SyncAction.Noop : SyncAction.Upload;        // 初めての同期
            if (local.LineageId != cloud.LineageId)
            {
                if (local.IsTombstone) return cloud.IsTombstone ? SyncAction.Noop : SyncAction.Download;   // 墓石は別の系譜の実体に負ける
                if (cloud.IsTombstone) return SyncAction.Upload;                                           // 新しい歴史は墓石を上書きする
                return SyncAction.Conflict;
            }
            bool lc = local.Counter > local.LastSynced, cc = cloud.Counter > local.LastSynced;
            if (lc && cc) return SyncAction.Conflict;                                                      // 番号が同じでも、両方が進んでいれば競合
            if (lc) return SyncAction.Upload;
            if (cc) return SyncAction.Download;
            return SyncAction.Noop;
        }
    }
}
