using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.World;
using Ekiden.Domain.Events;

namespace Ekiden.Domain.Game
{
    /// <summary>候補 1 人の見え方(08 R-08)。<see cref="QObs"/> は視察のあとだけ、成長タイプなどは練習会のあとだけ。</summary>
    public sealed class ScoutView
    {
        public WorldRunner Candidate;
        public int Index;
        public ScoutStage Stage;
        public double V;                          // 市場評価(最初から見える)
        public bool TopTier;                      // 上位級(奨学金枠が要る)
        public DisplayRules.RivalryLevel Rivalry; // 競合度
        public double RivalryWin;                 // 練習会まで進めて枠があれば競り勝つ割合の実測
        public double? QObs;                      // 卒業時の見込みの見積もり(視察後)
        public double Sigma;                      // 見積もりの誤差(1σ。スカウト担当の Lv で決まる)
        public bool StyleVisible, GiftPresenceVisible, GiftKindVisible, GrowthTypeVisible;
    }

    /// <summary>スカウト(08 R-08・05 §7): 視察 6〜11 月 → 声かけ・練習会 12〜2 月、1 回 1 ポイント、好感度 +30 / +60(合計 +90)。確定は年度末の割り当て(05 §8)。
    /// 手動の行動が 1 つでもあれば、その行動が年度末の提示に使われる。行動がなく <c>scout_auto</c> なら、検算済みのおまかせ方針(<see cref="PlayerAgent.ChooseTargetsFor"/>)。</summary>
    public sealed partial class GameSession
    {
        public const int ScoutTargetRecommended = 8;                  // K_UI_SCOUT_TARGET_REC(08 R-08)
        public const double ScoutPreValueGap = 7.2;                   // 視察の事前の価値 (V + 7.2) × pwin の 7.2 = 伸びしろの平均 24 × 0.3

        private readonly SortedDictionary<int, ScoutStage> _scout = new SortedDictionary<int, ScoutStage>();     // 候補の番号 → 段階(None は持たない)
        private List<WorldRunner> _pool;
        private int _poolYear = int.MinValue;

        /// <summary>今年度の候補(翌年度の入学ぶん)。年度ごとの鍵で作るので、年度末の割り当てに使う候補と同じ。</summary>
        public IReadOnlyList<WorldRunner> ScoutPool
        {
            get
            {
                int y = Year;
                if (_pool == null || _poolYear != y)
                {
                    _pool = PoolGenerator.Generate(Rngs, y, WorldK.K_POOL_N, World.ElitePrefBonus);
                    _poolYear = y;
                }
                return _pool;
            }
        }

        /// <summary>行動した候補の段階(行動していない候補は含まない)。</summary>
        public IReadOnlyDictionary<int, ScoutStage> ScoutStages { get { return _scout; } }

        public ScoutStage StageOf(int index)
        {
            ScoutStage st;
            return _scout.TryGetValue(index, out st) ? st : ScoutStage.None;
        }

        /// <summary>使えるスカウトのポイント(06 の補正 <c>scout_pts</c> を含む)。</summary>
        public int ScoutPointsAvailable { get { return Math.Max(0, (int)(State.ScoutPts + State.ModSum(ModKind.ScoutPts))); } }

        public ScoutView ViewCandidate(int index)
        {
            IReadOnlyList<WorldRunner> pool = ScoutPool;
            if (index < 0 || index >= pool.Count) return null;
            WorldRunner c = pool[index];
            ScoutStage st = StageOf(index);
            var v = new ScoutView { Candidate = c, Index = index, Stage = st, V = c.V, TopTier = c.V >= WorldK.K_V_TOP, Sigma = Scouting.InfoSd(School.ScoutLv) };
            v.Rivalry = DisplayRules.Rivalry(c.V, out double win);
            v.RivalryWin = win;
            if (st >= ScoutStage.Observed) v.QObs = Scouting.ObserveQ(c, School.ScoutLv, Rngs);
            v.StyleVisible = Scouting.StyleVisible(st);
            v.GiftPresenceVisible = Scouting.GiftPresenceVisible(st);
            v.GiftKindVisible = Scouting.GiftKindVisible(st, School.ScoutLv);
            v.GrowthTypeVisible = Scouting.GrowthTypeVisible(st);
            return v;
        }

        // ------------------------------------------------------------ 行動
        public OpResult ScoutObserve(int index) { return ScoutAct(index, ScoutStage.Observed); }

        public OpResult ScoutTalk(int index) { return ScoutAct(index, ScoutStage.Talked); }

        public OpResult ScoutInvite(int index) { return ScoutAct(index, ScoutStage.Invited); }

        private OpResult ScoutAct(int index, ScoutStage next)
        {
            IReadOnlyList<WorldRunner> pool = ScoutPool;
            if (index < 0 || index >= pool.Count) return OpResult.Fail("その候補はいない");
            ScoutStage cur = StageOf(index);
            int month = MonthIndex;
            if (!Scouting.CanAct(cur, next, month))
            {
                switch (next)
                {
                    case ScoutStage.Observed:
                        return OpResult.Fail(cur != ScoutStage.None ? "視察済みです" : "視察できるのは 6〜11 月です");
                    case ScoutStage.Talked:
                        if (cur == ScoutStage.None) return OpResult.Fail("先に視察が要ります");
                        if (cur != ScoutStage.Observed) return OpResult.Fail("声かけ済みです");
                        return OpResult.Fail("声かけできるのは 12〜2 月です");
                    default:
                        if (cur == ScoutStage.Invited) return OpResult.Fail("練習会に招待済みです");
                        if (cur != ScoutStage.Talked) return OpResult.Fail("先に声かけが要ります");
                        return OpResult.Fail("練習会に招待できるのは 12〜2 月です");
                }
            }
            if (ScoutPointsAvailable < Scouting.ObserveCost) return OpResult.Fail("スカウトのポイントが足りない");
            State.ScoutPts -= 1;                                                         // 補正ぶんを含めて使う(補正が切れると、その分だけ減る)
            _scout[index] = next;
            return OpResult.Success;
        }

