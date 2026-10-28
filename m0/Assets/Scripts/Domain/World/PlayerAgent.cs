using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Race;
using Ekiden.Domain.Recruit;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Tickets;

namespace Ekiden.Domain.World
{
    /// <summary>1 年度ごとの自校の記録(検算用。参照モデルの <c>stats</c>)。</summary>
    public sealed class PlayerYearStats
    {
        public int Year;
        public double P;                       // 先発 10 人の力の平均
        public double Prestige, Funds, Lv;
        public int PowerRank;                  // 戦力順位(1 = 最強)
        public int Points;                     // その年度の評価ポイント
        public bool[] Wins = new bool[3];      // 第一〜第三戦の優勝
        public int[] Place = { 99, 99, 99 };   // 順位(出場しなければ 99)
        public double Top1;                    // 他校の最強の戦力
        public int CoachRank;
        public double Cum;
    }

    /// <summary>自校の抽象(検算用。05 §14・09 §16.3 の受け入れ): 施設の強化・資金・おまかせスカウト・特別招聘券を、参照モデル(<c>ref_model_05.py</c>)の方針で自動で行う。ゲーム本体の自校は Runner で育つ(06 まで)。</summary>
    public sealed class PlayerAgent : IPlayerSide
    {
        public School School { get; }
        public readonly TicketState Tickets = new TicketState();
        public readonly List<PlayerYearStats> Stats = new List<PlayerYearStats>();
        public int NextKind;                                   // 招聘の種別を交互に(スーパールーキー → 留学生 → …)
        public bool TicketsEnabled = true;                     // 『券を使わない』検算のとき false
        public double PowerBonus;                              // 検証用の上積み(既定 0)


        public PlayerAgent(School school) { School = school; }

        // ---- IPlayerSide(世界の年度処理への差し込み。05 §9)
        List<RaceRunner> IPlayerSide.RaceRoster(double lv, IRngFactory rngs)
        {
            var l = new List<RaceRunner>();
            foreach (WorldRunner r in School.Roster) l.Add(AiRacing.ToPlayerRaceRunner(r, lv, rngs, PowerBonus));
            return l;
        }

        double? IPlayerSide.PlayerP10 { get { return null; } }

        int? IPlayerSide.PlayerRosterCount { get { return null; } }

        QualifierTeam IPlayerSide.PrepareQualifier(int qualId, List<RaceRunner> roster, IRngFactory rngs) { return null; }

        RaceTeam IPlayerSide.PrepareRace(RaceKind kind, int raceId, Env[] envs, List<RaceRunner> roster, IRngFactory rngs)
        {
            if (roster.Count < RaceTables.Rule(kind).Run) return null;                          // 出走人数に足りなければ棄権(04 §4.1 E1)
            var (lineup, instr) = AiRacing.PreparePlayerLineup(roster, kind, envs, rngs, raceId, School.Id);
            return new RaceTeam { Id = School.Id, Lineup = lineup, Instr = instr, Geki = RaceTables.Rule(kind).Geki };
        }

        void IPlayerSide.AfterRaces(WorldSim world, int year, YearResult res) { Record(year, world.Schools, world.TeamPower, res); }

        void IPlayerSide.Graduating(IReadOnlyList<WorldRunner> graduates) { CountGraduates(graduates); }

        PlayerOffer IPlayerSide.Offer(WorldSim world, IReadOnlyList<WorldRunner> pool, IRngFactory rngs) { return MakeOffer(ChooseTargets(world.Schools, pool, rngs)); }

        List<WorldRunner> IPlayerSide.Intake(List<WorldRunner> allocated, WorldRunner ticket)
        {
            if (ticket != null)
            {
                if (allocated.Count >= 10)                                    // 定員 10 のうち 1 人分を招聘枠が占める(Q の最も低い入学者を外す)
                {
                    WorldRunner lowest = allocated[0];
                    foreach (WorldRunner r in allocated) if (r.Q < lowest.Q) lowest = r;
                    allocated.Remove(lowest);
                }
                ticket.Index = -1;
                allocated.Add(ticket);
            }
            return allocated;
        }

        /// <summary>開始時の施設(05 §5。強豪 / 中堅 / 弱小 / 同好会)。</summary>
        public static Training.FacilityLevels StartFacilities(Difficulty d)
        {
            return Scenarios.For(d).Facilities;
        }

