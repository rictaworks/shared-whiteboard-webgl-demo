using System.Collections.Generic;
using UnityEngine;

namespace Whiteboard.Drawing
{
    /// <summary>
    /// requirements.md 10章。点列からリボン状メッシュを生成する。
    /// 曲がり角は丸ジョイン、両端は丸キャップ。表示は隣接点の中点を端点とする
    /// 二次曲線で結ぶが、保存・送信する点列（呼び出し元が保持する raw な点列）は
    /// このクラスの入力・出力に影響しない（このクラスは表示用メッシュの生成のみを担う）。
    /// </summary>
    public static class RibbonMeshBuilder
    {
        private const int CircleSegments = 10;
        private const int SamplesPerCurveSegment = 6;

        public static Mesh Build(IReadOnlyList<Vector2> rawPoints, float width)
        {
            var mesh = new Mesh();
            if (rawPoints == null || rawPoints.Count == 0)
            {
                return mesh;
            }

            float radius = Mathf.Max(width * 0.5f, 0.001f);
            var path = Smooth(rawPoints);

            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            if (path.Count == 1)
            {
                AppendCircleFan(vertices, triangles, path[0], radius);
                ApplyToMesh(mesh, vertices, triangles);
                return mesh;
            }

            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2 a = path[i];
                Vector2 b = path[i + 1];
                Vector2 dir = (b - a);
                if (dir.sqrMagnitude < 1e-10f)
                {
                    continue;
                }
                dir.Normalize();
                Vector2 normal = new Vector2(-dir.y, dir.x) * radius;

                int baseIdx = vertices.Count;
                vertices.Add(a - normal);
                vertices.Add(a + normal);
                vertices.Add(b - normal);
                vertices.Add(b + normal);

                triangles.Add(baseIdx + 0);
                triangles.Add(baseIdx + 1);
                triangles.Add(baseIdx + 2);
                triangles.Add(baseIdx + 2);
                triangles.Add(baseIdx + 1);
                triangles.Add(baseIdx + 3);
            }

            // 丸ジョイン・丸キャップ：経路上の全点に円のファンを重ねることで
            // 折れ角・両端の隙間を確実に埋める（デモ品質のため簡潔さを優先）。
            foreach (var p in path)
            {
                AppendCircleFan(vertices, triangles, p, radius);
            }

            ApplyToMesh(mesh, vertices, triangles);
            return mesh;
        }

        private static void ApplyToMesh(Mesh mesh, List<Vector3> vertices, List<int> triangles)
        {
            if (vertices.Count > 0)
            {
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
            }
        }

        private static void AppendCircleFan(List<Vector3> vertices, List<int> triangles, Vector2 center, float radius)
        {
            int baseIdx = vertices.Count;
            vertices.Add(center);
            for (int s = 0; s <= CircleSegments; s++)
            {
                float angle = (s / (float)CircleSegments) * Mathf.PI * 2f;
                vertices.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
            for (int s = 0; s < CircleSegments; s++)
            {
                triangles.Add(baseIdx);
                triangles.Add(baseIdx + 1 + s);
                triangles.Add(baseIdx + 2 + s);
            }
        }

        /// <summary>
        /// 隣接点の中点を端点とする二次曲線（Bezier）で結んだサンプル点列を生成する。
        /// 保存・送信する点列（入力 rawPoints）はこの処理の影響を受けない。
        /// </summary>
        public static List<Vector2> Smooth(IReadOnlyList<Vector2> points)
        {
            var result = new List<Vector2>();
            int n = points.Count;
            if (n == 0)
            {
                return result;
            }
            if (n <= 2)
            {
                result.AddRange(points);
                return result;
            }

            result.Add(points[0]);
            for (int i = 0; i < n - 1; i++)
            {
                Vector2 p0 = i == 0 ? points[0] : (points[i - 1] + points[i]) * 0.5f;
                Vector2 p1 = points[i];
                Vector2 p2 = i == n - 2 ? points[i + 1] : (points[i] + points[i + 1]) * 0.5f;

                for (int s = 1; s <= SamplesPerCurveSegment; s++)
                {
                    float t = s / (float)SamplesPerCurveSegment;
                    Vector2 a = Vector2.Lerp(p0, p1, t);
                    Vector2 b = Vector2.Lerp(p1, p2, t);
                    Vector2 pt = Vector2.Lerp(a, b, t);
                    result.Add(pt);
                }
            }
            return result;
        }
    }
}
