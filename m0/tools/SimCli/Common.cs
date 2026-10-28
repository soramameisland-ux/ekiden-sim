using System;
using System.Collections.Generic;
using System.Linq;

namespace Ekiden.SimCli
{
    internal static class Common
    {
        /// <summary>シード番号(1, 2, …)からの master_seed。world・accept・stability・determinism で共通(05 §14 の参照と同じ式)。</summary>
        public static ulong WorldSeed(int seed) { return Ekiden.Verification.VerifyCommon.WorldSeed(seed); }

        /// <summary>下位のスマホは開発機の何倍遅いと見るか(09 §14.2 の予算の判定の余裕。実機での確認は M1・L-38)。</summary>
        public const double PhoneSlowdown = 5.0;

        /// <summary>引数のうち『--』で始まらないもの(位置引数)。</summary>
        public static List<string> Positional(string[] a) { return a.Where(x => !x.StartsWith("--", StringComparison.Ordinal)).ToList(); }

        public static bool Flag(string[] a, string name) { return a.Contains("--" + name); }

        public static int IntArg(List<string> pos, int i, int dflt) { return pos.Count > i ? int.Parse(pos[i]) : dflt; }

        public static string Mark(bool ok) { return Ekiden.Verification.VerifyCommon.Mark(ok); }
    }
}
