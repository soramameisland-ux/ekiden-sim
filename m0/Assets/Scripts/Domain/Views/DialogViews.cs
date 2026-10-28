using System.Collections.Generic;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Race;

namespace Ekiden.Domain.Views
{
    /// <summary>選択肢の効果 1 つ(06 §5.2)。R-15 の『効果の要約』は画面が文字列のキーから組み立てる(やる気 +1 → 『やる気+1』など)。ここは構造だけを返す。</summary>
    public sealed class EffectView
    {
        public EffectOp Op;
        public Target To;                           // 対象('R' = 本人・'All' = 全員・'Top10' = 主力 …)
        public int Amount;                          // mot・fat・bond・bondg・funds・scout・guide の変化量
        public double? Probability;                 // 確率つきの効果(mot・fat・xp・skill)
        public XpStatMode StatMode;                 // xp: 能力の決め方
        public Stat? Stat;                          // xp(Fixed のとき)
        public int Xu;                              // xp: 経験値(xu ÷ 10 を表示)
        public string SkillId;                      // skill: 'skill.…'
        public SkillRank? SkillRank;
        public InjurySeverityKind? Severity;        // inj
        public ModKind? Mod;                        // mod
        public double ModValue;
        public int Weeks;                           // mod・ob_role: 期間(週)
        public ObRole? Role;                        // ob_role
    }

    public sealed class ChoiceView
    {
        public string Id = "";
        public string LabelKey = "";                // '{イベント ID}.choice.{Id}'
        public bool Enabled;                        // req を満たす(満たさなければ暗くする)
        public bool IsDefault;                      // 『おまかせの既定』の印(06 §7.4)
        public List<EffectView> Effects = new List<EffectView>();       // 要約に出す最大 3 つ(R-15)
        public int MoreEffects;                     // あふれた分(『ほか n件』)
    }

    /// <summary>イベント(08a SC-36)。</summary>
    public sealed class EventDialogView
    {
        public string EventId = "";
        public string TitleKey = "";
        public EventCategory Category;
        public bool Interactive;                    // 選択肢が 2 つ以上(1 つなら『OK』だけのお知らせ)
        public bool Awakening;                      // 特殊能力の覚醒(金・青・赤の枠。I-53)
        public string SkillId;                      // 覚醒した特殊能力('skill.…')
        public SkillRank? SkillRank;
        public RunnerRowView Runner, Partner;       // 当事者(2 人のイベントは両方)
        public readonly List<ChoiceView> Choices = new List<ChoiceView>();
    }

    /// <summary>コンディション警告の 1 人(SC-41)。</summary>
    public sealed class WarnRowView
    {
        public RunnerRowView Row;
        public int Fatigue;                         // いま
        public int NextFatigue;                     // 次の週の終わりの予測(いまの計画のとおりなら。正確)
        public bool HasIndividualMenu;              // 個別指導のメニューが指定されている
        public bool SafetyNext;                     // 次の週の終わりに疲労 85 以上(安全網が働く水準)になる見込み。いまのモデルでは疲労 85 未満から 1 週で届くことはなく、通常は false
        public double InjuryNext;                   // 次の週に故障が起きる確率(R-05)
    }

    public sealed class ConditionWarnView
    {
        public int Threshold;                       // 警告の値(70。合宿の月は 80)
        public bool Camp;
        public readonly List<WarnRowView> Rows = new List<WarnRowView>();
    }

    /// <summary>故障の知らせ(SC-42)。</summary>
    public sealed class InjuryDialogView
    {
        public RunnerRowView Row;
        public InjurySeverity Severity;
        public int WeeksLeft;
        public bool Registered;                     // 次の大会の登録メンバー
        public RaceKind? NextRace;
        public int WeeksUntilRace;
        public bool MissesRace;                     // 大会までに治らない
        public bool InOrder;                        // 提出したオーダーの先発(オーダーを出していなければ false = おまかせの配置は、故障者を外して組み直される)
        public int? Section;                        // 先発の区間
        public readonly List<OrderCandidateView> Substitutes = new List<OrderCandidateView>();    // 代役の候補(その区間の適性の高い順。故障中でない選手)
    }
}
