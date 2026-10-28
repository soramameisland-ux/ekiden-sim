using System;
using Ekiden.Verification;

namespace Ekiden.SimCli
{
    /// <summary>T3 決定性(09 §16.1)。本体は <see cref="DeterminismRunner"/>(Unity の実機・IL2CPP の確認と共有)。
    /// 端末・ビルドをまたいだビット一致は求めない(§8.4)。同じビルドの中の一致だけを見る。出した SHA-256 は記録用。</summary>
    internal static class DeterminismCmd
    {
        public static int Run(string[] args)
        {
            var pos = Common.Positional(args);
            int bad = DeterminismRunner.Run(Console.Out, Common.IntArg(pos, 0, 4), Common.IntArg(pos, 1, 45));
            return bad == 0 ? 0 : 1;
        }
    }
}
