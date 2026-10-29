using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Training;
using Ekiden.Domain.World;

namespace Ekiden.Domain.Team
{
    /// <summary>自校(実体の選手)の大会の側(04 §4・§5.3・§10): 登録(おまかせ)・当日の準備・結果の選手への反映。</summary>
    public static class TeamRacing
    {
        /// <summary>選手が大会に出られるか: 軽傷までの故障(調子は −2 で出る。04 O3・03 §6)・イベントで欠場にされていない。中傷以上は出られない。</summary>
        public static bool CanRun(TeamState s, Runner r) { return CanPlace(r) && !s.Bench.Contains(r.Id); }

        /// <summary>オーダーに配置できるか(04 O3): 故障が軽傷まで。</summary>
        public static bool CanPlace(Runner r) { return r.InjurySeverity <= InjurySeverity.Minor; }

        /// <summary>登録できるか(04 E3): 重傷でない(中傷以下は登録できる。大会までに治らなければ出られず、差し替えになる)。</summary>
        public static bool CanRegister(Runner r) { return r.InjurySeverity != InjurySeverity.Severe; }

        private static double LongRating(RaceRunner r) { return RaceTables.Rating(r.Stats, SectionType.Long); }

        /// <summary>おまかせ登録(04 §4.2): 出られる選手のうち、区間適性を見た最適な配置の人数 + 長距離の高い順。留学生の登録は <c>intl_reg_max</c> まで(貢献の小さい方から外す)。
        /// 出られる選手が出走人数に足りなければ、出られる全員(その大会は棄権になる。E1)。</summary>
        public static List<int> AutoRegister(TeamState s, RaceKind kind)
        {
            RaceRule rule = RaceTables.Rule(kind);
            var rr = s.Runners.Where(r => CanRun(s, r)).OrderBy(r => r.Id).Select(RaceRunner.From).ToList();
            if (rr.Count < rule.Run) return rr.Select(x => x.RunnerId).ToList();
            foreach (RaceRunner x in rr) x.Form = 0;                                         // 登録の時点では調子は未確定
            List<RaceRunner> entry = AiRacing.PickPlayerEntry(rr, kind);
            var intl = new HashSet<int>(s.Runners.Where(r => r.Overseas).Select(r => r.Id));
            while (entry.Count(e => intl.Contains(e.RunnerId)) > rule.IntlReg)
            {
                RaceRunner worst = entry.Where(e => intl.Contains(e.RunnerId)).OrderBy(LongRating).First();
                entry.Remove(worst);
                RaceRunner next = rr.Where(x => !intl.Contains(x.RunnerId) && !entry.Contains(x)).OrderByDescending(LongRating).FirstOrDefault();
                if (next != null) entry.Add(next);
            }
            return entry.Select(e => e.RunnerId).ToList();
        }

        /// <summary>当日の準備(04 §5.3・§9): 登録メンバー(故障で出られない人は、出られる人で差し替える。第三戦は『登録に追加』された人も)→ 当日までの欠場の抽選 → 最適な配置と
        /// おまかせの当日変更・指示。調子は実際の調子。出走人数に足りなければ null(棄権。E1)。乱数の鍵は他校と同じ(label 4・b = 8000 + チーム ID)。</summary>
        public static RaceTeam PrepareRace(TeamState s, int teamId, RaceKind kind, int raceId, Env[] envs, IRngFactory rngs)
        {
            RaceRule rule = RaceTables.Rule(kind);
            var ids = new List<int>(s.Entries[(int)kind]);
            if (kind == RaceKind.NewYear) foreach (int f in s.Forced.OrderBy(x => x)) if (!ids.Contains(f)) ids.Add(f);
            Dictionary<int, Runner> able = s.Runners.Where(r => CanRun(s, r)).ToDictionary(r => r.Id);
            var pool = new List<RaceRunner>();
            foreach (int id in ids) if (able.ContainsKey(id)) pool.Add(RaceRunner.From(able[id]));
            if (pool.Count < rule.Entry)                                                      // 登録メンバーの故障は差し替える(§5.3)
            {
                var others = able.Values.Where(r => !ids.Contains(r.Id)).OrderBy(r => r.Id).Select(RaceRunner.From).ToList();
                RaceSimulator.StableSort(others, (a, b) => LongRating(b).CompareTo(LongRating(a)));
                foreach (RaceRunner o in others) { if (pool.Count >= rule.Entry) break; pool.Add(o); }
            }
            if (pool.Count < rule.Run) return null;
            var real = new Dictionary<int, RaceRunner>();
            foreach (RaceRunner p in pool) real[p.RunnerId] = p;
            Pcg32 avail = rngs.Stream(RngRegistry.LabelRace, raceId, 8000 + teamId);
            var pool2 = new List<RaceRunner>();
            foreach (RaceRunner d in pool) if (avail.Uniform() < RaceConstants.AvailEntry) pool2.Add(d);
            AiRacing.Pad(pool2, rule.Run);
            var baseForm = new List<RaceRunner>();
            foreach (RaceRunner d in pool2) { RaceRunner b = d.Clone(); b.Form = 0; baseForm.Add(b); }       // 配置の選定は調子が未確定のとき(03 §6)
            var day = new List<RaceRunner>();
            foreach (RaceRunner e in AiRacing.PickPlayerEntry(baseForm, kind))
                if (avail.Uniform() < RaceConstants.AvailDay) day.Add(e.RunnerId != 0 && real.ContainsKey(e.RunnerId) ? real[e.RunnerId].Clone() : e);
            AiRacing.Pad(day, rule.Run);
            var (lineup, instr, _) = DayPlanner.PlanDay(day, kind, envs, PlanMode.Auto);
            return new RaceTeam { Id = teamId, Lineup = lineup, Instr = instr, Geki = rule.Geki };
        }

