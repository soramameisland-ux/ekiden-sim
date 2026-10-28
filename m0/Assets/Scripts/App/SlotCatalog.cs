using System.Collections.Generic;
using Ekiden.Persistence;
using Ekiden.Persistence.Container;
using Ekiden.Platform;

namespace Ekiden.App
{
    /// <summary>スロット 1 つの見出し(SC-01 の『続きから』・SC-03 のセーブの選択・SC-40 のポーズ。09 §4.2 の SaveMeta)。ヘッダだけから作る。</summary>
    public sealed class SlotSummary
    {
        public int Slot;
        public bool HasData;                // 自動セーブ(A / B)か手動セーブのどちらかが有効
        public bool Broken;                 // ファイルはあるが、有効なものが 1 つもない(『このセーブは読み込めません』。ファイルは消さない)
        public string SchoolNameKey = "";   // ヘッダの school_name(Loc を渡さずに保存したときは文字列のキー)
        public string ScenarioId = "", Difficulty;
        public int Year, MonthIndex;
        public long PlaySeconds;
        public long SavedAt;                // UTC 秒(表示用)
        public long SaveCounter;
        public bool HasManual;
        public long ManualSavedAt;
        public bool CanResume;              // 再開の条件に合う中断セーブがある(『続きから』は中断した場所から)
    }

    /// <summary>スロットの一覧。</summary>
    public static class SlotCatalog
    {
        public static List<SlotSummary> List(IFileStore fs, int slots = SaveConstants.K_SLOTS)
        {
            var store = new SlotStore(fs);
            var list = new List<SlotSummary>();
            for (int slot = 1; slot <= slots; slot++)
            {
                var s = new SlotSummary { Slot = slot };
                SlotLoad auto = null;
                try { auto = store.LoadAuto(slot); }
                catch (LoadException) { }
                SlotLoad manual = store.ReadManual(slot);
                s.HasManual = manual != null;
                if (manual != null) s.ManualSavedAt = manual.Header.Num("saved_at", 0);
                SlotLoad head = auto;
                if (manual != null && (head == null || manual.Header.SaveCounter > head.Header.SaveCounter)) head = manual;      // 一覧に出すのは新しいほう
                if (head != null)
                {
                    s.HasData = true;
                    HeaderInfo h = head.Header;
                    s.SchoolNameKey = h.Str("school_name") ?? "";
                    s.ScenarioId = h.Str("scenario_id") ?? "";
                    s.Difficulty = h.Str("difficulty");
                    s.Year = (int)h.Num("year", 1);
                    s.MonthIndex = (int)h.Num("month_index", 0);
                    s.PlaySeconds = h.Num("play_seconds", 0);
                    s.SavedAt = h.Num("saved_at", 0);
                    s.SaveCounter = h.SaveCounter;
                    s.CanResume = auto != null && store.CanResume(slot);
                }
                else s.Broken = fs.Exists(SlotStore.AutoPath(slot, 'a')) || fs.Exists(SlotStore.AutoPath(slot, 'b')) || fs.Exists(SlotStore.ManualPath(slot));
                list.Add(s);
            }
            return list;
        }
    }
}
