using System;
using System.Collections.Generic;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.Training;

namespace Ekiden.Domain.Team
{
    /// <summary>年度内の週の番号と大会週(04・05)。通算週 abs_week は 0 始まり、1 年度 = 48 週(4 月第 1 週が 0)。</summary>
    public static class Calendar
    {
        public const int WeeksPerYear = 48;
        public static readonly int[] RaceWeeks = { 25, 28, 36 };          // 第一戦・第二戦・第三戦の年内の週

        public static int FloorDiv(int a, int b)
        {
            int q = a / b;
            return (a % b != 0 && ((a < 0) != (b < 0))) ? q - 1 : q;
        }

        public static int FloorMod(int a, int b)
        {
            int m = a % b;
            return m < 0 ? m + b : m;
        }

        /// <summary>次の大会までの週数(大会週は 0)と、その種別(1〜3)。同じ週数なら種別の小さい方。</summary>
        public static void NextRace(int yearWeek, out int weeks, out int kind)
        {
            weeks = int.MaxValue;
            kind = 0;
            for (int i = 0; i < RaceWeeks.Length; i++)
            {
                int w = FloorMod(RaceWeeks[i] - yearWeek, WeeksPerYear);
                if (w < weeks) { weeks = w; kind = i + 1; }
            }
        }
    }

    /// <summary>選手どうしの絆(0〜1000)。値が 0 の組は持たない。</summary>
    public sealed class BondTable
    {
        private readonly Dictionary<long, int> _v = new Dictionary<long, int>();

        public static long Key(int a, int b)
        {
            return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        }

        public int Get(int a, int b)
        {
            int v;
            return _v.TryGetValue(Key(a, b), out v) ? v : 0;
        }

        public void Add(int a, int b, int d)
        {
            long k = Key(a, b);
            int cur;
            _v.TryGetValue(k, out cur);
            int v = Math.Max(0, Math.Min(1000, cur + d));
            if (v != 0) _v[k] = v;
            else _v.Remove(k);
        }

        public void Set(int a, int b, int v)
        {
            long k = Key(a, b);
            if (v != 0) _v[k] = v;
            else _v.Remove(k);
        }

        public int Count { get { return _v.Count; } }

        /// <summary>(小さい方の ID, 大きい方の ID, 絆)を ID の昇順に並べて返す。保存と比較用。</summary>
        public List<int[]> Items()
        {
            var list = new List<int[]>();
            foreach (var kv in _v) list.Add(new[] { (int)(kv.Key >> 32), (int)(kv.Key & 0xFFFFFFFFL), kv.Value });
            list.Sort((x, y) => x[0] != y[0] ? x[0].CompareTo(y[0]) : x[1].CompareTo(y[1]));
            return list;
        }

        public void RemoveAllNotIn(ISet<int> ids)
        {
            var del = new List<long>();
            foreach (long k in _v.Keys)
                if (!ids.Contains((int)(k >> 32)) || !ids.Contains((int)(k & 0xFFFFFFFFL))) del.Add(k);
            foreach (long k in del) _v.Remove(k);
        }
    }

    /// <summary>一定の期間だけ効く補正(06 §6)。</summary>
    public sealed class TimedModifier
    {
        public const int TeamTarget = -1;       // 対象がチーム全体(選手の ID は 1 以上)

        public ModKind Kind;
        public int To;                          // TeamTarget か選手の ID
        public double Value;
        public int Until;                       // この週になったら消える(絶対週)
        public string Src;                      // 発生源のイベント ID
    }

    /// <summary>OB(02 §4.7。06 §8 で役割を持つ)。自校の卒業生だけを持つ(他校は卒業した時点で破棄。09 §5.2)。
    /// <c>Pref</c> = 出身の添字(1〜47 = 県 / 0 = 海外。<c>ob_network</c> の添字と同じ)。<c>Power</c> = 卒業時の力 = <see cref="TeamState.LongP(int[])"/>(<c>FinalStats</c>)。保存形には持たない(<c>FinalStats</c> から導出)。</summary>
    public sealed class OBRecord
    {
        public int Pref;
        public CareerPath CareerPath = CareerPath.Employment;
        public double Power;                    // 卒業時の力
        public ObRole Role = ObRole.None;
        public int Until;                       // 役割の期限(絶対週)

        /// <summary>実業団に進んだ(<see cref="CareerPath"/> が Corporate)。false を代入すると Corporate 以外にする(Corporate だったときは Employment)。</summary>
        public bool Corp
        {
            get { return CareerPath == CareerPath.Corporate; }
            set { CareerPath = value ? CareerPath.Corporate : (CareerPath == CareerPath.Corporate ? CareerPath.Employment : CareerPath); }
        }

