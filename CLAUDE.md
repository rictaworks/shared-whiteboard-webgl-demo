# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Claude Safety Rules

## 削除系コマンドの禁止（重要）

以下のルールはこのワークスペース内のすべての会話で絶対に守られる：

- Claude はファイルまたはディレクトリを削除するコマンドを一切生成してはならない。
  例：rm, rm -rf, rm *, rmdir, unlink, cache --delete,
      lftp mirror --delete, rsync --delete, git clean -df, find -delete 等。

- 削除が必要な場合でも、Claude は削除コマンドを提案せず、
  「手動で削除してください」といった説明に留めること。

- 削除の推奨・削除操作の自動判断も禁止。

- ssh / lftp / デプロイ系スクリプトを生成する場合でも、
  削除コマンドの生成は禁止。

これらはすべての会話・コード生成に適用される。

## シークレット管理（重要）

- `config/master.key` など機密ファイルを `git add` するコードを生成してはならない
- デプロイスクリプト・セットアップ手順でも同様
- シークレットは必ず環境変数（RAILS_MASTER_KEY 等）で渡すこと
- `.gitignore` への追加を確認する手順を必ずコードに含めること
- 初回コミット前に `git status` でステージング確認を促すこと

---

## プロジェクト概要

**共同編集ホワイトボード（デモ版）**。打合せの参加者全員が、URLを共有するだけでサインアップも設定も不要のまま、同じホワイトボードに**同時に書き・同時に消せる**環境を提供する展示物。共同編集の同期が破綻しやすい3点（描いている最中は見えない・消したはずの線が相手に残る・切断中の変更が抜ける）を、**描画中から全員に見え、誰が消しても全員から消え、切断後も欠落なく追いつく**設計で解決することを示す。

仕様の正は [`requirements.md`](requirements.md)（用語定義・ブリッジ仕様・同期仕様・ER図・DFD・シーケンス図・クラス図・状態遷移図・ユースケース図を含む全30章）。実装前に必ず参照すること。本ファイルには要約と横断的な注意点のみを記す。

- フロントエンドは **Unity WebGL**（利用者が明示的に指定。選定は行わない）。ブラウザの Pointer Events・WebSocket・任意ヘッダ付き HTTP は Unity 標準の経路では扱えないため、jslib による入力・通信・環境の3ブリッジを介する（requirements.md 7章）。Unity標準のポインタ入力は無効化する。
- 共同編集はクライアント内で完結しない。**中継（Go/Gin）が操作の順序を確定・再配信し、アプリケーション（Rails）が操作ログを永続化する**。中継とアプリケーション間、フロントエンドから両者への通信はいずれも自システム内の通信であり、外部API呼び出しではない。
- **認証・認可は設計に組み込まない**（requirements.md 4.2 / 28章）。氏名・ニックネームは使用せず、参加者は非個人ラベル（参加者A・B・C…）で表す。セッションキー（アプリケーション層が発行する不透明識別子）が全テーブルのオーナーキーであり、PlayerPrefsに保持してヘッダで送る。ボードトークン（参加URL用の不透明識別子）とセッションキーは別物で、セッションをまたぐ参照・操作は参加レコードを持つボードに限る（6章・28章）。
- Bot対策はハニーポット方式。reCAPTCHAは用いない。
- ボードは日次リセット（JST 03:00・全テーブル削除）まで存続する。永続的な保管は想定しない。
- 測定・保守・監視は対象外（デモ版のため）。

## アーキテクチャ（requirements.md 2 / 5 / 7 章が正）

| 層 | 技術 | デプロイ先 | 役割 |
|---|---|---|---|
| フロントエンド | Unity WebGL（C#） | Unity Play（無料・手動ZIPアップロード） | 描画・履歴・送信キュー・書き出し |
| 入力・通信ブリッジ | jslib（Pointer Events・WebSocket・fetch） | Unity WebGL ビルドに同梱 | ブラウザAPIとC#の橋渡し |
| 中継 | Gin（Go） | Railway（無料） | WebSocket接続の維持、操作の順序確定と再配信、進行中ストローク・カーソルの配信 |
| アプリケーション | Rails | Railway（無料・SQLite） | セッション・ボード・参加・操作ログの永続化・所有権検証 |

