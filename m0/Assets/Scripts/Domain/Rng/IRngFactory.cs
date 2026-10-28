namespace Ekiden.Domain.Rng
{
    /// <summary>乱数は鍵つきストリームだけ(09 §15.3 D-1)。</summary>
    public interface IRngFactory
    {
        Pcg32 Stream(int label, long a, long b);
    }

    public sealed class KeyedRngFactory : IRngFactory
    {
        private readonly ulong _masterSeed;

        public KeyedRngFactory(ulong masterSeed)
        {
            _masterSeed = masterSeed;
        }

        public ulong MasterSeed { get { return _masterSeed; } }

        public Pcg32 Stream(int label, long a, long b)
        {
            return new Pcg32(Mix.Derive(_masterSeed, label, a, b));
        }
    }
}
