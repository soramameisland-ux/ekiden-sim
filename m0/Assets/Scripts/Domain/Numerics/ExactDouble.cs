using System.Globalization;

namespace Ekiden.Domain.Numerics
{
    /// <summary>
    /// 10 進の文字列 → <c>double</c>。<b>最近接に丸めることを保証する</b>読み取り(09 §15.3 D-13。L-71)。
    /// Unity(Mono・IL2CPP)の <c>double.Parse</c> は、まれに最近接に丸めず 1 ulp ずれる(<c>Q6</c> の値で約 0.05%。.NET は 0)。保存の JSON を読み直すと値が変わり、
    /// 『保存 → 読み込み → 続行 = 続行のみ』が崩れるので、保存と条件式の数の読み取りはこれを使う。
    /// やり方: 符号・整数部・小数部の数字を<b>整数</b> m として読み(m ≤ 2^53)、<c>m / 10^k</c>(k ≤ 22)を 1 回の除算で求める。
    /// m も 10^k(k ≤ 22)も double で正確に表せ、IEEE の除算は最近接に丸めるので、結果は 10 進の最近接の double になる(.NET の <c>double.Parse</c> と一致)。
    /// <c>Q6.Round</c> の値は <c>整数 / 1e6</c> なので、小数 6 桁以下の表記は <c>Q6.Round</c> とビット一致する。
    /// </summary>
    public static class ExactDouble
    {
        private static readonly double[] Pow10 =
        {
            1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
        };

        private const long MaxMantissa = 9007199254740992L;          // 2^53。これ以下の整数は double で正確に表せる

        /// <summary>
        /// 速い経路だけ: <c>[-]数字[.数字]</c>(指数なし)で、仮数が 2^53 以下・小数が 22 桁以下のとき成功する。最近接に丸めた値を返す。成功しなければ false(<paramref name="value"/> は 0)。
        /// </summary>
        public static bool TryParseExact(string s, out double value)
        {
            value = 0.0;
            if (s == null) return false;
            int n = s.Length, i = 0;
            bool neg = false;
            if (i < n && s[i] == '-') { neg = true; i++; }
            long m = 0;
            int digits = 0, frac = 0;
            bool dot = false;
            for (; i < n; i++)
            {
                char c = s[i];
                if (c >= '0' && c <= '9')
                {
                    int d = c - '0';
                    if (m > (MaxMantissa - d) / 10) return false;        // m × 10 + d が 2^53 を超える
                    m = m * 10 + d;
                    digits++;
                    if (dot) frac++;
                }
                else if (c == '.' && !dot) dot = true;
                else return false;                                         // 指数・空白・符号など
            }
            if (digits == 0 || frac >= Pow10.Length) return false;
            double v = (double)m / Pow10[frac];
            value = neg ? -v : v;
            return true;
        }

        /// <summary>
        /// 速い経路(<see cref="TryParseExact"/>)で読み、できなければ従来の <c>double.TryParse</c>(指数つき・17 桁を超える数など。保存には出ない)。
        /// 保存の値(<c>Q6</c> で丸めた、小数 6 桁以下)はいつも速い経路に入る。
        /// </summary>
        public static bool TryParse(string s, out double value)
        {
            if (TryParseExact(s, out value)) return true;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>失敗したら <see cref="System.FormatException"/>(<c>double.Parse</c> と同じ)。</summary>
        public static double Parse(string s)
        {
            double v;
            if (!TryParse(s, out v)) throw new System.FormatException("数として読めない: " + s);
            return v;
        }
    }
}
