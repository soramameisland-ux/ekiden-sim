using System.Collections.Generic;

namespace Ekiden.Domain.Events
{
    /// <summary>06 の定数表(§2。<c>K_EV_</c> 接頭辞)。初期値案。後でマスタから読む形に差し替える(09 §12.1)。</summary>
    public static class EventConstants
    {
        public const int K_EV_WEEK = 2;                 // 1週に起こるイベントの上限
        public const int K_EV_YEAR = 22;                // 1年度に起こるイベントの上限(exempt と覚醒は数えない)
        public const int K_EV_RUNNER_MONTH = 1;         // 1人が1ヶ月に当事者になれる回数
        public const double K_T_INTERACT = 12.0, K_T_NOTICE = 4.0;
        public const double K_AWAKEN_BLUE = 0.10, K_AWAKEN_GOLD = 0.05, K_AWAKEN_RED = 0.08;
        public const int K_XP_MAX = 400;                // 1つの効果で1人に与える XP の上限(xu)
        public const int K_FUNDS_MIN = -1500, K_FUNDS_MAX = 500;
        public const int K_MOD_WEEKS_MAX = 48;
        public const int K_EVENT_LOG_MAX = 50;          // 保存するイベントの履歴の件数(09 §2 の同名の定数と同じ)
        public const int MaxSkills = 8;                 // 1人が持てる特殊能力の数(02 §3.7)
        public const string SkillPrefix = "skill.";

        public static double AwakenP(SkillRank rank)
        {
            return rank == SkillRank.Blue ? K_AWAKEN_BLUE : rank == SkillRank.Gold ? K_AWAKEN_GOLD : K_AWAKEN_RED;
        }

        /// <summary>補正の種類ごとの範囲(06 §6)。</summary>
        public static void ModCap(ModKind kind, out double lo, out double hi)
        {
            switch (kind)
            {
                case ModKind.EMult: lo = 0.80; hi = 1.20; break;
                case ModKind.RAdd: lo = -0.06; hi = 0.06; break;
                case ModKind.InjMult: lo = 0.5; hi = 2.0; break;
                case ModKind.FormAdd: lo = -4; hi = 4; break;
                case ModKind.FundsYear: lo = -300; hi = 600; break;
                default: lo = -4; hi = 8; break;
            }
        }

        public static double ClampMod(ModKind kind, double v)
        {
            double lo, hi;
            ModCap(kind, out lo, out hi);
            return System.Math.Max(lo, System.Math.Min(hi, v));
        }
    }

    /// <summary>06 §10 のカウンター(<c>ctr(r, 'name')</c>)の名前。</summary>
    public static class Counters
    {
        public const string HillWeeks = "hill_weeks", Camps = "camps", CampOverload = "camp_overload", Injuries = "injuries", InjuryFree = "injury_free",
            PeakedRaces = "peaked_races", Overtakes = "overtakes", LeadSections = "lead_sections", PackSections = "pack_sections", SoloSections = "solo_sections",
            BigWins = "big_wins", EkidenGood = "ekiden_good";

        public static readonly string[] All =
        {
            HillWeeks, Camps, CampOverload, Injuries, InjuryFree, PeakedRaces, Overtakes, LeadSections, PackSections, SoloSections, BigWins, EkidenGood,
        };
    }

    public enum EventCategory { Team, Management, Individual, Race, Hometown, Graduation, Awakening }

    /// <summary>当事者の範囲(06 §4.1)。</summary>
    public enum EventScope { Team, Runner, Pair }

    /// <summary>normal = 毎週 / race = 大会週の後処理のあと(race.* が使える)。</summary>
    public enum EventPhaseKind { Normal, Race }

    public enum OnceKind { None, Runner, Save }

    public enum EffectOp { Mot, Fat, Xp, Bond, Bondg, Inj, Skill, Funds, Scout, Guide, Mod, Captain, Bench, Entry, ObRole }

    /// <summary>効果の対象(06 §5.1)。</summary>
    public enum Target { None, R, P, All, G1, G2, G3, G4, Top10, SamePref, SameGroup, Far, Near, Overseas, Captain, Team }

    /// <summary>補正の種類(06 §6)。</summary>
    public enum ModKind { EMult, RAdd, InjMult, FormAdd, FundsYear, ScoutPts }

    public enum CaptainSelect { TopBond, TopP, TopMen }

    public enum ObRole { None, Coach, ScoutHelper, Sponsor }

    public enum SkillRank { Blue, Gold, Red }

    /// <summary>XP の対象の指定。能力そのもの、最も低い能力(同じなら添字の小さい方)、乱数。</summary>
    public enum XpStatMode { Fixed, Weakest, Random }

    /// <summary>効果 1 つ(06 §5.2)。使わない項目は既定値のまま。</summary>
    public sealed class Effect
    {
        public EffectOp Op;
        public Target To;
        public int D;                       // mot・fat・bond・bondg・funds・scout・guide の変化量
        public double? P;                   // 確率付き効果(mot・fat・xp・skill)
        public XpStatMode StatMode;
        public int Stat;                    // xp: 能力の添字(StatMode = Fixed)
        public int Xu;                      // xp
        public InjurySeverityKind Sev;      // inj
        public string SkillId;              // skill(接頭辞 skill. なし)
        public ModKind Kind;                // mod
        public double Value;                // mod
        public int Weeks;                   // mod・ob_role
        public CaptainSelect Select;        // captain
        public ObRole Role;                 // ob_role
    }