        // ------------------------------------------------------------ おまかせスカウト(08 R-08。ボタン)
        /// <summary>おまかせ視察(6〜11 月): 勧誘のぶん(目標 8 人 × 2 ポイント)を残して、事前の価値 (V + 7.2) × pwin の高い順に視察する。視察した人数を返す。</summary>
        public int AutoScoutObserve()
        {
            if (!Scouting.CanObserve(MonthIndex)) return 0;
            int n = Math.Max(0, ScoutPointsAvailable - 2 * ScoutTargetRecommended);
            if (n == 0) return 0;
            IReadOnlyList<WorldRunner> pool = ScoutPool;
            Func<WorldRunner, double> pwin = PWinFunction();
            var cand = pool.Where(c => StageOf(c.Index) == ScoutStage.None).Select(c => new KeyValuePair<double, WorldRunner>((c.V + ScoutPreValueGap) * pwin(c), c)).ToList();
            Race.RaceSimulator.StableSort(cand, (a, b) => b.Key.CompareTo(a.Key));
            int done = 0;
            foreach (var kv in cand)
            {
                if (done >= n) break;
                if (ScoutObserve(kv.Value.Index).Ok) done++;
            }
            return done;
        }

        /// <summary>おまかせ勧誘(12〜2 月): 視察ずみの候補から、見込み Q_obs × pwin の高い順に目標 8 人まで選び(上位級は奨学金枠の数まで)、声かけ + 練習会を行う。目標にした人数を返す。</summary>
        public int AutoScoutRecruit()
        {
            if (!Scouting.CanTalk(MonthIndex)) return 0;
            IReadOnlyList<WorldRunner> pool = ScoutPool;
            Func<WorldRunner, double> pwin = PWinFunction();
            int already = _scout.Count(kv => kv.Value >= ScoutStage.Talked);
            int want = Math.Max(0, ScoutTargetRecommended - already);
            int topLeft = Math.Max(0, School.ScholSlots() - _scout.Count(kv => kv.Value >= ScoutStage.Talked && pool[kv.Key].V >= WorldK.K_V_TOP));
            var cand = pool.Where(c => StageOf(c.Index) == ScoutStage.Observed)
                           .Select(c => new KeyValuePair<double, WorldRunner>(Scouting.ObserveQ(c, School.ScoutLv, Rngs) * pwin(c), c)).ToList();
            Race.RaceSimulator.StableSort(cand, (a, b) => b.Key.CompareTo(a.Key));
            int chosen = 0;
            foreach (var kv in cand)
            {
                if (chosen >= want) break;
                WorldRunner c = kv.Value;
                if (c.V >= WorldK.K_V_TOP)
                {
                    if (topLeft <= 0) continue;
                    topLeft--;
                }
                if (!ScoutTalk(c.Index).Ok) break;                                       // ポイント切れ
                ScoutInvite(c.Index);
                chosen++;
            }
            return chosen;
        }

        /// <summary>自校が競り勝つ見込み pwin(05 §7.3。画面には出さず、おまかせの選び方にだけ使う)。</summary>
        private Func<WorldRunner, double> PWinFunction()
        {
            var p10 = new Dictionary<int, double>();
            foreach (School s in World.Schools) p10[s.Id] = s.IsPlayer ? P10() : PlayerAgent.P10Of(s);
            return r =>
            {
                double baseP = WorldMath.OfferBase(School, r, p10[School.Id]) + WorldK.InterestMax;
                double bestR = double.MinValue;
                foreach (School s in World.Schools)
                {
                    if (s.IsPlayer) continue;
                    double v = WorldMath.OfferBase(s, r, p10[s.Id]);
                    if (v > bestR) bestR = v;
                }
                bestR += r.V >= WorldK.K_V_TOP ? WorldK.RivalTargetInterest * 0.8 : WorldK.RivalBaseInterest * 0.8;
                return 1.0 / (1.0 + Math.Exp(-(baseP - bestR) / 8.0));
            };
        }

        /// <summary>年度末の割り当てで、手動の行動があるときの自校の提示(好感度。段階から決まる)。</summary>
        private PlayerOffer ManualScoutOffer(Dictionary<int, int> obByPref)
        {
            Dictionary<int, ScoutStage> stages = _scout.ToDictionary(kv => kv.Key, kv => kv.Value);
            return r =>
            {
                ScoutStage st;
                stages.TryGetValue(r.Index, out st);
                obByPref.TryGetValue(r.Overseas ? 0 : r.PrefectureId, out int n);
                return Scouting.Interest(st) + WorldK.ObBonus * Math.Min(5, (int)Math.Sqrt(n));
            };
        }
    }
}
