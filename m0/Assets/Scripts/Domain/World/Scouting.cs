using System;
using System.Collections.Generic;
using Ekiden.Domain.Rng;

namespace Ekiden.Domain.World
{
    /// <summary>スカウトの段階(05 §7.1)。視察 → 声かけ → 練習会に招待。</summary>
    public enum ScoutStage { None = 0, Observed = 1, Talked = 2, Invited = 3 }

    /// <summary>自校のスカウト候補 1 人の状態(02 §4.6)。</summary>
    public sealed class ScoutEntry
    {
        public WorldRunner Candidate;
        public ScoutStage Stage;
        public bool Target;                 // 目標に入れている
        public double? QObs;                // 視察後に見える Q の見積もり
    }

    /// <summary>スカウト活動の規則(05 §7・08 R-08)。ポイント・時期・好感度・情報の見え方。</summary>
    public static class Scouting
    {
        public const int PointsPerTarget = 3;
        public const int ObserveCost = 1, TalkCost = 1, InviteCost = 1;
        public const double TalkInterest = 30.0, InviteInterest = 60.0;                 // 好感度 +30 / +60(合計 +90)
        public const double InfoSd0 = 8.0, InfoSdStep = 1.2;
        public const int MonthlyPoints = 4;
        // month_index: 0 = 4 月 … 11 = 3 月。視察 6〜11 月(2〜7)・声かけと練習会 12〜2 月(8〜10)
        public const int ObserveFrom = 2, ObserveTo = 7, TalkFrom = 8, TalkTo = 10;
        public const int GrantFrom = 2, GrantTo = 10;                                   // ポイントの付与は 6〜2 月の毎月

        /// <summary>年間ポイント = 36 + 4 × floor(ランク / 3) + 補正(06 の `scout_pts`)。</summary>
        public static int AnnualPoints(int rank, int extra = 0)
        {
            return 36 + 4 * (rank / 3) + extra;
        }

        /// <summary>その月に付与するポイント: 6〜2 月は毎月 +4。ランクによる追加分(4 × floor(ランク/3))は 6 月(month_index 2)に一括。補正はその月に付く(6〜2 月以外なら 6 月に付ける)。</summary>
        public static int MonthlyGrant(int monthIndex, int rank, int extraThisMonth = 0)
        {
            if (monthIndex < GrantFrom || monthIndex > GrantTo) return 0;
            int g = MonthlyPoints;
            if (monthIndex == GrantFrom) g += 4 * (rank / 3);
            return g + extraThisMonth;
        }

        public static bool CanObserve(int monthIndex) { return monthIndex >= ObserveFrom && monthIndex <= ObserveTo; }
        public static bool CanTalk(int monthIndex) { return monthIndex >= TalkFrom && monthIndex <= TalkTo; }

        /// <summary>σ_info = max(1, 8 − 1.2 × (スカウト担当 Lv − 1))。</summary>
        public static double InfoSd(int scoutLv)
        {
            return Math.Max(1.0, InfoSd0 - InfoSdStep * (scoutLv - 1));
        }

        /// <summary>視察で見える Q の見積もり = Q + N(0, σ_info)。候補の look_seed から決まる(label 8・b = 11)ので、同じ候補は同じ誤差で見える。</summary>
        public static double ObserveQ(WorldRunner c, int scoutLv, IRngFactory rngs)
        {
            return c.Q + rngs.Stream(RngRegistry.LabelLook, c.LookSeed, 11).Normal(0.0, InfoSd(scoutLv));
        }

        /// <summary>段階から決まる好感度(視察 0 / 声かけ +30 / 練習会 +90)。</summary>
        public static double Interest(ScoutStage stage)
        {
            switch (stage)
            {
                case ScoutStage.Talked: return TalkInterest;
                case ScoutStage.Invited: return TalkInterest + InviteInterest;
                default: return 0.0;
            }
        }

        /// <summary>行動の前提(段階と時期)を満たすか。</summary>
        public static bool CanAct(ScoutStage current, ScoutStage next, int monthIndex)
        {
            switch (next)
            {
                case ScoutStage.Observed: return current == ScoutStage.None && CanObserve(monthIndex);
                case ScoutStage.Talked: return current == ScoutStage.Observed && CanTalk(monthIndex);
                case ScoutStage.Invited: return current == ScoutStage.Talked && CanTalk(monthIndex);
                default: return false;
            }
        }

        // ---- 情報の見え方(05 §7.2・10 §8.1)
        public static bool StyleVisible(ScoutStage stage) { return stage >= ScoutStage.Observed; }                       // 個性は視察すると見える
        public static bool GiftPresenceVisible(ScoutStage stage) { return stage >= ScoutStage.Invited; }                 // 『素養あり』は練習会の後
        public static bool GiftKindVisible(ScoutStage stage, int scoutLv) { return stage >= ScoutStage.Invited && scoutLv >= 3; }
        public static bool GrowthTypeVisible(ScoutStage stage) { return stage >= ScoutStage.Invited; }                   // 成長タイプ・故障しやすさ・素質の細部
    }

    /// <summary>スカウトのポイントの残り(同じ年度の中では繰り越す。3 月に 0。05 §7.1)。</summary>
    public sealed class ScoutPoints
    {
        public int Available { get; private set; }

        public void Grant(int n) { Available += n; }

        public bool TrySpend(int n)
        {
            if (n > Available) return false;
            Available -= n;
            return true;
        }

        /// <summary>3 月に残りは消える(次年度へは繰り越さない)。</summary>
        public void YearEnd() { Available = 0; }
    }
}
