using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Whiteboard.UI
{
    public class LoadingView
    {
        public RectTransform Root;
        public Text ProgressText;
    }

    public class BoardListView
    {
        public RectTransform Root;
        public RectTransform ListContent;
        public InputField TitleInput;
        public InputField HoneypotInput;
        public Button CreateButton;
        public Text InfoText;
        public Text ResetNoticeText;
    }

    public class JoinView
    {
        public RectTransform Root;
        public Text BoardNameText;
        public Text ParticipantCountText;
        public Button JoinButton;
        public Text ErrorText;
    }

    public class BoardView
    {
        public RectTransform Root;
        public InputField BoardTitleInput;
        public Button CopyUrlButton;
        public Button BackButton;
        public Button ExportButton;
        public Button UndoButton;
        public Button RedoButton;
        public Button ClearButton;
        public Button ResetViewButton;
        public Button FitAllButton;
        public Text StatusText;
        public Text ZoomText;
        public RectTransform ParticipantListContent;
        public List<Button> ColorButtons = new List<Button>();
        public List<Button> ToolButtons = new List<Button>();
        public List<Button> WidthButtons = new List<Button>();
        // 現在選択中のツール・色・太さを示す状態表示（unity-ugui-runtime-uiスキル
        // レビュー・Issue #10）。各ボタンと同数・同順のOutlineで、選択中のものだけ
        // enabled=trueにする。色のみに頼らない区別として枠線の有無を使う。
        public List<Outline> ToolSelectionOutlines = new List<Outline>();
        public List<Outline> ColorSelectionOutlines = new List<Outline>();
        public List<Outline> WidthSelectionOutlines = new List<Outline>();
        public RectTransform CanvasHost; // 描画キャンバス（world quad用カメラのビューポート範囲を示すUIプレースホルダ）
    }
}
