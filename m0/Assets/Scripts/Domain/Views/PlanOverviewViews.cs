using System.Collections.Generic;
using Ekiden.Domain.Race;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;

namespace Ekiden.Domain.Views
{
    /// <summary>重点の選択肢 1 つ(08 R-04)。</summary>
    public sealed class FocusOptionView
    {
        public PlanFocus Focus;
        public bool Selected;
        public string[] Months = new string[12];    // 4 月〜3 月の推奨のテーマ('th.base_build' など)
        public double[] DeltaP = new double[5];     // 標準プランとの適性値 P の差(Fast / Mid / Long / Climb / Descent の順)
        public double InjuryPerYear;                // 故障の回数 / 人年
    }

    /// <summary>年間方針(08a SC-10)。</summary>
    public sealed class AnnualPlanView
    {
        public PlanFocus Focus;
        public int TargetRace;                      // 0 = 第一戦 / 1 = 第二戦 / 2 = 第三戦
        public PeakingMode[] Peaking = new PeakingMode[AnnualPlan.PeakingKinds];       // 第一〜第三戦・予選会・記録会
        public bool PlanAuto;                       // 月間計画をおまかせにしている
        public int ExpectedTargetRank;              // 期待目標(前年度の第三戦の順位 − 1)
        public int ExpectedBonusFunds;              // 達成したときの予算ボーナス(万円)
        public readonly List<FocusOptionView> Options = new List<FocusOptionView>();
    }

    /// <summary>予報(1 区間グループぶん。第三戦は往路と復路の 2 つ)。</summary>
    public sealed class EnvForecastView
    {
        public double Temp, Wind;
        public bool Rain;
    }

    /// <summary>出場校の上位選手 1 人(08 R-09)。</summary>
    public sealed class RivalRunnerView
    {
        public string FamilyName = "", GivenName = "", FamilyKana = "", GivenKana = "";
        public int Grade;
        public double FitLong;                      // 区間適性 Long
        public int Pb5000, Pb10000;                 // 持ちタイム(ds)
    }

    public sealed class PreviewSchoolView
    {
        public int SchoolId;
        public string NameKey = "";
        public int Rank;                            // 前評判の順位
        public double Value;                        // 前評判の値(登録メンバーの上位 run_size 人の区間適性 Long の平均)
        public char Mark;                           // ◎ / 〇 / ▲ / △(印なしは ' ')
        public bool IsOwn;
        public int PowerRank;                       // 戦力順位
        public readonly List<RivalRunnerView> Top = new List<RivalRunnerView>();      // 区間適性 Long の上位 3 人
    }

    /// <summary>他校情報・予報(08a SC-23)。</summary>
    public sealed class PreviewView
    {
        public RaceKind Kind;
        public readonly List<PreviewSchoolView> Schools = new List<PreviewSchoolView>();
        public readonly List<EnvForecastView> Forecast = new List<EnvForecastView>();
        public double TopMarkWinRate = 0.55;        // 『◎の優勝率は約 55%』の注記(08 R-09)
        public double TempError = 1.5, RainHitRate = 0.85;        // 予報の誤差の注記(気温 ±1.5℃・雨の的中率 85%。04 §6)
    }
}
