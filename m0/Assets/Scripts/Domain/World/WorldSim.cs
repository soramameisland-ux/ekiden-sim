using System;
using System.Collections.Generic;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;

namespace Ekiden.Domain.World
{
    public sealed class YearResult
    {
        public int Year;
        public Dictionary<RaceKind, List<int>> Order = new Dictionary<RaceKind, List<int>>();     // 大会ごとの順位(学校 ID)
        public List<int> Qualified = new List<int>();                                            // 予選会の通過校
        public Dictionary<RaceKind, int> Winner = new Dictionary<RaceKind, int>();
    }

    /// <summary>36 校の世界(05)。自校なしの進行(予熱)と、他校の毎年度の動き。</summary>
    public sealed class WorldSim
    {
        public readonly List<School> Schools = new List<School>();
        public readonly IRngFactory Rngs;

        /// <summary>過去の大会の順位表と区間記録・大会記録(02 §4.8。予熱の年度から)。</summary>
        public RaceHistory History { get; private set; } = new RaceHistory();

        /// <summary>強豪県の候補の能力の基準値への加算(02 §6。既定 3.0)。検算のために 0 にして、参照モデル(これを持たない)と比べられる。</summary>
        public double ElitePrefBonus { get; private set; } = WorldK.ElitePrefBonus;

        private static readonly (Region region, Tier tier)[] Plan = BuildPlan();

        private static (Region, Tier)[] BuildPlan()
        {
            var l = new List<(Region, Tier)>();
            void Add(Region r, Tier t, int n) { for (int i = 0; i < n; i++) l.Add((r, t)); }
            Add(Region.Kanto, Tier.Elite, 3); Add(Region.Kanto, Tier.Mid, 7); Add(Region.Kanto, Tier.Low, 10);
            Add(Region.Kinki, Tier.Elite, 1); Add(Region.Kinki, Tier.Mid, 2); Add(Region.Kinki, Tier.Low, 3);
            Add(Region.Chubu, Tier.Elite, 1); Add(Region.Chubu, Tier.Mid, 1); Add(Region.Chubu, Tier.Low, 2);
            Add(Region.Kyushu, Tier.Mid, 1); Add(Region.Kyushu, Tier.Low, 1);
            Add(Region.Tohoku, Tier.Elite, 1); Add(Region.Tohoku, Tier.Low, 1);
            Add(Region.Hokkaido, Tier.Low, 1); Add(Region.Chugoku, Tier.Mid, 1);
            return l.ToArray();
        }

        public static int SchoolCount { get { return Plan.Length; } }

        private WorldSim(IRngFactory rngs) { Rngs = rngs; }

        /// <summary>新規の世界: 36 校を作り、4 学年ぶんの名簿を入学の割り当てで作り、予熱(burnIn 年度。自校なし)を回す(05 §9.1)。</summary>
        public static WorldSim Create(IRngFactory rngs, int burnIn = 15, double? elitePrefBonus = null)
        {
            var w = new WorldSim(rngs);
            if (elitePrefBonus.HasValue) w.ElitePrefBonus = elitePrefBonus.Value;
            for (int i = 0; i < Plan.Length; i++)
            {
                var s = new School { Id = i, Region = Plan[i].region, Tier = Plan[i].tier };
                s.Prestige = WorldK.TierPrestige[(int)s.Tier]; s.Hist = WorldK.TierHist[(int)s.Tier]; s.Base = WorldK.TierBase[(int)s.Tier];
                w.Schools.Add(s);
            }
            // 初期の名簿: 学年 g の入学年度 = −burnIn − (g − 1)(予熱の年度とは別の鍵)
            for (int g = 1; g <= 4; g++)
            {
                int entryYear = -burnIn - (g - 1);
                List<WorldRunner> pool = PoolGenerator.Generate(rngs, entryYear, WorldK.K_POOL_N, w.ElitePrefBonus);
                Dictionary<int, List<WorldRunner>> intake = Allocator.Allocate(w.Schools, pool, rngs, entryYear, null, null);
                foreach (School s in w.Schools)
                {
                    foreach (WorldRunner r in intake[s.Id])
                    {
                        r.Prog = 0.0;
                        double lv = s.Lv();
                        for (int gg = 1; gg < g; gg++)
                        {
                            r.Grade = gg;
                            double inc = (WorldMath.ProgBase(r.Growth, gg, lv) - WorldMath.ProgBase(r.Growth, gg - 1, lv)) * WorldMath.GapAdj(r.Gap);
                            r.Prog = Q6.Round(Math.Min(0.95, r.Prog + inc));
                        }
                        r.Grade = g;
                        r.SchoolId = s.Id;
                        s.Roster.Add(r);
                    }
                }
            }
            for (int y = 0; y < burnIn; y++) w.PlayYear(-burnIn + 1 + y);
            return w;
        }