- 中継層をGoとするのは、1ボードあたり最大10接続へ毎秒数十回の差分を低遅延で再配信する必要があり、CRUD主体ではなく高速並列処理・リアルタイム通信の要件に該当するため（2.2節）。デモ版の簡略構成として他デモで採る「Cloudflare Workers/Pages一本化」はここでは選択しない。
- デプロイはいずれも人間が手動で行う（Unity Playへのアップロード・Railwayへの`railway up`。root CLAUDE.mdの「デプロイは常にデスクトップから実行」方針に一致）。CI/CDでの自動デプロイは組まない。
- 開発の正は**Windows一本化**（`D:\github\rictaworks\shared-whiteboard-webgl-demo`）。Unity部分はUnity Editor CLIビルドがWindowsローカル前提（`.claude/agents/unity-dev.md`）である一方、Go/Railsは他の同種デモ（questboard・x-follower-gate等）ではWSL2 devcontainerが通例だが、本リポジトリはUnity・Go/Rails混在という初めての構成のため、利用者確認のうえWindows一本化を選択した（2026-09-17）。Go/RailsのローカルDB起動はDocker Desktop for Windowsを想定するが、具体的なcompose構成・コマンドはIssue #1実装時に確定する。

### 同期の要点（requirements.md 11章が正。多数の要件をまたぐため要約）

- 中継サーバーはボードごとにハブを持ち、確定操作・取消フラグ変更を到着順に連番採番して全接続へ再配信する。連番はハブ内で単調増加（端末側の時計を使わない）。
- 進行中ストロークは50msごとの差分配信、確定時は全点列を操作として送信する。
- ハブは直近500件/5分の直近バッファを保持し、参加・タブ復帰・再接続時はこのバッファまたはアプリケーション層からの再取得で欠番なく追いつく。欠番のまま後続を適用しない。
- 同一操作IDの再送は再採番せず既存の連番を返す（冪等性）。消去済み対象への消去はエラーとしない。

## AIモデル分担（デモ版共通・`rictaworks/context` の `ClaudeCode.md` が正）

| フェーズ | 担当モデル |
|---|---|
| Issue分割 | Sonnet |
| 実装 | Haiku |
| reviewer, pr-checker | Sonnet |
| Security Review | Opus |
| コンテンツライティング | GPT |
| ファクトチェック | Gemini |

上記はルート `H:\マイドライブ\RictaWorks\CLAUDE.md` の「開発AI役割分担（受託案件共通）」節（AIか人間かの大分類・モデル名は参考値と明記）とは別内容であり、こちらはデモ版に固有の具体的モデル割当てである。実際の実装・レビューはClaude Code（モデルは都度変わる）で一括して行っており、上記は目安として記載する。

## 開発フロー

- **実装方式：1 issue のワンショットで実装する**（requirements.md 27章）。複数Issueに分割しない。
- ブランチワークフロー：`Assets/**`（Unity）・`src/**`（Go中継・Railsアプリケーション）の変更は main に直接コミット・プッシュせず、必ずブランチを切って `gh pr create` で PR を作成する。それ以外（このファイル・`requirements.md`・`DOCS/`・`SPEC/` 等）は main への直接push を許可する。
- **AIセッティング（CLAUDE.md本文・`.claude/`配下の設定・エージェント定義等）はPRを作らず、必ずmainブランチで直接コミット・pushすること。** PR化しない。
- **デモ版のため公開スピードを優先し、正式なcode-review・audit・security-gate・reportを省略してよい**。フローは `issue → setting & coding → security review → add, commit, push → reviewer & pr-checker → merge →（Unity Playへの手動アップロード・Railwayへの手動デプロイ）→ user test` とする（mergeそのものでは本番デプロイされない点が他の一部デモと異なる。上記アーキテクチャ節参照）。
- コミット前に必ずセキュリティレビューを行うこと。マージ前に必ずreviewerとpr-checkerを実行すること（`.claude/agents/` に定義。後述）。
- TDD厳守：plan → red test → coding → green test。Unity C#はUnity Test Framework（NUnit）、Go/RailsはGo標準テスト／RSpec。フロントの確認（Unity WebGLページ）はPlaywrightで行う。
- 時刻はJST、エンコードはUTF-8。日本語版のみ開発する（requirements.mdの対象外に多言語化は含まれない。参加者ラベルも「参加者A/B/C」であり氏名の多言語対応は不要）。
- 文字列リテラルは設定ファイル／DBに分離し、ハードコードを検出するテストを書くこと。
- Unity標準UIコンポーネントで実現できる範囲では、ネイティブの `alert()` / `confirm()` / `prompt()` に相当する確認ダイアログを使わず、requirements.md 19章の画面仕様に沿ったカスタムUIで表現すること。フォールバック禁止（例外処理を明示的に書く）。
- 環境変数は `.env`（`.env.example` がテンプレート）を参照する。Go/Rails側は開発環境・本番環境の判定を実装し分岐できるようにする。
- バージョン番号は `メジャー2桁.マイナー2桁.デバッグ2桁`（初期値 `01.01.00`）。タグは最初から `git tag -a`（注釈付き）。
- 画面文言は**ですます調**で統一する（だである調禁止）。

