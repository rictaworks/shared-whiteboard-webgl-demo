// test/pr2/lib/env.js
//
// 共通設定。PR #2 のユーザーテスト手順（PR本文）が使うポート（Rails:3001,
// 中継:8080）とは意図的にずらし、手動確認用サーバーと衝突しないようにする。
// requirements.md 2.4節のヘッダ名（X-Session-Key）・28章の内部シークレット
// ヘッダ（X-Internal-Secret）は本番と同じ実装を通すので、ここでは値だけ用意する。

import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

export const REPO_ROOT = path.resolve(__dirname, "..", "..", "..");
export const BACKEND_DIR = path.join(REPO_ROOT, "src", "backend");
export const RELAY_DIR = path.join(REPO_ROOT, "src", "relay");

export const RAILS_PORT = Number(process.env.PR2_RAILS_PORT ?? 3012);
export const RELAY_PORT = Number(process.env.PR2_RELAY_PORT ?? 8091);

// "localhost" ではなく 127.0.0.1 を明示する：両サーバーをループバック限定
// （IPv4）でバインドしているため（Windowsファイアーウォールの許可ダイアログ
// を避ける。processes.js の -b/RELAY_HOST 参照）、"localhost" が ::1(IPv6) に
// 解決される環境だと接続できなくなる。
export const RAILS_BASE_URL = `http://127.0.0.1:${RAILS_PORT}`;
export const RELAY_WS_URL = `ws://127.0.0.1:${RELAY_PORT}/ws`;

export const INTERNAL_SHARED_SECRET = "pr2-e2e-shared-secret-not-a-real-secret";

// Node の fetch / WebSocket は Origin ヘッダを送らないため、relay 側
// (originAllowed の"空Originは常に許可"パス)にも Rails 側
// (enforce_allowed_origin の"Origin未送信は判定しない"パス)にも
// ここで指定する値は実質影響しない。requirements.md 28章の設定項目として
// 本番同様に値を入れておくだけ。
export const ALLOWED_ORIGINS = "http://localhost:*";

export const RUBY_BIN_DIR = process.env.PR2_RUBY_BIN_DIR ?? "C:\\Ruby33-x64\\bin";
export const GO_BIN_DIR = process.env.PR2_GO_BIN_DIR ?? "D:\\devtools\\go\\bin";
export const GOPATH = process.env.PR2_GOPATH ?? "D:\\devtools\\gopath";
export const GOMODCACHE = process.env.PR2_GOMODCACHE ?? "D:\\devtools\\gopath\\pkg\\mod";
export const GOCACHE = process.env.PR2_GOCACHE ?? "D:\\devtools\\gocache";
