using System;

namespace Ekiden.Platform
{
    /// <summary>進め方を決められる時計(テスト・SimCli 用)。</summary>
    public sealed class FakeClock : IClock
    {
        public long Utc = 1800000000L;
        public double Monotonic;

        public long UtcNow() { return Utc; }

        public double MonotonicSeconds() { return Monotonic; }

        public void Advance(double seconds)
        {
            Monotonic += seconds;
            Utc += (long)Math.Floor(seconds);
        }
    }
}
