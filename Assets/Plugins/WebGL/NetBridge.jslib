// 通信ブリッジ（requirements.md 7.2章・11.4章 / INTEGRATION_CONTRACT.md 5・7章）。
// WebSocketの接続・送受信と、fetchによるHTTP要求を担う。タブが非表示でUnityの
// 更新が停止していても受信は継続してバッファへ蓄積する。バッファには上限を設け、
// 上限超過時はバッファを破棄して overflow フラグを立てる。
mergeInto(LibraryManager.library, {
  WB_Net_Connect: function (urlPtr) {
    var url = UTF8ToString(urlPtr);

    if (!window.__wbNet) {
      window.__wbNet = {
        ws: null,
        recvBuffer: [],
        recvOverflow: false,
        recvMaxCount: 2000, // 直近バッファ(500件)より十分大きい安全マージン
        responses: [],
        disconnectFlag: false,
      };
    }
    var state = window.__wbNet;

    try {
      if (state.ws) {
        try { state.ws.close(); } catch (e) { /* no-op */ }
      }
      var ws = new WebSocket(url);
      state.ws = ws;

      ws.onopen = function () {
        // Unity側はこの合図を受けてから join メッセージを送信する
        // （WebSocketがOPENになる前の送信は黙って失敗するため）。
        if (state.recvBuffer.length >= state.recvMaxCount) {
          state.recvBuffer.length = 0;
          state.recvOverflow = true;
        } else {
          state.recvBuffer.push(JSON.stringify({ type: '__bridge_connected' }));
        }
      };

      ws.onmessage = function (ev) {
        if (state.recvBuffer.length >= state.recvMaxCount) {
          // 上限に達した場合はバッファを破棄して「再取得が必要」の印を立てる。
          state.recvBuffer.length = 0;
          state.recvOverflow = true;
          return;
        }
        state.recvBuffer.push(ev.data);
      };

      ws.onclose = function () {
        state.disconnectFlag = true;
      };

      ws.onerror = function () {
        state.disconnectFlag = true;
      };
    } catch (err) {
      state.disconnectFlag = true;
    }

    // ページ終了時：Unityの処理を待たず、直近のスナップショット（未送信キュー）を
    // 通信ブリッジから直接送出する（requirements.md 7.3章・14章）。リスナーは1回だけ登録する。
    if (!state.pagehideBound) {
      state.pagehideBound = true;
      window.addEventListener('pagehide', function () {
        if (state.ws && state.ws.readyState === WebSocket.OPEN && state.pendingSnapshot) {
          try {
            var msgs = JSON.parse(state.pendingSnapshot);
            for (var i = 0; i < msgs.length; i++) {
              state.ws.send(JSON.stringify(msgs[i]));
            }
          } catch (e) {
            // ページ終了間際のため失敗しても復旧処理は行わない。
          }
        }
      });
    }
  },

  WB_Net_Send: function (jsonPtr) {
    var state = window.__wbNet;
    if (!state || !state.ws || state.ws.readyState !== WebSocket.OPEN) {
      return;
    }
    var json = UTF8ToString(jsonPtr);
    try {
      state.ws.send(json);
    } catch (err) {
      // 送信失敗は再接続ロジック（Unity側）が検知する。
    }
  },

  WB_Net_Drain: function () {
    var state = window.__wbNet;
    if (!state) {
      return '[]';
    }
    // 切断通知も一種のメッセージとしてUnity側へ伝える。
    var payload = state.recvBuffer.slice();
    state.recvBuffer.length = 0;
    var wrapped = [];
    for (var i = 0; i < payload.length; i++) {
      wrapped.push(payload[i]);
    }
    if (state.disconnectFlag) {
      wrapped.push(JSON.stringify({ type: '__bridge_disconnected' }));
      state.disconnectFlag = false;
    }
    if (wrapped.length === 0) {
      return '[]';
    }
    // 各要素は既にJSON文字列なので、配列としてそのまま埋め込む。
    return '[' + wrapped.join(',') + ']';
  },

  WB_Net_Overflowed: function () {
    var state = window.__wbNet;
    if (!state) {
      return 0;
    }
    var v = state.recvOverflow ? 1 : 0;
    state.recvOverflow = false;
    return v;
  },

  WB_Net_Request: function (methodPtr, pathPtr, headersJsonPtr, bodyJsonPtr, requestId) {
    var state = window.__wbNet;
    if (!state) {
      window.__wbNet = { ws: null, recvBuffer: [], recvOverflow: false, recvMaxCount: 2000, responses: [], disconnectFlag: false };
      state = window.__wbNet;
    }
    var method = UTF8ToString(methodPtr);
    var path = UTF8ToString(pathPtr);
    var headersJson = UTF8ToString(headersJsonPtr);
    var bodyJson = UTF8ToString(bodyJsonPtr);

    var headers = {};
    try {
      if (headersJson) {
        headers = JSON.parse(headersJson);
      }
    } catch (e) {
      headers = {};
    }
    if (bodyJson && bodyJson.length > 0 && !headers['Content-Type']) {
      headers['Content-Type'] = 'application/json';
    }

    var base = window.__wbApiBase || '';
    fetch(base + path, {
      method: method,
      headers: headers,
      body: (bodyJson && bodyJson.length > 0) ? bodyJson : undefined,
    }).then(function (resp) {
      return resp.text().then(function (text) {
        state.responses.push({ requestId: requestId, status: resp.status, body: text });
      });
    }).catch(function () {
      state.responses.push({ requestId: requestId, status: 0, body: '' });
    });
  },

  WB_Net_DrainResponses: function () {
    var state = window.__wbNet;
    if (!state || state.responses.length === 0) {
      return '[]';
    }
    var json = JSON.stringify(state.responses);
    state.responses.length = 0;
    return json;
  },

  WB_Net_SendOnPageHide: function (jsonArrayPtr) {
    var state = window.__wbNet;
    if (!state || !state.ws || state.ws.readyState !== WebSocket.OPEN) {
      return;
    }
    var jsonArray = UTF8ToString(jsonArrayPtr);
    try {
      var msgs = JSON.parse(jsonArray);
      for (var i = 0; i < msgs.length; i++) {
        state.ws.send(JSON.stringify(msgs[i]));
      }
    } catch (err) {
      // ページ終了間際のため失敗しても復旧処理は行わない。
    }
  },

  WB_Net_SetPendingSnapshot: function (jsonArrayPtr) {
    var state = window.__wbNet;
    if (!state) {
      window.__wbNet = { ws: null, recvBuffer: [], recvOverflow: false, recvMaxCount: 2000, responses: [], disconnectFlag: false };
      state = window.__wbNet;
    }
    state.pendingSnapshot = UTF8ToString(jsonArrayPtr);
  },
});
