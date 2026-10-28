using System;
using System.Linq;
using Ekiden.Domain.Game;
using Ekiden.Domain.World;

namespace Ekiden.SimCli
{
    /// <summary>進行制御(GameFlow。08 §6)で、何も操作せずに『続ける』だけを押したときの停止の並びを年度ごとに出す。おまかせ(StepWeek)と同じ結果になることも確かめる。</summary>
    internal static class FlowCmd
    {
        public static int Run(string[] args)
        {
            var pos = Common.Positional(args);
            int seed = Common.IntArg(pos, 0, 1);
            Difficulty d = pos.Count > 1 ? (Difficulty)Enum.Parse(typeof(Difficulty), pos[1]) : Difficulty.Mid;
            int years = Common.IntArg(pos, 2, 2);
            bool eventStops = Common.Flag(args, "events");
            GameSession gs = GameSession.New(Common.WorldSeed(seed), d);
            gs.State.Settings.EventAuto = !eventStops;
            var flow = new GameFlow(gs);
            Console.WriteLine("== 進行制御(" + d + "・シード " + seed + "・event_auto = " + gs.State.Settings.EventAuto + ")。何も操作せずに『続ける』だけ");
            for (int y = 1; y <= years; y++)
            {
                var keys = new System.Collections.Generic.List<string>();
                Stop s;
                do { s = flow.Advance(); keys.Add(s.Key); } while (s.Kind != StopKind.YearEndTurnover);
                int reports = keys.Count(k => k == "MonthReport");
                Console.WriteLine("  " + y + " 年度: 停止 " + keys.Count + " 回 — " + string.Join(" → ", keys.Where(k => k != "MonthReport")) + "(月末レポート " + reports + " 回)");
            }
            GameSession auto = GameSession.New(Common.WorldSeed(seed), d);
            auto.State.Settings.EventAuto = gs.State.Settings.EventAuto;
            auto.AutoUpgrade = false; auto.AutoDeclareTickets = false;
            auto.PlayYears(years);
            bool same = Ekiden.Persistence.Snapshot.GameSnapshot.Capture(auto).ToJson() == Ekiden.Persistence.Snapshot.GameSnapshot.Capture(gs).ToJson();
            Console.WriteLine("  " + Common.Mark(same) + " 何も操作しない流れ = おまかせ(StepWeek。強化と招聘の宣言は画面の操作なので除く)とバイト一致");
            return same ? 0 : 1;
        }
    }
}
