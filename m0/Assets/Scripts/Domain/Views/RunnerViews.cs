using System.Collections.Generic;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Race;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Tickets;

namespace Ekiden.Domain.Views
{
    /// <summary>特殊能力(名前のキーは <c>{Id}.name</c>)。色 = 金・青・赤(<see cref="Rank"/>)。</summary>
    public sealed class SkillView
    {
        public string Id;                   // "skill.mountain_god" など
        public SkillRank Rank;
    }

    /// <summary>選手の行(08 §7.1 <c>RunnerRow</c>。部員一覧・エントリー・オーダー・卒業生などで共通)。画面に出す値を、規則(R-01・R-17 ほか)で作った形で持つ。</summary>
    public sealed class RunnerRowView
    {
        public int Id;
        public string FamilyName = "", GivenName = "", FamilyKana = "", GivenKana = "";
        public int Grade;
        public bool Captain;
        public int Group;                           // 0 = A / 1 = B / 2 = C
        public double P;                            // 総合力(P_long)
        public char Rank;                           // R-01
        public int PRank;                           // 力の順位(1 = 最強)
        public bool Regular;                        // 力の上位 10 人で故障していない
        public int Fatigue;
        public int Form;                            // −2〜+2(絶不調〜絶好調)
        public int Motivation;                      // 1〜5
        public InjurySeverity Injury;
        public int InjuryWeeksLeft;
        public int SkillCount, GoldSkills, BlueSkills, RedSkills;
        public int Pref;                            // 出身県(留学生は 0)
        public string PrefName = "";
        public int HighSchool;                      // 出身高校 ID(0 = なし)
        public string HighSchoolNameKey;            // "hs.{id}.name"(文字列テーブル)。なければ null
        public bool Overseas;
        public int Country;                         // 出身国(留学生のみ 1〜24)
        public string CountryName = "";
        public List<string> StyleChips = new List<string>();      // R-17('form.speed' など。三つとも普通なら 'standard')。個性が付いていなければ空
        public List<string> Traits = new List<string>();          // R-16 の性格タグ('serious' など)
        public bool Ticket;                         // 特別招聘の印(07 §11)
        public TicketKind TicketKind;
        public bool MountainAdvice;                 // 登坂 / 下りの素養が判明している(『山対策のグループ(C)で伸ばせます』の案内。R-17)
        public bool SameHometownAsSelected;         // 選んでいる選手と同郷(印 ●)
        public bool SameHighSchoolAsSelected;       // 同じ高校(印 ◆)
    }

    public sealed class StatView
    {
        public Stat Stat;
        public int Value;
        public char Rank;                           // R-01
        public PotentialHint Hint;                  // R-02(素質の数値は出さない)
        public int DeltaThisMonth;                  // 今月のここまでの増減(いまの能力 − 今月の初めの能力)
        public int DeltaLastMonth;                  // 前の月の増減(今月の初めの能力 − 前の月の初めの能力)
    }

    /// <summary>区間適性(04 §5.1)の 1 つ。◎ ≥ 70 / 〇 ≥ 60 / △。</summary>
    public sealed class FitView
    {
        public SectionType Type;
        public double Value;                        // 特殊能力の加算は含めない(区間ごとの値は GameSession.SectionFit)
        public char Mark;
    }

    public sealed class BondView
    {
        public int RunnerId;
        public string Name = "";
        public int Value;                           // 0〜1000
        public bool SameGroup, SameHometown, SameHighSchool;
    }

    /// <summary>選手詳細(SC-19)。</summary>
    public sealed class RunnerDetailView
    {
        public RunnerRowView Row;
        public List<StatView> Stats = new List<StatView>();       // SPD STA PAC KCK CLB DWN CON MEN REC DUR の順
        public List<FitView> Fits = new List<FitView>();          // Fast / Mid / Long / Climb / Descent の順
        public int Pb5000, Pb10000, PbHalf;                       // R-03(ds。Fmt.Time で整形)
        public List<SkillView> Skills = new List<SkillView>();
        public List<BondView> Bonds = new List<BondView>();       // 絆の大きい順(最大 8 人)
        public int Races, SectionWins, Brakes, RelayWins;         // 経歴
        public int EnrollWeek;
        public GrowthType Growth;
        public Style? Style;
        public List<Gift> Gifts = new List<Gift>();               // 判明したものだけ(10 §8)
        public bool GiftRevealed;
        public char ThemeFitMark;                                 // 自分のグループの今月のテーマとの相性(R-17)
        public string ThemeId;
        public Team.MenuSpec IndividualMenu;                      // 個別指導のメニュー(なければ null)
        public int IndividualUntilAbsWeek;                        // その期限(通算週)
        public bool Talked;                                       // 今月に面談した
    }
}
