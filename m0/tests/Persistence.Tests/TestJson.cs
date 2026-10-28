using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Ekiden.Persistence.Json;

namespace Ekiden.Persistence.Tests
{
    /// <summary>テスト用: ゴールデンデータの読み込みと、JSON の突き合わせ。</summary>
    public static class TestJson
    {
        public static JsonObject LoadGolden(string file)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Golden", file);
            using (var fs = File.OpenRead(path)) using (var gz = new GZipStream(fs, CompressionMode.Decompress)) using (var sr = new StreamReader(gz, new UTF8Encoding(false)))
                return (JsonObject)JsonReader.Parse(sr.ReadToEnd());
        }

        /// <summary>期待値と実際の JSON を突き合わせる(数は相対誤差 1e-12。オブジェクトはキーの集合と各値で、順序は無視)。違いの場所を返す(なければ null)。</summary>
        public static string Diff(JsonValue expected, JsonValue actual, string path = "")
        {
            var en = expected as JsonNumber;
            if (en != null)
            {
                var an = actual as JsonNumber;
                if (an == null) return path + ": 数でない";
                return Math.Abs(en.Value - an.Value) <= 1e-12 * Math.Max(1.0, Math.Abs(en.Value)) ? null : path + ": 期待 " + en.Value + " / 実際 " + an.Value;
            }
            var eo = expected as JsonObject;
            if (eo != null)
            {
                var ao = actual as JsonObject;
                if (ao == null) return path + ": オブジェクトでない";
                if (eo.Count != ao.Count) return path + ": キーの数が違う(期待 " + eo.Count + " / 実際 " + ao.Count + ")";
                foreach (string k in eo.Keys)
                {
                    JsonValue v;
                    if (!ao.TryGet(k, out v)) return path + "." + k + ": 実際にない";
                    string d = Diff(eo[k], v, path + "." + k);
                    if (d != null) return d;
                }
                return null;
            }
            var ea = expected as JsonArray;
            if (ea != null)
            {
                var aa = actual as JsonArray;
                if (aa == null) return path + ": 配列でない";
                if (ea.Count != aa.Count) return path + ": 長さが違う(期待 " + ea.Count + " / 実際 " + aa.Count + ")";
                for (int i = 0; i < ea.Count; i++)
                {
                    string d = Diff(ea[i], aa[i], path + "[" + i + "]");
                    if (d != null) return d;
                }
                return null;
            }
            return expected.StructEquals(actual) ? null : path + ": 期待 " + expected.ToJson() + " / 実際 " + (actual == null ? "なし" : actual.ToJson());
        }
    }
}
