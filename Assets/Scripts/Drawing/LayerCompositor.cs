using System.Collections.Generic;
using UnityEngine;
using Whiteboard.Data;

namespace Whiteboard.Drawing
{
    /// <summary>
    /// requirements.md 10章。確定済み層・進行中層（自分・他者）の合成を担う。
    /// 固定サイズの世界座標領域（WorldHalfExtent）を対象にRenderTextureへ焼き込み、
    /// 表示側（Boot/UiBuilderが生成するワールド空間クアッド）はこのテクスチャを
    /// そのまま貼るだけでよく、カメラのパン・ズームには追加のUV計算を要しない
    /// （クアッド自体を固定領域として世界に配置し、通常のカメラ描画に委ねるため）。
    /// マーカーの半透明合成は確定済み層への焼き込み時に1回だけ適用する（9章）。
    /// </summary>
    public class LayerCompositor
    {
        private RenderTexture _committed;
        private RenderTexture _localActive;
        private RenderTexture _remoteActive;

        public RenderTexture Committed => _committed;
        public RenderTexture LocalActive => _localActive;
        public RenderTexture RemoteActive => _remoteActive;

        public Vector2 WorldHalfExtent { get; }

        private Material _material;
        private int _baseResolution;
        private float _pixelRatio = 1f;

        // 他者の進行中ストロークは複数人が同時に描く可能性があるため、
        // strokeId ごとに累積点列を保持し、DropRemoteActive時のみ全体を再描画する。
        private readonly Dictionary<string, RemoteActiveEntry> _remoteActiveStrokes = new Dictionary<string, RemoteActiveEntry>();

        private class RemoteActiveEntry
        {
            public List<Vector2> Points = new List<Vector2>();
            public string Color;
            public ToolKind Tool;
            public StrokeWidth Width;
        }

        public LayerCompositor(float worldHalfExtent = 3000f, int baseResolution = 2048)
        {
            WorldHalfExtent = new Vector2(worldHalfExtent, worldHalfExtent);
            _baseResolution = baseResolution;
            EnsureMaterial();
            Resize(new Vector2Int(baseResolution, baseResolution), 1f);
        }

        private void EnsureMaterial()
        {
            if (_material != null)
            {
                return;
            }
            // UI/Default を使う（Hidden/Internal-Coloredではない）：本プロジェクトのuGUI
            // Image/Text コンポーネントが実際に参照するため、WebGLビルドのシェーダ剥ぎ取り
            // 対象にならず、GraphicsSettingsのAlways Included Shadersを変更する必要がない。
            // （このマシンのheadless WebGLビルドでは、Always Included Shadersへの追加が
            // unity_builtin_extra書き込み時のアサーション失敗 'm_LockCount == 0' を
            // 引き起こすことを実機確認したため、この方式を採る。）
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

        /// <summary>端末のピクセル比を反映し、描画面はブラウザウィンドウのサイズ変更に追随する。</summary>
        public void Resize(Vector2Int screenSize, float pixelRatio)
        {
            _pixelRatio = Mathf.Max(pixelRatio, 0.5f);
            int resolution = _baseResolution;

            ReleaseIfNeeded(ref _committed);
            ReleaseIfNeeded(ref _localActive);
            ReleaseIfNeeded(ref _remoteActive);

            _committed = CreateLayerTexture(resolution);
            _localActive = CreateLayerTexture(resolution);
            _remoteActive = CreateLayerTexture(resolution);
        }

        private RenderTexture CreateLayerTexture(int resolution)
        {
            var rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32)
            {
                name = "WhiteboardLayer",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            rt.Create();
            ClearTexture(rt);
            return rt;
        }

        private static void ReleaseIfNeeded(ref RenderTexture rt)
        {
            if (rt != null)
            {
                rt.Release();
                rt = null;
            }
        }

        private void ClearTexture(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, new Color(0, 0, 0, 0));
            RenderTexture.active = prev;
        }

        /// <summary>自分の進行中ストロークを毎フレーム描き直す。</summary>
        public void DrawLocalActive(Mesh mesh, string colorHex)
        {
            if (LocalActive == null || mesh == null)
            {
                return;
            }
            ClearTexture(LocalActive);
            DrawMeshInto(LocalActive, mesh, ColorUtil.HexToColor(colorHex, MasterData.PenOpacity));
        }

