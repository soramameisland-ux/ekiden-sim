using System.Collections.Generic;

namespace Ekiden.Domain.Achievements
{
    /// <summary>実績の判定の種類(02 §3.10 の <c>AchCond</c>)。<see cref="Special"/> は個別の判定(山の神)。</summary>
    public enum AchCond { RaceRank, Clear, Sweep, Streak, Count, Special }

    /// <summary>実績の定義(マスタ。01 §3.3 の一覧)。条件の項目は種類ごとに使うものだけ。</summary>
    public sealed class AchievementDef
    {
        public string Id = "";                  // 例: <c>ach.clear.low.no_invite</c>。名称は <c>{Id}.title</c> / <c>{Id}.desc</c>(文字列表)
        public AchCond Kind;
        public bool Hidden;                     // 解除するまで内容を隠す
        public int Race = -1;                   // RaceRank / Streak: 大会(0 第一戦 / 1 第二戦 / 2 第三戦)。−1 = どの大会でも
        public int RankMax;                     // RaceRank: この順位以内
        public string Difficulty;               // Clear: 開始校(Elite / Mid / Low / Club)。null = 問わない
        public bool NoInvite;                   // Clear: クリア時点で特別招聘券を一度も使っていない
        public int N;                           // Streak: 連続の年数 / Sweep: 連続の年数 / Count: 回数
        public string Metric = "";              // Count: prefectures / hall_of_fame / section_wins / years
    }

    /// <summary>実績の一覧(01 §3.3 の初期案。条件はすべて測れる形)。</summary>
    public static class AchievementMaster
    {
        public static readonly IReadOnlyList<AchievementDef> All = Build();

        private static List<AchievementDef> Build()
        {
            var l = new List<AchievementDef>();
            // 大会
            l.Add(new AchievementDef { Id = "ach.race.seed", Kind = AchCond.RaceRank, Race = 2, RankMax = 10 });
            l.Add(new AchievementDef { Id = "ach.race.podium", Kind = AchCond.RaceRank, RankMax = 3 });
            l.Add(new AchievementDef { Id = "ach.race.win", Kind = AchCond.RaceRank, RankMax = 1 });
            l.Add(new AchievementDef { Id = "ach.race.win.fall", Kind = AchCond.RaceRank, Race = 0, RankMax = 1 });
            l.Add(new AchievementDef { Id = "ach.race.win.national", Kind = AchCond.RaceRank, Race = 1, RankMax = 1 });
            l.Add(new AchievementDef { Id = "ach.race.win.newyear", Kind = AchCond.RaceRank, Race = 2, RankMax = 1 });
            l.Add(new AchievementDef { Id = "ach.race.streak2", Kind = AchCond.Streak, N = 2 });
            l.Add(new AchievementDef { Id = "ach.race.streak5", Kind = AchCond.Streak, N = 5 });
            // クリア
            l.Add(new AchievementDef { Id = "ach.clear", Kind = AchCond.Clear });
            l.Add(new AchievementDef { Id = "ach.clear.elite", Kind = AchCond.Clear, Difficulty = "Elite" });
            l.Add(new AchievementDef { Id = "ach.clear.mid", Kind = AchCond.Clear, Difficulty = "Mid" });
            l.Add(new AchievementDef { Id = "ach.clear.low", Kind = AchCond.Clear, Difficulty = "Low" });
            l.Add(new AchievementDef { Id = "ach.clear.club", Kind = AchCond.Clear, Difficulty = "Club" });
            l.Add(new AchievementDef { Id = "ach.clear.low.no_invite", Kind = AchCond.Clear, Difficulty = "Low", NoInvite = true });
            l.Add(new AchievementDef { Id = "ach.clear.club.no_invite", Kind = AchCond.Clear, Difficulty = "Club", NoInvite = true, Hidden = true });
            l.Add(new AchievementDef { Id = "ach.sweep", Kind = AchCond.Sweep, N = 1 });
            l.Add(new AchievementDef { Id = "ach.sweep.2", Kind = AchCond.Sweep, N = 2 });
            // 育成
            l.Add(new AchievementDef { Id = "ach.dev.prefectures", Kind = AchCond.Count, Metric = "prefectures", N = 47 });
            l.Add(new AchievementDef { Id = "ach.dev.hall", Kind = AchCond.Count, Metric = "hall_of_fame", N = 10 });
            l.Add(new AchievementDef { Id = "ach.dev.mountain_god", Kind = AchCond.Special });
            l.Add(new AchievementDef { Id = "ach.dev.section_wins", Kind = AchCond.Count, Metric = "section_wins", N = 100 });
            // 継続
            l.Add(new AchievementDef { Id = "ach.years.25", Kind = AchCond.Count, Metric = "years", N = 25 });
            l.Add(new AchievementDef { Id = "ach.years.50", Kind = AchCond.Count, Metric = "years", N = 50 });
            return l;
        }
    }
}
