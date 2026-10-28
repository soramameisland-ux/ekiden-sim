using System.Collections.Generic;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;

namespace Ekiden.Domain.Views
{
    /// <summary>あるグループが、あるテーマを今月の残りの週で続けたときの見込み(08 R-05。今月の週ごとのメニューは、故障 &gt; 個別指導 &gt; 調整(ピーキング)&gt; テーマの順で決める = 実際の週の解決と同じ)。</summary>
    public sealed class ThemeOptionView
    {
        public string ThemeId;                       // "th.speed" など(名前のキーは {ThemeId}.name)
        public bool Recommended;                     // 年間方針の推奨プランのテーマ
        public bool Selected;                        // このグループがいま選んでいる
        public int FitGood, FitOk, FitLow;           // 相性 ◎ / 〇 / △ の人数(R-17。素養は判明したものだけ反映)
        public List<MenuSpec> Weeks = new List<MenuSpec>();   // 4 週のメニュー(ポイント練習 2 つ + 走行量)
        public double FatigueEndAvg;                 // 月末の疲労(グループの平均)
        public int FatigueEndMax;                    // 月末の疲労(最大)
        public int Attention;                        // 要注意の人数(予測の疲労が 70 以上になる週がある)
        public double ExpectedInjuries;              // 故障の見込み(件。グループの合計)
        public int SafetyWeeks;                      // 安全網が働く週の数(のべ。グループの合計)
        public double[] GainP = new double[5];       // 区間タイプ別の適性値 P の伸び(平均。Fast / Mid / Long / Climb / Descent)
        public double? GoodChance;                   // 次の大会で『好調以上』の見込み(グループの平均。今月だけこのテーマ、来月以降は推奨プラン。大会が 8 週以内で出場できるときだけ)
    }

    /// <summary>ある大会の週の調子の見込み(08 R-05 ⑥・SC-16・21・22)。現在の状態から大会の週の練習が解決されるまでを、乱数を除いて進めたもの。
    /// 疲労・能力は正確で、調子と故障は確率。週のメニューは 個別指導 &gt; 調整(登録済みの大会。未登録ならおまかせ登録の見込み)&gt; 月間計画(今月)/ 推奨プラン(来月以降)。</summary>
    public sealed class FormOutlookView
    {
        public RaceKind Kind;
        public int WeeksAhead;                       // 大会の週まであと何週(0 = 今週が大会の週)
        public bool Determined;                      // 大会の週の練習がすでに解決された = 調子は確定している(Form は実際の調子の 1.0)
        public bool Injured;                         // いま故障している(回復して間に合うかは PInjury とは別。PGood は 0)
        public bool Tapered;                         // 調整(ピーキング)の週が入る
        public double[] Form = new double[5];        // 調子の確率 [絶不調, 不調, 普通, 好調, 絶好調](故障しなかった場合)
        public double PInjury;                       // 大会の週までに故障する確率
        public double PGood;                         // 好調以上 = (1 − PInjury) × (好調 + 絶好調)。故障中は 0
        public int FatigueAtRace;                    // 大会の週の終わりの疲労の見込み
    }

    public sealed class GroupPlanView
    {
        public int Group;                            // 0 = A / 1 = B / 2 = C
        public int Count, Injured;
        public double AvgFatigue;
        public string SelectedTheme;                 // いま選んでいるテーマ(th.custom を含む)
        public List<ThemeOptionView> Options = new List<ThemeOptionView>();   // 選べるテーマ(自由編集の週メニューがあれば th.custom も)
    }

    /// <summary>月間計画(SC-16)。</summary>
    public sealed class PlanView
    {
        public int MonthIndex;                       // 0 = 4 月 … 11 = 3 月
        public int WeekOfMonth;                      // 0〜3(今月の何週目か。0 なら 4 週ぶん)
        public Screens.PlanFocus Focus;
        public int TargetRace;                       // 注目大会 0〜2
        public string RecommendedTheme;
        public bool CampMonth;                       // 夏合宿の月(故障率 ×1.3・経験値 ×1.5。03 §7)
        public int GuidanceLeft;                     // 今月に残る指導ポイント
        public bool PlanAuto;
        public RaceKind? OutlookRace;                // 『次の大会の調子の見込み』の大会(8 週以内で自校に出場権がある次の大会。なければ null)
        public int OutlookWeeks;                     // その大会の週まであと何週
        public List<GroupPlanView> Groups = new List<GroupPlanView>();
    }
}
