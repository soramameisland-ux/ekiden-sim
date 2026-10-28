using System;
using System.Globalization;
using Ekiden.Domain.Numerics;

namespace Ekiden.Domain.Text
{
    /// <summary>数値・時間の整形(08 §7.4 の正本。09 §13)。整数だけで計算し、浮動小数に頼らない(時間)。</summary>
    public static class Fmt
    {
        public const string Minus = "−"; // −

        private static string C(long n) { return n.ToString("N0", CultureInfo.InvariantCulture); }

        /// <summary>区間・記録(単位 ds = 0.1 秒)。1 時間以上は h:mm:ss.s、未満は m:ss.s。</summary>
        public static string Time(long ds)
        {
            if (ds < 0) throw new ArgumentOutOfRangeException(nameof(ds));
            long h = ds / 36000, rem = ds % 36000;
            long m = rem / 600; rem %= 600;
            long s = rem / 10, f = rem % 10;
            return h > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}.{3}", h, m, s, f)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}.{2}", m, s, f);
        }

        /// <summary>総合タイム: 秒未満は切り捨て。1 時間以上は h:mm:ss、未満は m:ss。</summary>
        public static string Total(long ds)
        {
            long sec = ds / 10;
            long h = sec / 3600, rem = sec % 3600;
            long m = rem / 60, s = rem % 60;
            return h > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", h, m, s)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", m, s);
        }

        /// <summary>タイム差: 0 は ±0.0。1 分未満は ±s.s、1 分以上は ±m:ss(秒未満は切り捨て)、1 時間以上は ±h:mm:ss。</summary>
        public static string Diff(long dds)
        {
            if (dds == 0) return "±0.0";
            string sign = dds > 0 ? "+" : Minus;
            long a = Math.Abs(dds);
            if (a < 600) return string.Format(CultureInfo.InvariantCulture, "{0}{1}.{2}", sign, a / 10, a % 10);
            long sec = a / 10;
            long h = sec / 3600, rem = sec % 3600;
            long m = rem / 60, s = rem % 60;
            return h > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}{1}:{2:00}:{3:00}", sign, h, m, s)
                : string.Format(CultureInfo.InvariantCulture, "{0}{1}:{2:00}", sign, m, s);
        }

        public static string Km(double km)
        {
            return (Rounding.HalfUp(km * 10) / 10.0).ToString("F1", CultureInfo.InvariantCulture) + " km";
        }

        /// <summary>金額(単位: 万円の整数)。1 億円(10000 万円)以上は億円で小数 1 桁(.0 は省く)。負は −。</summary>
        public static string Money(long man)
        {
            string sign = man < 0 ? Minus : "";
            long a = Math.Abs(man);
            if (a >= 10000)
            {
                long t = Rounding.HalfUpLong(a / 1000.0);
                long whole = t / 10, frac = t % 10;
                return frac == 0
                    ? sign + C(whole) + "億円"
                    : sign + C(whole) + "." + frac.ToString(CultureInfo.InvariantCulture) + "億円";
            }
            return sign + C(a) + "万円";
        }

        /// <summary>『第12年度』。week(0〜47)を渡すと『第12年度 10月2週』。月は 4 月始まりで 4 週ずつ。</summary>
        public static string Year(int y, int? week = null)
        {
            string baseText = "第" + y.ToString(CultureInfo.InvariantCulture) + "年度";
            if (week == null) return baseText;
            int mi = week.Value / 4, wk = week.Value % 4;
            return baseText + " " + ((3 + mi) % 12 + 1).ToString(CultureInfo.InvariantCulture) + "月" + (wk + 1).ToString(CultureInfo.InvariantCulture) + "週";
        }

        public static string Grade(int g)
        {
            return g.ToString(CultureInfo.InvariantCulture) + "年";
        }

        public static string Pct(double p)
        {
            double x = p * 100.0;
            if (x < 5.0) return "5%未満";
            if (x > 95.0) return "95%超";
            return Rounding.HalfUp(x).ToString(CultureInfo.InvariantCulture) + "%";
        }

        public static string Rank(int n, bool promoted = false)
        {
            return n.ToString(CultureInfo.InvariantCulture) + (promoted ? "位 繰上" : "位");
        }

        /// <summary><see cref="Time"/> の逆(往復テスト用)。</summary>
        public static long ParseTime(string s)
        {
            string[] parts = s.Split(':');
            double sec = double.Parse(parts[parts.Length - 1], CultureInfo.InvariantCulture);
            long m = long.Parse(parts[parts.Length - 2], CultureInfo.InvariantCulture);
            long h = parts.Length == 3 ? long.Parse(parts[0], CultureInfo.InvariantCulture) : 0;
            return h * 36000 + m * 600 + Rounding.HalfUpLong(sec * 10);
        }
    }
}
