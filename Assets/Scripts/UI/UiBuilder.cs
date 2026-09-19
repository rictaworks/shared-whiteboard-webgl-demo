using UnityEngine;
using UnityEngine.UI;
using Whiteboard.Data;

namespace Whiteboard.UI
{
    /// <summary>
    /// requirements.md 19章。画面はすべてコードで生成する（Editor GUI操作を要しない）。
    /// ロード画面・一覧画面・参加画面・ボード画面の4画面を構築する。
    /// 文言はですます調で統一する。
    /// </summary>
    public class UiBuilder
    {
        private readonly RectTransform _root;

        public UiBuilder(RectTransform root)
        {
            _root = root;
        }

        public LoadingView BuildLoading()
        {
            var root = UiFactory.CreatePanel("LoadingScreen", _root, new Color(0.97f, 0.97f, 0.98f));
            UiFactory.Stretch(root);

            var progress = UiFactory.CreateText("Progress", root, "読み込み中です…", 28, UiFactory.TextColor, TextAnchor.MiddleCenter);
            UiFactory.Stretch((RectTransform)progress.transform, 40, 40, 40, 40);

            return new LoadingView { Root = root, ProgressText = progress };
        }

        public BoardListView BuildBoardList()
        {
            var root = UiFactory.CreatePanel("BoardListScreen", _root, new Color(0.97f, 0.97f, 0.98f));
            UiFactory.Stretch(root);
            root.gameObject.SetActive(false);

            var header = UiFactory.CreateText("Header", root, "ホワイトボード一覧", 32, UiFactory.TextColor);
            UiFactory.SetAnchoredBox((RectTransform)header.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 60), new Vector2(0, -40));

            var notice = UiFactory.CreateText("ResetNotice", root, "ボードは毎日午前3時（日本時間）にリセットされます。参加URLを知っている方はどなたでも参加できます。", 16, UiFactory.MutedColor);
            UiFactory.SetAnchoredBox((RectTransform)notice.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(-40, 44), new Vector2(0, -84));

            // 作成フォーム
            var createPanel = UiFactory.CreatePanel("CreatePanel", root, UiFactory.PanelColor);
            UiFactory.SetAnchoredBox(createPanel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(-40, 90), new Vector2(0, -150));

            var titleInput = UiFactory.CreateInputField("TitleInput", createPanel, "ボード名（任意）");
            UiFactory.SetAnchoredBox((RectTransform)titleInput.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(320, 50), new Vector2(170, 0));

            var honeypot = UiFactory.CreateHoneypotField(createPanel);

            var createButton = UiFactory.CreateButton("CreateButton", createPanel, "ボードを作成する", UiFactory.AccentColor, Color.white);
            UiFactory.SetAnchoredBox((RectTransform)createButton.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(200, 50), new Vector2(-110, 0));

            // 一覧（スクロール）
            // unity-ugui-runtime-uiスキルのレビュー（Issue #10・不変条件5）：ScrollRectは
            // Viewport（RectMask2Dのみ）→Content（LayoutGroup＋ContentSizeFitter）の3層
            // 構成で組む。従来はScrollRect本体とRectMask2D・Contentを同一階層にまとめており、
            // ScrollRect.viewportも未割り当てだった（Unityの暗黙フォールバックに依存）。
            var scrollRoot = UiFactory.CreatePanel("ListScroll", root, UiFactory.PanelColor);
            UiFactory.SetAnchoredBox(scrollRoot, new Vector2(0, 0), new Vector2(1, 1), new Vector2(-40, 0), new Vector2(0, -260));
            var scrollRect = scrollRoot.gameObject.AddComponent<ScrollRect>();

