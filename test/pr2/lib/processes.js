// test/pr2/lib/processes.js
//
// Rails（src/backend）とGo中継（src/relay）の「開発サーバー」を実際に
// 起動・停止するヘルパー。PR本文の手動テスト手順が使うコマンドと同じもの
// （bin/rails server / go run 相当）を、本テストからも実プロセスとして
// 起動する。Unity WebGL側は別ファイル（demo-common-ui.test.js）で静的に
// 確認するのみで、ここでは扱わない。
//
// 注意：`go run .` は子プロセスとして実バイナリを起動するため、親を kill
// してもポートを掴んだままの子が残ることがある。そのため事前に
// `go build` で単一の実行ファイルへビルドしてから直接起動し、確実に
// kill できるようにしている。

import { spawn } from "node:child_process";
import net from "node:net";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";

import {
  BACKEND_DIR,
  RELAY_DIR,
  RAILS_PORT,
  RELAY_PORT,
  RAILS_BASE_URL,
  INTERNAL_SHARED_SECRET,
  ALLOWED_ORIGINS,
  RUBY_BIN_DIR,
  GO_BIN_DIR,
  GOPATH,
  GOMODCACHE,
  GOCACHE,
} from "./env.js";

function waitForPort(port, { timeoutMs = 30000, intervalMs = 300 } = {}) {
  const deadline = Date.now() + timeoutMs;
  return new Promise((resolve, reject) => {
    const attempt = () => {
      const socket = net.connect({ port, host: "127.0.0.1" });
      socket.once("connect", () => {
        socket.end();
        resolve();
      });
      socket.once("error", () => {
        socket.destroy();
        if (Date.now() > deadline) {
          reject(new Error(`port ${port} did not open within ${timeoutMs}ms`));
        } else {
          setTimeout(attempt, intervalMs);
        }
      });
    };
    attempt();
  });
}

async function waitForRailsReady({ timeoutMs = 30000 } = {}) {
  const deadline = Date.now() + timeoutMs;
  // ポートが開いていても、WEBrick起動直後はRailsアプリの初期化中で
  // 接続がリセットされることがあるため、実際にAPIが200を返すまで待つ。
  // POST /api/v1/sessions は副作用（セッション作成）があるが、テスト対象
  // のRailsアプリ自体であり、ここで1件増えても後続シナリオに影響しない。
  for (;;) {
    try {
      const res = await fetch(`${RAILS_BASE_URL}/api/v1/sessions`, { method: "POST" });
      if (res.ok) return;
    } catch {
      // まだ受け付けていない
    }
    if (Date.now() > deadline) {
      throw new Error("rails server did not become ready in time");
    }
    await new Promise((r) => setTimeout(r, 300));
  }
}

function prefixedEnv(binDir) {
  return `${binDir};${process.env.PATH ?? process.env.Path ?? ""}`;
}

export async function startRails() {
  // `bin/rails` は shebang 付きのRubyスクリプトで、cmd.exe（shell:true時に
  // Windowsが使うシェル）はshebangを解釈できず直接実行できない
  // （PR本文の手動手順はGit Bash経由なので問題にならないが、ここは
  // child_processから直接起動するため明示的に ruby 経由で呼ぶ）。
  const child = spawn(
    "ruby",
    ["bin/rails", "server", "-p", String(RAILS_PORT), "-b", "127.0.0.1", "-e", "development"],
    {
      cwd: BACKEND_DIR,
      env: {
        ...process.env,
        PATH: prefixedEnv(RUBY_BIN_DIR),
        RAILS_ENV: "development",
        INTERNAL_SHARED_SECRET,
        ALLOWED_ORIGINS,
      },
      stdio: ["ignore", "pipe", "pipe"],
    }
  );

  const logs = [];
  child.stdout.on("data", (d) => { const s = d.toString(); logs.push(s); if (process.env.PR2_DEBUG) process.stderr.write(`[rails] ${s}`); });
  child.stderr.on("data", (d) => { const s = d.toString(); logs.push(s); if (process.env.PR2_DEBUG) process.stderr.write(`[rails!] ${s}`); });

  let stopping = false;
  child.on("exit", (code, signal) => {
    if (!stopping && code !== null && code !== 0) {
      console.error(`[pr2] rails server exited early (code=${code}, signal=${signal}):\n${logs.join("")}`);
    }
  });

  try {
    await waitForRailsReady();
  } catch (err) {
    stopping = true;
    child.kill();
    throw new Error(`${err.message}\n--- rails logs ---\n${logs.join("")}`);
  }

  return {
    process: child,
    logs,
    async stop() {
      stopping = true;
      await killTree(child);
    },
  };
}

