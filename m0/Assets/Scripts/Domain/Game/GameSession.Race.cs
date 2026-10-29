using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.World;
using Calendar = Ekiden.Domain.Team.Calendar;

namespace Ekiden.Domain.Game
{
    /// <summary>前評判(08 R-09)の 1 行。</summary>
    public sealed class PreviewRow
    {
        public int SchoolId, Rank;
        public double Value;                // 登録メンバー(他校はおまかせ登録)の上位 run_size 人の区間適性 Long の平均
        public char Mark;                   // ◎ 1 位 / 〇 2〜3 位 / ▲ 4〜6 位 / △ 7〜10 位
        public bool IsOwn;
    }

    /// <summary>区間オーダー(SC-22)・環境の予報と前評判(SC-23)・当日(SC-24)・予選会の登録(04 §3.2・§5)。</summary>
    public sealed partial class GameSession
    {
        private readonly int[][] _orders = new int[3][];              // 大会ごとの提出されたオーダー(区間 → 選手 ID)。null = おまかせ
        private int[] _qualifierEntries;                              // 予選会の登録(12 名の選手 ID)。null = おまかせ
        private RaceDay _raceDay;
        private bool _weekResolved;                                   // 今週の練習が解決された(オーダーの提出は週の初めまで)

        public const int QualifierDeadlineWeek = QualifierWeek - 4;

        /// <summary>いま開いている当日(<see cref="OpenRaceDay"/> から <see cref="PlayWeekRaces"/> まで)。</summary>
        public RaceDay CurrentRaceDay { get { return _raceDay; } }

        // ------------------------------------------------------------ オーダー(04 §5.2。O1〜O3)
        /// <summary>提出されたオーダー(区間 → 選手 ID)。出していなければ null(おまかせ)。</summary>
        public IReadOnlyList<int> OrderOf(RaceKind kind) { return _orders[(int)kind]; }

        /// <summary>おまかせのオーダー(登録メンバーのうち出られる選手から、区間適性を見た最適な配置。調子は未確定なので 0 で比べる)。登録が出走人数に足りなければ null。</summary>
        public int[] SuggestOrder(RaceKind kind)
        {
            var runners = State.Entries[(int)kind].Select(State.FindRunner).Where(r => r != null && TeamRacing.CanRun(State, r)).OrderBy(r => r.Id).Select(RaceRunner.From).ToList();
            if (runners.Count < RaceTables.Rule(kind).Run) return null;
            foreach (RaceRunner r in runners) r.Form = 0;
            int[] asg = DayPlanner.BestLineup(runners, kind).assignment;
            return asg.Select(j => runners[j].RunnerId).ToArray();
        }

        /// <summary>オーダーを提出する(エントリーの締切から、大会の週の初めまで。何度でも直せる)。O1 区間数と同じ人数を登録メンバーから重複なく / O2 留学生の出走は 1 人まで / O3 故障中は配置できない。</summary>
        public OpResult SetOrder(RaceKind kind, IReadOnlyList<int> runnerIds)
        {
            int k = (int)kind, yw = Yw;
            int n = RaceTables.Course(kind).Count;
            if (yw < TeamWeek.EntryDeadlineWeeks[k] || yw > Calendar.RaceWeeks[k] || _weekResolved) return OpResult.Fail(OpErr.OrderWindow);
            if (State.Entries[k].Count == 0) return OpResult.Fail(OpErr.OrderNeedEntry);
            if (runnerIds.Count != n) return OpResult.Fail(OpErr.OrderCount, n);
            if (runnerIds.Distinct().Count() != n) return OpResult.Fail(OpErr.OrderDup);
            int intl = 0;
            foreach (int id in runnerIds)
            {
                if (!State.Entries[k].Contains(id)) return OpResult.Fail(OpErr.OrderNotEntered);
                Runner r = State.FindRunner(id);
                if (r == null) return OpResult.Fail(OpErr.RunnerNone);
                if (!TeamRacing.CanPlace(r)) return OpResult.Fail(OpErr.OrderInjured);
                if (r.Overseas) intl++;
            }
            if (intl > RaceTables.Rule(kind).IntlRun) return OpResult.Fail(OpErr.IntlRun, RaceTables.Rule(kind).IntlRun);
            _orders[k] = runnerIds.ToArray();
            return OpResult.Success;
        }

        /// <summary>オーダーを取り消しておまかせに戻す。</summary>
        public OpResult ClearOrder(RaceKind kind)
        {
            if (_weekResolved) return OpResult.Fail(OpErr.OrderLocked);
            _orders[(int)kind] = null;
            return OpResult.Success;
        }

        /// <summary>選手を区間に置いたときの区間適性値 P(特殊能力の加算を含む)と目安(P 70 以上 ◎ / 60 以上 〇 / 未満 △)。</summary>
        public KeyValuePair<double, char> SectionFit(RaceKind kind, int runnerId, int section)
        {
            IReadOnlyList<SectionDef> secs = RaceTables.Course(kind);
            int longest = 0;
            for (int i = 1; i < secs.Count; i++) if (secs[i].Distance > secs[longest].Distance) longest = i;
            RaceRunner r = RaceRunner.From(State.FindRunner(runnerId));
            double p = RaceMath.SectionRating(r, secs[section].Type, section == longest, section == secs.Count - 1);
            return new KeyValuePair<double, char>(p, p >= 70.0 ? '◎' : (p >= 60.0 ? '〇' : '△'));
        }

