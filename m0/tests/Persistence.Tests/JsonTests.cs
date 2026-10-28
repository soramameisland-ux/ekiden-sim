using System;
using System.Linq;
using Ekiden.Persistence.Json;
using NUnit.Framework;

namespace Ekiden.Persistence.Tests
{
    public class JsonTests
    {
        [Test]
        public void RoundTrip_PreservesOrder_AndLexicalNumbers()
        {
            const string src = "{\"b\":1,\"a\":[true,false,null,\"x\"],\"n\":1.50,\"e\":1e-07,\"big\":12345678901234567890,\"neg\":-0,\"s\":\"日本語\\n\\\"\\\\\"}";
            JsonValue v = JsonValue.Parse(src);
            Assert.AreEqual(src, v.ToJson(), "読んだ数は元の文字列のまま書き戻す");
            CollectionAssert.AreEqual(new[] { "b", "a", "n", "e", "big", "neg", "s" }, ((JsonObject)v).Keys.ToArray());
        }

        [Test]
        public void Writer_NewNumbers_AreIntegersOrQ6()
        {
            Assert.AreEqual("12", new JsonNumber(12L).ToJson());
            Assert.AreEqual("0.5", new JsonNumber(0.5).ToJson());
            Assert.AreEqual("12.345679", new JsonNumber(12.3456789).ToJson());
            Assert.AreEqual("0", new JsonNumber(-0.0).ToJson());
            Assert.AreEqual("0.000001", new JsonNumber(Ekiden.Domain.Numerics.Q6.Round(5e-07)).ToJson(), "書く前に Q6 を通すのは Domain の側(09 §5.3)");
            Assert.AreEqual("3", new JsonNumber(3.0).ToJson());
            var o = new JsonObject();
            o["z"] = new JsonNumber(1L);
            o["a"] = new JsonNumber(0.25);
            Assert.AreEqual("{\"z\":1,\"a\":0.25}", o.ToJson(), "キーは入れた順(宣言順)");
        }

        [TestCase("")]
        [TestCase("{")]
        [TestCase("{\"a\":}")]
        [TestCase("[1,2,]")]
        [TestCase("{\"a\" 1}")]
        [TestCase("01")]
        [TestCase("1.")]
        [TestCase("-")]
        [TestCase("tru")]
        [TestCase("\"abc")]
        [TestCase("\"a\\qb\"")]
        [TestCase("\"\\u12\"")]
        [TestCase("[1] x")]
        [TestCase("{'a':1}")]
        public void Parser_Rejects_Malformed(string src)
        {
            Assert.Throws<JsonException>(() => JsonValue.Parse(src));
        }

        [Test]
        public void Parser_Rejects_TooDeep_AndControlChars()
        {
            string deep = new string('[', 100) + new string(']', 100);
            Assert.Throws<JsonException>(() => JsonValue.Parse(deep));
            Assert.Throws<JsonException>(() => JsonValue.Parse("\"a\u0001b\""));
            Assert.DoesNotThrow(() => JsonValue.Parse(new string('[', 60) + new string(']', 60)));
        }

        [Test]
        public void Escapes_AreMinimal_AndUnicodeSurvives()
        {
            var s = new JsonString("a\u0001b\t\"\\/😀");
            Assert.AreEqual("\"a\\u0001b\\t\\\"\\\\/😀\"", s.ToJson());
            Assert.AreEqual("a\u0001b\t\"\\/😀", ((JsonString)JsonValue.Parse(s.ToJson())).Value);
            Assert.AreEqual("😀", ((JsonString)JsonValue.Parse("\"\\ud83d\\ude00\"")).Value);
        }

        [Test]
        public void DeepClone_IsIndependent_AndStructEquals()
        {
            JsonObject a = (JsonObject)JsonValue.Parse("{\"x\":[1,2,{\"y\":3}],\"z\":null}");
            JsonObject b = (JsonObject)a.DeepClone();
            Assert.IsTrue(a.StructEquals(b));
            ((JsonArray)b["x"]).Add(new JsonNumber(9L));
            Assert.IsFalse(a.StructEquals(b));
            Assert.AreEqual(3, ((JsonArray)a["x"]).Count);
            Assert.IsTrue(((JsonObject)JsonValue.Parse("{\"a\":1,\"b\":2}")).SameContent((JsonObject)JsonValue.Parse("{\"b\":2,\"a\":1}")));
            Assert.IsFalse(((JsonObject)JsonValue.Parse("{\"a\":1,\"b\":2}")).StructEquals(JsonValue.Parse("{\"b\":2,\"a\":1}")), "StructEquals はキーの順も見る");
        }
    }
}