        // ---- 年度の途中の記録
        public PlayerYearStats Record(int year, IReadOnlyList<School> schools, Func<School, double> teamPower, YearResult res)
        {
            double mine = teamPower(School);
            var st = new PlayerYearStats
            {
                Year = year, P = Q6.Round(mine), Prestige = School.Prestige, Funds = School.Funds, Lv = Q6.Round(School.Lv()), Points = School.YearPoints, CoachRank = School.Rank(), Cum = School.CumPoints,
            };
            int better = 0;
            double top1 = double.MinValue;
            foreach (School s in schools)
            {
                double p = teamPower(s);
                if (p > mine) better++;
                if (!s.IsPlayer && p > top1) top1 = p;
            }
            st.PowerRank = 1 + better;
            st.Top1 = Q6.Round(top1);
            RaceKind[] kinds = { RaceKind.Fall, RaceKind.National, RaceKind.NewYear };
            for (int i = 0; i < 3; i++)
            {
                st.Wins[i] = res.Winner[kinds[i]] == School.Id;
                int idx = res.Order[kinds[i]].IndexOf(School.Id);
                st.Place[i] = idx >= 0 ? idx + 1 : 99;
            }
            Stats.Add(st);
            return st;
        }

        // ---- 年度末の資金と施設の強化(05 §4・08 R-11)
        public void Finance()
        {
            School s = School;
            var f = s.Fac;
            double income = WorldMath.Income(s.Prestige, s.YearPoints);
            double staff = WorldMath.StaffExpense(f.Coach, f.Trainer, f.Nutri, f.Scout);
            double upkeep = WorldMath.FacilityUpkeep(f.Dorm, f.Track, f.Hill, f.Gym, f.Medical);
            double funds = Q6.Round(s.Funds + (income - staff - upkeep - WorldMath.RunnerExpense(s.Roster.Count)));
            FacilityPolicy.Upgrade(ref f, ref funds, s.Rank());
            s.Funds = funds;
            s.Fac = f;
            s.ScoutLv = f.Scout;
        }

        /// <summary>県別 OB の人数の地方別の集計から、OB ネットワークのレベル(0〜5 = min(5, floor(√人数))。02 §4.4)。</summary>
        public int ObLevel(Region region)
        {
            int n;
            School.Ob.TryGetValue((int)region, out n);
            return Math.Min(5, (int)Math.Sqrt(n));
        }

        /// <summary>卒業生を OB に数える(地方別。招聘の留学生は『関東』扱い = 参照モデルと同じ)。</summary>
        public void CountGraduates(IEnumerable<WorldRunner> graduates)
        {
            foreach (WorldRunner r in graduates)
            {
                int reg = (int)r.Region;
                int n;
                School.Ob.TryGetValue(reg, out n);
                School.Ob[reg] = n + 1;
            }
        }

        // ---- 特別招聘券(07 §4・§5。無料の獲得だけ)
        /// <summary>年度末: 大会の節目 → 使用(使えるなら使う)→ 年度末の節目。招聘する選手を返す(なければ null)。呼ぶのは卒業・学年の繰り上げのあと。</summary>
        public WorldRunner TicketStep(int year, YearResult res, IRngFactory rngs)
        {
            if (!TicketsEnabled) return null;
            TicketState t = Tickets;
            RaceKind[] kinds = { RaceKind.Fall, RaceKind.National, RaceKind.NewYear };
            int wins = 0;
            for (int i = 0; i < 3; i++)
            {
                int idx = res.Order[kinds[i]].IndexOf(School.Id);
                int rank = idx >= 0 ? idx + 1 : 100;
                if (rank == 1) wins |= 1 << i;
                TicketMilestones.EarnAfterRace(t, year, i + 1, rank, wins);
            }
            int active = School.Roster.Count(r => r.Ticket);
            WorldRunner ticket = null;
            if (t.CanDeclare(year, 5, active))
            {
                TicketKind kind = (NextKind % 2 == 0) ? TicketKind.Rookie : TicketKind.Intl;
                NextKind++;
                t.Declare(year, kind);
                t.ConfirmEnrollment();                           // この抽象では宣言と同じ年度末に入学する
                SpecialRecruit sr = SpecialRecruitGenerator.Generate(rngs, year, kind);
                ticket = new WorldRunner
                {
                    A0 = Q6.Round(sr.A0), Gap = Q6.Round(sr.Gap), Growth = (GrowthKind)(int)sr.Growth, Profile = sr.Profile, LookSeed = sr.LookSeed, Ticket = true,
                    Region = kind == TicketKind.Rookie ? Geo.Prefectures[sr.Pref - 1].Region : Region.Kanto, Overseas = false,
                };
                ticket.SetValues();
            }
            TicketMilestones.EarnAtYearEnd(t, year, School.Rank(), year);
            return ticket;
        }

