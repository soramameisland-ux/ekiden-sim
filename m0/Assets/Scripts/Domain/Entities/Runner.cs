using System.Collections.Generic;
using Ekiden.Domain.Team;

namespace Ekiden.Domain.Entities
{
    public enum RecruitSource { Normal = 0, Ticket = 1 }

    /// <summary>その週の大会の、選手ごとの結果(06 §3.2 の race.*)。大会のあと以外は 0。</summary>
    public struct RaceFlags
    {
        public int InLineup, Brake, SectionRank, SectionWin, Record;
    }

    /// <summary>選手(02 §4.1)。M0 の段階3では、週の解決(03)に要る項目と個性・素養だけを持つ。残りは段階を進めながら足す。</summary>
    public sealed class Runner
    {
        public int Id;
        public int[] Stats = new int[StatInfo.Count];
        public int[] Potential = new int[StatInfo.Count];
        public int[] Xp = new int[StatInfo.Count];                 // 0.1 XP 単位(xu)
        public int Fatigue;
        public int Motivation = 3;
        public int Grade = 1;
        public GrowthType GrowthType = GrowthType.Normal;
        public int TaperWeeks;
        public bool[] SharpenHistory = new bool[3];
        public InjurySeverity InjurySeverity = InjurySeverity.None;
        public int InjuryWeeksLeft;
        public readonly HashSet<string> Skills = new HashSet<string>();
        public double BondAvg;
        public int Form;                                           // −2〜+2
        public int WeekLevelups;

        // 出身・経歴(02 §4.1)と、06 の条件式・イベントが読む項目
        public RecruitSource RecruitSource;                        // 入部の経路(招聘の印。卒業して OB になっても残す。07 §11)
        public Tickets.TicketKind TicketKind;                      // RecruitSource = Ticket のときの種別
        public int Pref;                                           // 出身県(1〜47。留学生は 0)
        public int HighSchool;                                     // 出身高校(0 = なし)
        public bool Overseas, Far, Near;                           // 留学生 / 遠方出身(距離帯2以上または海外)/ 近郊出身(距離帯0)
        public int EnrollWeek;                                     // 入学した通算週
        public int Races, SectionWins, Brakes;                     // 通算の出走数・区間賞・ブレーキ(Runner.career)
        public int RelayWins;                                      // 通算の大会優勝(Runner.career.relay_wins。規則には作用しない)
        // 月の途中経過(03 §7.2 のやる気の規則 M1・M6 と、夏合宿の記録 06 §10)。月末に使って消す
        public int MonthGain;                                      // その月の能力アップの合計(M6)
        public int HighFatigueWeeks;                               // 疲労 85 以上の連続週数(M1)
        public bool OverloadPending;                               // 疲労 85 以上が 2 週続いた(その月の最終週にやる気 −1。M1)
        public int CampMaxFatigue;                                 // 夏合宿の月の疲労の最大値(camp_overload の判定)
        public int Country;                                        // 出身国(留学生のみ。1〜24。日本人は 0)
        public int InjuryAge = 99;                                 // 故障してからの週(故障がなければ 99)
        public int Group = 2;                                      // 0=A 1=B 2=C
        public bool Captain;
        public int PRank = 99;                                     // 力の順位(1 = 最強。TeamState.Refresh が付ける)
        public bool Regular;                                       // 力の上位10人で故障していない
        public double GapAvg;                                      // 隠し: 素質と能力の差の平均
        public RaceFlags RaceFlags;                                // その週の大会の結果(大会のあと以外は 0)
        public int LastEventMonth = -1;                            // 最後にイベントの当事者になった月(年度×12+月)
        public readonly Dictionary<string, int> Counters = new Dictionary<string, int>();   // 06 §10 のカウンター(ctr)
        public readonly HashSet<string> EventOnce = new HashSet<string>();                  // once = runner のイベントで経験したもの
        public readonly Dictionary<string, int> EventCooldown = new Dictionary<string, int>(); // イベントごとの最後の週(当事者ごとのクールダウン)

        public int Ctr(string name)
        {
            int v;
            return Counters.TryGetValue(name, out v) ? v : 0;
        }

        public void AddCtr(string name, int d)
        {
            Counters[name] = Ctr(name) + d;
        }

        // 名前・外見・性格(02 §4.1)。表示の層だけで、規則には作用しない(生成は look_seed から。名前の表は M1)
        public string FamilyName = "", GivenName = "", FamilyKana = "", GivenKana = "";
        public Appearance Look;
        public AppearanceOverride LookOverride;                    // null = 変えていない
        public uint LookSeed;
        public readonly List<string> Traits = new List<string>();  // 性格タグ(最大 3。02 §2 Trait)