        // 殿堂・レジェンド・OB 一覧のための記録(02 §4.7)。規則には作用しない
        public int RunnerId;
        public string Name = "", Kana = "", HighSchoolName = "";
        public int SchoolId, EnrollYear, GradYear;
        public int[] FinalStats = new int[StatInfo.Count];
        public readonly List<string> Skills = new List<string>();
        public int T5000, T10000, THalf;        // 卒業時の記録(導出値。ds)
        public int RaceWins, SectionWins, Records;
        public bool HallOfFame;
        public RecruitSource RecruitSource;
        public Appearance Look;
        public Style? Style;
        public Gift[] Gifts = new Gift[0];

        /// <summary>卒業する選手から作る(年度末の卒業。06 §8 の役割の選出に使う『卒業時の力』は <see cref="TeamState.LongP(int[])"/>)。</summary>
        public static OBRecord FromGraduate(Runner r, int gradYear, int enrollYear, CareerPath path, string highSchoolName = "", bool hallOfFame = false)
        {
            var o = new OBRecord
            {
                Pref = r.Overseas ? 0 : r.Pref, CareerPath = path, RunnerId = r.Id, Name = r.FamilyName + r.GivenName, Kana = r.FamilyKana + r.GivenKana, HighSchoolName = highSchoolName,
                EnrollYear = enrollYear, GradYear = gradYear, RaceWins = r.RelayWins, SectionWins = r.SectionWins, Records = 0, HallOfFame = hallOfFame,
                RecruitSource = r.RecruitSource, Look = r.Look, Style = r.Style, Gifts = (Gift[])r.Gifts.Clone(),
            };
            System.Array.Copy(r.Stats, o.FinalStats, StatInfo.Count);
            foreach (string k in r.Skills) o.Skills.Add(k);
            o.Skills.Sort(System.StringComparer.Ordinal);
            o.Power = TeamState.LongP(o.FinalStats);
            o.T5000 = Screens.DisplayRules.DerivePb(o.FinalStats, Screens.DisplayRules.PbEvent.M5000);
            o.T10000 = Screens.DisplayRules.DerivePb(o.FinalStats, Screens.DisplayRules.PbEvent.M10000);
            o.THalf = Screens.DisplayRules.DerivePb(o.FinalStats, Screens.DisplayRules.PbEvent.Half);
            return o;
        }
    }

    /// <summary>イベントの記録(回数と最後の週)。</summary>
    public sealed class EventRecord
    {
        public int Count;
        public int Last;
    }

    /// <summary>自校の状態(選手・絆・資金・補正・OB・イベントの記録)。06 のイベントはこれを読み書きする。</summary>
    public sealed class TeamState
    {
        public ulong Seed;                      // マスタ乱数(検算用の rival_same_hs の近似に使う)
        public int AbsWeek;
        public readonly List<Runner> Runners = new List<Runner>();
        public readonly BondTable Bonds = new BondTable();
        public FacilityLevels Fac;
        public int Funds;                       // 万円
        public double Prestige;
        public double Morale;
        public int CoachRank;
        public double ScoutPts;                 // 今年度のスカウトポイント(補正は含まない)
        public int GuidanceLeft = 4;            // 今月に残る指導ポイント

        public readonly int[] Rank = { 99, 99, 99 };       // 今年度の第一〜第三戦の順位(まだなら 99)
        public readonly int[] LastRank = { 8, 8, 8 };      // 前年度の順位(なければ 8)
        public int YearPoints;

        public readonly Dictionary<string, int> YearCounts = new Dictionary<string, int>();   // 今年度に起こった回数(イベントごと)
        public int YearTotal;                   // 今年度に起こった回数(exempt を除く)
        public readonly Dictionary<string, EventRecord> Fired = new Dictionary<string, EventRecord>();
        public readonly Dictionary<string, Dictionary<long, int>> PairCooldown = new Dictionary<string, Dictionary<long, int>>();   // イベント → (2人の鍵 → 最後の週)
        public readonly List<TimedModifier> Mods = new List<TimedModifier>();
        public readonly List<OBRecord> Obs = new List<OBRecord>();
        public readonly HashSet<int> Bench = new HashSet<int>();     // 次の大会で欠場(04 §5.3)
        public readonly HashSet<int> Forced = new HashSet<int>();    // 次の第三戦の登録に追加(04 §4)

