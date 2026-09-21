// 入力ブリッジ（requirements.md 7.1章・8章 / INTEGRATION_CONTRACT.md 7章）。
// Pointer Events を購読し、入力バッファへ蓄積する。Unity側は WB_Input_Drain() で
// 毎フレーム一括取り出しを行う。Unity標準のポインタ入力はここでは無効化しない
// （EventSystem自体を使わない構成にすることで対応するため、このファイルの責務は
// 「ブリッジ経由で全イベントを確実に捕捉すること」のみ）。
mergeInto(LibraryManager.library, {
  WB_Input_Init: function (canvasSelectorPtr) {
    var selector = UTF8ToString(canvasSelectorPtr);

    if (!window.__wbInput) {
      window.__wbInput = {
        buffer: [],
        canvas: null,
      };
    }
    var state = window.__wbInput;

    var canvas = document.querySelector(selector) || document.querySelector('#unity-canvas') || document.querySelector('canvas');
    if (!canvas) {
      return;
    }
    state.canvas = canvas;

    // 既定ジェスチャ（スクロール・ページズーム・長押しメニュー・ドラッグ選択）の抑止。
    canvas.style.touchAction = 'none';
    canvas.style.userSelect = 'none';
    document.body.style.touchAction = 'none';

    function pushPoint(type, ev, x, y) {
      state.buffer.push({
        id: ev.pointerId,
        type: type,
        pointerType: ev.pointerType || 'mouse',
        x: x,
        y: y,
        button: ev.button,
        t: (ev.timeStamp || performance.now()) / 1000.0,
      });
    }

    function toCanvasCoords(ev) {
      var rect = canvas.getBoundingClientRect();
      // C#側は Screen.width/height（Unity内部の実解像度＝CSSピクセル×devicePixelRatio）を
      // 基準にワールド座標変換する（Boot.cs の UpdateCameraFromController）。
      // getBoundingClientRect はCSSピクセル単位を返すため、devicePixelRatio相当の
      // 倍率（canvas.width / rect.width）を掛けて同じ基準に揃える
      // （揃えないとdevicePixelRatio!=1の環境で描画位置が大きくずれる。本番相当環境で実際に確認）。
      var scaleX = canvas.width / rect.width;
      var scaleY = canvas.height / rect.height;
      return [(ev.clientX - rect.left) * scaleX, (ev.clientY - rect.top) * scaleY];
    }

    function handlePointerEvent(type) {
      return function (ev) {
        // pointerdownでpreventDefault()すると、ブラウザがmousedown/mouseupの
        // 互換イベント合成を止めてしまい、EventSystem（StandaloneInputModule）が
        // 一切クリックを検知できなくなる不具合を本番で確認した（UIボタンが
        // 反応しない）。ジェスチャ抑止はtouch-action:none（既に設定済み・上記）と
        // 個別のcontextmenu/dragstart/gesturestartのpreventDefaultで足りるため、
        // ここでは呼ばない。

        // 1イベントに複数の座標が束ねられている場合（getCoalescedEvents）は
        // それらをすべて個別の点として追記する。
        var events = (type === 'move' && typeof ev.getCoalescedEvents === 'function')
          ? ev.getCoalescedEvents()
          : [ev];
        if (!events || events.length === 0) {
          events = [ev];
        }

        for (var i = 0; i < events.length; i++) {
          var e = events[i];
          var xy = toCanvasCoords(e);
          pushPoint(type, ev, xy[0], xy[1]);
        }

        if (type === 'down') {
          try {
            canvas.setPointerCapture(ev.pointerId);
          } catch (err) {
            // キャプチャ不可のブラウザでも致命的ではない。
          }
        }
      };
    }

    canvas.addEventListener('pointerdown', handlePointerEvent('down'), { passive: false });
    canvas.addEventListener('pointermove', handlePointerEvent('move'), { passive: false });
    canvas.addEventListener('pointerup', handlePointerEvent('up'), { passive: false });
    canvas.addEventListener('pointercancel', handlePointerEvent('cancel'), { passive: false });

    // ホイールの伝播抑止（ズームはUnity側でCtrl+ホイール等として処理するためpreventDefaultのみ）。
    canvas.addEventListener('wheel', function (ev) {
      ev.preventDefault();
      state.buffer.push({
        id: -1,
        type: 'wheel',
        pointerType: 'mouse',
        x: ev.deltaX,
        y: ev.deltaY,
        button: 0,
        t: (ev.timeStamp || performance.now()) / 1000.0,
      });
    }, { passive: false });

    // 長押しメニュー・ドラッグ選択の抑止。
    canvas.addEventListener('contextmenu', function (ev) { ev.preventDefault(); });
    canvas.addEventListener('dragstart', function (ev) { ev.preventDefault(); });
    canvas.addEventListener('gesturestart', function (ev) { ev.preventDefault(); });
  },

  // C#へ文字列を返すjslib関数は、JSの文字列をそのままreturnしても正しく
  // マーシャリングされない（IL2CPPはポインタを期待するため、素の文字列を
  // アドレスとして誤読し空文字列になる。本番で実際に発生し診断済み）。
  // Unity公式ドキュメントの malloc+stringToUTF8 パターンで明示的にヒープへ
  // 書き込み、そのポインタを返す。
  WB_Input_Drain: function () {
    var state = window.__wbInput;
    var json;
    if (!state || state.buffer.length === 0) {
      json = '[]';
    } else {
      json = JSON.stringify(state.buffer);
      state.buffer.length = 0;
    }
    var bufferSize = lengthBytesUTF8(json) + 1;
    var buffer = _malloc(bufferSize);
    stringToUTF8(json, buffer, bufferSize);
    return buffer;
  },

  WB_Input_Capture: function (pointerId) {
    var state = window.__wbInput;
    if (!state || !state.canvas) {
      return;
    }
    try {
      state.canvas.setPointerCapture(pointerId);
    } catch (err) {
      // no-op
    }
  },

  // 実機バグ修正（2026-09-21・Issue #37）：このブリッジはキャンバス上の
  // Pointer イベントを、アプリの画面状態（一覧／ボード等）に関わらず無条件に
  // バッファへ蓄積し続ける。Boot.HandleInput() は _phase == InBoard の
  // フレームでしかバッファを drain しないため、一覧画面でボタンを押した際の
  // pointerdown/up がバッファに滞留し、その後ボード画面へ遷移して
  // _phase == InBoard になった最初のフレームで「古い入力」がそのまま
  // 新しい画面上の描画として処理されてしまい、遷移直前のボタン座標に
  // 意図しない点が描かれていた（本番実機で複数回再現）。画面遷移の直前に
  // C# 側から呼び、その時点で滞留しているイベントを読み捨てる。
  WB_Input_Flush: function () {
    var state = window.__wbInput;
    if (state) {
      state.buffer.length = 0;
    }
  },
});