        /// <summary>保存した状態から世界を組み立てる(09 §5。読み込み)。乱数は鍵つきで状態を持たないので、学校・選手の状態だけで続きを進められる。</summary>
        public static WorldSim Restore(IRngFactory rngs, double elitePrefBonus, IEnumerable<School> schools, RaceHistory history = null)
        {
            var w = new WorldSim(rngs) { ElitePrefBonus = elitePrefBonus };
            w.Schools.AddRange(schools);
            if (history != null) w.History = history;
            return w;
        }

        public School ById(int id) { return Schools[id]; }

        /// <summary>自校を決める(05 §5): 戦力順位(先発 10 人の力の平均の順位)で開始校を選び、開始時の施設・資金・ポイントを設定する。以後の <see cref="PlayYear"/> には、返した <see cref="PlayerAgent"/> を渡す。</summary>
        public PlayerAgent StartPlayer(Difficulty d)
        {
            return new PlayerAgent(ClaimPlayerSchool(d));
        }

        /// <summary>難易度の戦力順位の学校を自校にし、開始時の施設・資金・ポイントを設定する(05 §5)。名簿(抽象)はそのまま残す。</summary>
        public School ClaimPlayerSchool(Difficulty d)
        {
            ScenarioDef sc = Scenarios.For(d);
            var ranked = new List<School>(Schools);
            Race.RaceSimulator.StableSort(ranked, (a, b) => TeamPower(b).CompareTo(TeamPower(a)));
            School s = ranked[sc.StrengthRank - 1];
            s.IsPlayer = true;
            s.Fac = sc.Facilities;
            s.ScoutLv = s.Fac.Scout;
            s.Funds = sc.Funds;
            s.CumPoints = 0.0;
            s.Ob.Clear();
            return s;
        }

        /// <summary>戦力 = 先発 10 人の力の平均(05 §1)。</summary>
        public double TeamPower(School s)
        {
            var ps = new List<double>();
            double lv = s.Lv();
            foreach (WorldRunner r in s.Roster) ps.Add(WorldMath.SeasonPower(r, lv));
            ps.Sort((a, b) => b.CompareTo(a));
            int n = Math.Min(10, ps.Count);
            double sum = 0;
            for (int i = 0; i < n; i++) sum += ps[i];
            return n > 0 ? sum / n : 0.0;
        }

        /// <summary>1 年度を一度に進める(自校なし、または検算用の抽象の自校): 3 大会と予選会 → 名声・基準名声・指導体制の更新 → 成長と卒業 → 入学の割り当て。</summary>
        public YearResult PlayYear(int year, PlayerAgent agent = null)
        {
            YearState st = BeginYear(year, agent);
            PlayQualifier(st);
            foreach (RaceKind kind in AllRaces) PlayRace(st, kind);
            return EndYear(st);
        }

        public static readonly RaceKind[] AllRaces = { RaceKind.Fall, RaceKind.National, RaceKind.NewYear };

        /// <summary>第一戦の出場権の順(前年度の評価ポイントの多い順、同じなら名声の高い順。安定な整列)。上位 20 校が出る(04 §3.1)。画面(SC-35)は自校の位置を見せる。</summary>
        public List<School> FallOrder()
        {
            var order = new List<School>(Schools);
            Race.RaceSimulator.StableSort(order, (a, b) => a.PrevPoints != b.PrevPoints ? b.PrevPoints.CompareTo(a.PrevPoints) : b.Prestige.CompareTo(a.Prestige));
            return order;
        }

        /// <summary>第二戦の出場権の順(名声の高い順。安定な整列)。上位 30 校が出る。</summary>
        public List<School> PrestigeOrder()
        {
            var order = new List<School>(Schools);
            Race.RaceSimulator.StableSort(order, (a, b) => b.Prestige.CompareTo(a.Prestige));
            return order;
        }

        /// <summary>第三戦のシード校(前年度の第三戦の上位 10 校。10 校に満たなければ名声の上位 10 校)。それ以外は予選会を走る。</summary>
        public List<int> SeedIds()
        {
            var seeds = new List<int>();
            foreach (School s in Schools) if (s.LastNewYearRank != null && s.LastNewYearRank.Value < 10) seeds.Add(s.Id);
            if (seeds.Count < 10)
            {
                seeds.Clear();
                List<School> byPrestige = PrestigeOrder();
                for (int i = 0; i < 10; i++) seeds.Add(byPrestige[i].Id);
            }
            return seeds;
        }

