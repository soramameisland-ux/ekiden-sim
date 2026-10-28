using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Ekiden.Persistence;
using Ekiden.Persistence.Container;
using NUnit.Framework;

namespace Ekiden.Persistence.Tests
{
    public class CrcAndConstantsTests
    {
        [Test]
        public void Crc32_CheckValue()
        {
            Assert.AreEqual(0xCBF43926u, Crc32.Compute(Encoding.ASCII.GetBytes("123456789")));
            Assert.AreEqual(0u, Crc32.Compute(new byte[0]));
        }

        [Test]
        public void Crc32_MatchesBitwiseDefinition_AndIncremental()
        {
            var rnd = new Random(32);
            for (int n = 0; n < 200; n++)
            {
                var data = new byte[rnd.Next(0, 300)];
                rnd.NextBytes(data);
                Assert.AreEqual(Slow(data), Crc32.Compute(data));
                int cut = data.Length == 0 ? 0 : rnd.Next(0, data.Length);
                uint inc = Crc32.Update(Crc32.Compute(data.AsSpan(0, cut)), data.AsSpan(cut));
                Assert.AreEqual(Crc32.Compute(data), inc);
            }
        }

        private static uint Slow(byte[] d)
        {
            uint c = 0xFFFFFFFF;
            foreach (byte b in d)
            {
                c ^= b;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320u : c >> 1;
            }
            return ~c;
        }

        [Test]
        public void SaveConstants_MatchDocTable()
        {
            string path = FindDoc("09_save_tech_spec.md");
            var doc = new System.Collections.Generic.Dictionary<string, string>();
            foreach (Match m in Regex.Matches(File.ReadAllText(path, Encoding.UTF8), @"^\|\s*`(K_[A-Z0-9_]+)`\s*\|\s*([^|]+?)\s*\|", RegexOptions.Multiline))
            {
                if (!doc.ContainsKey(m.Groups[1].Value)) doc[m.Groups[1].Value] = Regex.Replace(m.Groups[2].Value, @"[ ,`]|\(.*?\)", "");
            }
            foreach (var kv in SaveConstants.All)
            {
                Assert.IsTrue(doc.ContainsKey(kv.Key), kv.Key + " が文書にない");
                Assert.AreEqual(doc[kv.Key], kv.Value, kv.Key);
            }
            CollectionAssert.IsEmpty(doc.Keys.Except(SaveConstants.All.Keys).ToList(), "コードにない定数");
        }

        private static string FindDoc(string name)
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                string p = Path.Combine(dir, name);
                if (File.Exists(p)) return p;
                dir = Path.GetDirectoryName(dir);
            }
            throw new FileNotFoundException(name);
        }
    }
}