        public void ClearLocalActive()
        {
            if (LocalActive != null)
            {
                ClearTexture(LocalActive);
            }
        }

        /// <summary>他者の進行中ストローク：受信した差分の点だけを進行中層へ追加描画する。</summary>
        public void AppendRemoteDelta(string strokeId, IReadOnlyList<Vector2> deltaPoints, string colorHex, ToolKind tool, StrokeWidth width)
        {
            if (RemoteActive == null || deltaPoints == null || deltaPoints.Count == 0)
            {
                return;
            }

            if (!_remoteActiveStrokes.TryGetValue(strokeId, out var entry))
            {
                entry = new RemoteActiveEntry { Color = colorHex, Tool = tool, Width = width };
                _remoteActiveStrokes[strokeId] = entry;
            }

            // 直近点から新規点列へつながる差分区間だけをメッシュ化して追加描画する
            // （全点を毎フレーム描き直さない）。
            var segment = new List<Vector2>();
            if (entry.Points.Count > 0)
            {
                segment.Add(entry.Points[entry.Points.Count - 1]);
            }
            segment.AddRange(deltaPoints);
            entry.Points.AddRange(deltaPoints);

            if (segment.Count >= 1)
            {
                float widthPx = MasterData.WidthToPixels(width);
                var mesh = RibbonMeshBuilder.Build(segment, widthPx);
                DrawMeshInto(RemoteActive, mesh, ColorUtil.HexToColor(colorHex, MasterData.PenOpacity));
            }
        }

        /// <summary>進行中破棄・発信者の離脱：残っている他者の進行中ストロークのみで全体を再描画する。</summary>
        public void DropRemoteActive(string strokeId)
        {
            _remoteActiveStrokes.Remove(strokeId);
            RedrawAllRemoteActive();
        }

        private void RedrawAllRemoteActive()
        {
            if (RemoteActive == null)
            {
                return;
            }
            ClearTexture(RemoteActive);
            foreach (var kv in _remoteActiveStrokes)
            {
                var entry = kv.Value;
                if (entry.Points.Count == 0)
                {
                    continue;
                }
                float widthPx = MasterData.WidthToPixels(entry.Width);
                var mesh = RibbonMeshBuilder.Build(entry.Points, widthPx);
                DrawMeshInto(RemoteActive, mesh, ColorUtil.HexToColor(entry.Color, MasterData.PenOpacity));
            }
        }

        /// <summary>確定済み層への焼き込み。マーカーの半透明合成はここで1回だけ適用する。</summary>
        public void Bake(Mesh mesh, string colorHex, float opacity)
        {
            if (Committed == null || mesh == null)
            {
                return;
            }
            DrawMeshInto(Committed, mesh, ColorUtil.HexToColor(colorHex, opacity));
        }

        /// <summary>Undo・Redo・消去等で確定済みの内容が変わる場合、操作ログから確定済み層を再構成する。</summary>
        public void Rebuild(IEnumerable<Drawing.Stroke> visibleStrokes)
        {
            if (Committed == null)
            {
                return;
            }
            ClearTexture(Committed);
            foreach (var stroke in visibleStrokes)
            {
                if (stroke.Points == null || stroke.Points.Count == 0)
                {
                    continue;
                }
                float widthPx = MasterData.WidthToPixels(stroke.Width);
                var mesh = RibbonMeshBuilder.Build(stroke.Points, widthPx);
                float opacity = MasterData.OpacityFor(stroke.Tool);
                DrawMeshInto(Committed, mesh, ColorUtil.HexToColor(stroke.Color, opacity));
            }
        }

        private void DrawMeshInto(RenderTexture rt, Mesh mesh, Color color)
        {
            if (_material == null)
            {
                EnsureMaterial();
                if (_material == null)
                {
                    return;
                }
            }

            var prev = RenderTexture.active;
            RenderTexture.active = rt;

            GL.PushMatrix();
            Matrix4x4 proj = Matrix4x4.Ortho(-WorldHalfExtent.x, WorldHalfExtent.x, -WorldHalfExtent.y, WorldHalfExtent.y, -1f, 1f);
            GL.LoadProjectionMatrix(proj);
            GL.modelview = Matrix4x4.identity;

            _material.color = color;
            _material.SetPass(0);
            Graphics.DrawMeshNow(mesh, Matrix4x4.identity);

            GL.PopMatrix();
            RenderTexture.active = prev;
        }
    }
}