        /// <summary>年度の最初(05 §9・04 §3): 出場権を決め、各校の大会用の選手を用意する。</summary>
        public YearState BeginYear(int year, IPlayerSide player = null)
        {
            var st = new YearState { Year = year, Player = player, Result = new YearResult { Year = year } };
            List<School> orderPrev = FallOrder(), byPrestige = PrestigeOrder();
            st.FallIds = new List<int>(); for (int i = 0; i < 20; i++) st.FallIds.Add(orderPrev[i].Id);
            st.NationalIds = new List<int>(); for (int i = 0; i < 30; i++) st.NationalIds.Add(byPrestige[i].Id);
            st.Seeds = SeedIds();
            st.Rosters = new Dictionary<int, List<RaceRunner>>();
            foreach (School s in Schools)
            {
                double lv = s.Lv();
                if (player != null && ReferenceEquals(s, player.School)) { st.Rosters[s.Id] = player.RaceRoster(lv, Rngs); continue; }
                var l = new List<RaceRunner>();
                foreach (WorldRunner r in s.Roster) l.Add(AiRacing.ToRaceRunner(r, lv, Rngs));
                st.Rosters[s.Id] = l;
            }
            return st;
        }

        /// <summary>年度末の精算(<see cref="EndYearSettle"/>)のあとで保存した状態から、後半(<see cref="EndYearTurnover"/>)に要る最小の年度の状態を作る。後半が使うのは年度・結果・自校だけ。</summary>
        public YearState ResumeSettledYear(int year, IPlayerSide player, YearResult result)
        {
            return new YearState { Year = year, Player = player, Result = result, Settled = true };
        }

        /// <summary>予選会(10 月。シード外のみ。04 §3.2)。通過した 13 校(<see cref="RaceConstants.QualPass"/>)が <see cref="YearResult.Qualified"/> に入る。</summary>
        public void PlayQualifier(YearState st)
        {
            int qid = st.Year * 10 + 4;
            var qteams = new List<QualifierTeam>();
            foreach (School s in Schools)
            {
                if (st.Seeds.Contains(s.Id)) continue;
                if (st.Player != null && ReferenceEquals(s, st.Player.School))
                {
                    QualifierTeam own = st.Player.PrepareQualifier(qid, st.Rosters[s.Id], Rngs);
                    if (own != null) { qteams.Add(own); continue; }
                }
                Pcg32 avail = Rngs.Stream(RngRegistry.LabelRace, qid, 8000 + s.Id);
                Pcg32 forms = Rngs.Stream(RngRegistry.LabelRace, qid, 9000 + s.Id);
                var pool = new List<RaceRunner>();
                foreach (RaceRunner d in st.Rosters[s.Id]) if (avail.Uniform() < RaceConstants.AvailEntry) pool.Add(d);
                AiRacing.Pad(pool, RaceConstants.QualEntry);
                RaceSimulator.StableSort(pool, (a, b) => RaceTables.Rating(b.Stats, SectionType.Long).CompareTo(RaceTables.Rating(a.Stats, SectionType.Long)));
                var runners = pool.GetRange(0, RaceConstants.QualEntry);
                for (int i = 0; i < runners.Count; i++) { runners[i] = runners[i].Clone(); runners[i].Form = AiRacing.DrawForm(forms); }
                qteams.Add(new QualifierTeam { Id = s.Id, Runners = runners.ToArray() });
            }
            Env qenv = RaceMath.GenEnv(Rngs.Stream(RngRegistry.LabelRace, qid, 9999), RaceKind.Fall)[0];
            st.QualifierResults = Qualifier.Run(qteams, (tid, ri) => Rngs.Stream(RngRegistry.LabelRace, qid, tid * 100 + ri), qenv);
            st.Result.Qualified.Clear();
            for (int i = 0; i < RaceConstants.QualPass; i++) st.Result.Qualified.Add(st.QualifierResults[i].Id);
            st.QualifierDone = true;
        }

        /// <summary>大会の識別子 <c>race_id = 年度 × 10 + 種別</c>(第一戦 1 / 第二戦 2 / 第三戦 3。04 §12)。</summary>
        public static int RaceId(YearState st, RaceKind kind) { return st.Year * 10 + (int)kind + 1; }