        /// <summary>登録の変更で外れた選手がオーダーに入っていたら、そのオーダーを取り消す。</summary>
        private void DropStaleOrders()
        {
            for (int k = 0; k < 3; k++)
                if (_orders[k] != null && _orders[k].Any(id => !State.Entries[k].Contains(id))) _orders[k] = null;
        }

        // ------------------------------------------------------------ 予報と前評判(SC-23)
        /// <summary>環境の予報(オーダー提出時に出る。実際の値に誤差を加えたもの。雨は 85% の的中率。04 §6)。</summary>
        public Env[] Forecast(RaceKind kind)
        {
            EnsureYear();
            return World.RaceForecast(_ys, kind);
        }

        /// <summary>前評判(08 R-09): 出場校ごとの値(登録メンバーの上位 run_size 人の区間適性 Long の平均)と順位・印。自校は登録メンバー、他校はおまかせ登録。</summary>
        public IReadOnlyList<PreviewRow> RacePreview(RaceKind kind)
        {
            YearState ys = PrepareYear();
            RaceRule rule = RaceTables.Rule(kind);
            var rows = new List<PreviewRow>();
            foreach (int sid in ys.Entrants(kind))
            {
                List<RaceRunner> entry;
                if (sid == School.Id)
                    entry = State.Entries[(int)kind].Select(State.FindRunner).Where(r => r != null && TeamRacing.CanRun(State, r)).Select(RaceRunner.From).ToList();
                else
                    entry = AiRacing.PickEntry(ys.Rosters[sid], kind);
                var ratings = entry.Select(e => RaceTables.Rating(e.Stats, SectionType.Long)).OrderByDescending(x => x).Take(rule.Run).ToList();
                double sum = 0;
                foreach (double v in ratings) sum += v;
                rows.Add(new PreviewRow { SchoolId = sid, Value = ratings.Count > 0 ? sum / ratings.Count : 0.0, IsOwn = sid == School.Id });
            }
            RaceSimulator.StableSort(rows, (a, b) => b.Value.CompareTo(a.Value));
            for (int i = 0; i < rows.Count; i++) { rows[i].Rank = i + 1; rows[i].Mark = DisplayRules.PreviewMark(i + 1); }
            return rows;
        }

        // ------------------------------------------------------------ 当日(SC-24)
        /// <summary>大会の当日を開く(練習が解決されて調子が確定したあと、レースの前)。当日変更・指示・檄を決めて <see cref="PlayWeekRaces"/> で走らせる。
        /// 出場権がない・出走人数に足りない(棄権)ときは null。提出されたオーダーがあればその配置から、なければ最適な配置から始まる。初期値は『おまかせ』。</summary>
        public RaceDay OpenRaceDay(RaceKind kind)
        {
            if (!_weekResolved || Calendar.RaceWeeks[(int)kind] != Yw) throw new InvalidOperationException("当日を開けるのは、その大会の週の練習が解決されたあと");
            YearState ys = PrepareYear();
            if (!ys.Entrants(kind).Contains(School.Id)) return null;
            int rid = WorldSim.RaceId(ys, kind);
            Env[] envs = World.RaceEnv(ys, kind);
            TeamRacing.DayPlan plan = TeamRacing.OpenDay(State, School.Id, kind, rid, envs, Rngs, _orders[(int)kind]);
            if (plan == null) return null;
            YearState captured = ys;
            _raceDay = new RaceDay(plan, State, team => World.PreviewRace(captured, kind, team));
            return _raceDay;
        }

        /// <summary>今週、自校が出る大会があるか(当日を開けるか)。</summary>
        public RaceKind? RaceThisWeek
        {
            get
            {
                int k = Array.IndexOf(Calendar.RaceWeeks, Yw);
                if (k < 0) return null;
                YearState ys = PrepareYear();
                return ys.Entrants((RaceKind)k).Contains(School.Id) ? (RaceKind?)k : null;
            }
        }

        // ------------------------------------------------------------ 予選会の登録(SC-21。シード外の年度の週 22)
        /// <summary>予選会の登録(12 名)。エントリーの締切(週 22)から予選会の週の初めまで。出さなければおまかせ(長距離の適性の高い順)。</summary>
        public OpResult SetQualifierEntries(IReadOnlyList<int> runnerIds)
        {
            int yw = Yw;
            if (yw < QualifierDeadlineWeek || yw > QualifierWeek || _weekResolved) return OpResult.Fail(OpErr.QualWindow, QualifierDeadlineWeek);
            if (PrepareYear().Seeds.Contains(School.Id)) return OpResult.Fail(OpErr.QualSeed);
            if (runnerIds.Count != RaceConstants.QualEntry) return OpResult.Fail(OpErr.QualCount, RaceConstants.QualEntry);
            if (runnerIds.Distinct().Count() != runnerIds.Count) return OpResult.Fail(OpErr.EntryDup);
            foreach (int id in runnerIds)
            {
                Runner r = State.FindRunner(id);
                if (r == null) return OpResult.Fail(OpErr.EntryUnknown);
                if (!TeamRacing.CanRegister(r)) return OpResult.Fail(OpErr.EntrySevere);
            }
            _qualifierEntries = runnerIds.ToArray();
            return OpResult.Success;
        }

        public IReadOnlyList<int> QualifierEntries { get { return _qualifierEntries; } }
    }
}
