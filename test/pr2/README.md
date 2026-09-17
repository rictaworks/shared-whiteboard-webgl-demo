# PR #2 テスト（tester）

PR #2（Issue #1 ワンショット実装）のユーザーテスト手順に対応する結合E2Eテスト。
実装は architecture 上 Unity WebGL + Go中継 + Rails の3層だが、Unity WebGLの
実ブラウザ確認は重すぎるため、ここでは **Rails（開発サーバー）+ Go中継（実バイナリ）の
結合動作**（同時描画・冪等な再送・消去済みへの再消去・追いつき）を対象とする。
Unity側は `demo-common-ui.test.js` で、ビルド済み `Build/WebGL/index.html`・`legal.html`
の静的なDOM構造のみ確認する（デモ共通UI4要素+GA4）。

## 実行方法

```bash
cd test/pr2
npm test
# もしくは
node --test
```

- 依存パッケージは無し（`node:test`・`node:assert`・組み込み `fetch`/`WebSocket` のみ）。Node.js v22以上が必要。
- `sync.test.js` は実行時に Rails（`ruby bin/rails server`）と Go中継（`go build` して実バイナリを起動）を
  ポート 3012 / 8091 で自動的に起動・停止する（PR本文の手動手順が使う 3001 / 8080 とは衝突しないようにずらしている）。
- 両サーバーともループバック（127.0.0.1）限定でバインドする（Railsは`-b 127.0.0.1`、Go中継は
  `RELAY_HOST=127.0.0.1`。中継側は`src/relay/internal/config/config.go`の`RELAY_HOST`環境変数、
  空なら従来通り全interface）。Windowsファイアーウォールの許可ダイアログを避けるための設定だが、
  この環境で6回連続フル実行した限りではループバック限定にする前・後のいずれでもダイアログは
  発生しなかった（無人実行で承認待ちが起きないことの確認済み）。
- Rails側は **実際の開発用DB**（`src/backend/db/development.sqlite3`）に対して書き込む。日次リセット
  （JST 03:00）まではデータが残るため、テストで使うop_id・stroke_idはすべて実行ごとに一意な接頭辞
  （`crypto.randomUUID()`の先頭8文字）を付けている。固定文字列のIDを再利用すると、
  `strokes.id`（board/session を跨ぐグローバルPK）でUNIQUE制約違反になるので注意。
- 詳細ログが欲しい場合は `PR2_DEBUG=1 node --test` で Rails・Go中継の標準出力をそのまま流す。

## ファイル

| ファイル | 内容 |
|---|---|
| `sync.test.js` | requirements.md 27章（一貫性・冪等性・継続性）に対応する結合E2Eシナリオ5件 |
| `demo-common-ui.test.js` | ビルド済みWebGLページの静的UI確認3件 |
| `lib/processes.js` | Rails・Go中継の起動・停止（実プロセス） |
| `lib/api-client.js` | Railsアプリケーション層（`/api/v1/*`）のHTTPクライアント |
| `lib/ws-client.js` | 中継サーバー（`/ws`）とのWebSocketクライアント |
| `lib/static-server.js` | `demo-common-ui.test.js` 専用の最小静的ファイルサーバー |
| `lib/env.js` | ポート・パス・シークレット等の共通設定 |

## 前提として直した環境の問題（tester作業中に発見）

- `src/backend/config.ru` が存在せず、`bin/rails server` が起動できなかった（`configuration config.ru
  not found`）。標準の1行ボイラープレートを追加して解消した。**PR本文の手動テスト手順もこのままでは
  手順の最初の起動コマンドで失敗するため、reviewerへの申告が必要。**
- `src/backend/vendor/bundle`（Windows向け）が不完全な状態だったため、`.bundle/config` が指す
  `vendor/bundle_wsl` へ `bundle install` を実行して解消した（Gemfile.lockのバージョンに追従する
  形で複数回実行が必要だった＝並行して動いていた別セッションのbundle操作と競合していたため）。