        // ---- 自校の運営状態のうち、保存に要る項目(02 §4.4・§4.9。規則を持たない画面の状態も含む)
        public readonly List<EventLogEntry> EventLog = new List<EventLogEntry>();   // 起こったイベントと選択(最新 K_EVENT_LOG_MAX 件)
        public readonly TicketState Tickets = new TicketState();                    // 特別招聘券(07 §3)
        public ScenarioRef Scenario = new ScenarioRef();
        public ClearInfo Cleared;                                                   // null = 未クリア。一度設定したら変えない
        public int NextRunnerId = 1, NextStaffId = 1;                               // 実行時 ID の採番(I-11)
        public string ManagerName = "監督";                                         // 8 文字まで(08 R-13)
        public int SchoolPref;                                                      // 自校の所在県(1〜47)。遠方・近郊の判定に使う
        public readonly string[] StaffNames = { "", "", "", "" };                   // StaffRoles.Names の順
        public AnnualPlan Plan = new AnnualPlan();
        public readonly string[] MonthlyPlan = { "th.base_build", "th.base_build", "th.base_build" };   // グループ A / B / C の今月のテーマ
        public MenuSpec[][] CustomWeeks;                                            // null | [3 グループ][4 週](th.custom のとき)
        public readonly SortedDictionary<int, IndividualMenu> IndividualMenus = new SortedDictionary<int, IndividualMenu>();   // 選手 ID → 個別指導のメニュー
        public readonly SortedSet<int> Talked = new SortedSet<int>();               // 今月に面談した選手
        public TeamSettings Settings = new TeamSettings();
        public readonly List<int>[] Entries = { new List<int>(), new List<int>(), new List<int>() };   // 次の第一〜第三戦の登録メンバー(04 §4。締切は大会の 4 週前。大会が終わったら空にする)

        /// <summary>イベントの履歴に足す。最新 <see cref="EventConstants.K_EVENT_LOG_MAX"/> 件を超えたぶんは古い方から捨てる。</summary>
        public void LogEvent(string id, int week, string choice)
        {
            EventLog.Add(new EventLogEntry { Id = id, Week = week, Choice = choice });
            if (EventLog.Count > EventConstants.K_EVENT_LOG_MAX) EventLog.RemoveRange(0, EventLog.Count - EventConstants.K_EVENT_LOG_MAX);
        }

        public Runner FindRunner(int id)
        {
            foreach (Runner r in Runners) if (r.Id == id) return r;
            return null;
        }

        // ---- 絆
        public int Bond(Runner a, Runner b) { return Bonds.Get(a.Id, b.Id); }

        public void AddBond(Runner a, Runner b, int d) { Bonds.Add(a.Id, b.Id, d); }

        /// <summary>入学した選手の初期の絆: 同じ県 +100、同じ高校でさらに +100(03 §7.1)。</summary>
        public void InitBonds(Runner r)
        {
            foreach (Runner o in Runners)
            {
                if (ReferenceEquals(o, r)) continue;
                int b = 0;
                if (r.Pref != 0 && r.Pref == o.Pref)
                {
                    b += 100;
                    if (r.HighSchool == o.HighSchool) b += 100;
                }
                if (b != 0) Bonds.Set(r.Id, o.Id, b);
            }
        }

        // ---- 補正
        /// <summary>チーム全体への補正の和(期限内のもの)。</summary>
        public double ModSum(ModKind kind)
        {
            double s = 0;
            foreach (TimedModifier m in Mods)
                if (m.Kind == kind && m.To == TimedModifier.TeamTarget && m.Until > AbsWeek) s += m.Value;
            return s;
        }

        /// <summary>週の解決に渡す補正(06 §6): 対象がチーム全体か本人の、期限内の補正を合成する。<c>E_mult</c> と <c>inj_mult</c> は積、<c>r_add</c> と <c>form_add</c> は和。種類ごとの範囲に収める。</summary>
        public WeekMods ModsFor(Runner r)
        {
            var wm = WeekMods.None;
            foreach (TimedModifier m in Mods)
            {
                if (m.Until <= AbsWeek || (m.To != TimedModifier.TeamTarget && m.To != r.Id)) continue;
                switch (m.Kind)
                {
                    case ModKind.EMult: wm.EMult *= m.Value; break;
                    case ModKind.RAdd: wm.RAdd += m.Value; break;
                    case ModKind.InjMult: wm.InjMult *= m.Value; break;
                    case ModKind.FormAdd: wm.FormAdd += m.Value; break;
                }
            }
            wm.EMult = EventConstants.ClampMod(ModKind.EMult, wm.EMult);
            wm.RAdd = EventConstants.ClampMod(ModKind.RAdd, wm.RAdd);
            wm.InjMult = EventConstants.ClampMod(ModKind.InjMult, wm.InjMult);
            wm.FormAdd = EventConstants.ClampMod(ModKind.FormAdd, wm.FormAdd);
            return wm;
        }

