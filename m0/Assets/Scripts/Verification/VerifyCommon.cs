namespace Ekiden.Verification
{
    /// <summary>SimCli と Unity の確認(`VerifyBootstrap`)で共通の小さな部品。</summary>
    public static class VerifyCommon
    {
        /// <summary>シード番号(1, 2, …)からの master_seed。world・accept・stability・determinism で共通(05 §14 の参照と同じ式)。</summary>
        public static ulong WorldSeed(int seed) { return (ulong)seed * 1000003UL + 17UL; }

        public static string Mark(bool ok) { return ok ? "○" : "×"; }
    }
}