            var viewport = UiFactory.CreateRect("Viewport", scrollRoot);
            UiFactory.Stretch(viewport);
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = Color.clear; // マスク描画専用（layout.md「ScrollRectのレイアウト構成」）
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = UiFactory.CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0, 0);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewport;
            scrollRect.content = content;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            var info = UiFactory.CreateText("Info", root, "", 18, UiFactory.MutedColor, TextAnchor.MiddleCenter);
            UiFactory.SetAnchoredBox((RectTransform)info.transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 30), new Vector2(0, 10));

            return new BoardListView
            {
                Root = root,
                ListContent = content,
                TitleInput = titleInput,
                HoneypotInput = honeypot,
                CreateButton = createButton,
                InfoText = info,
                ResetNoticeText = notice,
            };
        }

        public RectTransform CreateBoardListRow(RectTransform parent, string title, string updatedAt, int participantCount)
        {
            var row = UiFactory.CreatePanel("BoardRow", parent, new Color(0.95f, 0.96f, 0.97f));
            var layoutElement = row.gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 64;

            var titleText = UiFactory.CreateText("Title", row, title, 22, UiFactory.TextColor);
            UiFactory.SetAnchoredBox((RectTransform)titleText.transform, new Vector2(0, 0), new Vector2(0.6f, 1), new Vector2(0, 0), new Vector2(16, 0));

            var metaText = UiFactory.CreateText("Meta", row, updatedAt + " ・ 参加者 " + participantCount + " 人", 16, UiFactory.MutedColor, TextAnchor.MiddleRight);
            UiFactory.SetAnchoredBox((RectTransform)metaText.transform, new Vector2(0.6f, 0), new Vector2(1, 1), new Vector2(0, 0), new Vector2(-16, 0));

            var button = row.gameObject.AddComponent<Button>();
            return row;
        }

        public JoinView BuildJoin()
        {
            var root = UiFactory.CreatePanel("JoinScreen", _root, new Color(0.97f, 0.97f, 0.98f));
            UiFactory.Stretch(root);
            root.gameObject.SetActive(false);

            var panel = UiFactory.CreatePanel("JoinPanel", root, UiFactory.PanelColor);
            UiFactory.SetAnchoredBox(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(480, 260), Vector2.zero);

            var boardName = UiFactory.CreateText("BoardName", panel, "", 26, UiFactory.TextColor, TextAnchor.MiddleCenter);
            UiFactory.SetAnchoredBox((RectTransform)boardName.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(-24, 40), new Vector2(0, -30));

            var participantCount = UiFactory.CreateText("ParticipantCount", panel, "", 18, UiFactory.MutedColor, TextAnchor.MiddleCenter);
            UiFactory.SetAnchoredBox((RectTransform)participantCount.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(-24, 26), new Vector2(0, -80));

            var errorText = UiFactory.CreateText("Error", panel, "", 16, new Color(0.8f, 0.2f, 0.2f), TextAnchor.MiddleCenter);
            UiFactory.SetAnchoredBox((RectTransform)errorText.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(-24, 26), new Vector2(0, -114));

            var joinButton = UiFactory.CreateButton("JoinButton", panel, "参加する", UiFactory.AccentColor, Color.white, 24);
            UiFactory.SetAnchoredBox((RectTransform)joinButton.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(240, 56), new Vector2(0, 30));

            return new JoinView { Root = root, BoardNameText = boardName, ParticipantCountText = participantCount, JoinButton = joinButton, ErrorText = errorText };
        }

        public BoardView BuildBoard()
        {
            var root = UiFactory.CreatePanel("BoardScreen", _root, Color.clear);
            UiFactory.Stretch(root);
            root.gameObject.SetActive(false);

            // キャンバス表示領域（実際の描画はワールド空間クアッド。このRectは範囲の目安として空ける）
            var canvasHost = UiFactory.CreateRect("CanvasHost", root);
            UiFactory.Stretch(canvasHost);

            // ヘッダー
            var header = UiFactory.CreatePanel("Header", root, new Color(1f, 1f, 1f, 0.92f));
            UiFactory.SetAnchoredBox(header, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 56), new Vector2(0, 0));

            // unity-ugui-runtime-uiスキルのレビュー（Issue #10）：タップ領域は参照解像度で
            // 44px四方以上（不変条件10）。従来は高さ40pxで基準未達だったため44pxへ引き上げる。
            // ヘッダーは56px・横方向にも十分な余白があるため、高さを増やしてもはみ出さない。
            var backButton = UiFactory.CreateButton("BackButton", header, "← 一覧へ", new Color(0.9f, 0.9f, 0.92f), UiFactory.TextColor, 18);
            UiFactory.SetAnchoredBox((RectTransform)backButton.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(120, 44), new Vector2(70, 0));

            var titleInput = UiFactory.CreateInputField("TitleInput", header, "ボード名");
            UiFactory.SetAnchoredBox((RectTransform)titleInput.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(320, 40), Vector2.zero);

            var copyUrlButton = UiFactory.CreateButton("CopyUrlButton", header, "参加URLを複製", new Color(0.9f, 0.9f, 0.92f), UiFactory.TextColor, 16);
            UiFactory.SetAnchoredBox((RectTransform)copyUrlButton.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(160, 44), new Vector2(-260, 0));

            var exportButton = UiFactory.CreateButton("ExportButton", header, "PNGで書き出す", new Color(0.9f, 0.9f, 0.92f), UiFactory.TextColor, 16);
            UiFactory.SetAnchoredBox((RectTransform)exportButton.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(150, 44), new Vector2(-90, 0));

            // ツールバー（下部）
            var toolbar = UiFactory.CreatePanel("Toolbar", root, new Color(1f, 1f, 1f, 0.94f));
            UiFactory.SetAnchoredBox(toolbar, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 84), new Vector2(0, 0));
            var toolbarLayout = toolbar.gameObject.AddComponent<HorizontalLayoutGroup>();
            // 間隔を8→6へ詰め、後述の色スワッチ拡大分の横幅を確保する（19要素が
            // 参照解像度幅1280pxに収まる範囲で最大限タップ領域を広げるための調整）。
            toolbarLayout.spacing = 6;
            toolbarLayout.padding = new RectOffset(12, 12, 10, 10);
            toolbarLayout.childForceExpandHeight = true;
            toolbarLayout.childForceExpandWidth = false;
            toolbarLayout.childControlWidth = true;
            toolbarLayout.childControlHeight = true;
            toolbarLayout.childAlignment = TextAnchor.MiddleLeft;

            var view = new BoardView
            {
                Root = root,
                BoardTitleInput = titleInput,
                CopyUrlButton = copyUrlButton,
                BackButton = backButton,
                ExportButton = exportButton,
                CanvasHost = canvasHost,
            };

            string[] toolLabels = { "ペン", "マーカー", "消しゴム" };
            foreach (var label in toolLabels)
            {
                var b = UiFactory.CreateButton("Tool_" + label, toolbar, label, new Color(0.93f, 0.93f, 0.95f), UiFactory.TextColor, 16);
                AddFixedWidth(b.transform, 76);
                view.ToolButtons.Add(b);
                view.ToolSelectionOutlines.Add(AddSelectionOutline(b.gameObject));
            }

            // unity-ugui-runtime-uiスキルのレビュー（Issue #10）：色スワッチのタップ領域は
            // 従来32px幅で基準（44px）未達だった。ツールバー全体の横幅予算（参照解像度1280px）
            // に収まる範囲で40pxへ拡大する（8色×8px=64px増、間隔を8→6へ詰めた分と合わせて
            // 収まる）。44pxへの完全な引き上げはツールバーの折り返し・スクロール化を伴う
            // より大きな改修が必要なため、本改修では見送り所見として報告する。
            foreach (var hex in MasterData.Colors)
            {
                var swatchRt = UiFactory.CreatePanel("Color_" + hex, toolbar, ColorUtilityParse(hex));
                AddFixedWidth(swatchRt, 40);
                var b = swatchRt.gameObject.AddComponent<Button>();
                view.ColorButtons.Add(b);
                view.ColorSelectionOutlines.Add(AddSelectionOutline(swatchRt.gameObject));
            }

            string[] widthLabels = { "細", "中", "太" };
            foreach (var label in widthLabels)
            {
                var b = UiFactory.CreateButton("Width_" + label, toolbar, label, new Color(0.93f, 0.93f, 0.95f), UiFactory.TextColor, 16);
                AddFixedWidth(b.transform, 50);
                view.WidthButtons.Add(b);
                view.WidthSelectionOutlines.Add(AddSelectionOutline(b.gameObject));
            }

            view.UndoButton = UiFactory.CreateButton("UndoButton", toolbar, "Undo", new Color(0.93f, 0.93f, 0.95f), UiFactory.TextColor, 16);
            AddFixedWidth(view.UndoButton.transform, 70);
            view.RedoButton = UiFactory.CreateButton("RedoButton", toolbar, "Redo", new Color(0.93f, 0.93f, 0.95f), UiFactory.TextColor, 16);
            AddFixedWidth(view.RedoButton.transform, 70);
            view.ClearButton = UiFactory.CreateButton("ClearButton", toolbar, "全消去", new Color(0.95f, 0.85f, 0.85f), new Color(0.6f, 0.1f, 0.1f), 16);
            AddFixedWidth(view.ClearButton.transform, 80);
            view.ResetViewButton = UiFactory.CreateButton("ResetViewButton", toolbar, "原点へ", new Color(0.93f, 0.93f, 0.95f), UiFactory.TextColor, 16);
            AddFixedWidth(view.ResetViewButton.transform, 70);
            view.FitAllButton = UiFactory.CreateButton("FitAllButton", toolbar, "全体表示", new Color(0.93f, 0.93f, 0.95f), UiFactory.TextColor, 16);
            AddFixedWidth(view.FitAllButton.transform, 80);

            // 状態表示（右上）
            var statusText = UiFactory.CreateText("StatusText", root, "同期中", 16, UiFactory.MutedColor, TextAnchor.MiddleRight);
            UiFactory.SetAnchoredBox((RectTransform)statusText.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(240, 24), new Vector2(-16, -66));
            view.StatusText = statusText;

            var zoomText = UiFactory.CreateText("ZoomText", root, "100%", 14, UiFactory.MutedColor, TextAnchor.MiddleRight);
            UiFactory.SetAnchoredBox((RectTransform)zoomText.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(120, 20), new Vector2(-16, -90));
            view.ZoomText = zoomText;

            // 参加者一覧（左上）
            var participantPanel = UiFactory.CreatePanel("ParticipantPanel", root, new Color(1f, 1f, 1f, 0.9f));
            UiFactory.SetAnchoredBox(participantPanel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(180, 160), new Vector2(106, -66));
            var participantContent = UiFactory.CreateRect("Content", participantPanel);
            UiFactory.Stretch(participantContent, 8, 8, 8, 8);
            var pLayout = participantContent.gameObject.AddComponent<VerticalLayoutGroup>();
            pLayout.spacing = 4;
            pLayout.childControlHeight = true;
            pLayout.childControlWidth = true;
            view.ParticipantListContent = participantContent;

            return view;
        }

        public RectTransform CreateParticipantRow(RectTransform parent, string label, string colorHex, bool isMe)
        {
            var row = UiFactory.CreateRect("P_" + label, parent);
            var layoutElement = row.gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 22;

            var dot = UiFactory.CreatePanel("Dot", row, ColorUtilityParse(colorHex));
            UiFactory.SetAnchoredBox(dot, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 14), new Vector2(9, 0));

            var text = UiFactory.CreateText("Label", row, label + (isMe ? "（自分）" : ""), 14, UiFactory.TextColor);
            UiFactory.SetAnchoredBox((RectTransform)text.transform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 0), new Vector2(24, 0));

            return row;
        }

        private static void AddFixedWidth(Transform t, float width)
        {
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
        }

        /// <summary>
        /// unity-ugui-runtime-uiスキルのレビュー（Issue #10）：現在選択中のツール・色・
        /// 太さを示す状態表示。押下遷移（Buttonの既定のColorTint）とは別に、選択が
        /// 「続いている」ことを示す持続的な表示が無かったため追加した。色のみに頼らない
        /// 区別として、選択中は枠線（Outline）を表示する。既定は非表示（enabled=false）で、
        /// Boot.cs側が現在の選択に応じて1つだけenabled=trueにする。
        /// </summary>
        private static Outline AddSelectionOutline(GameObject go)
        {
            var outline = go.AddComponent<Outline>();
            outline.effectColor = UiFactory.AccentColor;
            outline.effectDistance = new Vector2(3f, -3f);
            outline.useGraphicAlpha = false;
            outline.enabled = false;
            return outline;
        }

        private static Color ColorUtilityParse(string hex)
        {
            return Drawing.ColorUtil.HexToColor(hex);
        }
    }
}