## ディレクトリ構成（管理用）

以下を作成済み。運用ルールに従って更新すること（`.gitignore` により `SPEC/` 以外は公開/非公開に関わらずコミットされないローカル専用ディレクトリ）。

| ディレクトリ | 用途 |
|---|---|
| `TASKS/` | タスク管理（ローカルのみ） |
| `DEBUG/` | バグ報告（ローカルのみ） |
| `CLIENT/` | クライアント要望等（ローカルのみ） |
| `WORK/` | 作業報告（ローカルのみ） |
| `ENV/` | `DEVELOPMENT.md`（開発環境）・`PRODUCTION.md`（本番環境）（ローカルのみ） |
| `SPEC/` | 仕様書。リバースエンジニアリング図（ER図・DFD・シーケンス図・クラス図・状態遷移図・ユースケース図）は `requirements.md` に集約済みだが、実装後に差分が生じた図はここに追記・更新する（コミット対象） |
| `DELETE/` | ゴミ箱（ローカルのみ・削除コマンドを使わずここへ移動する） |

想定するコード配置（Issue #1実装時に確定。tourist-flow-balancer-demo等の先例に合わせる）：

```
Assets/Scripts/   # Unity WebGL（C#）。シーン・UI・オブジェクトはすべてコード生成、Editor GUI操作を要しない
Assets/Editor/    # CLIビルド用（BuildScript.cs）
Assets/WebGLTemplates/  # jslibブリッジを同梱するWebGLテンプレート
src/relay/        # 中継サーバー（Go/Gin）
src/backend/      # アプリケーション（Rails）
```

## コマンド（Issue #1実装時に確定・現状は想定のみ）

- **Unityビルド**（CLI・バッチモード）：`Unity.exe -batchmode -nographics -executeMethod BuildScript.BuildWebGL -quit`
- **Unityテスト**：`Unity.exe -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults results.xml`
- Unity Editorのライセンス有効化・実機ブラウザ確認の手順はClaude Desktop側の`.claude/agents/unity-dev.md`（`20_開発`ワークスペース側）を参照。このリポジトリ単体には持たない。
- **中継（Go/Gin）**・**アプリケーション（Rails）**のローカル起動・テストコマンドはIssue #1実装時に確定する（Docker Desktop for Windows想定）。

## 参照ドキュメント

| ファイル | 用途 |
|---|---|
| `requirements.md` | 仕様の正（用語定義・スコープ・ブリッジ仕様・同期仕様・画面仕様・ER図・DFD・シーケンス図・クラス図・状態遷移図・ユースケース図） |
| `DOCS/CRAP.md` | デザイン4原則（Contrast / Repetition / Alignment / Proximity） |
| `DOCS/DP.md` | 開発原則（YAGNI/KISS/DRY/SOLID等） |
| `DOCS/TM.md` | テストメソッド・フレームワーク概要 |
| `.claude/CC.md` | コンプライアンス10項目 |
| `.claude/OWASP10.md` | OWASP Top 10（セキュリティレビュー基準） |
| `.claude/QC10.md` | 品質管理10項目 |
| `.claude/TEST-HARNESS-SAFETY.md` | テストハーネスの安全性チェックリスト |
| `.claude/Manager.md` | プロジェクト管理指針 |
| `.claude/auto-optimizer.md` | CLAUDE.md 自動最適化エージェント用プロンプト |
| `.claude/init-prompt.md` | 本CLAUDE.md生成時に`rictaworks/context`の`ClaudeCode.md`から抽出した適用済みルール一覧（コミット対象外・参照用） |

## Sub Agent（`.claude/agents/` 作成時にそのまま使う定義）

- **pr-checker**：レビューはしない。PRのタイトルと本文を日本語にする。非エンジニア（ブラウザしか使わない利用者）向けのユーザーテスト手順をPR本文に丁寧に書く。
- **tester**：全PRを対象に、PRに書かれたユーザーテスト手順の実行スクリプトを作成する（`DOCS/TM.md`に記載のテストを含む）。テストは`test/pr***/`に作成し、対象は開発サーバーとする。
- **reviewer**：issueの受け入れ要件を満たすこと、および`.claude/CC.md`・`.claude/OWASP10.md`・`.claude/QC10.md`・`DOCS/CRAP.md`・`DOCS/DP.md`・`DOCS/TM.md`を満たすことを検証する。