        /// <summary>大会の環境(04 §6)。日ごとに決まった乱数で、全チーム共通。</summary>
        public Env[] RaceEnv(YearState st, RaceKind kind) { return RaceMath.GenEnv(Rngs.Stream(RngRegistry.LabelRace, RaceId(st, kind), 9999), kind); }

        /// <summary>環境の予報(オーダー提出時に出る。04 §6)。</summary>
        public Env[] RaceForecast(YearState st, RaceKind kind) { return RaceMath.Forecast(Rngs.Stream(RngRegistry.LabelRace, RaceId(st, kind), 9998), RaceEnv(st, kind)); }

        /// <summary>大会に出るチームを、参加校の順に作る。自校の枠は <paramref name="own"/> を呼んで入れる(null = 棄権)。他校は鍵つきの乱数で決まるので、何度呼んでも同じ。</summary>
        public List<RaceTeam> BuildTeams(YearState st, RaceKind kind, Env[] envs, Func<RaceTeam> own)
        {
            int rid = RaceId(st, kind);
            var teams = new List<RaceTeam>();
            foreach (int sid in st.Entrants(kind))
            {
                if (st.Player != null && sid == st.Player.School.Id)
                {
                    RaceTeam mine = own();
                    if (mine != null) teams.Add(mine);                                         // null = 棄権(04 §4.1 E1)
                    continue;
                }
                var (lineup, instr) = AiRacing.PrepareLineup(st.Rosters[sid], kind, envs, Rngs, rid, sid);
                teams.Add(new RaceTeam { Id = sid, Lineup = lineup, Instr = instr, Geki = 0 });
            }
            return teams;
        }

        /// <summary>自校のチームを差し替えて、結果の見込みを出す(年度の状態は変えない)。当日の画面・レース進行が、いまの指示で何度でも再計算するのに使う。</summary>
        public List<TeamResult> PreviewRace(YearState st, RaceKind kind, RaceTeam own)
        {
            Env[] envs = RaceEnv(st, kind);
            int rid = RaceId(st, kind);
            List<RaceTeam> teams = BuildTeams(st, kind, envs, () => own);
            return RaceSimulator.Simulate(kind, teams, envs, (tid, si) => Rngs.Stream(RngRegistry.LabelRace, rid, tid * 100 + si));
        }

        /// <summary>大会 1 つ(第一〜第三戦)。第三戦は予選会のあと。順位と優勝校は <see cref="YearResult"/> に入り、出走したチームと区間ごとの結果を返す。</summary>
        public RaceOutcome PlayRace(YearState st, RaceKind kind)
        {
            if (kind == RaceKind.NewYear && !st.QualifierDone && st.Result.Qualified.Count == 0) throw new InvalidOperationException("第三戦は予選会のあと");
            int rid = RaceId(st, kind);
            Env[] envs = RaceEnv(st, kind);
            List<RaceTeam> teams = BuildTeams(st, kind, envs, () => st.Player.PrepareRace(kind, rid, envs, st.Rosters[st.Player.School.Id], Rngs));
            List<TeamResult> sim = RaceSimulator.Simulate(kind, teams, envs, (tid, si) => Rngs.Stream(RngRegistry.LabelRace, rid, tid * 100 + si));
            var order = new List<int>();
            foreach (TeamResult t in sim) order.Add(t.Id);
            st.Result.Order[kind] = order;
            st.Result.Winner[kind] = order[0];
            List<RecordUpdate> records = History.Record(st.Year, kind, sim, teams);
            return new RaceOutcome { Kind = kind, RaceId = rid, Teams = teams, Results = sim, Records = records };
        }

        /// <summary>年度末: 名声ポイント → 名声・基準名声・指導体制 → 資金の精算 → 成長と卒業 → 入学の割り当て(自校は <see cref="IPlayerSide"/> の hook)。
        /// <see cref="EndYearSettle"/> と <see cref="EndYearTurnover"/> を続けて行う(おまかせ・検算用)。</summary>
        public YearResult EndYear(YearState st)
        {
            EndYearSettle(st);
            return EndYearTurnover(st);
        }

