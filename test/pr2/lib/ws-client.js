// test/pr2/lib/ws-client.js
//
// 中継サーバー（src/relay）とのWebSocket接続を扱う薄いラッパー。
// requirements.md 11.1節のメッセージ種別をそのままJSONで送受信する。
// Node.js（v22+）組み込みのWebSocket（WHATWGのWebSocket API）を使い、
// 依存パッケージを増やさない（DOCS/DP.md YAGNI）。

import { RELAY_WS_URL } from "./env.js";

export class WhiteboardSocket {
  constructor(url = RELAY_WS_URL) {
    this.url = url;
    this.ws = null;
    // 受信した全メッセージ（デバッグ・事後アサーション用）。
    this.log = [];
    // waitFor がまだ見つけていないメッセージのバックログ（先着順）。
    this.backlog = [];
    this._waiters = [];
    this.closed = false;
  }

  connect() {
    return new Promise((resolve, reject) => {
      this.ws = new WebSocket(this.url);
      const onError = (ev) => reject(new Error(`ws connect error: ${ev?.message ?? ev}`));
      this.ws.addEventListener("open", () => {
        this.ws.removeEventListener("error", onError);
        resolve();
      });
      this.ws.addEventListener("error", onError);
      this.ws.addEventListener("message", (ev) => this._onMessage(ev.data));
      this.ws.addEventListener("close", () => {
        this.closed = true;
      });
    });
  }

  _onMessage(raw) {
    let msg;
    try {
      msg = JSON.parse(raw);
    } catch {
      return;
    }
    this.log.push(msg);

    for (let i = this._waiters.length - 1; i >= 0; i--) {
      const w = this._waiters[i];
      if (w.predicate(msg)) {
        this._waiters.splice(i, 1);
        clearTimeout(w.timer);
        w.resolve(msg);
        return;
      }
    }
    this.backlog.push(msg);
  }

  send(obj) {
    if (this.closed) throw new Error("cannot send on closed socket");
    this.ws.send(JSON.stringify(obj));
  }

  join(sessionKey, boardToken, lastSeq = 0) {
    this.send({ type: "join", session_key: sessionKey, board_token: boardToken, last_seq: lastSeq });
  }

  sendOp({ opId, kind, stroke, targetStrokeIds }) {
    this.send({
      type: "op",
      op_id: opId,
      kind,
      ...(stroke ? { stroke } : {}),
      ...(targetStrokeIds ? { target_stroke_ids: targetStrokeIds } : {}),
    });
  }

  sendUndoFlag(opId, undone) {
    this.send({ type: "undo_flag", op_id: opId, undone });
  }

  /** predicate(msg) にマッチする最初のメッセージを待つ（既着分も含む）。 */
  waitFor(predicate, { timeoutMs = 5000, label = "message" } = {}) {
    const idx = this.backlog.findIndex(predicate);
    if (idx !== -1) {
      const [msg] = this.backlog.splice(idx, 1);
      return Promise.resolve(msg);
    }
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        const i = this._waiters.indexOf(w);
        if (i !== -1) this._waiters.splice(i, 1);
        reject(new Error(`timeout (${timeoutMs}ms) waiting for ${label}. log so far: ${JSON.stringify(this.log)}`));
      }, timeoutMs);
      const w = { predicate, resolve, timer };
      this._waiters.push(w);
    });
  }

  waitForType(type, opts) {
    return this.waitFor((m) => m.type === type, { label: type, ...opts });
  }

  /** その型のメッセージが timeoutMs 以内に来ないことを確認する。 */
  async assertNoMessage(predicate, { timeoutMs = 800 } = {}) {
    const idx = this.backlog.findIndex(predicate);
    if (idx !== -1) {
      throw new Error(`unexpected message already present: ${JSON.stringify(this.backlog[idx])}`);
    }
    let matched = null;
    try {
      matched = await this.waitFor(predicate, { timeoutMs, label: "(should not arrive)" });
    } catch {
      return; // timeout = 来なかった = 期待どおり
    }
    throw new Error(`unexpected message arrived: ${JSON.stringify(matched)}`);
  }

  close() {
    try {
      this.ws?.close();
    } catch {
      // ignore
    }
  }
}

export function samplePoints(offset = 0) {
  return [
    [offset, offset],
    [offset + 1, offset + 1],
    [offset + 2, offset + 0.5],
  ];
}

export function sampleStroke(id, { color = "#1A1A1A", tool = "pen", width = "medium", points = samplePoints() } = {}) {
  return { id, tool, color, width, points };
}
