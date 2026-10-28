namespace Ekiden.Domain.Entities
{
    /// <summary>10 能力(02 §2。この順序が配列の添字)。</summary>
    public enum Stat { SPD = 0, STA = 1, PAC = 2, KCK = 3, CLB = 4, DWN = 5, CON = 6, MEN = 7, REC = 8, DUR = 9 }

    public enum GrowthType { Early = 0, Normal = 1, Late = 2 }

    public enum InjurySeverity { None = 0, Minor = 1, Moderate = 2, Severe = 3 }

    /// <summary>個性の『型』(10 §2.1)。</summary>
    public enum StyleForm { Speed, Balance, Long }

    /// <summary>個性の『走法』(10 §2.1)。</summary>
    public enum StyleRun { Pack, Free, Solo }

    /// <summary>個性の『展開』(10 §2.1)。</summary>
    public enum StyleFlow { Chase, Even, Lead }

    /// <summary>素養(10 §2.2)。</summary>
    public enum Gift { Climber, Descender, Ace }

    public static class StatInfo
    {
        public const int Count = 10;
    }
}
