using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Race;
using Ekiden.Domain.Recruit;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Text;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.Training;
using Ekiden.Domain.World;
using Calendar = Ekiden.Domain.Team.Calendar;

namespace Ekiden.Domain.Game
{
    /// <summary>1 週の結果(画面に見せるもの)。</summary>
    public sealed class WeekOutcome
    {
        public int AbsWeek;
        public List<WeekReport> Reports;
        public List<int> RunnerIds;              // Reports と同じ並びの選手 ID
        public List<FiredEvent> Events;
        public RaceOutcome Race;                 // その週に大会があれば(自校が出ていなくても世界の結果)
        public RaceInfo? OwnRace;                // 自校が出走したとき
        public YearResult YearEnd;               // 年度末の週なら、その年度の結果
        public YearTurnover Turnover;            // 同じく、卒業と退部
    }

    /// <summary>世界(<see cref="WorldSim"/>)と自校(<see cref="TeamState"/>)の保存の形に入らない、1 回のプレイの進行の状態。保存の層(<c>GameSnapshot</c>)が JSON にする。</summary>
    public sealed class GameSessionExtras
    {
        public List<PlayerYearStats> Stats = new List<PlayerYearStats>();
        public int YearWins;                   // 今年度の優勝の集合(bit 0〜2)
        public double RacePower;               // 第三戦の週の戦力(Q6 で閉じている)
        public int NextTicketKind;
        public YearResult YearInProgress;      // 年度の途中なら、ここまでの結果(予選会の通過校と大会の順位)。年度の最初の週なら null
        public bool YearSettled;               // 年度末の精算まで済んだ(強化の画面。第 48 週の終わり)
        public SortedDictionary<int, ScoutStage> Scout = new SortedDictionary<int, ScoutStage>();     // 今年度のスカウトの行動(候補の番号 → 段階)
        public SortedSet<int> Guided = new SortedSet<int>();                                          // 今週、個別指導のメニューを指定した選手(取り消しでポイントが戻る)
        public int[][] Orders = new int[3][];                                                         // 提出された区間オーダー(第一〜第三戦。null = おまかせ)
        public int[] QualifierEntries;                                                                // 予選会の登録(null = おまかせ)
    }

    /// <summary>
    /// ゲーム本体の 1 回のプレイ(M0 の頭脳。画面なし): 36 校の世界(<see cref="WorldSim"/>。他校は抽象)+ 自校の実体の選手(<see cref="TeamState"/>)。
    /// 1 週ずつ <see cref="StepWeek"/> で進める(06 §7.1): 週の最初 → 全選手の週の解決と絆 → 予選会・大会と大会後の処理 → イベント → 月末 → 年度末(05 §12)。
    /// 方針は『おまかせ』(月間計画・登録・当日の配置・スカウト・施設の強化・特別招聘・イベントの選択)。世界の年度処理は <see cref="IPlayerSide"/> の差し込みで共有し、
    /// 他校・名声・入学の割り当ては検算済みの抽象の自校(<see cref="PlayerAgent"/>)と同じ道を通る。
    /// </summary>
    public sealed partial class GameSession : IPlayerSide
    {
        public const int BurnInYears = 15;
        public const int QualifierWeek = 26;               // 10 月第 3 週(04 §3.2)
        public const int TicketDeclareWeek = 40;           // 2 月の最初の週: 招聘の宣言(おまかせ)。大会の節目はすべて済んでいる
        public const int RaceFatigueWeekThird = 36;

        public ulong Seed { get; }
        public IRngFactory Rngs { get; }
        public WorldSim World { get; private set; }
        public TeamState State { get; private set; }
        public School School { get; private set; }
        public readonly List<PlayerYearStats> Stats = new List<PlayerYearStats>();
        public YearState YearInProgress { get { return _ys; } }

        /// <summary>年度末の精算まで済んでいるか(強化の画面。第 48 週の終わり)。</summary>
        public bool IsYearSettled { get { return _ys != null && _ys.Settled; } }