        /// <summary>当日の準備(04 §5.3)の途中経過: 当日に出られる登録メンバー(実際の調子つき)と、提出時の配置。</summary>
        public sealed class DayPlan
        {
            public RaceKind Kind;
            public int RaceId, TeamId;
            public Env[] Envs;
            public List<RaceRunner> Day;           // 当日出られる登録メンバー(欠場を除く。出走人数に足りなければ補充要員)。調子は実際の調子
            public int[] PreSwap;                   // 提出時の配置(区間 → Day の添字)。オーダーを出していれば出したとおり(欠けた区間は最適な代役)、なければ最適な配置
        }

        /// <summary>
        /// 当日の準備を、当日変更の前まで進める(<see cref="PrepareRace"/> と同じ乱数の引き方・同じ順)。提出されたオーダー(<paramref name="order"/>: 区間 → 選手 ID。null = おまかせ)があれば、
        /// その配置から始める。登録時・当日の欠場で出られない選手の区間は、残りの登録メンバーの中の最適な代役に置き換える(当日変更の回数は使わない。04 §5.3)。
        /// 出走人数に足りなければ null(棄権)。
        /// </summary>
        public static DayPlan OpenDay(TeamState s, int teamId, RaceKind kind, int raceId, Env[] envs, IRngFactory rngs, IReadOnlyList<int> order)
        {
            RaceRule rule = RaceTables.Rule(kind);
            int n = RaceTables.Course(kind).Count;
            var ids = new List<int>(s.Entries[(int)kind]);
            if (kind == RaceKind.NewYear) foreach (int f in s.Forced.OrderBy(x => x)) if (!ids.Contains(f)) ids.Add(f);
            Dictionary<int, Runner> able = s.Runners.Where(r => CanRun(s, r)).ToDictionary(r => r.Id);
            var pool = new List<RaceRunner>();
            foreach (int id in ids) if (able.ContainsKey(id)) pool.Add(RaceRunner.From(able[id]));
            if (pool.Count < rule.Entry)
            {
                var others = able.Values.Where(r => !ids.Contains(r.Id)).OrderBy(r => r.Id).Select(RaceRunner.From).ToList();
                RaceSimulator.StableSort(others, (a, b) => LongRating(b).CompareTo(LongRating(a)));
                foreach (RaceRunner o in others) { if (pool.Count >= rule.Entry) break; pool.Add(o); }
            }
            if (pool.Count < rule.Run) return null;
            var real = new Dictionary<int, RaceRunner>();
            foreach (RaceRunner p in pool) real[p.RunnerId] = p;
            Pcg32 avail = rngs.Stream(RngRegistry.LabelRace, raceId, 8000 + teamId);
            var pool2 = new List<RaceRunner>();
            foreach (RaceRunner d in pool) if (avail.Uniform() < RaceConstants.AvailEntry) pool2.Add(d);
            AiRacing.Pad(pool2, rule.Run);
            var baseForm = new List<RaceRunner>();
            foreach (RaceRunner d in pool2) { RaceRunner b = d.Clone(); b.Form = 0; baseForm.Add(b); }
            // 当日の欠場の抽選を引く順(先発 → 控え)と、提出時の配置
            List<RaceRunner> entryList;
            int[] plannedBase = null;                                        // 区間 → baseForm の添字(オーダーがあるとき)
            if (order == null) entryList = AiRacing.PickPlayerEntry(baseForm, kind);
            else
            {
                plannedBase = PlanFromOrder(baseForm, order, kind);
                entryList = new List<RaceRunner>();
                var used = new HashSet<int>();
                foreach (int j in plannedBase) { entryList.Add(baseForm[j]); used.Add(j); }
                var rest = new List<int>();
                for (int j = 0; j < baseForm.Count; j++) if (!used.Contains(j)) rest.Add(j);
                RaceSimulator.StableSort(rest, (a, b) => LongRating(baseForm[b]).CompareTo(LongRating(baseForm[a])));
                for (int i = 0; i < rest.Count && entryList.Count < rule.Entry; i++) entryList.Add(baseForm[rest[i]]);
            }
            var day = new List<RaceRunner>();
            var dayOfEntry = new int[entryList.Count];                        // entryList の添字 → day の添字(欠場は -1)
            for (int i = 0; i < entryList.Count; i++)
            {
                RaceRunner e = entryList[i];
                if (avail.Uniform() < RaceConstants.AvailDay)
                {
                    dayOfEntry[i] = day.Count;
                    day.Add(e.RunnerId != 0 && real.ContainsKey(e.RunnerId) ? real[e.RunnerId].Clone() : e);
                }
                else dayOfEntry[i] = -1;
            }
            AiRacing.Pad(day, rule.Run);
            var baseDay = new List<RaceRunner>();
            foreach (RaceRunner d in day) { RaceRunner b = d.Clone(); b.Form = 0; baseDay.Add(b); }
            int[] pre;
            if (order == null) pre = DayPlanner.BestLineup(baseDay, kind).assignment;
            else
            {
                pre = new int[n];
                var taken = new HashSet<int>();
                for (int si = 0; si < n; si++) { pre[si] = dayOfEntry[si]; if (pre[si] >= 0) taken.Add(pre[si]); }
                double[][] cost = DayPlanner.CostMatrix(baseDay, kind);
                for (int si = 0; si < n; si++)
                {
                    if (pre[si] >= 0) continue;                               // 欠場した先発の区間は、最適な代役(残りの中で基準タイムが最小)
                    int best = -1;
                    for (int j = 0; j < baseDay.Count; j++)
                    {
                        if (taken.Contains(j)) continue;
                        if (best < 0 || cost[si][j] < cost[si][best]) best = j;
                    }
                    pre[si] = best; taken.Add(best);
                }
            }
            return new DayPlan { Kind = kind, RaceId = raceId, TeamId = teamId, Envs = envs, Day = day, PreSwap = pre };
        }

