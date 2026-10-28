using System;

namespace Ekiden.Domain.Numerics
{
    /// <summary>丸め(02 §1 / D-5)。<c>Math.Round</c> の既定(偶数丸め)は使わない。</summary>
    public static class Rounding
    {
        /// <summary>floor(x + 0.5)。</summary>
        public static int HalfUp(double x)
        {
            return (int)Math.Floor(x + 0.5);
        }

        public static long HalfUpLong(double x)
        {
            return (long)Math.Floor(x + 0.5);
        }
    }
}
