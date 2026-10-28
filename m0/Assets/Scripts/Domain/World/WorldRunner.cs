using System;
using Ekiden.Domain.Rng;

namespace Ekiden.Domain.World
{
    /// <summary>候補・他校の選手(軽量。02 §4.3 の RivalRunner と 05 §6 の PoolCandidate)。力 A0 と進捗率で持ち、レース時に 10 能力へ復元する(04)。</summary>
    public sealed class WorldRunner
    {
        public double A0, Gap;
        public GrowthKind Growth;
        public Profile Profile;
        public int Grade;                  // 在籍中の学年 1〜4(候補のうちは 0)
        public double Prog;                // 前年度までの累計進捗率(0〜0.95)
        public bool Overseas;
        public int PrefectureId;           // 日本人のみ(1〜47)
        public Region Region;              // 日本人の出身地方(留学生は意味を持たない)
        public int CountryIndex;           // 留学生のみ(0〜23)
        public double HighSchoolU;         // 出身高校の抽選値(0〜1。高校の表が決まってから引く。02 §6)
        public uint LookSeed;              // 外見・名前・n10・個性・素養(09 §8.2 label 8)
        public double Q, V;                // 本当の価値・市場評価(05 §1)
        public int SchoolId = -1;
        public bool Ticket;                // 特別招聘で入った選手
        public int Index;                  // 候補プールの中の番号(提示スコアの乱数の鍵に使う)
        private double[] _n10;

        public void SetValues()
        {
            Q = A0 + 0.6 * Gap;
            V = A0 + WorldK.K_V_GAP_W * Gap;
        }

        /// <summary>10 能力への展開の偏り n10(最初の 6 つは N(0,3)、CON・MEN・REC・DUR は N(0,4))。look_seed から決まる(label 8・b = 10)。</summary>
        public double[] N10(IRngFactory rngs)
        {
            if (_n10 != null) return _n10;
            Pcg32 g = rngs.Stream(RngRegistry.LabelLook, LookSeed, 10);
            var n = new double[10];
            for (int i = 0; i < 6; i++) n[i] = g.Normal(0, 3);
            for (int i = 6; i < 10; i++) n[i] = g.Normal(0, 4);
            _n10 = n;
            return n;
        }
    }
}
