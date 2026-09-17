using System.Collections.Generic;
using UnityEngine;
using Whiteboard.Bridges;
using Whiteboard.Data;
using Whiteboard.Drawing;

namespace Whiteboard.Export
{
    /// <summary>
    /// requirements.md 18章。表示用と別の描画面を生成し、全ストロークの外接矩形+余白・
    /// 白背景で書き出す。取消フラグの立っている操作・消去済みのストローク・進行中ストロークは
    /// 呼び出し側（BoardState.VisibleStrokes()）で既に除外されていることを前提とする。
    /// </summary>
    public class PngExporter
    {
        private const float MarginWorldUnits = 40f;
        private const float PixelsPerWorldUnit = 2f;
        private const int MaxDimension = 4096;

        private Material _material;

        public byte[] Export(IEnumerable<Stroke> visibleStrokes)
        {
            var strokes = new List<Stroke>();
            foreach (var s in visibleStrokes)
            {
                if (s?.Points != null && s.Points.Count > 0)
                {
                    strokes.Add(s);
                }
            }

            if (strokes.Count == 0)
            {
                return EncodeWhite(64, 64);
            }

            Vector2 min = strokes[0].Points[0];
            Vector2 max = min;
            foreach (var s in strokes)
            {
                foreach (var p in s.Points)
                {
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
            }
            min -= new Vector2(MarginWorldUnits, MarginWorldUnits);
            max += new Vector2(MarginWorldUnits, MarginWorldUnits);

            Vector2 size = max - min;
            int width = Mathf.Clamp(Mathf.CeilToInt(size.x * PixelsPerWorldUnit), 1, MaxDimension);
            int height = Mathf.Clamp(Mathf.CeilToInt(size.y * PixelsPerWorldUnit), 1, MaxDimension);

            var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            rt.Create();
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.white);

            EnsureMaterial();
            if (_material != null)
            {
                GL.PushMatrix();
                var proj = Matrix4x4.Ortho(min.x, max.x, min.y, max.y, -1f, 1f);
                GL.LoadProjectionMatrix(proj);
                GL.modelview = Matrix4x4.identity;

                foreach (var s in strokes)
                {
                    float widthPx = MasterData.WidthToPixels(s.Width);
                    var mesh = RibbonMeshBuilder.Build(s.Points, widthPx);
                    float opacity = MasterData.OpacityFor(s.Tool);
                    _material.color = ColorUtil.HexToColor(s.Color, opacity);
                    _material.SetPass(0);
                    Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
                }

                GL.PopMatrix();
            }

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            RenderTexture.active = prevActive;
            rt.Release();

            byte[] png = tex.EncodeToPNG();
            Object.Destroy(tex);
            return png;
        }

        public void ExportAndDownload(IEnumerable<Stroke> visibleStrokes, string filename)
        {
            byte[] bytes = Export(visibleStrokes);
            EnvBridge.Download(bytes, filename);
        }

        private static byte[] EncodeWhite(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color32[w * h];
            var white = new Color32(255, 255, 255, 255);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = white;
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Object.Destroy(tex);
            return png;
        }

        private void EnsureMaterial()
        {
            if (_material != null)
            {
                return;
            }
            // UI/Default を使う（Hidden/Internal-Coloredではない）：uGUIが実際に参照するため
            // WebGLビルドのシェーダ剥ぎ取り対象にならない。理由はLayerCompositor.csを参照。
            var shader = Shader.Find("UI/Default");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/Internal-Colored");
            }
            if (shader != null)
            {
                _material = new Material(shader);
            }
        }
    }
}
