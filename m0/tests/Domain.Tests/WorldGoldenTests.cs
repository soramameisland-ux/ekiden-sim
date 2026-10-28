using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Ekiden.Domain.Training;
using Ekiden.Domain.World;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>05 の乱数を使わない式を、参照モデルのゴールデンデータと突き合わせる(`verify/gen_golden_05.py`)。</summary>
    public class WorldGoldenTests
    {
        [Test]
        public void Formulas_MatchReference()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Golden", "world_cases.json.gz");
            JsonElement root;
            using (var fs = File.OpenRead(path)) using (var gz = new GZipStream(fs, CompressionMode.Decompress)) using (var doc = JsonDocument.Parse(gz)) root = doc.RootElement.Clone();
            int n = 0;
            foreach (JsonElement c in root.EnumerateArray())
            {
                n++;
                string tag = "case " + n;
                JsonElement rj = c.GetProperty("runner"), sj = c.GetProperty("school");
                var r = new WorldRunner
                {
                    A0 = rj.GetProperty("A0").GetDouble(), Gap = rj.GetProperty("gap").GetDouble(), Growth = (GrowthKind)Enum.Parse(typeof(GrowthKind), rj.GetProperty("gtype").GetString()),
                    Region = (Region)Enum.Parse(typeof(Region), rj.GetProperty("region").GetString()), Grade = rj.GetProperty("grade").GetInt32(), Prog = rj.GetProperty("prog").GetDouble(),
                };
                r.SetValues();
                var s = new School
                {
                    Region = (Region)Enum.Parse(typeof(Region), sj.GetProperty("region").GetString()), Prestige = sj.GetProperty("prestige").GetDouble(), CoachQ = sj.GetProperty("coach_q").GetDouble(),
                    IsPlayer = sj.GetProperty("is_player").GetBoolean(), CumPoints = sj.GetProperty("cum_points").GetDouble(),
                };
                if (s.IsPlayer)
                {
                    JsonElement f = sj.GetProperty("fac");
                    s.Fac = new FacilityLevels { Coach = f.GetProperty("coach").GetInt32(), Trainer = f.GetProperty("trainer").GetInt32(), Nutri = f.GetProperty("nutri").GetInt32(), Dorm = f.GetProperty("dorm").GetInt32(), Track = f.GetProperty("track").GetInt32(), Hill = f.GetProperty("hill").GetInt32(), Gym = f.GetProperty("gym").GetInt32(), Medical = f.GetProperty("medical").GetInt32() };
                }
                double lv = s.Lv();
                Near(c.GetProperty("lv").GetDouble(), lv, tag + " lv");
                Near(c.GetProperty("dorm").GetDouble(), s.Dorm(), tag + " dorm");
                Assert.AreEqual(c.GetProperty("schol").GetInt32(), s.ScholSlots(), tag + " schol");
                Assert.AreEqual(c.GetProperty("rank").GetInt32(), s.Rank(), tag + " rank");
                Near(c.GetProperty("offer").GetDouble(), WorldMath.OfferBase(s, r, c.GetProperty("p10").GetDouble()), tag + " offer");
                Near(c.GetProperty("season_power").GetDouble(), WorldMath.SeasonPower(r, lv), tag + " season_power");
                Near(c.GetProperty("season_power_full").GetDouble(), WorldMath.SeasonPower(r, lv, 1.0), tag + " season_power(1.0)");
                for (int y = 0; y <= 4; y++) Near(c.GetProperty("prog_base")[y].GetDouble(), WorldMath.ProgBase(r.Growth, y, lv), tag + " prog_base " + y);
                Near(c.GetProperty("gap_adj").GetDouble(), WorldMath.GapAdj(r.Gap), tag + " gap_adj");
                int[] ks = { 1, 2, 5, 10, 20 };
                for (int i = 0; i < ks.Length; i++) Near(c.GetProperty("fame")[i].GetDouble(), WorldMath.FamePoint(ks[i]), tag + " fame");
                Near(c.GetProperty("income").GetDouble(), WorldMath.Income(s.Prestige, c.GetProperty("year_points").GetInt32()), tag + " income");
                WorldMath.SeasonEnd(r, lv);
                // 保存する double は代入のときに Q6 を通す(09 D-10)ので、参照(丸めなし)の Q6 と一致する
                Assert.AreEqual(Ekiden.Domain.Numerics.Q6.Round(c.GetProperty("prog_after").GetDouble()), r.Prog, tag + " prog_after");
            }
            Assert.AreEqual(4000, n);
            TestContext.Out.WriteLine("世界の式: " + n + " 件が参照モデルと一致");
        }

        private static void Near(double expected, double actual, string what)
        {
            Assert.AreEqual(expected, actual, 1e-9 * Math.Max(1.0, Math.Abs(expected)), what);
        }
    }
}
