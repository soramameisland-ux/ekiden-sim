using System.Collections.Generic;
using Ekiden.Domain.Race;

namespace Ekiden.Domain.Game
{
    /// <summary>
    /// 当日(<see cref="RaceDay"/>)の決定の記録。中断セーブの <c>suspend.race</c>(09 §4.6)の中身。
    /// 練習の解決(調子の確定)は『オーダー確定後』の状態から同じ結果でやり直せ、乱数は指示・檄・当日変更で引き方が変わらない(04 §12)ので、
    /// この記録を当日に戻せば、通過した区間の結果も含めて中断の前と同じ状態になる(再計算は <see cref="RaceDay.Preview"/>)。
    /// </summary>
    public sealed class RaceDayRecord
    {
        public struct Swap
        {
            public int Section;             // 提出時の配置から替わった区間
            public int RunnerId;            // いまその区間を走る選手(自校の選手 ID)
        }

        public RaceKind Kind;
        public int RaceId;
        public int NextSection;             // 通過した区間の数(= 次に指示を出す区間)。区間の数に等しければレースは終わっている
        public bool Started;
        public bool Touched;
        public int[] Instructions;          // 区間ごとの指示(<see cref="Instruction"/> の値)
        public int[] GekiUsed;              // 区間ごとの檄: -1 未決(自動)/ 0 使わない / 1 使う
        public int GekiSpent;               // 使った檄の回数
        public List<Swap> Swaps = new List<Swap>();
    }
}
