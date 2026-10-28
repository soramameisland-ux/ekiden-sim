using Ekiden.Domain.Entities;

namespace Ekiden.Domain.Training
{
    /// <summary>1 選手の 1 週の解決の結果(03 §6)。</summary>
    public sealed class WeekReport
    {
        public Menu Menu;
        public double E;
        public double R;
        public double L;
        public int FEnd;
        public int[] XpAdd = new int[StatInfo.Count];     // 0.1 XP 単位(xu)
        public int Levelups;
        public double? InjuryP;                            // 故障中の週は null
        public InjurySeverity? NewInjury;                  // その週に起きた故障(なければ null)
        public int NewInjuryWeeks;
        public bool GiftRevealed;                          // その週に素養が判明した(素養を持つ選手だけ。月末レポートの gift_reveal。10 §8)
        public int Peaking;
        public double Noise;
        public double FormScore;
        public int Form;
    }
}
