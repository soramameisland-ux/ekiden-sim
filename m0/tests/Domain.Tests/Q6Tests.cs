using System;
using Ekiden.Domain.Numerics;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>09 TV-4: Q6 と書式(§5.3)。</summary>
    public class Q6Tests
    {
        [TestCase(0.0, "0")]
        [TestCase(-0.0, "0")]
        [TestCase(1e-7, "0")]
        [TestCase(5e-7, "0.000001")]
        [TestCase(0.1 + 0.2, "0.3")]
        [TestCase(123456789.12345679, "123456789.123457")]
        [TestCase(2.5e-6, "0.000003")]
        [TestCase(-3e-6, "-0.000003")]
        [TestCase(0.5, "0.5")]
        [TestCase(0.95, "0.95")]
        public void EdgeValues(double x, string expected)
        {
            Assert.AreEqual(expected, Q6.Format(Q6.Round(x)));
        }

        [Test]
        public void RoundTripAndIdempotent_MillionRandom()
        {
            var rng = new Random(6);
            double[] mags = { 1.0, 10.0, 1000.0, 1e5, 1e9 };
            int badRt = 0, badIdem = 0;
            for (int i = 0; i < 1000000; i++)
            {
                double x = (rng.NextDouble() * 2 - 1) * mags[rng.Next(mags.Length)];
                double q = Q6.Round(x);
                if (double.Parse(Q6.Format(q), System.Globalization.CultureInfo.InvariantCulture) != q) badRt++;
                if (Q6.Round(q) != q) badIdem++;
            }
            Assert.AreEqual(0, badRt, "書式 → 読み戻しの不一致");
            Assert.AreEqual(0, badIdem, "Q6 が冪等でない");
        }
    }
}
