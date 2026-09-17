using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Whiteboard.Data;
using Whiteboard.Drawing;

namespace Whiteboard.Tests.EditMode
{
    public class EraserHitTesterTests
    {
        private static Stroke MakeStroke(string id, params Vector2[] points)
        {
            return new Stroke
            {
                Id = id,
                Tool = ToolKind.Pen,
                Color = "#1A1A1A",
                Width = StrokeWidth.Medium,
                Points = new List<Vector2>(points),
                AuthorLabel = "参加者A",
            };
        }

        [Test]
        public void Hit_DetectsIntersectingStroke()
        {
            var stroke = MakeStroke("s1", new Vector2(0, 0), new Vector2(100, 0));
            var eraserPath = new List<Vector2> { new Vector2(50, -20), new Vector2(50, 20) };

            var hits = EraserHitTester.Hit(eraserPath, 5f, new List<Stroke> { stroke });

            Assert.Contains("s1", hits);
        }

        [Test]
        public void Hit_DoesNotDetectFarStroke()
        {
            var stroke = MakeStroke("s1", new Vector2(0, 0), new Vector2(100, 0));
            var eraserPath = new List<Vector2> { new Vector2(50, 500), new Vector2(60, 500) };

            var hits = EraserHitTester.Hit(eraserPath, 5f, new List<Stroke> { stroke });

            Assert.IsEmpty(hits);
        }

        [Test]
        public void Hit_IgnoresGivenStrokesOnlyByWhatIsPassedIn()
        {
            // 呼び出し側が確定済みストロークのみを渡す前提（進行中は含めない）。
            var committed = MakeStroke("committed", new Vector2(0, 0), new Vector2(10, 0));
            var eraserPath = new List<Vector2> { new Vector2(5, 0) };

            var hits = EraserHitTester.Hit(eraserPath, 5f, new List<Stroke> { committed });

            Assert.AreEqual(1, hits.Count);
            Assert.AreEqual("committed", hits[0]);
        }
    }
}
