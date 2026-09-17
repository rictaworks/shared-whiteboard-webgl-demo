using System.Collections.Generic;
using UnityEngine;
using Whiteboard.Data;

namespace Whiteboard.Drawing
{
    /// <summary>
    /// requirements.md 9章。消しゴムの当たり判定は確定済みのストロークのみを対象とし、
    /// 進行中のストローク（自分・他者）は対象としない。呼び出し側は committedStrokes に
    /// 確定済みストロークのみを渡すこと。
    /// </summary>
    public static class EraserHitTester
    {
        public static List<string> Hit(IReadOnlyList<Vector2> eraserPath, float eraserRadius, IEnumerable<Stroke> committedStrokes)
        {
            var hits = new List<string>();
            if (eraserPath == null || eraserPath.Count == 0 || committedStrokes == null)
            {
                return hits;
            }

            foreach (var stroke in committedStrokes)
            {
                if (stroke == null || stroke.Points == null || stroke.Points.Count == 0)
                {
                    continue;
                }

                float strokeRadius = MasterData.WidthToPixels(stroke.Width) * 0.5f;
                float combined = strokeRadius + eraserRadius;

                if (IntersectsPath(eraserPath, stroke.Points, combined))
                {
                    hits.Add(stroke.Id);
                }
            }

            return hits;
        }

        private static bool IntersectsPath(IReadOnlyList<Vector2> pathA, IReadOnlyList<Vector2> pathB, float maxDist)
        {
            var segmentsA = ToSegments(pathA);
            var segmentsB = ToSegments(pathB);

            foreach (var a in segmentsA)
            {
                foreach (var b in segmentsB)
                {
                    if (SegmentsWithinDistance(a.Item1, a.Item2, b.Item1, b.Item2, maxDist))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static List<(Vector2, Vector2)> ToSegments(IReadOnlyList<Vector2> points)
        {
            var segments = new List<(Vector2, Vector2)>();
            if (points.Count == 1)
            {
                segments.Add((points[0], points[0]));
                return segments;
            }
            for (int i = 0; i < points.Count - 1; i++)
            {
                segments.Add((points[i], points[i + 1]));
            }
            return segments;
        }

        private static bool SegmentsWithinDistance(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2, float maxDist)
        {
            return DistanceSegmentToSegment(a1, a2, b1, b2) <= maxDist;
        }

        /// <summary>
        /// 2つの線分間の最短距離（2D）。端点同士の距離だけでは、端点から離れた場所で
        /// 交差・接近する線分（十字に交わる等）を見逃すため、標準的な線分-線分間最短距離の
        /// 算出（クランプ付きパラメトリック解法）で正確に判定する。
        /// </summary>
        private static float DistanceSegmentToSegment(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
        {
            const float eps = 1e-8f;
            Vector2 d1 = q1 - p1;
            Vector2 d2 = q2 - p2;
            Vector2 r = p1 - p2;
            float a = Vector2.Dot(d1, d1);
            float e = Vector2.Dot(d2, d2);
            float f = Vector2.Dot(d2, r);

            float s, t;

            if (a <= eps && e <= eps)
            {
                return Vector2.Distance(p1, p2);
            }

            if (a <= eps)
            {
                s = 0f;
                t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector2.Dot(d1, r);
                if (e <= eps)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector2.Dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom > eps ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;

                    if (t < 0f)
                    {
                        t = 0f;
                        s = Mathf.Clamp01(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = Mathf.Clamp01((b - c) / a);
                    }
                }
            }

            Vector2 closest1 = p1 + d1 * s;
            Vector2 closest2 = p2 + d2 * t;
            return Vector2.Distance(closest1, closest2);
        }
    }
}
