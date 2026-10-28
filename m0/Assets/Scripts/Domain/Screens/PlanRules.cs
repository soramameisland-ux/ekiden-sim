using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Training;

namespace Ekiden.Domain.Screens
{
    public enum PlanFocus { Balanced = 0, Speed = 1, Long = 2 }

    /// <summary>推奨プランとグループ編成(R-04)・個別指導の検証(R-06)。</summary>
    public static class PlanRules
    {
        public const int K_UI_GROUP_A = 10, K_UI_GROUP_B = 10;
        public const int K_UI_GUIDE_COST = 1, K_UI_GUIDE_POINTS = 4;

        // 4月〜3月(month_index 0..11)。Balanced は 03 §11 の標準プラン
        private static readonly string[][] Templates =
        {
            new[] { "th.base_build", "th.speed", "th.base_build", "th.mountain", "th.camp", "th.speed", "th.base_build", "th.base_build", "th.mountain", "th.recovery", "th.speed", "th.base_build" },
            new[] { "th.base_build", "th.speed", "th.speed", "th.mountain", "th.camp", "th.speed", "th.base_build", "th.speed", "th.mountain", "th.recovery", "th.speed", "th.base_build" },
            new[] { "th.base_build", "th.speed", "th.base_build", "th.base_build", "th.camp", "th.speed", "th.base_build", "th.base_build", "th.base_build", "th.recovery", "th.speed", "th.base_build" },
        };

        /// <summary>重点ごとの、標準プラン(バランス)との適性値 P の差(08 R-04 の表。4 年間・1500 人・施設 Lv2 の検算。並びは <c>SectionType</c> = Fast / Mid / Long / Climb / Descent)。年間方針の画面がそのまま出す(I-14)。</summary>
        public static readonly double[][] FocusDeltaP =
        {
            new[] { 0.0, 0.0, 0.0, 0.0, 0.0 },
            new[] { 1.19, 0.27, 0.02, -0.40, 0.34 },
            new[] { 0.35, 0.54, 0.58, -1.27, -1.81 },
        };

        /// <summary>重点ごとの故障の回数 / 人年(同じ表)。</summary>
        public static readonly double[] FocusInjuryPerYear = { 0.349, 0.344, 0.349 };

        /// <summary>推奨プラン: 重点ごとの 12 か月のテンプレートから、その月のテーマを返す(グループによらず同じ。動的な調整はしない)。</summary>
        public static string AutoPlan(PlanFocus focus, int monthIndex) { return Templates[(int)focus][monthIndex]; }

        /// <summary>グループの自動編成: 力 P_long の高い順(同値は ID の小さい方が上)に、上位 10 人を A(0)・次の 10 人を B(1)・残りを C(2)。入力は (runnerId, P_long)。</summary>
        public static Dictionary<int, int> AutoGroups(IReadOnlyList<KeyValuePair<int, double>> pLong)
        {
            var order = pLong.ToList();
            order.Sort((a, b) =>
            {
                int c = b.Value.CompareTo(a.Value);
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });
            var res = new Dictionary<int, int>();
            for (int i = 0; i < order.Count; i++) res[order[i].Key] = i < K_UI_GROUP_A ? 0 : (i < K_UI_GROUP_A + K_UI_GROUP_B ? 1 : 2);
            return res;
        }

        /// <summary>03 §5.5 の個別指定のメニューの検証。エラーの文言のリスト(空なら有効)。mileage が null なら『走行量は必須』。</summary>
        public static List<string> ValidateMenu(TrainingMaster m, string ks1, string ks2, string mileage, bool injured = false)
        {
            var errs = new List<string>();
            if (injured) errs.Add("故障中は指定できない");
            if (ks1 == ks2) errs.Add("ポイント練習の2枠は異なるIDにする");
            foreach (string k in new[] { ks1, ks2 })
            {
                if (k == "ks.cross_train") errs.Add("ks.cross_train は指定できない");
                if (!m.KeySessions.ContainsKey(k)) errs.Add("未定義のポイント練習 " + k);
            }
            if (mileage == null || !m.Mileages.ContainsKey(mileage)) errs.Add("走行量は必須");
            return errs;
        }

        /// <summary>個別指導の可否(R-06)。kind = "menu" / "talk"。戻り値は (可否, 理由)。</summary>
        public static KeyValuePair<bool, string> CanGuide(TrainingMaster m, int pointsLeft, string kind, bool injured, bool alreadyTalked = false, Tuple<string, string, string> menu = null)
        {
            if (pointsLeft < K_UI_GUIDE_COST) return Pair(false, "指導ポイントが足りない");
            if (kind == "menu")
            {
                if (injured) return Pair(false, "故障中の選手には指定できない");
                if (menu == null || ValidateMenu(m, menu.Item1, menu.Item2, menu.Item3).Count > 0) return Pair(false, "メニューが不正");
                return Pair(true, "今週から月末まで有効");
            }
            if (kind == "talk")
            {
                if (alreadyTalked) return Pair(false, "面談は1人につき月1回");
                return Pair(true, "やる気 +1(03 M3)");
            }
            return Pair(false, "不明な種別");
        }

        private static KeyValuePair<bool, string> Pair(bool b, string s) { return new KeyValuePair<bool, string>(b, s); }
    }
}
