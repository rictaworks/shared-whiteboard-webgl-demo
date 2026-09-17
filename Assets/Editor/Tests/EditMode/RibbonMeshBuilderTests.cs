using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Whiteboard.Drawing;

namespace Whiteboard.Tests.EditMode
{
    public class RibbonMeshBuilderTests
    {
        [Test]
        public void Build_WithMultiplePoints_ProducesNonEmptyValidMesh()
        {
            var points = new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10) };
            var mesh = RibbonMeshBuilder.Build(points, 8f);

            Assert.Greater(mesh.vertexCount, 0, "頂点数は0より大きいこと");
            Assert.Greater(mesh.triangles.Length, 0, "三角形インデックスが存在すること");
            Assert.AreEqual(0, mesh.triangles.Length % 3, "三角形インデックス数は3の倍数であること");
        }

        [Test]
        public void Build_WithSinglePoint_ProducesDotMesh()
        {
            var points = new List<Vector2> { new Vector2(3, 4) };
            var mesh = RibbonMeshBuilder.Build(points, 8f);

            Assert.Greater(mesh.vertexCount, 0, "1点のみでもドット用の円メッシュを生成すること");
        }

        [Test]
        public void Build_WithEmptyPoints_ProducesEmptyMesh()
        {
            var mesh = RibbonMeshBuilder.Build(new List<Vector2>(), 8f);
            Assert.AreEqual(0, mesh.vertexCount);
        }

        [Test]
        public void Build_WithNullPoints_DoesNotThrowAndProducesEmptyMesh()
        {
            Mesh mesh = null;
            Assert.DoesNotThrow(() => mesh = RibbonMeshBuilder.Build(null, 8f));
            Assert.AreEqual(0, mesh.vertexCount);
        }

        [Test]
        public void Smooth_PassesThroughFirstAndLastRawPoints()
        {
            var points = new List<Vector2> { new Vector2(0, 0), new Vector2(5, 5), new Vector2(10, 0) };
            var smoothed = RibbonMeshBuilder.Smooth(points);

            Assert.AreEqual(points[0], smoothed[0], "始点は保たれること");
            Assert.AreEqual(points[points.Count - 1], smoothed[smoothed.Count - 1], "終点は保たれること");
            Assert.Greater(smoothed.Count, points.Count, "曲線補間によりサンプル点が増えること");
        }
    }
}
