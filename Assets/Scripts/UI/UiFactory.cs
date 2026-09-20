using UnityEngine;
using UnityEngine.UI;

namespace Whiteboard.UI
{
    /// <summary>
    /// requirements.md 19章・27章「ビルド」。シーン・UI・オブジェクトはすべてコードで生成する。
    /// uGUI要素生成のための共通ヘルパー（日本語フォントの読み込みを含む）。
    /// </summary>
    public static class UiFactory
    {
        private static Font _jpFont;

        public static Font JapaneseFont
        {
            get
            {
                if (_jpFont == null)
                {
                    _jpFont = Resources.Load<Font>("Fonts/NotoSansJP-VF");
                }
                return _jpFont;
            }
        }

        public static readonly Color TextColor = new Color(0.12f, 0.12f, 0.14f);
        public static readonly Color PanelColor = new Color(1f, 1f, 1f, 0.98f);
        public static readonly Color AccentColor = new Color(0.15f, 0.47f, 0.87f);
        public static readonly Color MutedColor = new Color(0.4f, 0.42f, 0.46f);

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            var rt = CreateRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return rt;
        }

        public static Text CreateText(string name, Transform parent, string content, int fontSize, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var rt = CreateRect(name, parent);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = JapaneseFont;
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            // セキュリティレビュー（2026-09-17）：uGUI Text の supportRichText は既定で true。
            // このヘルパーはボード名（参加者なら誰でも変更できる・requirements.md 17章）や
            // 参加者一覧など、他の参加者が入力した文字列も表示するため、既定のままでは
            // <size=...>やタグの繰り込みによる表示崩し・描画負荷DoS（Unity版のリッチテキスト
            // インジェクション）を許してしまう。常にプレーンテキストとして描画する。
            text.supportRichText = false;
            return text;
        }

        public static Button CreateButton(string name, Transform parent, string label, Color bgColor, Color textColor, int fontSize = 24)
        {
            var rt = CreatePanel(name, parent, bgColor);
            var button = rt.gameObject.AddComponent<Button>();
            var labelRt = CreateRect("Label", rt);
            Stretch(labelRt);
            var text = labelRt.gameObject.AddComponent<Text>();
            text.font = JapaneseFont;
            text.text = label;
            text.fontSize = fontSize;
            text.color = textColor;
            text.alignment = TextAnchor.MiddleCenter;
            text.supportRichText = false; // 同上（ボタンラベルは静的文言のみだが防御的に統一する）。
            // 実機バグ修正（2026-09-20）：NotoSansJP-VF（可変フォント）はuGUIの旧Text
            // コンポーネント（TextMeshProではない）では既定のウェイト軸で描画され、
            // Regular相当の細い字形になる。白文字を青系のアクセントカラー背景に載せる
            // 「ボードを作成する」ボタン等で、アンチエイリアシングの縁が背景色に溶け込み、
            // 本人の実機で「グレーアウトして押せなさそうに見える」と指摘された
            // （色の値自体はColor.whiteで正しいが、視覚的な太さ・コントラストが不足していた）。
            // FontStyle.Boldへ変更し、字形を太くして視認性を上げる。
            text.fontStyle = FontStyle.Bold;
            return button;
        }

        public static InputField CreateInputField(string name, Transform parent, string placeholder)
        {
            var rt = CreatePanel(name, parent, new Color(0.95f, 0.96f, 0.97f));
            var input = rt.gameObject.AddComponent<InputField>();

            var placeholderRt = CreateRect("Placeholder", rt);
            Stretch(placeholderRt, 8, 4, 8, 4);
            var placeholderText = placeholderRt.gameObject.AddComponent<Text>();
            placeholderText.font = JapaneseFont;
            placeholderText.text = placeholder;
            placeholderText.fontSize = 20;
            placeholderText.color = new Color(0.6f, 0.6f, 0.6f);
            placeholderText.alignment = TextAnchor.MiddleLeft;
            placeholderText.supportRichText = false;

            var textRt = CreateRect("Text", rt);
            Stretch(textRt, 8, 4, 8, 4);
            var text = textRt.gameObject.AddComponent<Text>();
            text.font = JapaneseFont;
            text.fontSize = 20;
            text.color = TextColor;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;

            input.textComponent = text;
            input.placeholder = placeholderText;
            return input;
        }

        /// <summary>
        /// ハニーポット項目：視覚的に隠して人間には見えないが、フォームDOM上には存在する入力欄。
        /// </summary>
        public static InputField CreateHoneypotField(Transform parent)
        {
            var rt = CreatePanel("HoneypotWebsite", parent, Color.clear);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-9999f, -9999f);
            var input = rt.gameObject.AddComponent<InputField>();
            var textRt = CreateRect("Text", rt);
            Stretch(textRt);
            var text = textRt.gameObject.AddComponent<Text>();
            text.font = JapaneseFont;
            text.fontSize = 1;
            text.color = Color.clear;
            input.textComponent = text;
            return input;
        }

        public static void Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        public static void SetAnchoredBox(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 sizeDelta, Vector2 anchoredPosition)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = anchoredPosition;
        }
    }
}
