# フォントライセンス表記

同梱フォント：`NotoSansJP-VF.ttf`（Noto Sans JP Variable Font）

- 提供元：Google Fonts（Noto Sans JP）
- ライセンス：SIL Open Font License 1.1
- 用途：uGUI `Text` コンポーネントで日本語グリフ（ひらがな・カタカナ・漢字）を表示するため。Unity組み込みフォント（`LegacyRuntime.ttf`）は日本語グリフを持たず、WebGLビルドでは `Font.CreateDynamicFontFromOSFont` によるOSフォント参照も機能しないため、本フォントファイルを同梱して `Resources.Load<Font>("Fonts/NotoSansJP-VF")` で読み込む。
- 取得元パス：`C:\Windows\Fonts\NotoSansJP-VF.ttf`（Windows同梱）

SIL Open Font License 1.1 の全文は https://scripts.sil.org/OFL を参照。本フォントの再配布・改変は同ライセンスの条件に従う。
