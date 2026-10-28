using System;
using System.Collections.Generic;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Rng;

namespace Ekiden.Domain.Race
{
    /// <summary>レースに出るチーム(04 §8)。Lineup は区間ごとの選手。</summary>
    public sealed class RaceTeam
    {
        public int Id;
        public RaceRunner[] Lineup;
        public Instruction[] Instr;          // null = 全区間 Keep
        public int Geki;                     // 使える檄の回数
        public double GekiMin;               // この大きさ以上の損失のときだけ使う(おまかせの方針)
        public double GekiF = 1.0;           // 檄の効き(f_k。04 §9.2)
        public bool[] GekiUse;               // null = おまかせの方針(上の GekiMin)。非 null = 区間ごとに『檄を使う』と決めたところだけ、異変(ブレーキ・失速)が起きたときに使う(手動。04 §9.3 のフル)
    }

    public sealed class RaceEvent
    {
        public string Kind;                  // "geki_brake" / "brake" / "stall" / "kuriage"
        public int Section;
        public double Value;
        public double Pos;
    }

    public sealed class TeamResult
    {
        public int Id;
        public int[] Sections;               // 区間ごとの所要時間(ds = 0.1 秒)
        public int[] Cum;                    // 累積(ds)。繰り上げのあった組は、繰り上げ後の公式タイムで数える
        public double[] IncidentPos;         // 区間ごとの『異変』の通知位置(1/3 か 2/3。ブレーキ・失速のどちらにも使う。04 §9.2)
        public int[] Flow;                   // 区間ごとの展開(04 §7.3): 0 = 一斉スタート・集団の先頭 / 1 = 全体の先頭(2 区以降)/ 2 = 集団走の追走側 / 3 = 単独走
        public int TotalDs;
        public int Rank;
        public bool Kuriage;
        public List<RaceEvent> Events = new List<RaceEvent>();
        public int GekiLeft;
        internal double TStart;
    }

