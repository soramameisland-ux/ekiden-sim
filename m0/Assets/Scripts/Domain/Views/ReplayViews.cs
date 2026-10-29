using System.Collections.Generic;
using Ekiden.Domain.Race;

namespace Ekiden.Domain.Views
{
    /// <summary>ダイジェストの再生(08 R-14.4)の 1 中継所分。区間 <see cref="Section"/> の終わりでの順位表・見どころ・自校の走者の 1 行。</summary>
    public sealed class ReplayStepView
    {
        public int Section;                         // 0 始まり
        public bool IsFinal;                        // 最終区間(フィニッシュの確定表示が続く)
        public bool IsHalfEnd;                      // 第三戦の往路の最後の区間(5 区。『往路の終わり』の表示が続く)
        public bool Peak;                           // 山場: 見どころに首位交代・区間記録・大会記録のどれかがある(演出を長くする)
        public readonly List<StandingLineView> Standings = new List<StandingLineView>();    // 中継所の順位表(先頭 3 校 + 自校の前後 1 校)
        public readonly List<HighlightView> Highlights = new List<HighlightView>();         // 見どころ(最大 2 件。04 §9.3 の優先順 = 自校が先)
        public OwnLegView OwnLeg;                   // 自校が出走したとき、この区間の自校の走者(区間タイム・区間順位・累積順位・出来事)。出場しなければ null
        public SectionPrizeView Prize;              // この区間の区間賞
    }

    /// <summary>大会の結果から組み立てた、ダイジェストの再生用のデータ(08 R-14。<c>GameSession.RaceReplay</c>)。保存しない。フルでレースを進めた大会も、結果から同じ形で作れる(あとから見る)。</summary>
    public sealed class RaceReplayView
    {
        public RaceKind Kind;
        public int Sections;
        public bool OwnEntered;
        public int OwnRank;                         // 自校の順位(出場しなければ 0)
        public int HalfSection = -1;                // 往路の最後の区間(第三戦だけ 4。ほかは −1)
        public readonly List<ReplayStepView> Steps = new List<ReplayStepView>();            // 区間の順
        public readonly List<ResultRowView> Finish = new List<ResultRowView>();            // フィニッシュの確定表示: 上位 3 校 + 自校(順位順)
    }
}
