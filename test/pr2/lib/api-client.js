// test/pr2/lib/api-client.js
//
// Railsアプリケーション層（src/backend、api/v1名前空間）のHTTPクライアント。
// requirements.md 2.4節どおり、セッションキーはヘッダ（X-Session-Key）でのみ
// 送る。ルーティングは src/backend/config/routes.rb に一致させている。

import { RAILS_BASE_URL } from "./env.js";

export class ApiClient {
  constructor(baseUrl = RAILS_BASE_URL) {
    this.baseUrl = baseUrl;
    this.sessionKey = null;
  }

  async issueSession() {
    const res = await fetch(`${this.baseUrl}/api/v1/sessions`, { method: "POST" });
    if (!res.ok) throw new Error(`issueSession failed: ${res.status}`);
    const body = await res.json();
    this.sessionKey = body.session_key;
    return body.session_key;
  }

  headers(extra = {}) {
    return {
      "Content-Type": "application/json",
      "X-Session-Key": this.sessionKey,
      ...extra,
    };
  }

  async createBoard(title) {
    const res = await fetch(`${this.baseUrl}/api/v1/boards`, {
      method: "POST",
      headers: this.headers(),
      body: JSON.stringify({ title }),
    });
    if (!res.ok) throw new Error(`createBoard failed: ${res.status} ${await res.text()}`);
    return res.json();
  }

  async joinByToken(boardToken) {
    const res = await fetch(`${this.baseUrl}/api/v1/boards/by_token/${boardToken}/join`, {
      method: "POST",
      headers: this.headers(),
    });
    if (!res.ok) throw new Error(`joinByToken failed: ${res.status} ${await res.text()}`);
    return res.json();
  }

  async showByToken(boardToken) {
    const res = await fetch(`${this.baseUrl}/api/v1/boards/by_token/${boardToken}`, {
      headers: this.headers(),
    });
    if (!res.ok) throw new Error(`showByToken failed: ${res.status} ${await res.text()}`);
    return res.json();
  }

  async fetchOps(boardId, { fromSeq, toSeq } = {}) {
    const params = new URLSearchParams();
    if (fromSeq !== undefined) params.set("from_seq", String(fromSeq));
    if (toSeq !== undefined) params.set("to_seq", String(toSeq));
    const qs = params.toString();
    const res = await fetch(`${this.baseUrl}/api/v1/boards/${boardId}/ops${qs ? `?${qs}` : ""}`, {
      headers: this.headers(),
    });
    if (!res.ok) throw new Error(`fetchOps failed: ${res.status} ${await res.text()}`);
    const body = await res.json();
    return body.ops;
  }
}