    /// <summary>複数チームのレースを区間ごとに進める(04 §7・§8)。</summary>
    public static class RaceSimulator
    {
        /// <param name="rngFor">(チーム ID, 区間の添字) → 乱数。通常は <c>rngs.Stream(4, raceId, チームID × 100 + 区間の添字)</c>(09 §8.2)。</param>
        public static List<TeamResult> Simulate(RaceKind race, IReadOnlyList<RaceTeam> teams, Env[] envByDay, Func<int, int, Pcg32> rngFor, bool gekiPolicy = true)
        {
            IReadOnlyList<SectionDef> secs = RaceTables.Course(race);
            int n = secs.Count;
            RaceRule rule = RaceTables.Rule(race);
            int longest = 0;
            for (int i = 1; i < n; i++) if (secs[i].Distance > secs[longest].Distance) longest = i;      // max は最初の最大(同値は前の区間)
            var state = new List<TeamResult>();
            foreach (RaceTeam t in teams)
                state.Add(new TeamResult { Id = t.Id, Sections = new int[n], Cum = new int[n], Flow = new int[n], IncidentPos = new double[n], GekiLeft = t.Geki });
            var arrivalsT = new double[teams.Count];
            var startsT = new double[teams.Count];
            for (int si = 0; si < n; si++)
            {
                SectionDef sec = secs[si];
                for (int k = 0; k < teams.Count; k++) startsT[k] = state[k].TStart;
                // 出発の遅い順(同値は登録の順 = 安定)
                var order = new List<int>();
                for (int k = 0; k < teams.Count; k++) order.Add(k);
                StableSort(order, (a, b) => startsT[a].CompareTo(startsT[b]));
                Env env = RaceMath.DayEnv(race, envByDay, si);
                for (int rankI = 0; rankI < order.Count; rankI++)
                {
                    int ti = order[rankI];
                    RaceTeam team = teams[ti];
                    RaceRunner runner = team.Lineup[si].Clone();
                    double t0 = startsT[ti];
                    double flow = 0.0;
                    bool flowPack = false, flowSolo = false;
                    bool leading = rankI == 0;
                    Style? sty = runner.Style;
                    double rs = sty != null ? RaceConstants.K_RUN_SCALE[(int)sty.Value.Run] : 1.0;
                    if (si > 0)
                    {
                        bool anyNear = false, follower = false;
                        foreach (int o in order)
                        {
                            if (o == ti || Math.Abs(startsT[o] - t0) > RaceConstants.G_PACK) continue;
                            anyNear = true;
                            if (startsT[o] < t0) follower = true;
                        }
                        if (anyNear)
                        {
                            if (follower) { flowPack = true; flow += RaceConstants.PackAdj * rs * (runner.Skills.Contains("skill.pack_runner") ? RaceConstants.PackSkillMult : 1.0); }
                        }
                        else if (!leading)
                        {
                            flowSolo = true;
                            double solo = RaceConstants.SoloAdj * rs + (runner.Skills.Contains("skill.solo_weak") ? RaceConstants.SoloWeakExtra : 0.0);
                            if (runner.Skills.Contains("skill.pack_runner")) solo = 0.0;
                            flow += solo;
                        }
                        // 追う展開: 自分より前に出発したチームのうち、最も後ろ(出発が遅い)のチームとの差
                        int ahead = -1;
                        foreach (int o in order) if (startsT[o] < t0) ahead = o;
                        if (ahead >= 0 && (t0 - startsT[ahead]) <= RaceConstants.G_CHASE)
                        {
                            if (runner.Skills.Contains("skill.overtake")) flow += RaceConstants.ChaseSkillAdj;
                            if (sty != null) flow += RaceConstants.K_STYLE_CHASE_ADJ[(int)sty.Value.Flow];
                        }
                    }
                    runner.Leading = leading && si > 0;
                    state[ti].Flow[si] = si == 0 ? 0 : (leading ? 1 : (flowPack ? 2 : (flowSolo ? 3 : 0)));
                    Instruction instr = team.Instr != null ? team.Instr[si] : Instruction.Keep;
                    Pcg32 rng = rngFor(team.Id, si);
                    LegDraw dr = RaceMath.DrawLeg(rng, runner, rule.Weight, instr, 1.0, sec.Distance, env.Temp, sec.Type);
                    double brake = dr.Brake, stall = dr.Stall;
                    TeamResult res = state[ti];
                    res.IncidentPos[si] = dr.BrakePos;
                    if (res.GekiLeft > 0 && (team.GekiUse != null || gekiPolicy))
                    {
                        double loss = Math.Max(brake, stall);
                        bool use = team.GekiUse != null ? (team.GekiUse[si] && loss > 0.0) : (loss > 0.0 && loss >= team.GekiMin);
                        if (use)
                        {
                            double gm = 1.0 - RaceConstants.GekiReduce * team.GekiF;
                            if (brake >= stall) brake *= gm; else stall *= gm;
                            res.GekiLeft -= 1;
                            res.Events.Add(new RaceEvent { Kind = "geki_brake", Section = si });
                        }
                    }
                    double p = RaceMath.SectionRating(runner, sec.Type, si == longest, si == n - 1);
                    double bt = RaceMath.BaseTime(sec.Distance, sec.Terrain, p);
                    double mult = 1.0 + RaceMath.FormAdj(runner) + RaceMath.EnvAdj(env, runner) + dr.Noise + dr.Boost + flow + RaceConstants.InstrAdj[(int)instr] + 0.0;
                    mult = Math.Max(RaceConstants.MultFloor, mult);
                    double tt = bt * (mult + stall) + bt * brake;
                    if (brake > 0.0) res.Events.Add(new RaceEvent { Kind = "brake", Section = si, Value = brake, Pos = dr.BrakePos });
                    if (stall > 0.0) res.Events.Add(new RaceEvent { Kind = "stall", Section = si, Value = stall });
                    int ds = Rounding.HalfUp(tt * 10.0);
                    res.Sections[si] = ds;
                    arrivalsT[ti] = t0 + ds / 10.0;
                }
                double leaderArr = double.PositiveInfinity;
                for (int k = 0; k < teams.Count; k++) leaderArr = Math.Min(leaderArr, arrivalsT[k]);
                for (int k = 0; k < teams.Count; k++)
                {
                    TeamResult s = state[k];
                    double arr = arrivalsT[k];
                    if (si < n - 1 && arr - leaderArr > rule.Kuriage)
                    {
                        s.Kuriage = true;
                        s.TStart = leaderArr + rule.Kuriage;
                        s.Events.Add(new RaceEvent { Kind = "kuriage", Section = si });
                    }
                    else s.TStart = arr;
                    s.Cum[si] = !s.Kuriage ? Rounding.HalfUp(arr * 10.0) : Rounding.HalfUp(s.TStart * 10.0);
                }
            }
            foreach (TeamResult s in state) s.TotalDs = s.Cum[n - 1];
            // 順位: 完走(繰り上げなし)→ 合計タイム → 最終区間から順にさかのぼった区間タイム → チームID
            var sorted = new List<TeamResult>(state);
            StableSort(sorted, CompareResults);
            for (int i = 0; i < sorted.Count; i++) sorted[i].Rank = i + 1;
            return sorted;
        }

        public static int CompareResults(TeamResult a, TeamResult b)
        {
            int c = a.Kuriage.CompareTo(b.Kuriage);
            if (c != 0) return c;
            c = a.TotalDs.CompareTo(b.TotalDs);
            if (c != 0) return c;
            for (int i = a.Sections.Length - 1; i >= 0; i--)
            {
                c = a.Sections[i].CompareTo(b.Sections[i]);
                if (c != 0) return c;
            }
            return a.Id.CompareTo(b.Id);
        }

        /// <summary>安定な整列(D-4。<c>List.Sort</c> は不安定)。</summary>
        public static void StableSort<T>(List<T> list, Comparison<T> cmp)
        {
            for (int i = 1; i < list.Count; i++)
            {
                T x = list[i];
                int j = i - 1;
                while (j >= 0 && cmp(list[j], x) > 0) { list[j + 1] = list[j]; j--; }
                list[j + 1] = x;
            }
        }
    }
}