    /// <summary>inj 効果の重さ。Entities.InjurySeverity と同じ並び(None を除く)。</summary>
    public enum InjurySeverityKind { Minor = 1, Moderate = 2, Severe = 3 }

    public sealed class ChoiceDef
    {
        public string Id, Label;
        public string Req;                  // 条件式。空なら常に選べる
        public Effect[] Fx = new Effect[0];

        internal CompiledExpr ReqExpr;
    }

    /// <summary>イベントの定義(06 §4)。データで持ち、条件式は <see cref="EventCatalog.Prepare"/> で実行形にする。</summary>
    public sealed class EventDef
    {
        public string Id;
        public EventCategory Category;
        public string Name;
        public EventScope Scope;
        public int[] Months, Weeks;         // null = 通年
        public EventPhaseKind Phase;
        public string Cond;                 // 空なら常に真
        public double P;
        public int Cooldown;
        public OnceKind Once;
        public int? MaxYear;
        public int Priority = 50;
        public bool Exempt;
        public ChoiceDef[] Choices;
        public string[] Default;
        public string Hint = "";
        public string SkillId;              // 覚醒イベントの特殊能力(接頭辞 skill. なし)

        internal CompiledExpr CondExpr;

        public int Roles { get { return DslCompiler.RolesOf(Scope); } }

        public bool Interactive { get { return Choices.Length >= 2; } }
    }

    /// <summary>特殊能力の覚醒の定義(06 §9。02 §3.7 の acquire)。</summary>
    public sealed class SkillDef
    {
        public string Id, Name;
        public SkillRank Rank;
        public string Acquire;              // 条件式。"false" = 取得条件なし(イベントでのみ取得)

        public SkillDef(string id, string name, SkillRank rank, string acquire)
        {
            Id = id; Name = name; Rank = rank; Acquire = acquire;
        }
    }

    /// <summary>イベントのカタログ(手書きのイベント + 覚醒イベント)。</summary>
    public sealed class EventCatalog
    {
        public IReadOnlyList<EventDef> Events { get; }
        public IReadOnlyList<SkillDef> Skills { get; }
        private readonly Dictionary<string, HashSet<string>> _exclusive = new Dictionary<string, HashSet<string>>();
        private readonly List<EventDef> _byPriority;

        public EventCatalog(IEnumerable<EventDef> authored, IEnumerable<SkillDef> skills, IEnumerable<KeyValuePair<string, string>> exclusive)
        {
            var all = new List<EventDef>(authored);
            var sk = new List<SkillDef>(skills);
            foreach (SkillDef s in sk)
            {
                if (s.Acquire == "false") continue;
                all.Add(new EventDef
                {
                    Id = "ev.awk." + s.Id, Category = EventCategory.Awakening, Name = "覚醒: " + s.Name, Scope = EventScope.Runner,
                    Cond = "not has_skill(r, '" + s.Id + "') and (" + s.Acquire + ")", P = EventConstants.AwakenP(s.Rank),
                    Choices = new[] { new ChoiceDef { Id = "ok", Label = "『" + s.Name + "』を覚える", Req = null, Fx = new[] { new Effect { Op = EffectOp.Skill, To = Target.R, SkillId = s.Id } } } },
                    Default = new[] { "ok" }, Phase = EventPhaseKind.Normal, Once = OnceKind.Runner, Priority = 95, Exempt = true, Hint = s.Rank.ToString(), SkillId = s.Id,
                });
            }
            Events = all;
            Skills = sk;
            foreach (var kv in exclusive)
            {
                Add(kv.Key, kv.Value);
                Add(kv.Value, kv.Key);
            }
            _byPriority = new List<EventDef>(all);
            _byPriority.Sort((a, b) =>
            {
                int c = b.Priority.CompareTo(a.Priority);
                return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
            });
        }

        private void Add(string a, string b)
        {
            HashSet<string> set;
            if (!_exclusive.TryGetValue(a, out set)) _exclusive[a] = set = new HashSet<string>();
            set.Add(b);
        }

        /// <summary>priority の大きい順、同じなら ID の順(06 §7.2)。</summary>
        public IReadOnlyList<EventDef> ByPriority { get { return _byPriority; } }

        /// <summary>この特殊能力と同時に持てない特殊能力(接頭辞 skill. なし)。</summary>
        public IEnumerable<string> ExclusiveWith(string skillId)
        {
            HashSet<string> set;
            return _exclusive.TryGetValue(skillId, out set) ? (IEnumerable<string>)set : new string[0];
        }

        /// <summary>すべての条件式を実行形にする。誤りがあれば <see cref="DslException"/>(検証は <see cref="EventValidator"/>)。</summary>
        public EventCatalog Prepare()
        {
            foreach (EventDef e in Events)
            {
                int roles = e.Roles;
                e.CondExpr = string.IsNullOrEmpty(e.Cond) ? null : DslCompiler.Compile(e.Cond, roles);
                foreach (ChoiceDef c in e.Choices) c.ReqExpr = string.IsNullOrEmpty(c.Req) ? null : DslCompiler.Compile(c.Req, roles);
            }
            return this;
        }
    }
}
