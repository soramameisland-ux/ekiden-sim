using Ekiden.Domain.Text;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>09 TV-11: 数値・時間の整形(§13)。参照モデル(ref_model_09.py fmt_vectors)の 59 件と、往復・単調・対称。</summary>
    public class FmtTests
    {
        private static readonly string M = Fmt.Minus;

        [TestCase(39543, "1:05:54.3")] [TestCase(0, "0:00.0")] [TestCase(9, "0:00.9")] [TestCase(599, "0:59.9")] [TestCase(600, "1:00.0")]
        [TestCase(8558, "14:15.8")] [TestCase(35999, "59:59.9")] [TestCase(36000, "1:00:00.0")] [TestCase(37849, "1:03:04.9")] [TestCase(360000, "10:00:00.0")]
        public void Time(long ds, string want) { Assert.AreEqual(want, Fmt.Time(ds)); }

        [TestCase(76080, "2:06:48")] [TestCase(76089, "2:06:48")] [TestCase(76090, "2:06:49")] [TestCase(35999, "59:59")] [TestCase(36000, "1:00:00")] [TestCase(0, "0:00")]
        public void Total(long ds, string want) { Assert.AreEqual(want, Fmt.Total(ds)); }

        [Test]
        public void Diff()
        {
            Assert.AreEqual("+12.3", Fmt.Diff(123)); Assert.AreEqual(M + "7.1", Fmt.Diff(-71)); Assert.AreEqual("±0.0", Fmt.Diff(0));
            Assert.AreEqual("+0.5", Fmt.Diff(5)); Assert.AreEqual("+59.9", Fmt.Diff(599)); Assert.AreEqual("+1:00", Fmt.Diff(600));
            Assert.AreEqual(M + "1:05", Fmt.Diff(-659)); Assert.AreEqual("+59:59", Fmt.Diff(35999)); Assert.AreEqual("+1:00:00", Fmt.Diff(36000));
            Assert.AreEqual(M + "1:01:01", Fmt.Diff(-36610));
        }

        [TestCase(21.0975, "21.1 km")] [TestCase(5.8, "5.8 km")] [TestCase(10.95, "11.0 km")] [TestCase(0.05, "0.1 km")] [TestCase(13.0, "13.0 km")]
        public void Km(double km, string want) { Assert.AreEqual(want, Fmt.Km(km)); }

        [Test]
        public void Money()
        {
            Assert.AreEqual("1,500万円", Fmt.Money(1500)); Assert.AreEqual("0万円", Fmt.Money(0)); Assert.AreEqual(M + "300万円", Fmt.Money(-300));
            Assert.AreEqual("9,999万円", Fmt.Money(9999)); Assert.AreEqual("1億円", Fmt.Money(10000)); Assert.AreEqual("1.2億円", Fmt.Money(12000));
            Assert.AreEqual("1.2億円", Fmt.Money(12499)); Assert.AreEqual("1.3億円", Fmt.Money(12500)); Assert.AreEqual("10億円", Fmt.Money(99999));
            Assert.AreEqual("123.5億円", Fmt.Money(1234567));
        }

        [TestCase(0.82, "82%")] [TestCase(0.0, "5%未満")] [TestCase(0.0499, "5%未満")] [TestCase(0.05, "5%")] [TestCase(0.054, "5%")]
        [TestCase(0.945, "95%")] [TestCase(0.95, "95%")] [TestCase(0.9501, "95%超")] [TestCase(1.0, "95%超")] [TestCase(0.4949, "49%")] [TestCase(0.495, "50%")]
        public void Pct(double p, string want) { Assert.AreEqual(want, Fmt.Pct(p)); }

        [Test]
        public void YearAndRank()
        {
            Assert.AreEqual("第12年度", Fmt.Year(12)); Assert.AreEqual("第12年度 10月3週", Fmt.Year(12, 26)); Assert.AreEqual("第1年度 4月1週", Fmt.Year(1, 0));
            Assert.AreEqual("第3年度 3月4週", Fmt.Year(3, 47)); Assert.AreEqual("第3年度 1月1週", Fmt.Year(3, 36));
            Assert.AreEqual("8位 繰上", Fmt.Rank(8, true)); Assert.AreEqual("1位", Fmt.Rank(1, false));
        }

        [Test]
        public void TimeRoundTrip_0to99999()
        {
            int bad = 0;
            for (long ds = 0; ds < 100000; ds++) if (Fmt.ParseTime(Fmt.Time(ds)) != ds) bad++;
            Assert.AreEqual(0, bad);
        }

        [Test]
        public void PctMonotonic()
        {
            double prev = -1; int bad = 0;
            for (int k = 0; k <= 10000; k++)
            {
                string t = Fmt.Pct(k / 10000.0);
                double v = t == "5%未満" ? 4.5 : t == "95%超" ? 95.5 : int.Parse(t.Substring(0, t.Length - 1));
                if (v < prev) bad++;
                prev = v;
            }
            Assert.AreEqual(0, bad);
        }

        [Test]
        public void DiffSymmetric()
        {
            int bad = 0;
            for (long d = 1; d < 40000; d++) if (Fmt.Diff(d).Substring(1) != Fmt.Diff(-d).Substring(1)) bad++;
            Assert.AreEqual(0, bad);
        }
    }
}
