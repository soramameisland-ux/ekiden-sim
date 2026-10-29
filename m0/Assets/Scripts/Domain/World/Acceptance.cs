using System;
using System.Collections.Generic;

namespace Ekiden.Domain.World
{
    /// <summary>1 シードぶん(自校ありの世界)の結果。</summary>
    public sealed class SeedResult
    {
        public double WinRate, TripleRate, Clear, P;
        public int Years;
    }

    /// <summary>統計の受け入れ(09 §16.3。T4)の 1 シードぶんの実行と、帯の判定。並列に回す側(SimCli)は世界ごとに別のインスタンスを使う(D-1・§14.3)。</summary>
    public static class Acceptance
    {
        /// <summary>参照(<c>ref_model_05.py</c>)の値と許容の幅: 優勝率 / 三冠率 / クリア年度 / 戦力 P(09 §16.3 の表。<c>DocSyncTests</c> が文書と一致を確かめる)。</summary>
        public struct Band
        {
            public double Win, WinTol, Triple, TripleTol, Clear, ClearTol, P, PTol;
        }

        public static readonly Dictionary<Difficulty, Band> Bands = new Dictionary<Difficulty, Band>
        {
            { Difficulty.Elite, new Band { Win = 0.617, WinTol = 0.052, Triple = 0.331, TripleTol = 0.060, Clear = 6.98, ClearTol = 1.25, P = 72.49, PTol = 0.33 } },
            { Difficulty.Mid, new Band { Win = 0.549, WinTol = 0.058, Triple = 0.262, TripleTol = 0.062, Clear = 11.65, ClearTol = 1.86, P = 72.15, PTol = 0.38 } },
            { Difficulty.Low, new Band { Win = 0.442, WinTol = 0.079, Triple = 0.188, TripleTol = 0.055, Clear = 25.55, ClearTol = 3.84, P = 70.68, PTol = 1.22 } },
        };

        /// <summary>強豪県の +3(02 §6)を入れた世界の戦力 P は、入れない世界(= 参照モデルと同じ構成)より高くなる(L-44)。その差(同じシード・対)が取ってよい範囲。</summary>
        public const double L44_P_SHIFT_MIN = -0.3, L44_P_SHIFT_MAX = 1.0;

        /// <summary>実体の選手を持つ自校(<see cref="Game.GameSession"/>)は、故障・疲労・調子のばらつきと、型に合わせた育成(L-24)を持たないぶん、同じ世界の抽象の自校より少し弱い。
        /// クリア年度の帯の幅に足す余裕(年度)。</summary>
        public const double REAL_CLEAR_SLACK = 1.0;

        public const int FirstYearForRates = 25;

        /// <summary>1 シードぶん: 方針『おまかせ』(<see cref="PlayerAgent"/>)で years 年度回す。25 年度以降(from)の優勝率・三冠率・戦力 P の平均と、クリア(3 大会の優勝経験が揃う)の年度(揃わなければ years + 1)。</summary>
        public static SeedResult RunSeed(ulong masterSeed, int years, Difficulty start, int from = FirstYearForRates, double? eliteBonus = null)
        {
            WorldSim w = WorldSim.Create(new Rng.KeyedRngFactory(masterSeed), 15, eliteBonus);
            PlayerAgent agent = w.StartPlayer(start);
            for (int y = 1; y <= years; y++) w.PlayYear(y, agent);
            return Summarize(agent, years, from);
        }

        /// <summary>実体の選手を持つ自校(<see cref="Game.GameSession"/>。『おまかせ』)で years 年度回す。指標の出し方は <see cref="RunSeed"/> と同じ。</summary>
        /// <param name="scoutButton">進行制御を通し、スカウトを画面の『おまかせスカウト』ボタン(08 R-08。目標 8 人)で行う(L-58)。<paramref name="skilled"/> が false のとき、ほかはおまかせ。
        /// 比べるときの基準は <paramref name="flowOnly"/>(進行制御を通すが、スカウトは年度末の検算済みの方針 = <c>scout_auto</c>)。</param>
        public static SeedResult RunSeedReal(ulong masterSeed, int years, Difficulty start, int from = FirstYearForRates, double? eliteBonus = null, bool skilled = false, bool scoutButton = false, bool flowOnly = false)
        {
            Game.GameSession gs = Game.GameSession.New(masterSeed, start, eliteBonus);
            if (scoutButton || flowOnly) PlayScoutPolicy(gs, years, scoutButton);
            else if (!skilled) gs.PlayYears(years);
            else PlaySkilled(gs, years);
            return Summarize(gs.Stats, years, from);
        }

        /// <summary>進行制御を通し、月末レポートの停止(おまかせの計画では月の初めに止まらないので、毎月必ず止まるここ)で『おまかせスカウト』のボタンを押す(視察は 6〜11 月・勧誘は 12〜2 月。毎月 1 回)。<paramref name="button"/> が false なら、年度末の <c>scout_auto</c> のまま(基準)。</summary>
        public static void PlayScoutPolicy(Game.GameSession gs, int years, bool button)
        {
            var flow = new Game.GameFlow(gs, autoUpgrade: true, autoTickets: true);
            gs.State.Settings.EventAuto = true;
            gs.State.Settings.ScoutAuto = !button;
            int done = 0;
            while (done < years)
            {
                Game.Stop s = flow.Advance();
                if (s.Kind == Game.StopKind.MonthReport && button)
                {
                    int m = gs.YearWeek / 4;
                    if (m >= 2 && m <= 7) gs.AutoScoutObserve();
                    else if (m >= 8 && m <= 10) gs.AutoScoutRecruit();
                }
                else if (s.Kind == Game.StopKind.YearEndTurnover) done++;
            }
        }

