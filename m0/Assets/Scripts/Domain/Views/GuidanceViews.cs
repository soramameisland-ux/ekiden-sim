using System.Collections.Generic;
using Ekiden.Domain.Game;
using Ekiden.Domain.Team;

namespace Ekiden.Domain.Views
{
    /// <summary>プリセットのメニュー(08 R-06。ケア = 03 の SAFE / 調整 = sharpen + pace_run + low)。</summary>
    public sealed class GuidancePresetView
    {
        public string Id = "";                      // "care" / "adjust"
        public MenuSpec Menu;
    }

    /// <summary>個別指導(SC-20。08 R-06)。対象の選手 1 人についての、面談とメニューの指定の可否・いまの指定・注意。</summary>
    public sealed class GuidanceView
    {
        public RunnerRowView Row;                   // 対象の要約(力・疲労・調子・やる気・故障)
        public int PointsLeft;                      // 今月に残る指導ポイント
        public int PointsMax;                       // 月の指導ポイント(主務で 4 / 5 / 6。05 §4.4)
        public bool Talked;                         // 今月に面談した
        public bool CanTalk;
        public Msg TalkWhy;                         // CanTalk の理由(false = 拒否の理由 err.guide.* / true = ヒント hint.guide.talk)
        public bool CanSetMenu;                     // 指定できる(ポイントがあり、故障中でない)
        public Msg MenuWhy;                         // 同上(err.guide.points / err.guide.injured / hint.guide.menu)
        public MenuSpec CurrentMenu;                // 指定中のメニュー(なければ null)
        public int CurrentUntilAbsWeek;
        public bool CanCancel;                      // 指定を取り消せる
        public bool CancelRefunds;                  // 取り消すとポイントが戻る(今週の指定で、まだ解決されていない)
        public bool SoloBonus;                      // 単独走◎: 個別指導の週は効率が高い(10 §5.2)
        public readonly List<GuidancePresetView> Presets = new List<GuidancePresetView>();
        public readonly List<Msg> Warnings = new List<Msg>();   // いま指定したら出る注意(調整メニューの上書き / 安全網が働かない)。メニューによらない
    }

    /// <summary>選んだメニューを今週から月末まで指定したときの予測(R-05。<see cref="GameSession.GuidancePreview"/>)。</summary>
    public sealed class GuidancePreviewView
    {
        public bool Valid;                          // メニューの検証(03 §5.5)を通った
        public Msg Why;                             // Valid が false のときの理由
        public char FitMark;                        // 相性の印(R-17。◎〇△)
        public int WeeksCovered;                    // 指定が効く週数(今週から月末まで)
        public int FatigueEnd;                      // 月末の疲労の予測
        public int FatigueMax;                      // 期間中の疲労の最大
        public int SafetyWeeks;                     // 安全網が働く週(指定は安全網の対象外なので、ふつうは 0)
        public double InjuryProb;                   // 期間中の故障の確率(合計)
        public int Levelups;                        // 能力が上がる回数の合計
        public int BaselineFatigueEnd;              // 指定しなかったとき(いまの月間計画)の月末の疲労
        public double BaselineInjuryProb;
        public int BaselineLevelups;
        public readonly double[] GainP = new double[5];     // 区間タイプ別の適性値の伸び(指定したとき − 指定する前)
        public readonly double[] BaselineGainP = new double[5];
    }
}