        // 個性・素養(10)。派生値は SetIndividuality で作る(保存しない)
        public Style? Style { get; private set; }
        public Gift[] Gifts { get; private set; } = new Gift[0];
        public bool GiftRevealed;
        public double[] Gm { get; private set; }                   // null = 乗数なし(1.0)
        public double BondK { get; private set; } = 0.15;
        public double IndMult { get; private set; } = 1.0;

        public bool IsInjured { get { return InjurySeverity != InjurySeverity.None; } }

        /// <summary>出身と自校の所在県から遠方・近郊を決める(06 §3.2): 海外または距離帯 2 以上 = 遠方、距離帯 0 = 近郊。保存しない派生値(セーブの読み込みで作り直す)。schoolPref = 0(未設定)のときは両方 false(海外は遠方)。</summary>
        public void DeriveDistance(int schoolPref)
        {
            Far = Near = false;
            if (Overseas) { Far = true; return; }
            if (schoolPref < 1 || schoolPref > 47 || Pref < 1 || Pref > 47) return;
            int band = World.Geo.Band(World.Geo.ById(Pref).Region, World.Geo.ById(schoolPref).Region);
            Far = band >= 2;
            Near = band == 0;
        }

        /// <summary>すべての項目の複製(予測・検算用。派生値は作り直す)。</summary>
        public Runner Clone()
        {
            var c = new Runner
            {
                Id = Id, Fatigue = Fatigue, Motivation = Motivation, Grade = Grade, GrowthType = GrowthType, TaperWeeks = TaperWeeks, InjurySeverity = InjurySeverity,
                InjuryWeeksLeft = InjuryWeeksLeft, BondAvg = BondAvg, Form = Form, WeekLevelups = WeekLevelups, GiftRevealed = GiftRevealed,
                RecruitSource = RecruitSource, TicketKind = TicketKind, Pref = Pref, HighSchool = HighSchool, Overseas = Overseas, Far = Far, Near = Near,
                EnrollWeek = EnrollWeek, Races = Races, SectionWins = SectionWins, Brakes = Brakes, InjuryAge = InjuryAge, Group = Group, Captain = Captain,
                PRank = PRank, Regular = Regular, GapAvg = GapAvg, RaceFlags = RaceFlags, LastEventMonth = LastEventMonth,
                RelayWins = RelayWins, MonthGain = MonthGain, HighFatigueWeeks = HighFatigueWeeks, OverloadPending = OverloadPending, CampMaxFatigue = CampMaxFatigue, Country = Country, FamilyName = FamilyName, GivenName = GivenName, FamilyKana = FamilyKana, GivenKana = GivenKana,
                Look = Look, LookOverride = LookOverride == null ? null : new AppearanceOverride { HairStyle = LookOverride.HairStyle, HairColor = LookOverride.HairColor }, LookSeed = LookSeed,
            };
            c.Traits.AddRange(Traits);
            System.Array.Copy(Stats, c.Stats, Stats.Length);
            System.Array.Copy(Potential, c.Potential, Potential.Length);
            System.Array.Copy(Xp, c.Xp, Xp.Length);
            c.SharpenHistory = (bool[])SharpenHistory.Clone();
            foreach (string s in Skills) c.Skills.Add(s);
            foreach (var kv in Counters) c.Counters[kv.Key] = kv.Value;
            foreach (string s in EventOnce) c.EventOnce.Add(s);
            foreach (var kv in EventCooldown) c.EventCooldown[kv.Key] = kv.Value;
            c.SetIndividuality(Style, Gifts);
            return c;
        }

        /// <summary>個性・素養を付ける(派生値を作り直す)。素質の上限の加算は生成の側(Individuality.AddGiftPotential)。</summary>
        public void SetIndividuality(Style? style, IReadOnlyList<Gift> gifts)
        {
            Style = style;
            Gifts = gifts == null ? new Gift[0] : new List<Gift>(gifts).ToArray();
            if (style == null)
            {
                Gm = null; BondK = 0.15; IndMult = 1.0;
            }
            else
            {
                Gm = Individuality.GrowthMult(style.Value, Gifts);
                BondK = Individuality.BondK(style.Value);
                IndMult = Individuality.IndMult(style.Value);
            }
        }
    }
}