        /// <summary>検算用の上級者の方針(04 §14.4): 進行制御(<see cref="Game.GameFlow"/>)を通して、当日変更は期待時間が最小の控えと、指示は期待時間が最小の指示にする。
        /// そのほか(登録・オーダー・月間計画・スカウト・強化・招聘)はおまかせ。人が操作すれば、おまかせより多少は良くなる、という前提の確認に使う。</summary>
        public static void PlaySkilled(Game.GameSession gs, int years)
        {
            var flow = new Game.GameFlow(gs, autoUpgrade: true, autoTickets: true);
            gs.State.Settings.EventAuto = true;
            int done = 0;
            while (done < years)
            {
                Game.Stop s = flow.Advance();
                if (s.Kind == Game.StopKind.RaceDayPrep)
                {
                    s.RaceDay.UseSkilledSwaps();
                    s.RaceDay.UseBestInstructions();
                }
                else if (s.Kind == Game.StopKind.YearEndTurnover) done++;
            }
        }

        public static SeedResult Summarize(PlayerAgent agent, int years, int from = FirstYearForRates)
        {
            return Summarize(agent.Stats, years, from);
        }

        public static SeedResult Summarize(IEnumerable<PlayerYearStats> stats, int years, int from = FirstYearForRates)
        {
            var seen = new bool[3];
            int clear = years + 1;
            double wins = 0, triple = 0, p = 0;
            int n = 0;
            foreach (PlayerYearStats st in stats)
            {
                for (int i = 0; i < 3; i++) seen[i] |= st.Wins[i];
                if (clear > years && seen[0] && seen[1] && seen[2]) clear = st.Year;
                if (st.Year >= from)
                {
                    int c = 0;
                    for (int i = 0; i < 3; i++) if (st.Wins[i]) c++;
                    wins += c;
                    triple += c == 3 ? 1 : 0;
                    p += st.P;
                    n++;
                }
            }
            if (n == 0) return new SeedResult { Clear = clear };
            return new SeedResult { WinRate = wins / (3.0 * n), TripleRate = triple / n, Clear = clear, P = p / n, Years = n };
        }

        /// <summary>シード間の平均。</summary>
        public static SeedResult Mean(IReadOnlyList<SeedResult> res)
        {
            var m = new SeedResult();
            foreach (SeedResult r in res) { m.WinRate += r.WinRate; m.TripleRate += r.TripleRate; m.Clear += r.Clear; m.P += r.P; m.Years += r.Years; }
            double n = res.Count;
            m.WinRate /= n; m.TripleRate /= n; m.Clear /= n; m.P /= n;
            return m;
        }

        public static bool WinInBand(Band b, SeedResult m) { return Math.Abs(m.WinRate - b.Win) <= b.WinTol; }
        public static bool TripleInBand(Band b, SeedResult m) { return Math.Abs(m.TripleRate - b.Triple) <= b.TripleTol; }
        public static bool ClearInBand(Band b, SeedResult m) { return Math.Abs(m.Clear - b.Clear) <= b.ClearTol; }
        public static bool PInBand(Band b, SeedResult m) { return Math.Abs(m.P - b.P) <= b.PTol; }

        /// <summary>T4a(等価性): 強豪県の +3 を外した世界(参照モデルと同じ構成)が、4 つの指標とも帯の内側にある。実装の取りこぼしを見つけるための検査。</summary>
        public static bool EquivalentToReference(Band b, SeedResult noBonus)
        {
            return WinInBand(b, noBonus) && TripleInBand(b, noBonus) && ClearInBand(b, noBonus) && PInBand(b, noBonus);
        }

        /// <summary>実体の自校の優勝率・三冠率の、帯の下側に足す余裕。実体は抽象の自校より、同じ世界で優勝率が 0.03 前後・三冠率が 0.03〜0.06 低い(05 §14.11。L-55)。
        /// 帯の幅は『C# の 120 シードと参照の 120 シードの差』の 3σ で、この系統的な差は見込んでいない(L-67 で参照の帯を取り直したとき、強豪の三冠率が下端を 0.009 下回った)。</summary>
        public const double REAL_RATE_SLACK = 0.03;

        /// <summary>T4c(実体の自校): 優勝率・三冠率は帯の内側(下側は <see cref="REAL_RATE_SLACK"/> まで許す)、クリア年度は帯の幅 + <see cref="REAL_CLEAR_SLACK"/> の内側、戦力 P は +3(L-44)を許した帯の内側。</summary>
        public static bool RealAccepted(Band b, SeedResult real)
        {
            return real.WinRate >= b.Win - b.WinTol - REAL_RATE_SLACK && real.WinRate <= b.Win + b.WinTol
                   && real.TripleRate >= b.Triple - b.TripleTol - REAL_RATE_SLACK && real.TripleRate <= b.Triple + b.TripleTol
                   && Math.Abs(real.Clear - b.Clear) <= b.ClearTol + REAL_CLEAR_SLACK
                   && real.P >= b.P - b.PTol && real.P <= b.P + b.PTol + L44_P_SHIFT_MAX;
        }

        /// <summary>T4b(仕様の世界): +3 を入れた世界で、優勝率・三冠率・クリア年度は帯の内側、戦力 P は +3 を外した同じシードの世界との差が既知の範囲(L-44)。</summary>
        public static bool SpecWorldAccepted(Band b, SeedResult withBonus, SeedResult noBonus)
        {
            double shift = withBonus.P - noBonus.P;
            return WinInBand(b, withBonus) && TripleInBand(b, withBonus) && ClearInBand(b, withBonus) && shift >= L44_P_SHIFT_MIN && shift <= L44_P_SHIFT_MAX;
        }
    }
}
