using System.Collections.Generic;

namespace Whiteboard.Data
{
    /// <summary>
    /// ツール種別。UIの選択肢としては pen/marker/eraser の3種。
    /// eraser は操作としては stroke_erase になるため Stroke.tool の値としては存在しない
    /// （INTEGRATION_CONTRACT.md 9章）。
    /// </summary>
    public enum ToolKind
    {
        Pen,
        Marker,
        Eraser
    }

    public enum StrokeWidth
    {
        Thin,
        Medium,
        Thick
    }

    /// <summary>
    /// requirements.md 20.3節・INTEGRATION_CONTRACT.md 9章のマスタデータ。
    /// Rails・Go中継と値を完全一致させること。
    /// </summary>
    public static class MasterData
    {
        // 8色・16進数固定。全システムで同一リストを持つ。
        public static readonly string[] Colors =
        {
            "#1A1A1A", // 黒
            "#E53935", // 赤
            "#1E88E5", // 青
            "#2E7D32", // 緑
            "#F9A825", // 黄
            "#8E24AA", // 紫
            "#FF6F00", // 橙
            "#546E7A", // 灰
        };

        public const string DefaultColor = "#1A1A1A";

        // 太さ3段階（px相当）。マーカーの既定は thick、ペンの既定は medium。
        public static float WidthToPixels(StrokeWidth width)
        {
            switch (width)
            {
                case StrokeWidth.Thin:
                    return 4f;
                case StrokeWidth.Medium:
                    return 8f;
                case StrokeWidth.Thick:
                    return 16f;
                default:
                    return 8f;
            }
        }

        public static string WidthToString(StrokeWidth width)
        {
            switch (width)
            {
                case StrokeWidth.Thin:
                    return "thin";
                case StrokeWidth.Medium:
                    return "medium";
                case StrokeWidth.Thick:
                    return "thick";
                default:
                    return "medium";
            }
        }

        public static StrokeWidth WidthFromString(string s)
        {
            switch (s)
            {
                case "thin":
                    return StrokeWidth.Thin;
                case "thick":
                    return StrokeWidth.Thick;
                default:
                    return StrokeWidth.Medium;
            }
        }

        public static string ToolToString(ToolKind tool)
        {
            switch (tool)
            {
                case ToolKind.Pen:
                    return "pen";
                case ToolKind.Marker:
                    return "marker";
                case ToolKind.Eraser:
                    return "eraser";
                default:
                    return "pen";
            }
        }

        public static ToolKind ToolFromString(string s)
        {
            switch (s)
            {
                case "marker":
                    return ToolKind.Marker;
                case "eraser":
                    return ToolKind.Eraser;
                default:
                    return ToolKind.Pen;
            }
        }

        public static StrokeWidth DefaultWidthFor(ToolKind tool)
        {
            return tool == ToolKind.Marker ? StrokeWidth.Thick : StrokeWidth.Medium;
        }

        // マーカーの半透明合成。ペンは不透明。
        public const float MarkerOpacity = 0.4f;
        public const float PenOpacity = 1.0f;

        public static float OpacityFor(ToolKind tool)
        {
            return tool == ToolKind.Marker ? MarkerOpacity : PenOpacity;
        }

        // 参加者ラベル：参加者A〜J（最大10）。
        public static readonly string[] ParticipantLabels =
        {
            "参加者A", "参加者B", "参加者C", "参加者D", "参加者E",
            "参加者F", "参加者G", "参加者H", "参加者I", "参加者J",
        };

        public const int MaxParticipants = 10;
        public const int MaxBoardsPerSession = 20;
        public const int MaxOpsPerBoard = 5000;
        public const int MaxPointsPerStroke = 1000;
        public const int MaxPointsPerMessage = 200;
        public const int RecentBufferMaxCount = 500;
        public const float RecentBufferMaxSeconds = 300f; // 5分

        public static string ColorForParticipantIndex(int index)
        {
            // 9,10人目は既存色の再利用でよい（9章）。
            return Colors[index % Colors.Length];
        }

        public static readonly HashSet<string> ColorSet = new HashSet<string>(Colors);
    }
}
