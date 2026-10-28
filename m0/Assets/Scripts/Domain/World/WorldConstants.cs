namespace Ekiden.Domain.World
{
    public enum Tier { Elite = 0, Mid = 1, Low = 2 }

    public enum GrowthKind { Early = 0, Normal = 1, Late = 2 }

    /// <summary>プロフィール(入学時の型。05 §6.2)。</summary>
    public enum Profile { Balanced = 0, Endurance = 1, Speed = 2, Mountain = 3 }

    /// <summary>05 §2 の定数表(初期値案)。</summary>
    public static class WorldK
    {
        // 候補プール
        public const int K_POOL_N = 520;
        public const double PoolAMean = 37.0, PoolASd = 7.5, PoolAMin = 28, PoolAMax = 72;
        public const double PoolGapMean = 24.0, PoolGapSd = 7.0, PoolGapSlope = 0.25, PoolGapMin = 8, PoolGapMax = 45;
        public const double ElitePrefBonus = 3.0;
        public const double OverseasShare = 0.03;
        public static readonly double[] GrowthShare = { 0.25, 0.55, 0.20 };                   // Early / Normal / Late
        public static readonly double[] ProfileShare = { 0.35, 0.25, 0.25, 0.15 };            // Balanced / Endurance / Speed / Mountain
        public const double K_V_GAP_W = 0.3, K_V_TOP = 51.5;
        // 提示スコア
        public const double K_W_PRESTIGE = 0.04;
        public static readonly double[] Local = { 12.0, 4.0, -6.0, -8.0 };                    // 距離帯 0..3
        public const double FacBonus = 4.0, ObBonus = 6.0;
        public const double PlaytimeRange = 30.0, PlaytimeScale = 10.0;
        public const double RivalNoise = 10.0, PlayerNoise = 10.0;
        public const double InterestMax = 90.0;
        public const double RivalTargetMult = 1.5, RivalTargetInterest = 63.0, RivalTargetTemp = 6.0, RivalBaseInterest = 20.0, RivalPercSd = 6.0;
        public const double K_RIVAL_CHASE = 58.0, ChaseFrom = 400.0, ChaseSpan = 600.0;
        public const double CoachOffer = 60.0;
        // 名声
        public const double K_PRESTIGE_ALPHA = 0.40, K_PRESTIGE_H = 14.0, K_PRESTIGE_STEP = 0.50;
        public const double K_FAME_TOP = 12.0, K_FAME_DECAY = 4.0, K_FAME_ENTRY = 0.5;
        public const double RivalRevert = 0.08, BaseDriftSd = 18.0, BaseMin = 20.0, BaseMax = 850.0;
        public const double CoachSd = 0.05, CoachRevert = 0.05, CoachMax = 0.30, CoachLv = 15.0;
        public const double RivalFacCoef = 3.5, RivalFacExp = 0.8;
        // 資金(万円)
        public const double IncBase = 800.0, IncPrestige = 1.0, IncPrestige2 = 0.0012, IncPoint = 25.0;
        public static readonly double[] ExpStaff = { 0, 40, 90, 160, 260 };
        public const double ExpFacUpkeep = 8.0, ExpPerRunner = 15.0;
        public const double UpBase = 300.0, UpSlope = 250.0, Reserve = 500.0;
        // 監督ランク
        public static readonly int[] RankCum = { 0, 6, 15, 30, 50, 80, 120, 170, 230, 300 };
        public const int FacCapBase = 3, FacCapR4 = 3, FacCapR5 = 5;
        // 入学
        public const int QuotaNormal = 10, QuotaSmall = 8, RosterMax = 40;
        public const double SmallPrestige = 150.0;
        // 券で入る選手(07 §6)
        public static readonly double[] Rookie = { 61.0, 2.0, 25.0, 3.0 };                    // A0 平均・標準偏差、ギャップ平均・標準偏差
        public static readonly double[] Intl = { 66.0, 2.0, 8.0, 2.0 };
        // 開始時の名声・名声ポイントの履歴・基準名声
        public static readonly double[] TierPrestige = { 780, 300, 60 };
        public static readonly double[] TierHist = { 21.6, 5.0, 0.8 };
        public static readonly double[] TierBase = { 720.0, 300.0, 60.0 };
        public static readonly int[] YearPoints = { 10, 6, 4, 2, 2, 2, 1, 1, 1, 1 };
    }
}
