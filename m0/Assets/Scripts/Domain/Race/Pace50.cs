using System;

namespace Ekiden.Domain.Race
{
    /// <summary>基準ペース(秒/km)。距離は有限の集合なので、倍精度の値の表を引く(`log` を呼ばない。04 §2・09 §8.4)。</summary>
    public static partial class Pace50
    {
        public static double Of(double distanceKm)
        {
            if (!Table.TryGetValue(distanceKm, out double v))
                throw new ArgumentException("pace50 の表にない距離: " + distanceKm.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return v;
        }

        /// <summary>表の値の検査用(式そのもの)。</summary>
        public static double Formula(double d)
        {
            return 176.0 + 6.0 * Math.Log(d / 5.0);
        }

        public static System.Collections.Generic.IEnumerable<double> Distances { get { return Table.Keys; } }
    }
}
