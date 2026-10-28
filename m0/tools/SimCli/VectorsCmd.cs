using System;
using Ekiden.Verification;

namespace Ekiden.SimCli
{
    /// <summary>T1 の主なベクタの値を『名前 値』の行で出す(09 §16.2)。本体は <see cref="VectorsRunner"/>(Unity の実機の確認と共有)。<c>tools/compare_vectors.py</c> が参照モデルと突き合わせる。</summary>
    internal static class VectorsCmd
    {
        public static int Run(string[] args)
        {
            VectorsRunner.Run(Console.Out);
            return 0;
        }
    }
}
