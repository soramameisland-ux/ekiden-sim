using System;
using System.Globalization;

namespace Ekiden.Domain.Numerics
{
    /// <summary>保存する double の丸め(09 §5.3 / D-10)。状態に書き戻す double は小数 6 桁に丸める。</summary>
    public static class Q6
    {
        /// <summary>小数 6 桁へ丸める。整数 / 1e6 は IEEE の除算が最近接に丸めるので、10 進の 6 桁表記の最近接の double になる。</summary>
        public static double Round(double x)
        {
            return Math.Floor(x * 1e6 + 0.5) / 1e6;
        }

        /// <summary>JSON に書く形: 小数 6 桁まで、末尾の 0 を省く。指数表記なし。-0 は 0。</summary>
        public static string Format(double x)
        {
            string t = x.ToString("F6", CultureInfo.InvariantCulture);
            if (t.IndexOf('.') >= 0)
            {
                t = t.TrimEnd('0').TrimEnd('.');
            }
            return t == "-0" || t.Length == 0 ? "0" : t;
        }
    }
}
