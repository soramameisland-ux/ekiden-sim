using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Persistence.Container;
using Ekiden.Persistence.Json;
using Ekiden.Persistence.Migration;
using Ekiden.Persistence.Snapshot;
using Ekiden.Persistence.Sync;
using Ekiden.Persistence.Validation;
using NUnit.Framework;

namespace Ekiden.Persistence.Tests
{
    /// <summary>永続化(09)を、参照モデル(verify/ref_model_09.py)のゴールデンデータと突き合わせる(`verify/gen_golden_09.py`)。</summary>
    public class PersistGoldenTests
    {
        private static JsonObject _root;

        [OneTimeSetUp]
        public void Load() { _root = TestJson.LoadGolden("persist_cases.json.gz"); }

        private static string Sha(byte[] b)
        {
            using (var h = System.Security.Cryptography.SHA256.Create()) return string.Concat(h.ComputeHash(b).Select(x => x.ToString("x2")));
        }

        [Test]
        public void Containers_DecodeLikeTheReference()
        {
            int n = 0, ok = 0;
            var codes = new Dictionary<string, int>();
            foreach (JsonValue cv in (JsonArray)_root["containers"])
            {
                var c = (JsonObject)cv;
                n++;
                string name = ((JsonString)c["name"]).Value;
                byte[] raw = Convert.FromBase64String(((JsonString)c["b64"]).Value);
                var dec = (JsonObject)c["decode"];
                HeaderInfo h;
                JsonValue p;
                string code;
                bool got = Eksv.TryDecode(raw, out h, out p, out code);
                bool want = ((JsonBool)dec["ok"]).Value;
                Assert.AreEqual(want, got, name + "(期待 " + (want ? "ok" : ((JsonString)dec["code"]).Value) + " / 実際 " + code + ")");
                if (want)
                {
                    ok++;
                    Assert.IsNull(TestJson.Diff(dec["header"], h.Header), name + " ヘッダ");
                    Assert.AreEqual(((JsonString)dec["payload_sha"]).Value, Sha(p.ToUtf8()), name + " ペイロード");
                }
                else
                {
                    string want_code = ((JsonString)dec["code"]).Value;
                    Assert.AreEqual(want_code, code, name);
                    codes[code] = codes.ContainsKey(code) ? codes[code] + 1 : 1;
                }
                var hdr = (JsonObject)c["header"];
                HeaderInfo hi;
                string hcode = null;
                try { hi = Eksv.DecodeHeader(raw); }
                catch (LoadException e) { hi = null; hcode = e.Code; }
                Assert.AreEqual(((JsonBool)hdr["ok"]).Value, hi != null, name + " ヘッダだけ");
                if (hi != null)
                {
                    Assert.IsNull(TestJson.Diff(hdr["header"], hi.Header), name);
                    Assert.AreEqual((long)((JsonNumber)hdr["pos"]).Value, hi.PayloadOffset, name);
                }
                else Assert.AreEqual(((JsonString)hdr["code"]).Value, hcode, name + " ヘッダの失敗のコード");
            }
            Assert.GreaterOrEqual(n, 400);
            Assert.Greater(ok, 3);
            foreach (string k in new[] { "truncated", "header_crc", "crc", "container_too_new", "header_len", "payload_len", "length", "header_json", "payload", "magic" })
                Assert.IsTrue(codes.ContainsKey(k), "失敗のコード " + k + " の例が含まれていない");
            TestContext.Out.WriteLine("コンテナ: " + n + " 件(正常 " + ok + ")の判定と失敗のコードが参照モデルと一致 " + string.Join(", ", codes.OrderBy(x => x.Key).Select(x => x.Key + " " + x.Value)));
        }

