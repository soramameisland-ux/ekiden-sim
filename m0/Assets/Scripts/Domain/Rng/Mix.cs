namespace Ekiden.Domain.Rng
{
    /// <summary>乱数の鍵の導出(03 §3 / 09 §8)。状態を持たない。</summary>
    public static class Mix
    {
        /// <summary>SplitMix64 の最終段(finalizer)。</summary>
        public static ulong Mix64(ulong x)
        {
            unchecked
            {
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
                return x ^ (x >> 31);
            }
        }

        /// <summary>鍵 (label, a, b) から 64bit のシードを作る。a・b は負でもよい(2 の補数で混ぜる)。</summary>
        public static ulong Derive(ulong masterSeed, int label, long a, long b)
        {
            unchecked
            {
                ulong x = Mix64(masterSeed ^ ((ulong)label * 0x9E3779B97F4A7C15UL));
                x = Mix64(x ^ (ulong)a);
                x = Mix64(x ^ (ulong)b);
                return x;
            }
        }
    }
}
