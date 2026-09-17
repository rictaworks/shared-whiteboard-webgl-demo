using System.Collections.Generic;
using NUnit.Framework;
using Whiteboard.Json;

namespace Whiteboard.Tests.EditMode
{
    public class MiniJsonTests
    {
        [Test]
        public void RoundTrip_ObjectWithNestedArraysAndTypes()
        {
            var original = new Dictionary<string, object>
            {
                ["str"] = "hello日本語",
                ["num"] = 3.5,
                ["boolTrue"] = true,
                ["boolFalse"] = false,
                ["nil"] = null,
                ["arr"] = new List<object> { 1.0, "two", false },
                ["nested"] = new Dictionary<string, object> { ["a"] = 1.0 },
            };

            string json = MiniJson.Serialize(original);
            var roundTripped = MiniJson.Deserialize(json) as Dictionary<string, object>;

            Assert.IsNotNull(roundTripped);
            Assert.AreEqual("hello日本語", roundTripped["str"]);
            Assert.AreEqual(3.5, (double)roundTripped["num"], 1e-9);
            Assert.AreEqual(true, roundTripped["boolTrue"]);
            Assert.AreEqual(false, roundTripped["boolFalse"]);
            Assert.IsNull(roundTripped["nil"]);

            var arr = roundTripped["arr"] as List<object>;
            Assert.IsNotNull(arr);
            Assert.AreEqual(3, arr.Count);
            Assert.AreEqual(1.0, (double)arr[0]);
            Assert.AreEqual("two", arr[1]);
            Assert.AreEqual(false, arr[2]);

            var nested = roundTripped["nested"] as Dictionary<string, object>;
            Assert.IsNotNull(nested);
            Assert.AreEqual(1.0, (double)nested["a"]);
        }

        [Test]
        public void Deserialize_EscapedCharacters()
        {
            string json = "{\"text\":\"line1\\nline2\\t\\\"quoted\\\"\"}";
            var d = MiniJson.Deserialize(json) as Dictionary<string, object>;
            Assert.AreEqual("line1\nline2\t\"quoted\"", d["text"]);
        }

        [Test]
        public void Deserialize_EmptyArrayAndObject()
        {
            Assert.AreEqual(0, ((List<object>)MiniJson.Deserialize("[]")).Count);
            Assert.AreEqual(0, ((Dictionary<string, object>)MiniJson.Deserialize("{}")).Count);
        }

        [Test]
        public void Serialize_EscapesSpecialCharacters()
        {
            var d = new Dictionary<string, object> { ["k"] = "a\"b\\c\nd" };
            string json = MiniJson.Serialize(d);
            var back = MiniJson.Deserialize(json) as Dictionary<string, object>;
            Assert.AreEqual("a\"b\\c\nd", back["k"]);
        }

        [Test]
        public void RoundTrip_ArrayOfPointsShapeUsedByContract()
        {
            // INTEGRATION_CONTRACT.md の points: [[x,y], ...] 形状を想定した往復確認。
            var points = new List<object>
            {
                new List<object> { 1.0, 2.0 },
                new List<object> { 3.5, -4.25 },
            };
            string json = MiniJson.Serialize(points);
            var back = MiniJson.Deserialize(json) as List<object>;

            Assert.AreEqual(2, back.Count);
            var p0 = back[0] as List<object>;
            Assert.AreEqual(1.0, (double)p0[0]);
            Assert.AreEqual(2.0, (double)p0[1]);
        }
    }
}
