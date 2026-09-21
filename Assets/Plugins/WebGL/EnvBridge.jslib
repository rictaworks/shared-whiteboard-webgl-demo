// 環境ブリッジ（requirements.md 7.3章 / INTEGRATION_CONTRACT.md 7章）。
// ページURLからのボードトークン読み取り、visibilitychange/pagehide通知、
// PNGダウンロードを担う。
mergeInto(LibraryManager.library, {
  // C#へ文字列を返すjslib関数は、JSの文字列をそのままreturnしても正しく
  // マーシャリングされない（IL2CPPはポインタを期待するため、素の文字列を
  // アドレスとして誤読し空文字列になる。本番で実際に発生し診断済み）。
  // Unity公式ドキュメントの malloc+stringToUTF8 パターンで明示的にヒープへ
  // 書き込み、そのポインタを返す。
  WB_Env_BoardToken: function () {
    // 参加URLの構成要素。クエリ ?b=<token> を既定の形とする。
    // ロード完了前に読み取った値は保持し、失わないようにキャッシュする。
    if (window.__wbBoardTokenCache === undefined) {
      var params = new URLSearchParams(window.location.search);
      window.__wbBoardTokenCache = params.get('b') || '';
    }
    var result = window.__wbBoardTokenCache;
    var bufferSize = lengthBytesUTF8(result) + 1;
    var buffer = _malloc(bufferSize);
    stringToUTF8(result, buffer, bufferSize);
    return buffer;
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
    var json;
    if (!state || state.events.length === 0) {
      json = '[]';
    } else {
      json = JSON.stringify(state.events);
      state.events.length = 0;
    }
    var bufferSize = lengthBytesUTF8(json) + 1;
    var buffer = _malloc(bufferSize);
    stringToUTF8(json, buffer, bufferSize);
    return buffer;
  },

  WB_Env_RelayWsUrl: function () {
    // 本番はindex.html側で <meta name="wb-ws-url" content="wss://..."> を設定する
    // （Unity PlayとGo中継サーバーはデプロイ先が異なるため、同一オリジンではない）。
    // ただしUnity Playは配布用zip内のindex.htmlを使わず独自のプレイヤーHTMLに
    // Buildフォルダのファイルだけを読み込ませるため、このmetaタグは反映されない
    // （本番で実際に発生し診断済み。issue #7）。同一オリジンへの相対フォールバックは
    // Unity Play上では中継サーバーに到達できないため、本番の中継サーバーURLを
    // 直接フォールバックとして使う。
    var meta = document.querySelector('meta[name="wb-ws-url"]');
    var result;
    if (meta && meta.content) {
      result = meta.content;
    } else {
      result = 'wss://relay-production-ff8d.up.railway.app/ws';
    }
    var bufferSize = lengthBytesUTF8(result) + 1;
    var buffer = _malloc(bufferSize);
    stringToUTF8(result, buffer, bufferSize);
    return buffer;
  },

  // 実機バグ修正（2026-09-20）：Unity Playの独自プレイヤーは、ページ読み込み直後だけでなく
  // 一覧→ボード等の画面遷移（EnterBoard）の直後もキャンバスへ自動でフォーカスを移さない
  // （既知のissue #20と同系統）。runInBackground=trueでメインループの完全停止は防げたが、
  // 遷移直後にフォーカスが無いままだとキャンバスの再描画・リサイズ反映が遅れ、本人の実機で
  // ボード画面のヘッダー・ツールバーが一時的に上下端で見切れて表示される現象が確認された。
  // 画面遷移の直後に明示的にキャンバスへフォーカスを移し、クリックを待たず再描画させる。
  WB_Env_FocusCanvas: function () {
    var canvas = document.querySelector('canvas');
    if (canvas && typeof canvas.focus === 'function') {
      canvas.focus();
    }
  },

  // 実装（2026-09-21・Issue #31）：「参加URLを複製」ボタンが未実装のスタブのまま
  // 本番に出ていた（コピーもURL組み立ても行わず「URLをコピーしました」とだけ表示）。
  // Unity Play では?b=形式の参加URLが原理的に機能しなかった（ゲームがクロスオリジン
  // iframeで動くため）が、Issue #36で自社ホスティングへ移行した今は window.location.origin
  // が本物のページのオリジンになるため成立する。URLの組み立てとクリップボードへの書き込みを
  // ここで一括して行い、結果（成功/失敗）を WB_Env_Drain の既存イベントキューに乗せて
  // C# 側へ非同期に伝える（navigator.clipboard.writeText は Promise を返すため、
  // この関数自体は同期的な戻り値を返せない）。
  WB_Env_CopyParticipationUrl: function (boardTokenPtr) {
    var token = UTF8ToString(boardTokenPtr);
    if (!window.__wbEnv) {
      window.__wbEnv = { events: [] };
    }
    var state = window.__wbEnv;

    if (!token) {
      state.events.push({ event: 'copy_failed' });
      return;
    }

    var url = window.location.origin + window.location.pathname + '?b=' + encodeURIComponent(token);

    function reportSuccess() { state.events.push({ event: 'copy_succeeded' }); }
    function reportFailure() { state.events.push({ event: 'copy_failed' }); }

    // navigator.clipboard は安全なコンテキスト（HTTPS or localhost）でのみ使える。
    // 使えない・失敗した場合は execCommand('copy') へフォールバックする
    // （非表示のtextareaへ値を入れて選択→コピーという伝統的な手法）。
    function fallbackCopy() {
      try {
        var textarea = document.createElement('textarea');
        textarea.value = url;
        textarea.style.position = 'fixed';
        textarea.style.opacity = '0';
        document.body.appendChild(textarea);
        textarea.focus();
        textarea.select();
        var ok = document.execCommand('copy');
        document.body.removeChild(textarea);
        if (ok) { reportSuccess(); } else { reportFailure(); }
      } catch (e) {
        reportFailure();
      }
    }

    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(url).then(reportSuccess, fallbackCopy);
    } else {
      fallbackCopy();
    }
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
