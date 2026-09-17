using System.Collections.Generic;
using UnityEngine;
using Whiteboard.Data;

namespace Whiteboard.Drawing
{
    /// <summary>
    /// requirements.md 8.2章・10章。ワールド座標変換後の点列から進行中ストロークを構築する。
    /// - 隣接点との画面上距離が最小間隔（ズーム正規化2px）未満の点は取り込まない（終点は必ず取り込む）
    /// - 1ストローク1,000点上限。上限到達時は自動確定し、続きを新規ストロークとして開始する必要があることを呼び出し側へ知らせる
    /// - 移動を伴わない接触は点1つのドットとして確定する
    /// </summary>
    public class StrokeBuilder
    {
        private const float MinScreenSpacingPx = 2f;

        private readonly List<Vector2> _points = new List<Vector2>();
        private readonly List<Vector2> _pendingDelta = new List<Vector2>();

        public bool IsActive { get; private set; }
        public string StrokeId { get; private set; }
        public ToolKind Tool { get; private set; }
        public string Color { get; private set; }
        public StrokeWidth Width { get; private set; }
        public IReadOnlyList<Vector2> Points => _points;

        /// <summary>直前の AddPoint 呼び出しで点数上限に到達したかどうか。</summary>
        public bool ReachedLimit { get; private set; }

        public void Begin(ToolKind tool, string color, StrokeWidth width, Vector2 firstPointWorld, string strokeId = null)
        {
            Tool = tool;
            Color = color;
            Width = width;
            StrokeId = strokeId ?? Stroke.NewStrokeId();
            _points.Clear();
            _pendingDelta.Clear();
            _points.Add(firstPointWorld);
            _pendingDelta.Add(firstPointWorld);
            IsActive = true;
            ReachedLimit = false;
        }

        /// <summary>
        /// 点を追加する。最小間隔未満であれば取り込まず false を返す。
        /// zoom はスクリーン距離への正規化に用いる（画面上distance = world距離 × zoom）。
        /// </summary>
        public bool AddPoint(Vector2 worldPoint, float zoom, bool isEndpoint = false)
        {
            if (!IsActive)
            {
                return false;
            }

            if (!isEndpoint && _points.Count > 0)
            {
                float worldDist = Vector2.Distance(_points[_points.Count - 1], worldPoint);
                float screenDist = worldDist * Mathf.Max(zoom, 0.0001f);
                if (screenDist < MinScreenSpacingPx)
                {
                    return false;
                }
            }

            _points.Add(worldPoint);
            _pendingDelta.Add(worldPoint);

            if (_points.Count >= MasterData.MaxPointsPerStroke)
            {
                ReachedLimit = true;
            }

            return true;
        }

        /// <summary>
        /// 前回呼び出し以降に追加された点（進行中差分送信用）を取り出し、バッファを空にする。
        /// </summary>
        public List<Vector2> DrainPendingDelta()
        {
            var result = new List<Vector2>(_pendingDelta);
            _pendingDelta.Clear();
            return result;
        }

        /// <summary>
        /// ストロークを確定する。移動を伴わない接触（点1つ）の場合もそのままドットとして返す。
        /// </summary>
        public Stroke Finish(string authorLabel)
        {
            var stroke = new Stroke
            {
                Id = StrokeId,
                Tool = Tool,
                Color = Color,
                Width = Width,
                Points = new List<Vector2>(_points),
                AuthorLabel = authorLabel,
            };
            IsActive = false;
            ReachedLimit = false;
            return stroke;
        }

        /// <summary>進行中ストロークを破棄する（2本目のポインタ検出時等）。</summary>
        public void Abort()
        {
            IsActive = false;
            _points.Clear();
            _pendingDelta.Clear();
            ReachedLimit = false;
        }
    }
}