        [Test]
        public void Migration_MatchesReference()
        {
            int n = 0, errors = 0, migrated = 0;
            foreach (JsonValue cv in (JsonArray)_root["migrate"])
            {
                var c = (JsonObject)cv;
                n++;
                var input = (JsonObject)c["input"];
                string before = input.ToJson();
                List<string> log;
                if (!c["error"].IsNull)
                {
                    var x = Assert.Throws<LoadException>(() => { List<string> l; SaveMigrator.Migrate(input, out l); }, "case " + n);
                    Assert.AreEqual(((JsonString)c["error"]).Value, x.Code);
                    errors++;
                    continue;
                }
                JsonObject got = SaveMigrator.Migrate(input, out log);
                Assert.AreEqual(before, input.ToJson(), "入力は変えない(case " + n + ")");
                string d = TestJson.Diff(c["output"], got);
                Assert.IsNull(d, "case " + n + ": " + d);
                CollectionAssert.AreEqual(((JsonArray)c["log"]).Items.Select(x => ((JsonString)x).Value).ToArray(), log, "case " + n + " 適用ログ");
                if (log.Count > 0) migrated++;
                List<string> log2;
                JsonObject again = SaveMigrator.Migrate(got, out log2);                // 冪等
                Assert.IsTrue(got.SameContent(again) && log2.Count == 0, "冪等(case " + n + ")");
            }
            Assert.GreaterOrEqual(n, 600);
            Assert.Greater(migrated, 300);
            Assert.GreaterOrEqual(errors, 1);
            TestContext.Out.WriteLine("マイグレーション: " + n + " 件(移行あり " + migrated + "・新しい版 " + errors + ")が参照モデルと一致・冪等");
        }

        [Test]
        public void Validation_MatchesReference_AndOracleAgrees()
        {
            int n = 0, fatal = 0, repaired = 0, clean = 0;
            var kinds = new Dictionary<string, int>();
            foreach (JsonValue cv in (JsonArray)_root["validate"])
            {
                var c = (JsonObject)cv;
                n++;
                string tag = ((JsonString)c["tag"]).Value;
                JsonValue input = c["input"];
                string before = input.ToJson();
                ValidationResult r = SaveValidator.ValidateAndRepair(input);
                Assert.AreEqual(before, input.ToJson(), "入力は変えない");
                string where = "case " + n + " [" + tag + "]";
                if (!c["fatal"].IsNull)
                {
                    Assert.AreEqual(((JsonString)c["fatal"]).Value, r.Fatal, where);
                    Assert.IsNull(r.Repaired, where);
                    fatal++;
                    continue;
                }
                Assert.IsNull(r.Fatal, where + " 致命にすべきでない: " + r.Fatal);
                CollectionAssert.AreEqual(((JsonArray)c["fixes"]).Items.Select(x => ((JsonString)x).Value).ToArray(), r.Fixes, where + " 修復の一覧");
                string d = TestJson.Diff(c["repaired"], r.Repaired);
                Assert.IsNull(d, where + " 修復後のセーブ: " + d);
                // 独立の検査(判定役)の一致
                if (!c["inv_input"].IsNull)
                    CollectionAssert.AreEqual(((JsonArray)c["inv_input"]).Items.Select(x => ((JsonString)x).Value).ToArray(), SaveInvariants.Check((JsonObject)input), where + " 修復前の不変条件");
                var viol = SaveInvariants.Check(r.Repaired);
                CollectionAssert.IsEmpty(viol, where + " 修復後の違反");
                CollectionAssert.AreEqual(((JsonArray)c["inv_repaired"]).Items.Select(x => ((JsonString)x).Value).ToArray(), viol);
                // 冪等: 修復後をもう一度検証しても変わらない
                ValidationResult again = SaveValidator.ValidateAndRepair(r.Repaired);
                Assert.IsNull(again.Fatal, where);
                Assert.AreEqual(0, again.Fixes.Count, where + " 2 度目の修復で変化(冪等でない)");
                Assert.IsTrue(again.Repaired.SameContent(r.Repaired), where);
                if (r.Fixes.Count == 0) clean++;
                else repaired++;
                foreach (string f in r.Fixes) { string k = f.Split(' ')[0]; kinds[k] = kinds.ContainsKey(k) ? kinds[k] + 1 : 1; }
                if (tag == "clean") Assert.AreEqual(0, r.Fixes.Count, where + " 正常なセーブに修復が入った");
            }
            Assert.GreaterOrEqual(n, 700);
            Assert.Greater(fatal, 100);
            Assert.Greater(repaired, 100);
            foreach (string k in new[] { "I-01", "I-04", "I-05", "I-08", "I-09", "I-12", "I-13", "I-14", "I-16", "I-18", "I-19", "I-20", "I-21", "counters" })
                Assert.IsTrue(kinds.ContainsKey(k), "修復 " + k + " の例が含まれていない");
            TestContext.Out.WriteLine("検証と修復: " + n + " 件(致命 " + fatal + "・修復 " + repaired + "・変化なし " + clean + ")が参照モデルと一致 / 内訳 " + string.Join(", ", kinds.OrderBy(x => x.Key).Select(x => x.Key + " " + x.Value)));
        }

