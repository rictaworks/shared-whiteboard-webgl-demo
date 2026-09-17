using System.Collections.Generic;
using UnityEngine;

namespace Whiteboard.Drawing
{
    /// <summary>
    /// requirements.md 16章。キャンバスは無限、カメラは正射影。ビューポートは参加者ごとに独立。
    /// ズームは0.25〜4倍、中心はポインタ位置。「原点へ戻る」「全体を表示する」を提供する。
    /// </summary>
    public class CameraController
    {
        public const float MinZoom = 0.25f;
        public const float MaxZoom = 4f;

        public Vector2 Center = Vector2.zero;
        public float Zoom = 1f;

        /// <summary>画面（キャンバス）サイズ（px）。ToWorld/ZoomAtの基準に用いる。</summary>
        public Vector2 ViewportSizePx = new Vector2(1024f, 768f);

        /// <summary>zoom=1のときの world:screen 比率（1 world unit が何 px に相当するか）。</summary>
        public float PixelsPerUnitAtZoom1 = 1f;

        private float UnitsPerPixel => 1f / Mathf.Max(PixelsPerUnitAtZoom1 * Zoom, 0.0001f);

        public Vector2 ToWorld(Vector2 screenPoint)
        {
            Vector2 centered = screenPoint - ViewportSizePx * 0.5f;
            // スクリーンYは下方向が正、ワールドYは上方向が正。
            Vector2 flipped = new Vector2(centered.x, -centered.y);
            return Center + flipped * UnitsPerPixel;
        }

        public void Pan(Vector2 screenDelta)
        {
            Vector2 flipped = new Vector2(-screenDelta.x, screenDelta.y);
            Center += flipped * UnitsPerPixel;
        }

        public void ZoomAt(Vector2 screenPoint, float factor)
        {
            Vector2 worldBefore = ToWorld(screenPoint);
            Zoom = Mathf.Clamp(Zoom * factor, MinZoom, MaxZoom);
            Vector2 worldAfter = ToWorld(screenPoint);
            Center += worldBefore - worldAfter;
        }

        public void FitAll(IEnumerable<Stroke> strokes)
        {
            bool any = false;
            Vector2 min = Vector2.zero;
            Vector2 max = Vector2.zero;

            foreach (var stroke in strokes)
            {
                if (stroke?.Points == null)
                {
                    continue;
                }
                foreach (var p in stroke.Points)
                {
                    if (!any)
                    {
                        min = p;
                        max = p;
                        any = true;
                    }
                    else
                    {
                        min = Vector2.Min(min, p);
                        max = Vector2.Max(max, p);
                    }
                }
            }

            if (!any)
            {
                Reset();
                return;
            }

            Center = (min + max) * 0.5f;
            Vector2 size = max - min;
            const float margin = 1.2f;

            float w = Mathf.Max(ViewportSizePx.x, 1f);
            float h = Mathf.Max(ViewportSizePx.y, 1f);
            float neededUnitsPerPixelX = (size.x * margin) / w;
            float neededUnitsPerPixelY = (size.y * margin) / h;
            float neededUnitsPerPixel = Mathf.Max(Mathf.Max(neededUnitsPerPixelX, neededUnitsPerPixelY), 0.0001f);

            float zoom = 1f / Mathf.Max(neededUnitsPerPixel * PixelsPerUnitAtZoom1, 0.0001f);
            Zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
        }

        public void Reset()
        {
            Center = Vector2.zero;
            Zoom = 1f;
        }
    }
}