        /// <summary>提出されたオーダー(区間 → 選手 ID)を、登録時の欠場を除いた登録メンバー(baseForm)の添字にする。出られない選手の区間は、残りの中の最適な代役(基準タイムが最小)。</summary>
        private static int[] PlanFromOrder(List<RaceRunner> baseForm, IReadOnlyList<int> order, RaceKind kind)
        {
            int n = order.Count;
            var plan = new int[n];
            var taken = new HashSet<int>();
            for (int si = 0; si < n; si++)
            {
                plan[si] = baseForm.FindIndex(r => r.RunnerId == order[si] && order[si] != 0);
                if (plan[si] >= 0 && !taken.Add(plan[si])) plan[si] = -1;
            }
            double[][] cost = DayPlanner.CostMatrix(baseForm, kind);
            for (int si = 0; si < n; si++)
            {
                if (plan[si] >= 0) continue;
                int best = -1;
                for (int j = 0; j < baseForm.Count; j++)
                {
                    if (taken.Contains(j)) continue;
                    if (best < 0 || cost[si][j] < cost[si][best]) best = j;
                }
                plan[si] = best; taken.Add(best);
            }
            return plan;
        }

        private static int Count<T>(IEnumerable<T> xs, Func<T, bool> f) { int n = 0; foreach (T x in xs) if (f(x)) n++; return n; }

