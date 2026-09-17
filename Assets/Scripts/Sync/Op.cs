using System.Collections.Generic;
using Whiteboard.Data;
using Whiteboard.Drawing;

namespace Whiteboard.Sync
{
    public enum OpKind
    {
        StrokeAdd,
        StrokeErase,
        Clear,
    }

    public static class OpKindUtil
    {
        public static string ToStringValue(OpKind kind)
        {
            switch (kind)
            {
                case OpKind.StrokeAdd:
                    return "stroke_add";
                case OpKind.StrokeErase:
                    return "stroke_erase";
                case OpKind.Clear:
                    return "clear";
                default:
                    return "stroke_add";
            }
        }

        public static OpKind FromStringValue(string s)
        {
            switch (s)
            {
                case "stroke_erase":
                    return OpKind.StrokeErase;
                case "clear":
                    return OpKind.Clear;
                default:
                    return OpKind.StrokeAdd;
            }
        }
    }

    /// <summary>
    /// requirements.md 24章クラス図・INTEGRATION_CONTRACT.md 2章「Opの形」。
    /// ボードに対する1単位の変更（ストローク追加・ストローク消去・全消去）。
    /// </summary>
    public class Op
    {
        public string OpId;
        public int Seq;
        public OpKind Kind;
        public string AuthorLabel;
        public string StrokeId; // kind=stroke_add のとき有効
        public List<string> TargetStrokeIds; // kind=stroke_erase のとき有効
        public bool Undone;

        /// <summary>kind=stroke_add のときの本体（点列等）。StrokeId と一致するIdを持つ。</summary>
        public Stroke StrokeData;

        public static Op NewStrokeAdd(string opId, Stroke stroke, string authorLabel)
        {
            return new Op
            {
                OpId = opId,
                Kind = OpKind.StrokeAdd,
                StrokeId = stroke.Id,
                StrokeData = stroke,
                AuthorLabel = authorLabel,
                Undone = false,
            };
        }

        public static Op NewStrokeErase(string opId, List<string> targetIds, string authorLabel)
        {
            return new Op
            {
                OpId = opId,
                Kind = OpKind.StrokeErase,
                TargetStrokeIds = targetIds,
                AuthorLabel = authorLabel,
                Undone = false,
            };
        }

        public static Op NewClear(string opId, string authorLabel)
        {
            return new Op
            {
                OpId = opId,
                Kind = OpKind.Clear,
                AuthorLabel = authorLabel,
                Undone = false,
            };
        }

        public Dictionary<string, object> ToDict()
        {
            var d = new Dictionary<string, object>
            {
                ["op_id"] = OpId,
                ["seq"] = (double)Seq,
                ["kind"] = OpKindUtil.ToStringValue(Kind),
                ["author_label"] = AuthorLabel,
                ["undone"] = Undone,
            };

            if (Kind == OpKind.StrokeAdd && StrokeData != null)
            {
                var pts = new List<object>();
                foreach (var p in StrokeData.Points)
                {
                    pts.Add(new List<object> { (double)p.x, (double)p.y });
                }
                d["stroke"] = new Dictionary<string, object>
                {
                    ["id"] = StrokeData.Id,
                    ["tool"] = MasterData.ToolToString(StrokeData.Tool),
                    ["color"] = StrokeData.Color,
                    ["width"] = MasterData.WidthToString(StrokeData.Width),
                    ["points"] = pts,
                };
            }
            else
            {
                d["stroke"] = null;
            }

            if (Kind == OpKind.StrokeErase && TargetStrokeIds != null)
            {
                var ids = new List<object>();
                foreach (var id in TargetStrokeIds)
                {
                    ids.Add(id);
                }
                d["target_stroke_ids"] = ids;
            }
            else
            {
                d["target_stroke_ids"] = null;
            }

            return d;
        }

        public static Op FromDict(Dictionary<string, object> d)
        {
            var op = new Op
            {
                OpId = GetString(d, "op_id"),
                Seq = (int)GetDouble(d, "seq"),
                Kind = OpKindUtil.FromStringValue(GetString(d, "kind")),
                AuthorLabel = GetString(d, "author_label"),
                Undone = GetBool(d, "undone"),
            };

            if (d.TryGetValue("stroke", out var strokeObj) && strokeObj is Dictionary<string, object> strokeDict)
            {
                var stroke = new Stroke
                {
                    Id = GetString(strokeDict, "id"),
                    Tool = MasterData.ToolFromString(GetString(strokeDict, "tool")),
                    Color = GetString(strokeDict, "color"),
                    Width = MasterData.WidthFromString(GetString(strokeDict, "width")),
                    AuthorLabel = op.AuthorLabel,
                };
                if (strokeDict.TryGetValue("points", out var ptsObj) && ptsObj is List<object> ptsList)
                {
                    foreach (var pObj in ptsList)
                    {
                        if (pObj is List<object> pair && pair.Count >= 2)
                        {
                            float x = (float)System.Convert.ToDouble(pair[0]);
                            float y = (float)System.Convert.ToDouble(pair[1]);
                            stroke.Points.Add(new UnityEngine.Vector2(x, y));
                        }
                    }
                }
                op.StrokeData = stroke;
                op.StrokeId = stroke.Id;
            }

            if (d.TryGetValue("target_stroke_ids", out var idsObj) && idsObj is List<object> idsList)
            {
                op.TargetStrokeIds = new List<string>();
                foreach (var idObj in idsList)
                {
                    op.TargetStrokeIds.Add(idObj?.ToString());
                }
            }

            return op;
        }

        private static string GetString(Dictionary<string, object> d, string key)
        {
            return d.TryGetValue(key, out var v) && v != null ? v.ToString() : null;
        }

        private static double GetDouble(Dictionary<string, object> d, string key)
        {
            return d.TryGetValue(key, out var v) && v != null ? System.Convert.ToDouble(v) : 0.0;
        }

        private static bool GetBool(Dictionary<string, object> d, string key)
        {
            return d.TryGetValue(key, out var v) && v is bool b && b;
        }
    }
}