        private static SyncRecord Rec(JsonValue v, bool device)
        {
            var o = v as JsonObject;
            if (o == null) return null;
            return new SyncRecord
            {
                LineageId = ((JsonNumber)o["lineage"]).Value.ToString(), Counter = (long)((JsonNumber)o["counter"]).Value, IsTombstone = o["node"].IsNull,
                LastSynced = device ? (long)((JsonNumber)o["last_synced"]).Value : 0,
            };
        }

        [Test]
        public void TeamSaves_BuiltByTheReference_RestoreAndCaptureBackToTheSameJson()
        {
            // 参照モデル(Python)が書いた自校の保存(検証器を修復なしで通る形)を、C# が復元 → 取り出しで、鍵の順も含めて同じ JSON に戻す(09 §5.6)
            int n = 0, runners = 0, obs = 0;
            foreach (JsonValue tv in (JsonArray)_root["teams"])
            {
                var doc = (JsonObject)tv;
                var team = TeamSnapshot.Restore(doc);
                JsonObject back = TeamSnapshot.Capture(team);
                Assert.AreEqual(doc.Keys, back.Keys, "保存 " + n + ": 鍵の順");
                foreach (string k in doc.Keys)
                    Assert.IsTrue(doc[k].StructEquals(back[k]), "保存 " + n + ": " + k + "\n参照: " + Cut(doc[k].ToJson()) + "\nC#  : " + Cut(back[k].ToJson()));
                Assert.AreEqual(doc.ToJson().Length > 0, true);
                // C# の検証器も、同じ文書を修復なしで通す
                JsonObject stub = (JsonObject)doc.DeepClone();
                var school = new JsonObject();
                school["id"] = new JsonNumber(0L);
                school["is_player"] = JsonBool.True;
                school["roster"] = new JsonArray(((JsonObject)doc["runners"]).Keys.Select(x => (JsonValue)new JsonNumber(long.Parse(x))));
                stub["schools"] = new JsonArray(new JsonValue[] { school });
                stub["rival_runners"] = new JsonArray();
                ValidationResult v = SaveValidator.ValidateAndRepair(stub);
                Assert.IsNull(v.Fatal);
                CollectionAssert.IsEmpty(v.Fixes, "保存 " + n);
                CollectionAssert.IsEmpty(SaveInvariants.Check(stub));
                runners += ((JsonObject)doc["runners"]).Count;
                obs += ((JsonArray)doc["ob_records"]).Count;
                n++;
            }
            Assert.AreEqual(150, n);
            TestContext.Out.WriteLine("自校の保存 " + n + " 件(選手 " + runners + " 人・OB " + obs + " 人)が参照モデルと同じ形に往復");
        }

        private static string Cut(string s) { return s.Length <= 300 ? s : s.Substring(0, 300) + "…"; }

        [Test]
        public void SyncDecide_MatchesReference()
        {
            var counts = new Dictionary<string, int>();
            int n = 0;
            foreach (JsonValue cv in (JsonArray)_root["sync"])
            {
                var c = (JsonObject)cv;
                SyncAction got = SyncDecider.Decide(Rec(c["L"], true), Rec(c["C"], false));
                string want = ((JsonString)c["action"]).Value;
                Assert.AreEqual(want, got.ToString().ToLowerInvariant(), "case " + n + " " + c.ToJson());
                counts[want] = counts.ContainsKey(want) ? counts[want] + 1 : 1;
                n++;
            }
            Assert.AreEqual(6000, n);
            foreach (string k in new[] { "noop", "upload", "download", "conflict" }) Assert.Greater(counts[k], 100, k);
        }

        [Test]
        public void ProfileMerge_MatchesReference()
        {
            int n = 0;
            foreach (JsonValue cv in (JsonArray)_root["merges"])
            {
                var c = (JsonObject)cv;
                ProfileData a = ProfileData.FromJson((JsonObject)c["a"]), b = ProfileData.FromJson((JsonObject)c["b"]);
                string d = TestJson.Diff(c["merged"], ProfileData.Merge(a, b).ToJson());
                Assert.IsNull(d, "case " + n + ": " + d);
                n++;
            }
            Assert.AreEqual(3000, n);
        }
    }
}