        /// <summary>年度末の前半(05 §12 の手順 1〜3): 名声ポイントと評価ポイントの集計 → 全校の名声・基準名声・指導体制 → 自校の資金の精算(<see cref="IPlayerSide.Finance"/>)。
        /// ここで一度止められる(画面は精算の結果を見せ、施設の強化を受け付けてから <see cref="EndYearTurnover"/> へ進む。08 §6.1)。</summary>
        public void EndYearSettle(YearState st)
        {
            if (st.Settled) throw new InvalidOperationException("年度末の精算は済んでいる");
            int year = st.Year;
            YearResult res = st.Result;
            IPlayerSide agent = st.Player;
            foreach (School s in Schools) { s.YearPoints = 0; s.FamePoints = 0.0; }
            foreach (var kv in res.Order)
            {
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    School s = Schools[kv.Value[i]];
                    s.FamePoints += WorldK.K_FAME_ENTRY + WorldK.K_FAME_TOP * Math.Exp(-i / WorldK.K_FAME_DECAY);
                    if (i < 10) s.YearPoints += WorldK.YearPoints[i];
                }
            }
            if (res.Winner[RaceKind.Fall] == res.Winner[RaceKind.National] && res.Winner[RaceKind.National] == res.Winner[RaceKind.NewYear])
            {
                School s = Schools[res.Winner[RaceKind.Fall]];
                s.YearPoints += 30; s.FamePoints += 30;
            }
            if (agent != null) agent.AfterRaces(this, year, res);
            // 年度末: 名声・基準名声・指導体制
            List<int> ny = res.Order[RaceKind.NewYear];
            for (int i = 0; i < ny.Count; i++) Schools[ny[i]].LastNewYearRank = i;
            foreach (School s in Schools)
            {
                if (!ny.Contains(s.Id)) s.LastNewYearRank = null;
                WorldMath.UpdatePrestige(s);
                if (!s.IsPlayer)
                {
                    Pcg32 g = Rngs.Stream(RngRegistry.LabelWorldUpdate, year, 100 + s.Id);
                    s.Base = Q6.Round(School.Clamp(s.Base + g.Normal(0, WorldK.BaseDriftSd), WorldK.BaseMin, WorldK.BaseMax));
                    s.Prestige = Q6.Round(s.Prestige + WorldK.RivalRevert * (s.Base - s.Prestige));
                    s.CoachQ = Q6.Round(School.Clamp(s.CoachQ * (1 - WorldK.CoachRevert) + g.Normal(0, WorldK.CoachSd), -WorldK.CoachMax, WorldK.CoachMax));
                }
                s.PrevPoints = s.YearPoints;
                s.CumPoints += s.YearPoints;
            }
            if (agent != null) agent.Finance();                         // 資金・施設の強化(成長の確定の前。施設レベルが成長に効く)
            st.Settled = true;
        }

        /// <summary>年度末の後半(05 §12 の手順 4〜7): 成長の確定と卒業 → 特別招聘 → 入学の割り当て。<see cref="EndYearSettle"/> のあと。</summary>
        public YearResult EndYearTurnover(YearState st)
        {
            if (!st.Settled) throw new InvalidOperationException("年度末の精算が先");
            int year = st.Year;
            YearResult res = st.Result;
            IPlayerSide agent = st.Player;
            // 成長の確定と卒業
            foreach (School s in Schools)
            {
                double lv = s.Lv();
                foreach (WorldRunner r in s.Roster) WorldMath.SeasonEnd(r, lv);
                if (agent != null && ReferenceEquals(s, agent.School)) agent.Graduating(s.Roster.FindAll(r => r.Grade == 4));
                s.Roster.RemoveAll(r => r.Grade == 4);
                foreach (WorldRunner r in s.Roster) r.Grade += 1;
            }
            WorldRunner ticketRunner = agent != null ? agent.TicketStep(year, res, Rngs) : null;
            // 入学
            List<WorldRunner> pool2 = PoolGenerator.Generate(Rngs, year, WorldK.K_POOL_N, ElitePrefBonus);
            PlayerOffer offer = agent != null ? agent.Offer(this, pool2, Rngs) : null;
            var misses = new List<AllocationMiss>();
            Dictionary<int, List<WorldRunner>> intake = Allocator.Allocate(Schools, pool2, Rngs, year, agent != null ? agent.School : null, offer, agent != null ? agent.PlayerP10 : null, agent != null ? agent.PlayerRosterCount : null,
                                                                             agent != null ? (Func<WorldRunner, bool>)agent.Targeted : null, misses);
            if (agent != null) agent.Missed(misses);
            foreach (School s in Schools)
            {
                List<WorldRunner> add = intake[s.Id];
                if (agent != null && ReferenceEquals(s, agent.School)) add = agent.Intake(add, ticketRunner);
                foreach (WorldRunner r in add) { r.Grade = 1; r.Prog = 0.0; r.SchoolId = s.Id; s.Roster.Add(r); }
            }
            return res;
        }
    }
}