        public TrainingMaster Master { get; }
        public EventCatalog Catalog { get; }
        private readonly TeamWeek _week;
        private readonly EventPhase _phase;
        private readonly IChoicePolicy _policy = new DefaultChoicePolicy();
        private YearState _ys;
        private int _yearWins;                              // 今年度の優勝の集合(bit 0〜2)。招聘券の節目(大会の直後に判定)に使う
        private double _racePower;                          // 第三戦の週(年度の 75% 経過)の戦力 = 先発 10 人の力の平均。世界の戦力 P と同じ時点
        private int _nextTicketKind;                        // 招聘の種別を交互に(スーパールーキー → 留学生 → …)
        private HashSet<int> _targets = new HashSet<int>(); // 今年度のスカウトの目標(候補の番号)
        private SpecialRecruit _ticketRecruit;              // 年度末に入学する招聘の選手

        public int Year { get { return Calendar.FloorDiv(State.AbsWeek, Calendar.WeeksPerYear) + 1; } }

        public int YearWeek { get { return TeamWeek.YearWeek(State); } }

        private GameSession(ulong seed, IRngFactory rngs, WorldSim world, School school, TrainingMaster master, EventCatalog catalog)
        {
            Seed = seed;
            Rngs = rngs;
            World = world;
            School = school;
            Master = master;
            Catalog = catalog;
            _week = new TeamWeek(master);
            _phase = new EventPhase(catalog, rngs, new ApproxEventWorld(seed));
        }

        // ------------------------------------------------------------ 新規ゲーム
        /// <summary>新規ゲーム(05 §5・§9.1): 世界を作って予熱(自校なし。15 年度)→ 難易度の戦力順位の学校を自校に → その名簿の途中の選手を実体の選手に展開する。</summary>
        public static GameSession New(ulong seed, Difficulty difficulty, double? elitePrefBonus = null)
        {
            var rngs = new KeyedRngFactory(seed);
            WorldSim world = WorldSim.Create(rngs, BurnInYears, elitePrefBonus);
            School school = world.ClaimPlayerSchool(difficulty);
            var gs = new GameSession(seed, rngs, world, school, TrainingMaster.CreateDefault(), EventCatalogData.Create());
            gs.Setup(difficulty);
            return gs;
        }

        /// <summary>保存した状態から組み立てる(09 §5.6)。取り出しは週の境目。年度の途中なら、その年度の出場権・他校の選手を <see cref="WorldSim.BeginYear"/> でやり直し(年度の途中では変わらない)、
        /// ここまでの結果を戻す。乱数は鍵つきで状態を持たないので、続きは保存しなかったときと一致する。</summary>
        public static GameSession Restore(WorldSim world, TeamState state, GameSessionExtras extras)
        {
            School school = world.Schools.Single(s => s.IsPlayer);
            var gs = new GameSession(state.Seed, world.Rngs, world, school, TrainingMaster.CreateDefault(), EventCatalogData.Create());
            gs.State = state;
            gs.Stats.AddRange(extras.Stats);
            gs._yearWins = extras.YearWins;
            gs._racePower = extras.RacePower;
            gs._nextTicketKind = extras.NextTicketKind;
            foreach (var kv in extras.Scout) gs._scout[kv.Key] = kv.Value;
            foreach (int id in extras.Guided) gs._guidedThisWeek.Add(id);
            for (int i = 0; i < 3; i++) gs._orders[i] = extras.Orders[i] == null ? null : (int[])extras.Orders[i].Clone();
            gs._qualifierEntries = extras.QualifierEntries == null ? null : (int[])extras.QualifierEntries.Clone();
            gs.SyncSchool();                                                  // 資金・施設は自校(TeamState)が正
            state.Refresh(assignGroups: false);                               // 派生値(力の順位・レギュラー・絆の平均など)は保存しない。週の最初の処理は済んでいるので、ここで作り直す(L-51)
            if (extras.YearSettled)
            {
                gs._ys = world.ResumeSettledYear(gs.Year, gs, extras.YearInProgress);       // 精算のあとは学校の状態が変わっているので、BeginYear はやり直さない
            }
            else if (TeamWeek.YearWeek(state) != 0)
            {
                gs._ys = world.BeginYear(gs.Year, gs);
                YearResult saved = extras.YearInProgress;
                if (saved != null)
                {
                    gs._ys.Result.Qualified.AddRange(saved.Qualified);
                    foreach (KeyValuePair<RaceKind, List<int>> kv in saved.Order)
                    {
                        gs._ys.Result.Order[kv.Key] = new List<int>(kv.Value);
                        gs._ys.Result.Winner[kv.Key] = kv.Value[0];
                    }
                    gs._ys.QualifierDone = saved.Qualified.Count > 0;
                }
            }
            return gs;
        }

