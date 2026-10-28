using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.World;

namespace Ekiden.Domain.Achievements
{
    /// <summary>1 年度の自校の大会の結果(判定の入力)。<see cref="Place"/> は 1 始まり。出なかった・まだなら 99。</summary>
    public sealed class YearFacts
    {
        public int Year;
        public int[] Place = { 99, 99, 99 };
    }

    /// <summary>実績の判定に要る事実(セーブ 1 つぶん)。状態から集める(<see cref="From"/>)か、試験で直接組む。</summary>
    public sealed class AchievementFacts
    {
        public readonly List<YearFacts> Years = new List<YearFacts>();
        public bool Cleared;
        public string Difficulty;                // 開始校(Elite / Mid / Low / Club)。シナリオパックなら null
        public bool ClearNoInvite;               // クリア時点で特別招聘券を一度も使っていない
        public int YearsPlayed;                  // いまの年度(1 始まり)
        public int HallOfFameCount;
        public int PrefecturesCovered;           // 卒業生の出身県の種類(1〜47)
        public int SectionWinsTotal;             // 在籍の選手 + OB の区間賞の合計
        public bool MountainGodWin;              // 『山の神』を持つ選手が第三戦の 5 区で区間賞(この大会の結果から)

        /// <summary>状態から集める。<paramref name="race"/> は、いま走った大会(山の神の判定に使う。なければ null)。</summary>
        public static AchievementFacts From(GameSession gs, RaceOutcome race = null)
        {
            var f = new AchievementFacts { Difficulty = gs.State.Scenario.Difficulty, YearsPlayed = gs.Year };
            var byYear = new SortedDictionary<int, YearFacts>();
            foreach (PlayerYearStats st in gs.Stats)
            {
                var y = new YearFacts { Year = st.Year };
                for (int k = 0; k < 3; k++) y.Place[k] = st.Place[k];
                byYear[st.Year] = y;
            }
            if (!byYear.ContainsKey(gs.Year))                                          // 今年度(精算の前)は、ここまでの順位
            {
                var y = new YearFacts { Year = gs.Year };
                for (int k = 0; k < 3; k++) y.Place[k] = gs.State.Rank[k];
                byYear[gs.Year] = y;
            }
            f.Years.AddRange(byYear.Values);
            if (gs.State.Cleared != null) { f.Cleared = true; f.ClearNoInvite = gs.State.Cleared.NoInvite; }
            f.HallOfFameCount = gs.State.Obs.Count(o => o.HallOfFame);
            f.PrefecturesCovered = gs.State.Obs.Where(o => o.Pref >= 1 && o.Pref <= 47).Select(o => o.Pref).Distinct().Count();
            f.SectionWinsTotal = gs.State.Runners.Sum(r => r.SectionWins) + gs.State.Obs.Sum(o => o.SectionWins);
            if (race != null && race.Kind == RaceKind.NewYear)
            {
                const int fifth = 4;
                RaceTeam team = race.Teams.FirstOrDefault(t => t.Id == gs.School.Id);
                TeamResult mine = race.Results.FirstOrDefault(t => t.Id == gs.School.Id);
                if (team != null && mine != null && fifth < mine.Sections.Length && mine.Sections[fifth] == race.Results.Min(t => t.Sections[fifth]))
                {
                    Entities.Runner r = gs.State.FindRunner(team.Lineup[fifth].RunnerId);
                    f.MountainGodWin = r != null && r.Skills.Contains("skill.mountain_god");
                }
            }
            return f;
        }
    }

    /// <summary>実績の判定(02 §3.10 / 01 §3.3)。事実から、いま満たしている実績の ID を、マスタの順に返す。解除は一度きり(消えない)で、記録は呼ぶ側(プロフィール)が持つ。</summary>
    public static class AchievementRules
    {
        public static List<string> Satisfied(AchievementFacts f)
        {
            var list = new List<string>();
            foreach (AchievementDef d in AchievementMaster.All) if (Check(d, f)) list.Add(d.Id);
            return list;
        }

        public static bool Check(AchievementDef d, AchievementFacts f)
        {
            switch (d.Kind)
            {
                case AchCond.RaceRank:
                    return f.Years.Any(y => d.Race >= 0 ? y.Place[d.Race] <= d.RankMax : y.Place.Any(p => p <= d.RankMax));
                case AchCond.Streak:
                    return LongestStreak(f, d.Race) >= d.N;
                case AchCond.Sweep:
                    return LongestSweepRun(f) >= d.N;
                case AchCond.Clear:
                    return f.Cleared && (d.Difficulty == null || d.Difficulty == f.Difficulty) && (!d.NoInvite || f.ClearNoInvite);
                case AchCond.Count:
                    switch (d.Metric)
                    {
                        case "prefectures": return f.PrefecturesCovered >= d.N;
                        case "hall_of_fame": return f.HallOfFameCount >= d.N;
                        case "section_wins": return f.SectionWinsTotal >= d.N;
                        case "years": return f.YearsPlayed >= d.N;
                        default: throw new ArgumentException("未知の指標: " + d.Metric);
                    }
                case AchCond.Special:
                    return d.Id == "ach.dev.mountain_god" && f.MountainGodWin;
                default:
                    return false;
            }
        }

        /// <summary>同じ大会(race が負ならどの大会でも)の優勝が続いた最長の年数(年度が連続していること)。</summary>
        public static int LongestStreak(AchievementFacts f, int race)
        {
            int best = 0;
            for (int k = 0; k < 3; k++)
            {
                if (race >= 0 && k != race) continue;
                int run = 0, prevYear = int.MinValue;
                foreach (YearFacts y in f.Years.OrderBy(x => x.Year))
                {
                    bool win = y.Place[k] == 1;
                    run = win ? (y.Year == prevYear + 1 && run > 0 ? run + 1 : 1) : 0;
                    prevYear = y.Year;
                    best = Math.Max(best, run);
                }
            }
            return best;
        }

        /// <summary>3 大会すべてで優勝した年度が連続した最長の年数。</summary>
        public static int LongestSweepRun(AchievementFacts f)
        {
            int best = 0, run = 0, prevYear = int.MinValue;
            foreach (YearFacts y in f.Years.OrderBy(x => x.Year))
            {
                bool sweep = y.Place[0] == 1 && y.Place[1] == 1 && y.Place[2] == 1;
                run = sweep ? (y.Year == prevYear + 1 && run > 0 ? run + 1 : 1) : 0;
                prevYear = y.Year;
                best = Math.Max(best, run);
            }
            return best;
        }
    }
}