export async function buildRelayBinary() {
  const outPath = path.join(
    os.tmpdir(),
    `pr2-relay-e2e-${process.pid}${process.platform === "win32" ? ".exe" : ""}`
  );

  await new Promise((resolve, reject) => {
    const build = spawn("go", ["build", "-o", outPath, "."], {
      cwd: RELAY_DIR,
      env: {
        ...process.env,
        PATH: prefixedEnv(GO_BIN_DIR),
        GOPATH,
        GOMODCACHE,
        GOCACHE,
      },
      stdio: ["ignore", "pipe", "pipe"],
    });
    let out = "";
    build.stdout.on("data", (d) => (out += d.toString()));
    build.stderr.on("data", (d) => (out += d.toString()));
    build.on("exit", (code) => {
      if (code === 0) resolve();
      else reject(new Error(`go build failed (code=${code}):\n${out}`));
    });
  });

  return outPath;
}

export async function startRelay(binaryPath, { appInternalUrl = RAILS_BASE_URL, port = RELAY_PORT, allowedOrigins = ALLOWED_ORIGINS, internalSharedSecret = INTERNAL_SHARED_SECRET } = {}) {
  const child = spawn(binaryPath, [], {
    env: {
      ...process.env,
      PORT: String(port),
      // ループバック限定でバインドし、Windowsファイアーウォールの許可ダイアログ
      // （待受プロセス初回起動時に出ることがある）を避ける
      // （src/relay/internal/config/config.go の RELAY_HOST。空なら従来通り全interface）。
      RELAY_HOST: "127.0.0.1",
      APP_INTERNAL_URL: appInternalUrl,
      INTERNAL_SHARED_SECRET: internalSharedSecret,
      ALLOWED_ORIGINS: allowedOrigins,
    },
    stdio: ["ignore", "pipe", "pipe"],
  });

  const logs = [];
  child.stdout.on("data", (d) => { const s = d.toString(); logs.push(s); if (process.env.PR2_DEBUG) process.stderr.write(`[relay] ${s}`); });
  child.stderr.on("data", (d) => { const s = d.toString(); logs.push(s); if (process.env.PR2_DEBUG) process.stderr.write(`[relay!] ${s}`); });

  let stopping = false;
  child.on("exit", (code, signal) => {
    if (!stopping && code !== null && code !== 0) {
      console.error(`[pr2] relay exited early (code=${code}, signal=${signal}):\n${logs.join("")}`);
    }
  });

  try {
    await waitForPort(port);
  } catch (err) {
    stopping = true;
    child.kill();
    throw new Error(`${err.message}\n--- relay logs ---\n${logs.join("")}`);
  }

  return {
    process: child,
    logs,
    async stop() {
      stopping = true;
      await killTree(child);
    },
  };
}

async function killTree(child) {
  if (child.exitCode !== null || child.signalCode !== null) return;
  if (process.platform === "win32") {
    await new Promise((resolve) => {
      const k = spawn("taskkill", ["/pid", String(child.pid), "/T", "/F"], { stdio: "ignore" });
      k.on("exit", () => resolve());
      k.on("error", () => resolve());
    });
  } else {
    child.kill("SIGKILL");
  }
  await new Promise((resolve) => {
    if (child.exitCode !== null || child.signalCode !== null) return resolve();
    child.once("exit", resolve);
    setTimeout(resolve, 3000);
  });
}