        // ---- おまかせスカウト(05 §7.3 の検算用の方針。参照モデルの choose_targets)
        /// <summary>スカウトポイントで『取れそうで価値の高い候補』に好感度を積む。戻り値は、狙った候補の番号の集合。</summary>
        public HashSet<int> ChooseTargets(IReadOnlyList<School> schools, IReadOnlyList<WorldRunner> pool, IRngFactory rngs)
        {
            return ChooseTargetsFor(School, P10Of(School), Scouting.AnnualPoints(School.Rank()), schools, pool, rngs);
        }

        /// <summary>学校の「名簿の力の 10 番目」(足りなければ 40)。提示スコアの出場機会の項に使う。</summary>
        public static double P10Of(School s)
        {
            var ps = new List<double>();
            double lv = s.Lv();
            foreach (WorldRunner r in s.Roster) ps.Add(WorldMath.SeasonPower(r, lv));
            ps.Sort((a, b) => b.CompareTo(a));
            return ps.Count >= 10 ? ps[9] : 40.0;
        }

        /// <summary>おまかせスカウトの本体。自校の P10 と使えるポイントは外から渡す(実体の選手を持つ自校は、名簿の力から自分で求める)。</summary>
        public static HashSet<int> ChooseTargetsFor(School player, double playerP10, int points, IReadOnlyList<School> schools, IReadOnlyList<WorldRunner> pool, IRngFactory rngs)
        {
            int k = points / Scouting.PointsPerTarget;
            var p10 = new Dictionary<int, double>();
            foreach (School s in schools) p10[s.Id] = ReferenceEquals(s, player) ? playerP10 : P10Of(s);
            var cand = new List<KeyValuePair<double, WorldRunner>>();
            foreach (WorldRunner r in pool)
            {
                double qObs = Scouting.ObserveQ(r, player.ScoutLv, rngs);
                double baseP = WorldMath.OfferBase(player, r, p10[player.Id]) + WorldK.InterestMax;
                double bestR = double.MinValue;
                foreach (School s in schools)
                {
                    if (s.IsPlayer) continue;
                    double v = WorldMath.OfferBase(s, r, p10[s.Id]);
                    if (v > bestR) bestR = v;
                }
                bestR += r.V >= WorldK.K_V_TOP ? WorldK.RivalTargetInterest * 0.8 : WorldK.RivalBaseInterest * 0.8;
                double pwin = 1.0 / (1.0 + Math.Exp(-(baseP - bestR) / 8.0));
                cand.Add(new KeyValuePair<double, WorldRunner>(qObs * pwin, r));
            }
            RaceSimulator.StableSort(cand, (a, b) => b.Key.CompareTo(a.Key));
            var targets = new HashSet<int>();
            int nTop = player.ScholSlots();
            int chosen = 0;
            foreach (var kv in cand)
            {
                if (chosen >= k) break;
                WorldRunner r = kv.Value;
                if (r.V >= WorldK.K_V_TOP)
                {
                    if (nTop <= 0) continue;
                    nTop--;
                }
                targets.Add(r.Index);
                chosen++;
            }
            return targets;
        }

        /// <summary>入学の提示スコアへの自校の追加分: 狙った候補の好感度(90)+ 県別 OB の効果。乱数(N(0,10))は割り当ての側で足す。</summary>
        public PlayerOffer MakeOffer(HashSet<int> targets)
        {
            return r => (targets.Contains(r.Index) ? WorldK.InterestMax : 0.0) + WorldK.ObBonus * (r.Overseas ? 0 : ObLevel(r.Region));
        }
    }
}
