// test/pr2/lib/static-server.js
//
// demo-common-ui.test.js が Build/WebGL 配下を確認するためだけに使う、
// 依存パッケージ無しの最小限の静的ファイルサーバー。パスの正規化で
// baseDir の外に出ないようにするだけの、テスト専用のシンプルな実装。

import fs from "node:fs";
import path from "node:path";

const CONTENT_TYPES = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".wasm": "application/wasm",
  ".unityweb": "application/octet-stream",
};

export default function handler(req, res, baseDir) {
  try {
    const urlPath = decodeURIComponent(new URL(req.url, "http://localhost").pathname);
    const relative = urlPath === "/" ? "/index.html" : urlPath;
    const resolved = path.normalize(path.join(baseDir, relative));

    if (!resolved.startsWith(path.normalize(baseDir))) {
      res.writeHead(403);
      res.end("forbidden");
      return;
    }

    fs.readFile(resolved, (err, data) => {
      if (err) {
        res.writeHead(404);
        res.end("not found");
        return;
      }
      const ext = path.extname(resolved);
      res.writeHead(200, { "Content-Type": CONTENT_TYPES[ext] ?? "application/octet-stream" });
      res.end(data);
    });
  } catch {
    res.writeHead(400);
    res.end("bad request");
  }
}
