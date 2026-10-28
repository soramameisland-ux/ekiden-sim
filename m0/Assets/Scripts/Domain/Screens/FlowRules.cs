using System;
using System.Collections.Generic;
using System.Linq;

namespace Ekiden.Domain.Screens
{
    /// <summary>進行の停止(R-07。08 §6.3 の決定表)・月末レポートの項目の選択(R-10)。</summary>
    public static class FlowRules
    {
        public const int K_UI_STOP_CAP_WEEK = 3;
        public const int K_UI_REPORT_MAX = 7, K_UI_REPORT_PER_KIND = 3;
        private static readonly string[] RaceNames = { "Fall", "National", "NewYear" };
        private static readonly int[] RaceWeeks = { 25, 28, 36 };
        private const int QualWeek = 26;

        /// <summary>週の初めに止まる場所(計画・登録・オーダー)。week は年度内の 0〜47。qualifies = 出場権のある大会(Fall / National / NewYear)。</summary>
        public static List<string> StopsBeforeWeek(int week, ISet<string> qualifies, bool seedSchool, bool planAuto)
        {
            var o = new List<string>();
            if (week == 0) o.Add("AnnualStart");
            if (week % 4 == 0 && !planAuto) o.Add("MonthPlan");
            for (int i = 0; i < 3; i++)
                if (qualifies.Contains(RaceNames[i]) && week == RaceWeeks[i] - 4) o.Add("Entry:" + RaceNames[i]);
            if (!seedSchool && week == QualWeek - 4) o.Add("Entry:Qualifier");
            for (int i = 0; i < 3; i++)
                if (qualifies.Contains(RaceNames[i]) && week == RaceWeeks[i]) o.Add("Order:" + RaceNames[i]);
            return o;
        }

        public struct EventInfo { public string Id; public int Choices; }

        public struct InjuryInfo { public int RunnerId; public int Severity; public int Group; public bool Registered; }   // Severity 1 = Minor / 2 = Moderate / 3 = Severe。Group 0 = A

        /// <summary>週の終わり(練習・大会・EventPhase のあと)に止まる場所と順序、月末レポートへ回すもの。</summary>
        public static void StopsAfterWeek(int week, IReadOnlyList<EventInfo> events, IReadOnlyList<InjuryInfo> injuries, bool warn, string raceKind, bool eventAuto,
                                          out List<string> stops, out List<string> deferred)
        {
            stops = new List<string>();
            deferred = new List<string>();
            if (raceKind != null) stops.Add("RaceDay:" + raceKind);
            foreach (EventInfo e in events)
            {
                if (e.Choices >= 2 && !eventAuto) stops.Add("Event:" + e.Id);
                else deferred.Add("Notice:" + e.Id);
            }
            foreach (InjuryInfo j in injuries)
            {
                if (raceKind != null) deferred.Add("Injury:" + j.RunnerId + "(当日の画面に表示)");           // 大会週の故障は、当日の画面で扱う(当日変更・自動の代役)
                else if (j.Severity == 3 || (j.Severity == 2 && (j.Group == 0 || j.Registered))) stops.Add("Injury:" + j.RunnerId);
                else deferred.Add("Injury:" + j.RunnerId);
            }
            if (week % 4 == 2 && warn) stops.Add("Warn");
            if (stops.Count > K_UI_STOP_CAP_WEEK)
            {
                for (int i = K_UI_STOP_CAP_WEEK; i < stops.Count; i++) deferred.Add(stops[i] + "(上限)");
                stops = stops.Take(K_UI_STOP_CAP_WEEK).ToList();
            }
            if (week % 4 == 3) stops.Add("MonthReport");
            if (week == 47) stops.Add("YearEnd");
        }

        // ---- R-10 月末レポートの項目の選択
        private static int Priority(string kind)
        {
            switch (kind)
            {
                case "injury_severe": return 100;
                case "injury_moderate": return 90;
                case "awaken": return 80;
                case "gift_reveal": return 75;
                case "rank_up": return 60;
                case "big_growth": return 50;
                case "return": return 40;
                case "notice": return 30;
                case "form_up": return 20;
                default: return 0;
            }
        }

        /// <summary>月末レポートの材料 1 件。<c>Kind</c> / <c>Key</c> / <c>Score</c> が選択の規則(TV-12)の入力。残りは画面に出すための付随情報(選択には使わない)。</summary>
        public struct ReportItem
        {
            public string Kind, Key;
            public double Score;
            public int RunnerId;                // 選手に関する項目(0 = なし)
            public string EventId;              // awaken・notice の元のイベント
            public string SkillId;              // awaken: 覚醒した特殊能力(skill. 接頭辞なし)
            public int Value;                   // rank_up: 能力の添字 / big_growth: 能力の増加の合計 / form_up: 調子の差
            public char Letter;                 // rank_up: 新しいランク文字
        }

        /// <summary>優先度の高い順、同じ種別は score の大きい順に最大 3 件、全体で 7 件。戻り値は表示する項目。『ほか n件』は items.Count − 戻り値の数。</summary>
        public static List<ReportItem> SelectReportItems(IReadOnlyList<ReportItem> items)
        {
            var byKind = new List<KeyValuePair<string, List<ReportItem>>>();
            foreach (ReportItem it in items)
            {
                var slot = byKind.FirstOrDefault(x => x.Key == it.Kind);
                if (slot.Value == null) { slot = new KeyValuePair<string, List<ReportItem>>(it.Kind, new List<ReportItem>()); byKind.Add(slot); }
                slot.Value.Add(it);
            }
            var cand = new List<KeyValuePair<int, ReportItem>>();
            foreach (var kv in byKind)
            {
                var lst = kv.Value.ToList();
                lst = StableSort(lst, (a, b) =>
                {
                    int c = b.Score.CompareTo(a.Score);
                    return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
                });
                foreach (ReportItem it in lst.Take(K_UI_REPORT_PER_KIND)) cand.Add(new KeyValuePair<int, ReportItem>(Priority(kv.Key), it));
            }
            cand = StableSort(cand, (a, b) =>
            {
                int c = b.Key.CompareTo(a.Key);
                if (c != 0) return c;
                c = b.Value.Score.CompareTo(a.Value.Score);
                return c != 0 ? c : string.CompareOrdinal(a.Value.Key, b.Value.Key);
            });
            return cand.Take(K_UI_REPORT_MAX).Select(x => x.Value).ToList();
        }

        private static List<T> StableSort<T>(List<T> list, Comparison<T> cmp)
        {
            var res = new List<T>(list);
            for (int i = 1; i < res.Count; i++)
            {
                T x = res[i];
                int j = i - 1;
                while (j >= 0 && cmp(res[j], x) > 0) { res[j + 1] = res[j]; j--; }
                res[j + 1] = x;
            }
            return res;
        }
    }
}
