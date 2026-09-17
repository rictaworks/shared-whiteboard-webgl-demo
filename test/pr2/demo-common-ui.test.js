// test/pr2/demo-common-ui.test.js
//
// PR本文の手順20〜22（デモ共通の表示要素）を確認する軽量テスト。
// Unity WebGLの実行そのもの（キャンバス描画・jslibブリッジ経由の入力）は
// サンドボックス版ブラウザでの実ブラウザ確認が重すぎるため対象外とし、
// ビルド済みの静的ページ（Build/WebGL/index.html・legal.html）のDOM構造
// だけをNode.jsでfetchして確認する。
//
// 実ブラウザでのUnity WebGL起動確認（キャンバスへの描画等）は、tester役の
// 範囲外（本番デプロイ後にClaudeがブラウザツールで実施するユーザーテスト
// の領域。20_開発/CLAUDE.md「ユーザーテストは…本番デプロイ後はClaudeが
// ブラウザツールで実行する」を参照）。

import { test, before, after } from "node:test";
import assert from "node:assert/strict";
import http from "node:http";
import path from "node:path";
import { fileURLToPath } from "node:url";
import handler from "./lib/static-server.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const buildDir = path.resolve(__dirname, "..", "..", "Build", "WebGL");

const PORT = Number(process.env.PR2_STATIC_PORT ?? 8099);
let server;
let baseUrl;

before(async () => {
  server = http.createServer((req, res) => handler(req, res, buildDir));
  await new Promise((resolve) => server.listen(PORT, "127.0.0.1", resolve));
  baseUrl = `http://127.0.0.1:${PORT}`;
});

after(async () => {
  await new Promise((resolve) => server.close(resolve));
});

async function getText(pathname) {
  const res = await fetch(`${baseUrl}${pathname}`);
  assert.equal(res.status, 200, `${pathname} should be reachable`);
  return res.text();
}

test("index.html にデモ共通UI必須要素（アンバー帯・戻るリンク・ご相談ボタン・GA4・/legalへのリンク）が存在する", async () => {
  const html = await getText("/index.html");

  assert.match(html, /これはデモ版です/, "アンバー色の「これはデモ版です」旨の帯");
  assert.match(html, /href="https:\/\/rictaworks\.jp\/#demos"/, "デモ一覧（rictaworks.jp）へ戻るリンク");
  assert.match(html, /ご相談はこちら/, "「ご相談はこちら」ボタン");
  assert.match(html, /googletagmanager\.com\/gtag\/js\?id=G-[A-Z0-9]+/, "GA4のgtag.jsタグ");
  assert.match(html, /gtag\(\s*['"]config['"]\s*,\s*['"]G-[A-Z0-9]+['"]\s*\)/, "GA4のconfig呼び出し");
  assert.match(html, /href="legal\.html"/, "利用規約・免責事項・連絡先（/legal相当）ページへのリンク");

  // 起動前は入力を受け付けないこと（requirements.md 19.4節）の最低限の裏付けとして、
  // ロード進捗UIが存在し、WebGL2非対応時は起動自体を止めるロジックがあることを確認する。
  assert.match(html, /id="unity-loading-bar"/, "ロード進捗の表示領域");
  assert.match(html, /supportsWebGL2/, "WebGL2非対応時の起動抑止ロジック");
});

test("legal.html（デモ共通UIの/legal相当ページ）が開け、規約・データ削除方針・連絡先を含む", async () => {
  const html = await getText("/legal.html");

  assert.match(html, /これはデモ版です/, "legal.htmlにもアンバー帯があること");
  assert.match(html, /href="index\.html"/, "ホワイトボードへ戻るリンク");
  assert.match(html, /利用規約・免責事項・連絡先/);
  assert.match(html, /googletagmanager\.com\/gtag\/js\?id=G-[A-Z0-9]+/, "GA4のgtag.jsタグ");
});

test("index.html と legal.html のGA4測定IDが一致する", async () => {
  const index = await getText("/index.html");
  const legal = await getText("/legal.html");

  const idFrom = (html) => html.match(/id=(G-[A-Z0-9]+)/)?.[1];
  const indexId = idFrom(index);
  const legalId = idFrom(legal);

  assert.ok(indexId, "index.htmlにGA4測定IDがあること");
  assert.equal(legalId, indexId, "legal.htmlも同じGA4測定IDを使っていること");
});
