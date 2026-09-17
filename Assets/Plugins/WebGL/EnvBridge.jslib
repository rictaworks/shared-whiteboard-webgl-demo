// 環境ブリッジ（requirements.md 7.3章 / INTEGRATION_CONTRACT.md 7章）。
// ページURLからのボードトークン読み取り、visibilitychange/pagehide通知、
// PNGダウンロードを担う。
mergeInto(LibraryManager.library, {
  WB_Env_BoardToken: function () {
    // 参加URLの構成要素。クエリ ?b=<token> を既定の形とする。
    // ロード完了前に読み取った値は保持し、失わないようにキャッシュする。
    if (window.__wbBoardTokenCache === undefined) {
      var params = new URLSearchParams(window.location.search);
      window.__wbBoardTokenCache = params.get('b') || '';
    }
    return window.__wbBoardTokenCache;
  },

  WB_Env_Init: function () {
    if (!window.__wbEnv) {
      window.__wbEnv = { events: [] };
    }
    var state = window.__wbEnv;

    document.addEventListener('visibilitychange', function () {
      state.events.push({ event: document.hidden ? 'hidden' : 'visible' });
    });

    window.addEventListener('pagehide', function () {
      state.events.push({ event: 'pagehide' });
      // Unity の処理を待たず、未送信の操作は Unity 側が WB_Net_SendOnPageHide を
      // 同一イベントハンドラ内で直接呼び出す（EnvBridge単体では送出しない）。
    });
  },

  WB_Env_Drain: function () {
    var state = window.__wbEnv;
    if (!state || state.events.length === 0) {
      return '[]';
    }
    var json = JSON.stringify(state.events);
    state.events.length = 0;
    return json;
  },

  WB_Env_RelayWsUrl: function () {
    // 本番はindex.html側で <meta name="wb-ws-url" content="wss://..."> を設定する
    // （Unity PlayとGo中継サーバーはデプロイ先が異なるため、同一オリジンではない）。
    var meta = document.querySelector('meta[name="wb-ws-url"]');
    if (meta && meta.content) {
      return meta.content;
    }
    var proto = (window.location.protocol === 'https:') ? 'wss:' : 'ws:';
    return proto + '//' + window.location.host + '/ws';
  },

  WB_Env_Download: function (bytesPtr, length, filenamePtr) {
    var filename = UTF8ToString(filenamePtr);
    var buffer = new Uint8Array(length);
    for (var i = 0; i < length; i++) {
      buffer[i] = HEAPU8[bytesPtr + i];
    }
    var blob = new Blob([buffer], { type: 'image/png' });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
  },
});