        // ---- OB
        public int ObCount { get { return Obs.Count; } }

        public int ObCorpCount
        {
            get
            {
                int n = 0;
                foreach (OBRecord o in Obs) if (o.Corp) n++;
                return n;
            }
        }

        /// <summary>選手の出身県の OB ネットワークのレベル(0〜5)= min(5, floor(√人数))。</summary>
        public int ObLevel(Runner r)
        {
            int n = 0;
            foreach (OBRecord o in Obs) if (o.Pref == r.Pref) n++;
            return Math.Min(5, (int)Math.Sqrt(n));
        }

        // ---- 派生値
        /// <summary>Long の区間適性の簡易値(06 の p_rank・主将の選出に使う。04 §5.1 の Long の重み)。足す順は D-12 のとおり STA・PAC・SPD・MEN・KCK。</summary>
        public static double LongP(Runner r) { return LongP(r.Stats); }

        public static double LongP(int[] stats)
        {
            double s = 0.0;
            s += 0.35 * stats[(int)Stat.STA];
            s += 0.25 * stats[(int)Stat.PAC];
            s += 0.20 * stats[(int)Stat.SPD];
            s += 0.10 * stats[(int)Stat.MEN];
            s += 0.10 * stats[(int)Stat.KCK];
            return s;
        }

        /// <summary>推奨の編成(08 R-04 <c>auto_groups</c>)を適用する: 力 <c>P_long</c> の高い順(同値は ID の小さい方が上)に、上位 10 人を A・次の 10 人を B・残りを C。
        /// 新規ゲームの開始時と、年度初めの『推奨の編成を適用』(おまかせでは自動)で呼ぶ。以後は自動で動かさない(新入部員は C)。</summary>
        public void AssignRecommendedGroups()
        {
            var pl = new List<KeyValuePair<int, double>>();
            foreach (Runner r in Runners) pl.Add(new KeyValuePair<int, double>(r.Id, LongP(r)));
            Dictionary<int, int> g = Screens.PlanRules.AutoGroups(pl);
            foreach (Runner r in Runners) r.Group = g[r.Id];
        }

        /// <summary>力の順位・レギュラー・素質との差・同じグループの絆の平均(と、assignGroups のときはグループ)を付け直す(週の最初)。
        /// assignGroups = true は 06 の参照モデルの <c>refresh</c> と同じ(毎週、力の順位でグループを決め直す。差分テスト・試験用の週ループ)。ゲーム本体は false(グループは保存される編成。08 R-04)。</summary>
        public void Refresh(bool assignGroups = true)
        {
            int n = Runners.Count;
            var keys = new double[n];
            var order = new int[n];
            for (int i = 0; i < n; i++) { keys[i] = -LongP(Runners[i]); order[i] = i; }
            Array.Sort(order, (a, b) =>
            {
                int c = keys[a].CompareTo(keys[b]);
                return c != 0 ? c : a.CompareTo(b);        // 同点は並びの順(安定な並べ替え)
            });
            for (int i = 0; i < n; i++)
            {
                Runner r = Runners[order[i]];
                r.PRank = i + 1;
                if (assignGroups) r.Group = i < 10 ? 0 : (i < 20 ? 1 : 2);
                r.Regular = i < 10 && !r.IsInjured;
                int gap = 0;
                for (int k = 0; k < StatInfo.Count; k++) gap += r.Potential[k] - r.Stats[k];
                r.GapAvg = gap / 10.0;
            }
            foreach (Runner r in Runners)
            {
                int cnt = 0;
                long sum = 0;
                foreach (Runner o in Runners)
                {
                    if (ReferenceEquals(o, r) || o.Group != r.Group) continue;
                    cnt++;
                    sum += Bonds.Get(r.Id, o.Id);
                }
                r.BondAvg = cnt > 0 ? (double)sum / cnt : 0.0;
            }
        }

        /// <summary>期限を過ぎた補正とOBの役割を外す(週の最初)。</summary>
        public void ExpireTimed()
        {
            Mods.RemoveAll(m => m.Until <= AbsWeek);
            foreach (OBRecord o in Obs)
                if (o.Role != ObRole.None && o.Until <= AbsWeek) o.Role = ObRole.None;
        }

        /// <summary>年度の最初に、今年度の記録を前年度へ送って空にする。</summary>
        public void RollYear()
        {
            for (int i = 0; i < 3; i++)
            {
                LastRank[i] = Rank[i] == 99 ? 8 : Rank[i];
                Rank[i] = 99;
            }
            YearCounts.Clear();
            YearTotal = 0;
            YearPoints = 0;
        }
    }
}
