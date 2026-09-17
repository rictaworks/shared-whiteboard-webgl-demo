using System;
using System.Collections.Generic;
using UnityEngine;
using Whiteboard.Data;

namespace Whiteboard.Drawing
{
    /// <summary>
    /// requirements.md 3章「ストローク」。ポインタの接触から離脱までの1筆。
    /// 点はワールド座標で保持し、ズーム倍率・パン量に依存しない。
    /// </summary>
    public class Stroke
    {
        public string Id;
        public ToolKind Tool;
        public string Color;
        public StrokeWidth Width;
        public List<Vector2> Points = new List<Vector2>();
        public string AuthorLabel;

        public Stroke Clone()
        {
            return new Stroke
            {
                Id = Id,
                Tool = Tool,
                Color = Color,
                Width = Width,
                Points = new List<Vector2>(Points),
                AuthorLabel = AuthorLabel,
            };
        }

        public static string NewStrokeId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
