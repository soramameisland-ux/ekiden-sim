using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Ekiden.Domain.Numerics;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>最近接に丸めることを保証する数の読み取り(09 §15.3 D-13。L-71)。.NET の <c>double.Parse</c> は正しいので、それと突き合わせる。Unity だけで起きる不具合は、速い経路が <c>double.Parse</c> を使わないことで防ぐ。</summary>
    public class ExactDoubleTests
    {
        private static long Bits(double v) { return BitConverter.DoubleToInt64Bits(v); }

        private static ulong Next(ref ulong s) { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return s; }          // xorshift64(固定の系列)

        [Test]
        public void Q6Values_AreReadBackBitExactly_ThroughTheFastPath()
        {
            ulong s = 88172645463325252UL;
            double[] scales = { 1.0, 1e3, 1e5, 1e7 };
            int n = 1000000;
            for (int i = 0; i < n; i++)
            {
                double x = ((Next(ref s) >> 11) / 9007199254740992.0 - 0.5) * 2.0 * scales[i & 3];
                double q = Q6.Round(x);
                string t = Q6.Format(q);
                double v;
                Assert.IsTrue(ExactDouble.TryParseExact(t, out v), "速い経路に入る: " + t);
                if (Bits(v) != Bits(q == 0 ? 0.0 : q)) Assert.Fail("Q6.Round とビットが違う: " + t + " " + Bits(v).ToString("x16") + " / " + Bits(q).ToString("x16"));
                Assert.AreEqual(Bits(double.Parse(t, NumberStyles.Float, CultureInfo.InvariantCulture)), Bits(v), "(.NET の最近接と一致) " + t);
            }
        }

        [Test]
        public void TheValueThatBrokeUnity_IsParsedExactly()
        {
            double v = ExactDouble.Parse("18.232545");
            Assert.AreEqual(0x40323b8811b1d92bL, Bits(v), "Unity の double.Parse は …d92c を返した(L-71)");
            Assert.AreEqual(Bits(Q6.Round(18.232545)), Bits(v));
        }

        [Test]
        public void MatchesTheCorrectlyRoundedParse_OnVariedPlainForms()
        {
            ulong s = 2685821657736338717UL;
            for (int i = 0; i < 300000; i++)
            {
                int digits = 1 + (int)(Next(ref s) % 15), frac = (int)(Next(ref s) % 18);
                var sb = new System.Text.StringBuilder();
                if ((Next(ref s) & 1) == 0) sb.Append('-');
                int intDigits = Math.Max(0, digits - frac);
                if (intDigits == 0) sb.Append('0'); else for (int k = 0; k < intDigits; k++) sb.Append((char)('0' + Next(ref s) % 10));
                if (frac > 0) { sb.Append('.'); for (int k = 0; k < frac; k++) sb.Append((char)('0' + Next(ref s) % 10)); }
                string t = sb.ToString();
                double v;
                Assert.IsTrue(ExactDouble.TryParse(t, out v), t);
                Assert.AreEqual(Bits(double.Parse(t, NumberStyles.Float, CultureInfo.InvariantCulture)), Bits(v), t);
            }
        }

        [TestCase("0")] [TestCase("-0")] [TestCase("0.0")] [TestCase("007")] [TestCase("5.")] [TestCase(".5")] [TestCase("-.25")] [TestCase("0.000000000000000000001")]
        [TestCase("9007199254740992")] [TestCase("123456789012345.6")] [TestCase("0.1")] [TestCase("0.3")] [TestCase("2.675")]
        public void EdgeForms_AgreeWithDoubleParse(string t)
        {
            double v;
            Assert.IsTrue(ExactDouble.TryParse(t, out v));
            Assert.AreEqual(Bits(double.Parse(t, NumberStyles.Float, CultureInfo.InvariantCulture)), Bits(v), t);
        }

        [TestCase("")] [TestCase("-")] [TestCase(".")] [TestCase("1.2.3")] [TestCase("1e5")] [TestCase("1E-5")] [TestCase("+1")] [TestCase(" 1")] [TestCase("1 ")] [TestCase("abc")] [TestCase("--1")]
        [TestCase("9007199254740993")] [TestCase("0.00000000000000000000001")]
        public void TheFastPath_RefusesWhatItCannotMakeExact(string t)
        {
            double v;
            Assert.IsFalse(ExactDouble.TryParseExact(t, out v), t);
            Assert.AreEqual(0.0, v);
        }

        [Test]
        public void TheFallback_ReadsWhatTheFastPathRefuses_AndRejectsGarbage()
        {
            double v;
            Assert.IsTrue(ExactDouble.TryParse("1e-5", out v)); Assert.AreEqual(1e-5, v);
            Assert.IsTrue(ExactDouble.TryParse("9007199254740993", out v)); Assert.AreEqual(9007199254740992.0, v);
            Assert.IsFalse(ExactDouble.TryParse("abc", out v));
            Assert.IsFalse(ExactDouble.TryParse(null, out v));
            Assert.Throws<FormatException>(() => ExactDouble.Parse("abc"));
        }

        /// <summary>保存と条件式の数の読み取りは <see cref="ExactDouble"/> だけを使う(D-13)。Unity の <c>double.Parse</c> は 1 ulp ずれることがあるので、.NET の試験では見つからない回帰を、ソースの走査で止める。</summary>
        [Test]
        public void NoDirectDoubleParse_InDomainOrPersistence()
        {
            string root = AppContext.BaseDirectory;
            while (root != null && !Directory.Exists(Path.Combine(root, "Assets", "Scripts", "Domain"))) root = Path.GetDirectoryName(root);
            Assert.IsNotNull(root, "Assets/Scripts が見つからない");
            string[] banned = { "double.Parse(", "double.TryParse(", "Convert.ToDouble(", "float.Parse(", "float.TryParse(", "decimal.Parse(" };
            var hits = new System.Collections.Generic.List<string>();
            foreach (string dir in new[] { "Domain", "Persistence", "App" })
                foreach (string f in Directory.EnumerateFiles(Path.Combine(root, "Assets", "Scripts", dir), "*.cs", SearchOption.AllDirectories))
                {
                    if (Path.GetFileName(f) == "ExactDouble.cs") continue;
                    string[] lines = File.ReadAllLines(f);
                    for (int i = 0; i < lines.Length; i++)
                        foreach (string b in banned)
                            if (lines[i].Contains(b) && !lines[i].TrimStart().StartsWith("//") && !lines[i].TrimStart().StartsWith("///"))
                                hits.Add(Path.GetFileName(f) + ":" + (i + 1) + " " + b);
                }
            Assert.IsEmpty(hits, "数の読み取りは ExactDouble を使う(09 D-13): " + string.Join(", ", hits));
        }
    }
}
