using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Ekiden.Domain.Race;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>地形係数(02 §3.5。L-69): 初期の区間記録(02 §4.8)から、`terr = (R / d + K_SLOPE × (P* − 50)) / pace50(d)` で較正した値。</summary>
    public class TerrainFactorTests
    {
        private static readonly string[] KindNames = { "第一戦", "第二戦", "第三戦" };

        [Test]
        public void Table_MatchesDoc02_AndTheCourses()
        {
            string text = File.ReadAllText(DocPaths.Find("02_data_spec.md"), Encoding.UTF8);
            int start = text.IndexOf("**地形係数(L-69", StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, "02 §3.5 に地形係数の表がある");
            int end = text.IndexOf("**RaceRules**", start, StringComparison.Ordinal);
            string section = text.Substring(start, end - start);
            int rows = 0;
            foreach (Match m in Regex.Matches(section, @"^\|\s*(第[一二三]戦)\s*\|\s*(\d+)\s*\|\s*([\d.]+)\s*\|\s*(\w+)\s*\|\s*([\d.]+)\s*\|", RegexOptions.Multiline))
            {
                int k = Array.IndexOf(KindNames, m.Groups[1].Value);
                int si = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) - 1;
                SectionDef sec = RaceTables.Course((RaceKind)k)[si];
                Assert.AreEqual(double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), sec.Distance, 1e-9, KindNames[k] + " " + (si + 1));
                Assert.AreEqual(m.Groups[4].Value, sec.Type.ToString(), KindNames[k] + " " + (si + 1));
                Assert.AreEqual(double.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture), sec.Terrain, 1e-9, KindNames[k] + " " + (si + 1));
                rows++;
            }
            Assert.AreEqual(24, rows);
        }

        [Test]
        public void EveryFactor_IsTheCalibrationOfTheSeedRecord_AtPStar()
        {
            for (int k = 0; k < 3; k++)
                for (int si = 0; si < RecordSeeds.CourseDs[k].Length; si++)
                {
                    SectionDef sec = RaceTables.Course((RaceKind)k)[si];
                    double r = RecordSeeds.CourseDs[k][si] / 10.0;
                    double terr = Math.Round((r / sec.Distance + RaceConstants.K_SLOPE * (RecordSeeds.TerrainPStar - 50.0)) / Pace50.Of(sec.Distance), 3, MidpointRounding.AwayFromZero);
                    Assert.AreEqual(terr, sec.Terrain, 1e-9, KindNames[k] + " " + (si + 1));
                    // 結果として、P* の走者の基準タイムが初期の記録に(係数の丸め 0.0005 の範囲で)一致する
                    double bt = RaceMath.BaseTime(sec.Distance, sec.Terrain, RecordSeeds.TerrainPStar);
                    double tol = 0.0005 * Pace50.Of(sec.Distance) * sec.Distance + 1e-6;
                    Assert.AreEqual(r, bt, tol, KindNames[k] + " " + (si + 1) + " の基準タイム");
                }
        }

        [Test]
        public void Factors_StayNearOne_ExceptTheMountainSections()
        {
            for (int k = 0; k < 3; k++)
                for (int si = 0; si < RaceTables.Course((RaceKind)k).Count; si++)
                {
                    SectionDef sec = RaceTables.Course((RaceKind)k)[si];
                    if (sec.Type == SectionType.Climb) Assert.That(sec.Terrain, Is.InRange(1.10, 1.17), "登り");
                    else Assert.That(sec.Terrain, Is.InRange(0.96, 1.06), KindNames[k] + " " + (si + 1));
                }
        }
    }
}