        /// <summary>保存に要る進行の状態(世界と自校の保存の形に入らないもの)。</summary>
        public GameSessionExtras CaptureExtras()
        {
            var ex = new GameSessionExtras { YearWins = _yearWins, RacePower = _racePower, NextTicketKind = _nextTicketKind };
            ex.Stats.AddRange(Stats);
            ex.YearInProgress = _ys == null ? null : _ys.Result;
            ex.YearSettled = _ys != null && _ys.Settled;
            foreach (var kv in _scout) ex.Scout[kv.Key] = kv.Value;
            foreach (int id in _guidedThisWeek) ex.Guided.Add(id);
            for (int i = 0; i < 3; i++) ex.Orders[i] = _orders[i] == null ? null : (int[])_orders[i].Clone();
            ex.QualifierEntries = _qualifierEntries == null ? null : (int[])_qualifierEntries.Clone();
            return ex;
        }

        private void Setup(Difficulty difficulty)
        {
            ScenarioDef sc = Scenarios.For(difficulty);
            int pref = Geo.HomePrefecture(School.Region);
            var s = new TeamState
            {
                Seed = Seed, SchoolPref = pref, Funds = Rounding.HalfUp(School.Funds), Prestige = School.Prestige, Morale = 50, CoachRank = School.Rank(), Fac = School.Fac,
                Scenario = new ScenarioRef { Id = sc.Id, Difficulty = difficulty.ToString(), PlayerSchoolId = School.Id },
            };
            s.Settings.PlanAuto = true;
            s.Settings.EventAuto = true;
            s.Settings.ScoutAuto = true;
            var roster = new List<WorldRunner>(School.Roster);
            if (sc.Members.HasValue && roster.Count > sc.Members.Value) roster = Thin(roster, sc.Members.Value, Rngs);
            foreach (WorldRunner w in roster)
            {
                Runner r = RunnerFactory.FromInFlight(w, s.NextRunnerId++, 1, Rngs, pref);
                NameGenerator.Assign(r, Rngs, TakenIn(s));
                s.Runners.Add(r);
                s.InitBonds(r);
            }
            School.Roster.Clear();                                       // 以後、自校の選手は TeamState が持つ(世界の抽象の名簿は空)
            State = s;
            s.AssignRecommendedGroups();                                 // 新規ゲームの開始時は推奨の編成(08 R-04)
            SyncSchool();
            _week.BeginWeek(s);                                          // 最初の週の最初の処理(以後は各週の終わりで次の週のぶんを行う)
        }

        /// <summary>名前の重複の判定(名前の生成。いまの部員と同じ姓名は避ける)。</summary>
        private static Func<string, bool> TakenIn(TeamState s)
        {
            return full => s.Runners.Any(x => x.FamilyName + x.GivenName == full);
        }

