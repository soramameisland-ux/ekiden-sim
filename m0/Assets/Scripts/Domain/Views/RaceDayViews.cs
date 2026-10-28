using System.Collections.Generic;
using Ekiden.Domain.Race;

namespace Ekiden.Domain.Views
{
    /// <summary>当日の区間 1 つ(08a SC-24 の lineup)。</summary>
    public sealed class DayLegView
    {
        public int Section;
        public RunnerRowView Runner;                // 自校の選手(補充要員など自校の名簿にいない走者は null)
        public string Name = "";
        public int Form;                            // 確定した調子(−2〜+2)
        public double Distance;
        public SectionType Type;
        public bool Mountain;
        public double Fit;                          // 区間適性値 P(特殊能力の加算を含む)
        public char FitMark;                        // ◎ / 〇 / △
        public char PaceMark;                       // 余裕の目安(◎ 攻める / 〇 維持 / △ 抑える)
        public Instruction Recommended;             // 目安どおりの指示
        public Instruction Instr;                   // いまの指示
        public double ExpectedSec;                  // いまの指示での期待時間
        public bool Swapped;                        // 当日変更で替わった
        public bool Passed;                         // 通過した区間(指示は変えられない)
    }

    /// <summary>控え 1 人(SC-24 の bench)。</summary>
    public sealed class BenchRunnerView
    {
        public int BenchIndex;                      // <c>RaceDay.Swap(区間, BenchIndex)</c> の引数
        public RunnerRowView Runner;                // 自校の名簿にいない走者は null
        public string Name = "";
        public int Form, Fatigue;
        public char HeatRank, WindRank, RainRank;   // 暑さ / 向かい風 / 雨への強さの印(STA / PAC / CON のランク。R-01)
        public double[] DeltaSec = new double[0];   // 区間ごと: この控えを入れたときの期待時間 − いまの先発の期待時間(負 = 速くなる)
    }

    /// <summary>大会の当日(08a SC-24)。</summary>
    public sealed class RaceDayView
    {
        public RaceKind Kind;
        public List<EnvForecastView> Env = new List<EnvForecastView>();      // 確定した環境(第三戦は往路と復路)
        public int Sections;
        public int SwapLimit, SwapsLeft, GekiLimit, GekiLeft;
        public bool Started, Touched;
        public int Resolved;                        // 通過した区間の数
        public readonly List<DayLegView> Legs = new List<DayLegView>();
        public readonly List<BenchRunnerView> Bench = new List<BenchRunnerView>();
        public int ExpectedRank;                    // いまの決定で最後まで走ったときの自校の順位
        public int ExpectedTotalDs;                 // 同じく自校の総合タイム
    }

    /// <summary>中継所の順位表の 1 行(SC-25 の standings)。</summary>
    public sealed class StandingLineView
    {
        public int Rank;
        public int SchoolId;
        public string NameKey = "";
        public bool IsOwn;
        public int CumDs, GapDs, SectionDs;
        public bool SeparatorBefore;                // 手前に『…』の区切りを出す(順位が飛んでいるとき)
    }

    /// <summary>レース進行(08a SC-25)。順位表は先頭 3 校 + 自校の前後 1 校(7 行まで)。</summary>
    public sealed class RaceLiveView
    {
        public RaceKind Kind;
        public int Sections;
        public int Resolved;                        // 通過した区間の数
        public bool Finished;
        public bool Started;
        public int GekiLeft;
        public DayLegView Next;                     // これから走る区間(終わっていれば null)
        public string IncidentKind;                 // "brake" / "stall"(なければ null)
        public double IncidentLoss, IncidentPos;    // 檄を使わないときの損失(基準タイムに対する割合)と通知の位置(1/3 か 2/3)
        public readonly List<StandingLineView> Standings = new List<StandingLineView>();    // 最後に通過した中継所(まだ通過していなければ空)
    }
}
