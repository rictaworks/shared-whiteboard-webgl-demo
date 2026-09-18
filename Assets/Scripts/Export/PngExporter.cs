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
        private Camera _bakeCamera;
        private GameObject _bakeMeshGo;
        private MeshFilter _bakeMeshFilter;
        private MeshRenderer _bakeMeshRenderer;

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

            EnsureMaterial();
            EnsureBakeCamera();
            if (_material != null)
            {
                // GL.PushMatrix+Graphics.DrawMeshNowの即時モード描画はWebGL(OpenGLES3)実機では
                // 描き込まれない（Unity Editor(D3D11)では正しく描画されるがWebGLビルドでは
                // 何も表示されない。本番相当環境で実機確認済み・LayerCompositor.DrawMeshIntoと
                // 同型の不具合）。Camera.Render()を使う通常の描画経路に置き換える。
                var center = (min + max) * 0.5f;
                _bakeCamera.transform.position = new Vector3(center.x, center.y, -1f);
                _bakeCamera.orthographicSize = Mathf.Max((max.y - min.y) * 0.5f, 0.01f);
                _bakeCamera.aspect = (max.x - min.x) / Mathf.Max(max.y - min.y, 0.0001f);
                _bakeCamera.targetTexture = rt;
                _bakeCamera.backgroundColor = Color.white;

                // 1回目は白背景でクリアするだけ（メッシュは無しの状態でRenderする）。
                _bakeCamera.clearFlags = CameraClearFlags.SolidColor;
                _bakeMeshRenderer.enabled = false;
                _bakeCamera.Render();
                _bakeMeshRenderer.enabled = true;

                foreach (var s in strokes)
                {
                    float widthPx = MasterData.WidthToPixels(s.Width);
                    var mesh = RibbonMeshBuilder.Build(s.Points, widthPx);
                    float opacity = MasterData.OpacityFor(s.Tool);
                    _material.color = ColorUtil.HexToColor(s.Color, opacity);
                    _bakeMeshFilter.sharedMesh = mesh;
                    _bakeMeshRenderer.sharedMaterial = _material;
                    _bakeCamera.clearFlags = CameraClearFlags.Nothing;
                    _bakeCamera.Render();
                }

                _bakeCamera.targetTexture = null;
            }

            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
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

        // 表示用クアウド（Boot.CreateLayerQuad）はDefault layer(0)にいる。bakeカメラの
        // cullingMaskをDefaultにすると表示用クアウドが書き出しPNGに混入しうるため
        // （PR #6レビューで指摘。LayerCompositorと同じ対応）、未使用のlayer 8を占有する。
        private const int BakeOnlyLayer = 8;

        private void EnsureBakeCamera()
        {
            if (_bakeCamera != null)
            {
                return;
            }

            _bakeMeshGo = new GameObject("WhiteboardExportBakeMesh") { hideFlags = HideFlags.HideAndDontSave, layer = BakeOnlyLayer };
            _bakeMeshFilter = _bakeMeshGo.AddComponent<MeshFilter>();
            _bakeMeshRenderer = _bakeMeshGo.AddComponent<MeshRenderer>();
            _bakeMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _bakeMeshRenderer.receiveShadows = false;
            _bakeMeshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            var camGo = new GameObject("WhiteboardExportBakeCamera") { hideFlags = HideFlags.HideAndDontSave };
            _bakeCamera = camGo.AddComponent<Camera>();
            _bakeCamera.enabled = false;
            _bakeCamera.orthographic = true;
            _bakeCamera.nearClipPlane = 0.1f;
            _bakeCamera.farClipPlane = 10f;
            _bakeCamera.cullingMask = 1 << BakeOnlyLayer;
        }
    }
}
