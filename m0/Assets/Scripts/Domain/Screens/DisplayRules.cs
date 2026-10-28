using System;
using System.Collections.Generic;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Race;

namespace Ekiden.Domain.Screens
{
    public enum PotentialHint { Room, Late, Cap, Max }

    /// <summary>画面に出す値の規則(08 §8)のうち、表示のための変換: ランク表記・素質のヒント・競合度・前評判の印・記録の導出。Domain に置き、画面は結果を表示する(08 原則 10)。</summary>
    public static class DisplayRules
    {
        public const double K_UI_HINT_LATE = 0.85, K_UI_HINT_CAP = 0.95;

        /// <summary>候補の評価★(05 §6.1。市場評価 V から): V &lt; 42 → 1 / &lt; 48 → 2 / &lt; 54 → 3 / &lt; 60 → 4 / それ以上 → 5。</summary>
        public static int Stars(double v)
        {
            return v < 42.0 ? 1 : (v < 48.0 ? 2 : (v < 54.0 ? 3 : (v < 60.0 ? 4 : 5)));
        }

        /// <summary>ランク文字の大小(S = 7 … G = 0)。月末レポートの『ランクアップ』の判定と score に使う(R-10)。</summary>
        public static int RankValue(char letter)
        {
            return "GFEDCBAS".IndexOf(letter);
        }

        /// <summary>R-01: 能力・力 P・適性値のランク。S ≥ 90 / A ≥ 80 / B ≥ 70 / C ≥ 60 / D ≥ 50 / E ≥ 40 / F ≥ 30 / G。</summary>
        public static char RankLetter(double v)
        {
            if (v >= 90) return 'S';
            if (v >= 80) return 'A';
            if (v >= 70) return 'B';
            if (v >= 60) return 'C';
            if (v >= 50) return 'D';
            if (v >= 40) return 'E';
            if (v >= 30) return 'F';
            return 'G';
        }

        /// <summary>R-02: 素質のヒント(能力 ÷ 素質)。素質の数値は出さない。</summary>
        public static PotentialHint Hint(int stat, int pot)
        {
            if (stat >= pot) return PotentialHint.Max;
            double r = (double)stat / pot;
            if (r >= K_UI_HINT_CAP) return PotentialHint.Cap;
            if (r >= K_UI_HINT_LATE) return PotentialHint.Late;
            return PotentialHint.Room;
        }

        // ---- R-03 記録(PB)は能力から導出する(保存しない)
        public enum PbEvent { M5000, M10000, Half }

        /// <summary>調子 0・環境なしの記録(ds = 0.1 秒の整数)。10 × 距離km × (pace50(距離km) − K_SLOPE × (P − 50))。P は 5000m = Fast / 10000m = Mid / ハーフ = Long の区間適性値(04 §5.1)。</summary>
        public static int DerivePb(int[] stats, PbEvent ev)
        {
            var d = new double[stats.Length];
            for (int i = 0; i < d.Length; i++) d[i] = stats[i];
            double km = ev == PbEvent.M5000 ? 5.0 : ev == PbEvent.M10000 ? 10.0 : 21.0975;
            SectionType typ = ev == PbEvent.M5000 ? SectionType.Fast : ev == PbEvent.M10000 ? SectionType.Mid : SectionType.Long;
            return DerivePbFromP(ev, RaceTables.Rating(d, typ));
        }

        public static int DerivePbFromP(PbEvent ev, double p)
        {
            double km = ev == PbEvent.M5000 ? 5.0 : ev == PbEvent.M10000 ? 10.0 : 21.0975;
            return Rounding.HalfUp(10.0 * km * (Pace50.Of(km) - RaceConstants.K_SLOPE * (p - 50.0)));
        }

        // ---- R-08 競合度・R-09 前評判の印
        public enum RivalryLevel { None, Low, Mid, High }

        /// <summary>R-08: 市場評価 V の帯による競合度。練習会まで進めて、枠があれば競り勝つ割合の実測(約 100% / 約 100% / 約 92% / 約 65%)を win に返す。</summary>
        public static RivalryLevel Rivalry(double v, out double win)
        {
            if (v < 51.5) { win = 1.00; return RivalryLevel.None; }
            if (v < 55.0) { win = 1.00; return RivalryLevel.Low; }
            if (v < 59.0) { win = 0.92; return RivalryLevel.Mid; }
            win = 0.65;
            return RivalryLevel.High;
        }

        /// <summary>R-09: 前評判の順位 → 印。◎ 1位 / 〇 2〜3位 / ▲ 4〜6位 / △ 7〜10位 / それ以外は '-'。</summary>
        public static char PreviewMark(int rank)
        {
            if (rank <= 1) return '◎';
            if (rank <= 3) return '〇';
            if (rank <= 6) return '▲';
            if (rank <= 10) return '△';
            return '-';
        }
    }
}
