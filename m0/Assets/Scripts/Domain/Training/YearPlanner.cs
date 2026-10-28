using System.Collections.Generic;

namespace Ekiden.Domain.Training
{
    /// <summary>月間計画から週のメニューを作る(03 §5)。標準プラン・大会週・ピーキングの検算用。</summary>
    public static class YearPlanner
    {
        public static readonly string[] StdPlan =
        {
            "th.base_build", "th.speed", "th.base_build", "th.mountain", "th.camp", "th.speed",
            "th.base_build", "th.base_build", "th.mountain", "th.recovery", "th.speed", "th.base_build",
        };

        /// <summary>年内の週番号(0 始まり)→ 想定区間距離(km)。03 §11 の検算用。</summary>
        public static readonly IReadOnlyDictionary<int, double> Races = new Dictionary<int, double> { { 25, 8.0 }, { 28, 13.0 }, { 36, 21.5 } };

        /// <summary>その週(年内 0〜47)のメニューと夏合宿の印。taper = 大会週から逆算して後ろから詰めるピーキング(T1=3 週前 … T4=大会週)。</summary>
        public static Menu WeekMenu(TrainingMaster m, int yearWeek, IReadOnlyList<string> plan, IReadOnlyDictionary<int, double> races, bool taper, out bool camp)
        {
            int month = yearWeek / 4, wk = yearWeek % 4;
            camp = plan[month] == "th.camp";
            Menu menu = m.Themes[plan[month]][wk];
            if (taper)
            {
                var keys = new List<int>(races.Keys);
                keys.Sort();
                int prev = -10;
                foreach (int t in keys)
                {
                    int start = System.Math.Max(t - 3, prev + 1);
                    if (start >= 0 && yearWeek >= start && yearWeek <= t) menu = m.Taper[4 - (t - start + 1) + (yearWeek - start)];
                    prev = t;
                }
            }
            return menu;
        }
    }
}
