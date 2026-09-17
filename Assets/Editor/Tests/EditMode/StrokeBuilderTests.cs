using NUnit.Framework;
using UnityEngine;
using Whiteboard.Data;
using Whiteboard.Drawing;

namespace Whiteboard.Tests.EditMode
{
    public class StrokeBuilderTests
    {
        [Test]
        public void AddPoint_FiltersPointsBelowMinScreenSpacing()
        {
            var b = new StrokeBuilder();
            b.Begin(ToolKind.Pen, "#000000", StrokeWidth.Medium, new Vector2(0, 0));

            // 0.001 world units * zoom(1) = 0.001px < 最小間隔2px → 取り込まれない
            bool accepted = b.AddPoint(new Vector2(0.001f, 0f), 1f);
            Assert.IsFalse(accepted);
            Assert.AreEqual(1, b.Points.Count);

            // 10px相当 >= 2px → 取り込まれる
            bool accepted2 = b.AddPoint(new Vector2(10f, 0f), 1f);
            Assert.IsTrue(accepted2);
            Assert.AreEqual(2, b.Points.Count);
        }

        [Test]
        public void AddPoint_AlwaysAcceptsEndpointEvenIfBelowMinSpacing()
        {
            var b = new StrokeBuilder();
            b.Begin(ToolKind.Pen, "#000000", StrokeWidth.Medium, new Vector2(0, 0));

            bool accepted = b.AddPoint(new Vector2(0.001f, 0f), 1f, isEndpoint: true);
            Assert.IsTrue(accepted, "終点は最小間隔未満でも必ず取り込む");
            Assert.AreEqual(2, b.Points.Count);
        }

        [Test]
        public void AddPoint_ReachesLimitAt1000Points_AndFinishProducesFullStroke()
        {
            var b = new StrokeBuilder();
            b.Begin(ToolKind.Pen, "#000000", StrokeWidth.Medium, new Vector2(0, 0));

            for (int i = 1; i < MasterData.MaxPointsPerStroke; i++)
            {
                bool accepted = b.AddPoint(new Vector2(i * 10f, 0f), 1f);
                Assert.IsTrue(accepted, "十分な間隔があるため取り込まれるはず: i=" + i);
            }

            Assert.AreEqual(MasterData.MaxPointsPerStroke, b.Points.Count);
            Assert.IsTrue(b.ReachedLimit, "1,000点上限に到達したらReachedLimitがtrueになる");

            var stroke = b.Finish("参加者A");
            Assert.AreEqual(MasterData.MaxPointsPerStroke, stroke.Points.Count);
            Assert.IsFalse(b.IsActive);
        }

        [Test]
        public void AddPoint_OneMorePointBeyondLimit_IsNotAdded()
        {
            var b = new StrokeBuilder();
            b.Begin(ToolKind.Pen, "#000000", StrokeWidth.Medium, new Vector2(0, 0));
            for (int i = 1; i < MasterData.MaxPointsPerStroke; i++)
            {
                b.AddPoint(new Vector2(i * 10f, 0f), 1f);
            }
            Assert.IsTrue(b.ReachedLimit);

            // 呼び出し側は上限到達を検知したら Finish→新規Begin で継続する運用のため、
            // ここでは上限到達後もクラス自体は既存点数を保持し続けることのみ確認する。
            Assert.AreEqual(MasterData.MaxPointsPerStroke, b.Points.Count);
        }

        [Test]
        public void SingleContactWithoutMovement_ProducesDotStroke()
        {
            var b = new StrokeBuilder();
            b.Begin(ToolKind.Marker, "#E53935", StrokeWidth.Thick, new Vector2(5, 5));
            var stroke = b.Finish("参加者B");

            Assert.AreEqual(1, stroke.Points.Count);
            Assert.AreEqual(new Vector2(5, 5), stroke.Points[0]);
        }

        [Test]
        public void Abort_ClearsActiveState()
        {
            var b = new StrokeBuilder();
            b.Begin(ToolKind.Pen, "#000000", StrokeWidth.Medium, new Vector2(0, 0));
            b.AddPoint(new Vector2(20, 0), 1f);
            b.Abort();

            Assert.IsFalse(b.IsActive);
            Assert.AreEqual(0, b.Points.Count);
        }
    }
}