        /// <summary>大会後の処理(04 §10)を選手に反映する。出走しなかった(棄権)ときは null を返し、何も変えない。
        /// 疲労(区間距離 × 1.6)・経験値(MEN: 30 × 大会の重み + 区間賞 30 + ブレーキを経験 20)・やる気・通算成績・カウンター(06 §10)・<c>race.*</c> の値(<see cref="Runner.RaceFlags"/>)・順位と評価ポイント。
        /// 戻り値は、その直後の EventPhase に渡す <see cref="RaceInfo"/>。</summary>
        public static RaceInfo? ApplyResult(TeamState s, RaceOutcome o, int teamId)
        {
            int idx = o.Results.FindIndex(t => t.Id == teamId);
            RaceTeam team = o.Teams.FirstOrDefault(t => t.Id == teamId);
            if (idx < 0 || team == null) return null;
            TeamResult tr = o.Results[idx];
            RaceRule rule = RaceTables.Rule(o.Kind);
            IReadOnlyList<SectionDef> course = RaceTables.Course(o.Kind);
            int k = (int)o.Kind, teamRank = idx + 1;
            double power = 0;
            foreach (RaceRunner x in team.Lineup) power += LongRating(x);
            power /= team.Lineup.Length;
            int expected = 1;
            foreach (RaceTeam t in o.Teams)
            {
                if (t.Id == teamId) continue;
                double p = 0;
                foreach (RaceRunner x in t.Lineup) p += LongRating(x);
                if (p / t.Lineup.Length > power) expected++;
            }
            for (int si = 0; si < team.Lineup.Length; si++)
            {
                Runner r = s.FindRunner(team.Lineup[si].RunnerId);
                if (r == null) continue;
                int secRank = 1 + Count(o.Results, t => t.Sections[si] < tr.Sections[si]);
                bool win = secRank == 1;
                bool brake = tr.Events.Any(e => e.Kind == "brake" && e.Section == si);
                bool record = o.Records.Any(u => u.Section == si && u.SchoolId == teamId && u.RunnerId == r.Id);       // 自校の走者の区間記録の更新(04 §8.3)
                int cumNow = 1 + Count(o.Results, t => t.Cum[si] < tr.Cum[si]);
                int cumPrev = si > 0 ? 1 + Count(o.Results, t => t.Cum[si - 1] < tr.Cum[si - 1]) : cumNow;
                r.RaceFlags = new RaceFlags { InLineup = 1, Brake = brake ? 1 : 0, SectionRank = secRank, SectionWin = win ? 1 : 0, Record = record ? 1 : 0 };
                r.Races++;
                if (win) r.SectionWins++;
                if (brake) r.Brakes++;
                if (record) r.Records++;
                if (teamRank == 1) r.RelayWins++;
                // カウンター(06 §10)
                if (r.TaperWeeks >= 2 && r.Form >= 1) r.AddCtr(Counters.PeakedRaces, 1);
                if (cumPrev - cumNow >= 3) r.AddCtr(Counters.Overtakes, 1);
                if (tr.Flow[si] == 1) r.AddCtr(Counters.LeadSections, 1);
                else if (tr.Flow[si] == 2) r.AddCtr(Counters.PackSections, 1);
                else if (tr.Flow[si] == 3) r.AddCtr(Counters.SoloSections, 1);
                if (teamRank <= 3 && secRank <= 5) r.AddCtr(Counters.BigWins, 1);
                if (r.Form >= 1 && teamRank <= 5 && secRank <= 3) r.AddCtr(Counters.EkidenGood, 1);
                // 疲労・経験値・やる気
                r.Fatigue = Math.Max(0, Math.Min(100, r.Fatigue + Rounding.HalfUp(course[si].Distance * TrainingMaster.K_RACE_FATIGUE_PER_KM)));
                Progression.ApplyXp(r, (int)Stat.MEN, Rounding.HalfUp(30 * rule.Weight) + (win ? 30 : 0) + (brake ? 20 : 0));
                int d = (win ? 1 : 0) + (teamRank == 1 ? 1 : 0) - (brake ? 1 : 0) + (teamRank <= expected - 3 ? 1 : (teamRank >= expected + 3 ? -1 : 0));
                r.Motivation = Math.Max(1, Math.Min(5, r.Motivation + d));
            }
            s.Rank[k] = teamRank;
            if (teamRank <= 10) s.YearPoints += WorldK.YearPoints[teamRank - 1];
            return new RaceInfo(k + 1, teamRank, expected);
        }

        /// <summary>大会が終わったら、その大会の登録と欠場の指定を消す(06 §11)。出走しなかったときも消す。</summary>
        public static void CloseRace(TeamState s, RaceKind kind)
        {
            s.Entries[(int)kind].Clear();
            s.Bench.Clear();
            if (kind == RaceKind.NewYear) s.Forced.Clear();
        }
    }
}