        /// <summary>部員数の指定(同好会など)のとき、名簿から無作為に残す(label 9・(0, 0)。Fisher–Yates の先頭 n 人。残った人は元の並びのまま)。</summary>
        private static List<WorldRunner> Thin(List<WorldRunner> roster, int keep, IRngFactory rngs)
        {
            Pcg32 g = rngs.Stream(RngRegistry.LabelTeamYear, 0, 0);
            var idx = new int[roster.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            for (int i = idx.Length - 1; i > 0; i--)
            {
                int j = g.RandInt(0, i);
                int t = idx[i]; idx[i] = idx[j]; idx[j] = t;
            }
            var chosen = new List<int>();
            for (int i = 0; i < keep; i++) chosen.Add(idx[i]);
            chosen.Sort();
            return chosen.Select(i => roster[i]).ToList();
        }

        // ------------------------------------------------------------ 週
        /// <summary>1 週を進める(おまかせ・検算用。止まらずに最後まで)。画面からは <see cref="GameFlow"/> が下の段階を 1 つずつ呼ぶ。</summary>
        public WeekOutcome StepWeek()
        {
            int yw = TeamWeek.YearWeek(State);
            var o = new WeekOutcome { AbsWeek = State.AbsWeek };
            o.Reports = ResolveTraining(out o.RunnerIds);
            o.Race = PlayWeekRaces(out o.OwnRace);
            EventSelection sel = SelectWeekEvents(o.OwnRace);
            o.Events = ResolveWeekEvents(sel, o.OwnRace, _policy);
            FinishWeek();
            if (yw == Calendar.WeeksPerYear - 1)
            {
                SettleYear();
                o.YearEnd = TurnoverYear();
                o.Turnover = LastTurnover;
            }
            AdvanceWeek();
            return o;
        }

        /// <summary>今年度の世界の準備(出場権・他校の選手)がなければ作る。</summary>
        private void EnsureYear()
        {
            int year = Year;
            if (_ys == null || _ys.Year != year)
            {
                _ys = World.BeginYear(year, this);
                _yearWins = 0;
            }
        }

        /// <summary>①全選手の週の解決と絆の更新(06 §7.1)。</summary>
        public List<WeekReport> ResolveTraining()
        {
            List<int> ids;
            return ResolveTraining(out ids);
        }

        /// <summary>同上。結果と同じ並びの選手 ID も返す。</summary>
        public List<WeekReport> ResolveTraining(out List<int> runnerIds)
        {
            EnsureYear();
            runnerIds = State.Runners.Select(r => r.Id).ToList();
            _weekResolved = true;
            return _week.TrainAll(State, Rngs);
        }

        /// <summary>今年度の世界の準備(出場権・他校の選手)を作る(なければ)。画面が出場権を調べるときに先に呼ぶ。</summary>
        public YearState PrepareYear()
        {
            EnsureYear();
            return _ys;
        }

        /// <summary>③予選会(第 26 週)・大会(第 25・28・36 週)と大会後の処理。大会のない週は null。自校が出走したときだけ <paramref name="own"/> が入る。</summary>
        public RaceOutcome PlayWeekRaces(out RaceInfo? own)
        {
            TeamState s = State;
            EnsureYear();
            int yw = TeamWeek.YearWeek(s), year = Year;
            own = null;
            if (yw == RaceFatigueWeekThird) _racePower = Q6.Round(OwnPower());      // 保存する値は Q6 で閉じる(09 D-10)
            if (yw == QualifierWeek) { World.PlayQualifier(_ys); _qualifierEntries = null; }
            int k = Array.IndexOf(Calendar.RaceWeeks, yw);
            if (k < 0) return null;
            var kind = (RaceKind)k;
            RaceOutcome race = World.PlayRace(_ys, kind);
            own = TeamRacing.ApplyResult(s, race, School.Id);
            TeamRacing.CloseRace(s, kind);
            _orders[k] = null;
            _raceDay = null;
            int idx = _ys.Result.Order[kind].IndexOf(School.Id);
            int rank = idx >= 0 ? idx + 1 : 100;
            if (rank == 1) _yearWins |= 1 << k;
            TicketMilestones.EarnAfterRace(s.Tickets, year, k + 1, rank, _yearWins);
            return race;
        }

        /// <summary>④イベントの発生の判定(06 §7.2 の前半。状態は変えない)。</summary>
        public EventSelection SelectWeekEvents(RaceInfo? own) { return _phase.Select(State, own); }

        /// <summary>いま選べる選択肢(req を満たすもの。定義の順)。</summary>
        public List<ChoiceDef> ValidChoices(FiredEvent f, RaceInfo? own) { return _phase.ValidChoices(State, f, own); }

        /// <summary>④イベントの選択と効果(06 §7.2 の後半)。k 番目から順に解決する。大会の結果(<c>race.*</c>)はこの週だけ。</summary>
        public List<FiredEvent> ResolveWeekEvents(EventSelection sel, RaceInfo? own, IChoicePolicy policy)
        {
            for (int k = 0; k < sel.Fired.Count; k++) _phase.Resolve(State, sel.Fired[k], k, own, policy);
            foreach (Runner r in State.Runners) r.RaceFlags = default(RaceFlags);
            return sel.Fired;
        }

        /// <summary>⑤週の終わりの処理: 招聘の宣言(おまかせのとき)・月末(第 4 週)。</summary>
        public void FinishWeek()
        {
            int yw = TeamWeek.YearWeek(State);
            if (yw == TicketDeclareWeek && AutoDeclareTickets) AutoDeclare(Year, yw / 4);
            if (yw % 4 == 3) _week.MonthEnd(State);
        }

        /// <summary>年度末の前半(05 §12 の手順 1〜3。第 48 週の終わり): 評価ポイント・名声・資金の精算。強化の前。</summary>
        public void SettleYear()
        {
            EnsureYear();
            World.EndYearSettle(_ys);
            SyncSchool();
        }

        /// <summary>年度末の後半(手順 4〜7): 成長 → 卒業と退部 → 招聘の入学 → 入学の割り当て。</summary>
        public YearResult TurnoverYear()
        {
            YearResult res = World.EndYearTurnover(_ys);
            State.Prestige = School.Prestige;
            State.CoachRank = School.Rank();
            _ys = null;
            return res;
        }

        /// <summary>週を閉じて次の週の最初の処理まで行う(保存の境目)。</summary>
        public void AdvanceWeek()
        {
            SyncSchool();                                                    // 世界の側の自校(資金・施設)を自校の状態に揃える(イベントが資金を変える)
            CloseWeekEdits();
            _weekResolved = false;
            State.AbsWeek++;
            _week.BeginWeek(State);                                          // 次の週の最初の処理。保存の境目はこれが済んだ状態(登録・月間計画などの画面はここから開く)
        }

        /// <summary>招聘の宣言をおまかせで行うか(<see cref="StepWeek"/> は 2 月の最初の週に行う。<see cref="GameFlow"/> は false で、宣言は画面の操作)。</summary>
        public bool AutoDeclareTickets { get; set; } = true;

        /// <summary>n 年度ぶん進める(年度の最初の週から始めること)。</summary>
        public void PlayYears(int n)
        {
            for (int i = 0; i < n; i++)
                for (int w = 0; w < Calendar.WeeksPerYear; w++) StepWeek();
        }

        /// <summary>直近の年度末の卒業と退部。</summary>
        public YearTurnover LastTurnover { get; private set; }

        /// <summary>直近の年度末の精算の内訳(年度の結果の画面 SC-12)。保存しないので、復元した直後は null。</summary>
        public Settlement LastSettlement { get; private set; }

        private int _settlementYear = -1, _turnoverYear = -1, _intakeYear = -1;

        /// <summary>直近の年度末に入学した選手の ID(入学者発表の画面)。</summary>
        public IReadOnlyList<int> LastIntakeIds { get { return _lastIntake; } }

        private List<int> _lastIntake = new List<int>();

        /// <summary>直近の入学者のうち、声かけ・練習会で狙っていた選手の ID と、候補のときの評価★(入学者発表の画面 SC-14)。保存しない。</summary>
        public IReadOnlyCollection<int> LastIntakeTargeted { get { return _lastIntakeTargeted; } }

        public IReadOnlyDictionary<int, int> LastIntakeStars { get { return _lastIntakeStars; } }

        private HashSet<int> _lastIntakeTargeted = new HashSet<int>();
        private Dictionary<int, int> _lastIntakeStars = new Dictionary<int, int>();

        private void AutoDeclare(int year, int month)
        {
            int active = State.Runners.Count(r => r.RecruitSource == RecruitSource.Ticket);
            if (!State.Tickets.CanDeclare(year, month, active)) return;
            TicketKind kind = (_nextTicketKind % 2 == 0) ? TicketKind.Rookie : TicketKind.Intl;
            _nextTicketKind++;
            State.Tickets.Declare(year, kind);
        }

        // ------------------------------------------------------------ 戦力
        /// <summary>自校の戦力 = 先発 10 人の力(Long の簡易値 <see cref="TeamState.LongP(Runner)"/>)の平均。世界の戦力(<see cref="WorldSim.TeamPower"/>)と同じ尺度。</summary>
        public double OwnPower()
        {
            var ps = State.Runners.Select(r => TeamState.LongP(r)).OrderByDescending(x => x).ToList();
            int n = Math.Min(10, ps.Count);
            double sum = 0;
            for (int i = 0; i < n; i++) sum += ps[i];
            return n > 0 ? sum / n : 0.0;
        }

        /// <summary>入学の割り当てに使う『来年度の名簿の力の 10 番目』(足りなければ 40。抽象の <see cref="PlayerAgent.P10Of"/> と同じ約束)。</summary>
        private double P10()
        {
            var ps = State.Runners.Select(r => TeamState.LongP(r)).OrderByDescending(x => x).ToList();
            return ps.Count >= 10 ? ps[9] : 40.0;
        }

        private void SyncSchool()
        {
            School.Funds = State.Funds;
            School.Fac = State.Fac;
            School.ScoutLv = State.Fac.Scout;
        }

        // ------------------------------------------------------------ IPlayerSide(世界の年度処理への差し込み)
        List<RaceRunner> IPlayerSide.RaceRoster(double lv, IRngFactory rngs)
        {
            return State.Runners.Select(RaceRunner.From).ToList();                // 大会の当日の準備は、そのときの選手の状態から作る(下の PrepareRace)
        }

        QualifierTeam IPlayerSide.PrepareQualifier(int qualId, List<RaceRunner> roster, IRngFactory rngs)
        {
            var pool = new List<RaceRunner>();
            Pcg32 avail = rngs.Stream(RngRegistry.LabelRace, qualId, 8000 + School.Id);
            if (_qualifierEntries != null)                                                              // 手動の登録: 登録した 12 名。欠場した選手のぶんは、残りの長距離の適性の高い順に補う
            {
                var rest = new List<RaceRunner>();
                foreach (Runner r in State.Runners.OrderBy(x => x.Id))
                {
                    if (!TeamRacing.CanRun(State, r) || Array.IndexOf(_qualifierEntries, r.Id) >= 0) continue;
                    rest.Add(RaceRunner.From(r));
                }
                RaceSimulator.StableSort(rest, (a, b) => RaceTables.Rating(b.Stats, SectionType.Long).CompareTo(RaceTables.Rating(a.Stats, SectionType.Long)));
                foreach (int id in _qualifierEntries)
                {
                    Runner r = State.FindRunner(id);
                    if (r == null || !TeamRacing.CanRun(State, r)) continue;
                    if (avail.Uniform() < RaceConstants.AvailEntry) pool.Add(RaceRunner.From(r));
                }
                for (int i = 0; i < rest.Count && pool.Count < RaceConstants.QualEntry; i++) pool.Add(rest[i]);
                AiRacing.Pad(pool, RaceConstants.QualEntry);
                return new QualifierTeam { Id = School.Id, Runners = pool.GetRange(0, RaceConstants.QualEntry).ToArray() };
            }
            foreach (Runner r in State.Runners.OrderBy(x => x.Id))
            {
                if (!TeamRacing.CanRun(State, r)) continue;
                if (avail.Uniform() < RaceConstants.AvailEntry) pool.Add(RaceRunner.From(r));      // 調子は実際の調子
            }
            AiRacing.Pad(pool, RaceConstants.QualEntry);
            RaceSimulator.StableSort(pool, (a, b) => RaceTables.Rating(b.Stats, SectionType.Long).CompareTo(RaceTables.Rating(a.Stats, SectionType.Long)));
            return new QualifierTeam { Id = School.Id, Runners = pool.GetRange(0, RaceConstants.QualEntry).ToArray() };
        }

        RaceTeam IPlayerSide.PrepareRace(RaceKind kind, int raceId, Env[] envs, List<RaceRunner> roster, IRngFactory rngs)
        {
            if (_raceDay != null && _raceDay.Kind == kind) return _raceDay.BuildTeam();                  // 画面で決めた当日
            int[] order = _orders[(int)kind];
            if (order != null)                                                                        // 提出されたオーダー + おまかせの当日
            {
                TeamRacing.DayPlan plan = TeamRacing.OpenDay(State, School.Id, kind, raceId, envs, rngs, order);
                return plan == null ? null : new RaceDay(plan, State, null).BuildTeam();
            }
            return TeamRacing.PrepareRace(State, School.Id, kind, raceId, envs, rngs);
        }

        void IPlayerSide.AfterRaces(WorldSim world, int year, YearResult res)
        {
            SyncSchool();
            State.YearPoints = School.YearPoints;
            double mine = _racePower;
            var st = new PlayerYearStats
            {
                Year = year, P = Q6.Round(mine), Prestige = School.Prestige, Funds = State.Funds, Lv = Q6.Round(School.Lv()), Points = School.YearPoints, CoachRank = School.Rank(), Cum = School.CumPoints,
            };
            int better = 0;
            double top1 = double.MinValue;
            foreach (School o in world.Schools)
            {
                if (o.IsPlayer) continue;
                double p = world.TeamPower(o);
                if (p > mine) better++;
                if (p > top1) top1 = p;
            }
            st.PowerRank = 1 + better;
            st.Top1 = Q6.Round(top1);
            RaceKind[] kinds = WorldSim.AllRaces;
            for (int i = 0; i < 3; i++)
            {
                st.Wins[i] = res.Winner[kinds[i]] == School.Id;
                int idx = res.Order[kinds[i]].IndexOf(School.Id);
                st.Place[i] = idx >= 0 ? idx + 1 : 99;
            }
            Stats.Add(st);
            if (State.Cleared == null && Stats.Any(x => x.Wins[0]) && Stats.Any(x => x.Wins[1]) && Stats.Any(x => x.Wins[2]))
                State.Cleared = new ClearInfo { Year = year, NoInvite = State.Tickets.InvitesDeclared == 0 };
        }

        /// <summary>年度末の精算のとき、推奨の強化も自動で行うか(おまかせ = true)。手動のとき(<see cref="GameFlow"/>)は false で、強化は画面の操作で行う。</summary>
        public bool AutoUpgrade { get; set; } = true;

        void IPlayerSide.Finance()
        {
            LastSettlement = TeamYear.Settle(State, School.Prestige, School.YearPoints, School.Rank(), AutoUpgrade);
            _settlementYear = Year;
            SyncSchool();
        }

        void IPlayerSide.Graduating(IReadOnlyList<WorldRunner> graduates)
        {
            LastTurnover = TeamYear.GraduateAndQuit(State, Year, Rngs);
            _turnoverYear = Year;
        }

        WorldRunner IPlayerSide.TicketStep(int year, YearResult res, IRngFactory rngs)
        {
            TicketState t = State.Tickets;
            WorldRunner marker = null;
            _ticketRecruit = null;
            if (t.Pending != null)
            {
                PendingInvite p = t.Pending;
                _ticketRecruit = SpecialRecruitGenerator.Generate(rngs, p.EnrollYear, p.Kind, p.Pref);
                marker = new WorldRunner { Ticket = true, Q = _ticketRecruit.Q, V = _ticketRecruit.V, Index = -1 };      // 割り当てには出ない。Intake で実体にする
                t.ConfirmEnrollment();
            }
            TicketMilestones.EarnAtYearEnd(t, year, School.Rank(), year);
            return marker;
        }

        double? IPlayerSide.PlayerP10 { get { return P10(); } }

        int? IPlayerSide.PlayerRosterCount { get { return State.Runners.Count; } }

        PlayerOffer IPlayerSide.Offer(WorldSim world, IReadOnlyList<WorldRunner> pool, IRngFactory rngs)
        {
            var obByPref = new Dictionary<int, int>();
            foreach (OBRecord ob in State.Obs)
            {
                obByPref.TryGetValue(ob.Pref, out int n);
                obByPref[ob.Pref] = n + 1;
            }
            if (_scout.Count > 0 || !State.Settings.ScoutAuto)                       // 手動の行動(なければ行動なし)を提示に使う
            {
                _targets = new HashSet<int>(_scout.Where(kv => kv.Value == ScoutStage.Invited).Select(kv => kv.Key));
                PlayerOffer manual = ManualScoutOffer(obByPref);
                _scout.Clear();
                State.ScoutPts = 0;
                return manual;
            }
            int points = Math.Max(0, (int)(State.ScoutPts + State.ModSum(ModKind.ScoutPts)));
            _targets = PlayerAgent.ChooseTargetsFor(School, P10(), points, world.Schools, pool, rngs);
            State.ScoutPts = 0;                                                      // スカウトのポイントは年度内の繰り越しだけ(05 §7.1)
            HashSet<int> targets = _targets;
            return r =>
            {
                obByPref.TryGetValue(r.Overseas ? 0 : r.PrefectureId, out int n);
                return (targets.Contains(r.Index) ? WorldK.InterestMax : 0.0) + WorldK.ObBonus * Math.Min(5, (int)Math.Sqrt(n));
            };
        }

        List<WorldRunner> IPlayerSide.Intake(List<WorldRunner> allocated, WorldRunner ticket)
        {
            TeamState s = State;
            int enrollWeek = Year * Calendar.WeeksPerYear;
            if (ticket != null && allocated.Count >= 10)                              // 定員 10 のうち 1 人分を招聘枠が占める(Q の最も低い入学者を外す)
            {
                WorldRunner lowest = allocated[0];
                foreach (WorldRunner r in allocated) if (r.Q < lowest.Q) lowest = r;
                allocated.Remove(lowest);
            }
            _lastIntake = new List<int>();
            _intakeYear = Year;
            _lastIntakeTargeted = new HashSet<int>();
            _lastIntakeStars = new Dictionary<int, int>();
            foreach (WorldRunner c in allocated)
            {
                Runner r = RunnerFactory.FromCandidate(c, s.NextRunnerId++, enrollWeek, Rngs, s.SchoolPref, _targets.Contains(c.Index));
                if (_targets.Contains(c.Index))
                {
                    _lastIntakeTargeted.Add(r.Id);
                    if (Scouting.GiftKindVisible(ScoutStage.Invited, School.ScoutLv)) r.GiftRevealed = true;      // スカウトで素養の種類を見た選手は入部と同時に判明(10 §8)
                }
                _lastIntakeStars[r.Id] = DisplayRules.Stars(c.V);
                NameGenerator.Assign(r, Rngs, TakenIn(s));
                s.Runners.Add(r);
                s.InitBonds(r);
                _lastIntake.Add(r.Id);
            }
            if (ticket != null && _ticketRecruit != null)
            {
                SpecialRecruit sr = _ticketRecruit;
                Runner t = SpecialRecruitGenerator.ToRunner(sr, s.NextRunnerId++);
                t.EnrollWeek = enrollWeek;
                t.LookSeed = sr.LookSeed;
                if (sr.Kind == TicketKind.Intl) t.Country = sr.CountryIndex + 1;
                else t.HighSchool = Geo.HighSchoolId(sr.Pref, sr.HighSchoolU);
                foreach (Trait tr in Presentation.Traits(Rngs, sr.LookSeed)) t.Traits.Add(tr.ToString());
                NameGenerator.Assign(t, Rngs, TakenIn(s));
                t.DeriveDistance(s.SchoolPref);
                s.Runners.Add(t);
                s.InitBonds(t);
                _lastIntake.Add(t.Id);
                _lastIntakeStars[t.Id] = DisplayRules.Stars(sr.V);
            }
            _ticketRecruit = null;
            return new List<WorldRunner>();                                          // 自校の選手は実体で持つ(世界の抽象の名簿には足さない)
        }
    }
}
