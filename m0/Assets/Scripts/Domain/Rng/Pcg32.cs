using System;

namespace Ekiden.Domain.Rng
{
    /// <summary>PCG32(XSH RR)。03 §3 の定義。1 回の draw = <see cref="U32"/> 1 回(<see cref="Normal"/> は 2 回)。</summary>
    public sealed class Pcg32
    {
        private const ulong Mult = 6364136223846793005UL;
        private ulong _state;
        private readonly ulong _inc;

        /// <summary>これまでに引いた <see cref="U32"/> の回数(09 §8.3 の『消費数』の検査用)。</summary>
        public int Draws { get; private set; }

        public Pcg32(ulong seed)
        {
            unchecked
            {
                _state = 0;
                _inc = (Mix.Mix64(seed ^ 0xDA3E39CB94B95BDBUL) << 1) | 1UL;
                Step();
                _state += seed;
                Step();
            }
        }

        private void Step()
        {
            unchecked
            {
                _state = _state * Mult + _inc;
            }
        }

        public uint U32()
        {
            unchecked
            {
                ulong old = _state;
                Step();
                Draws++;
                uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
                int rot = (int)(old >> 59);
                return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
            }
        }

        /// <summary>(0, 1) の一様乱数。</summary>
        public double Uniform()
        {
            return (U32() + 0.5) / 4294967296.0;
        }

        /// <summary>a 以上 b 以下の整数(一様)。</summary>
        public int RandInt(int a, int b)
        {
            return a + (int)(Uniform() * (b - a + 1));
        }

        /// <summary>正規乱数(Box-Muller。常に 2 回消費)。</summary>
        public double Normal(double mu, double sigma)
        {
            double u1 = Uniform();
            double u2 = Uniform();
            return mu + sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
