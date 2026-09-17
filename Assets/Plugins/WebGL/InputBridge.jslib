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
      return [ev.clientX - rect.left, ev.clientY - rect.top];
    }

    function handlePointerEvent(type) {
      return function (ev) {
        ev.preventDefault();

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

  WB_Input_Drain: function () {
    var state = window.__wbInput;
    if (!state || state.buffer.length === 0) {
      return '[]';
    }
    var json = JSON.stringify(state.buffer);
    state.buffer.length = 0;
    return json;
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
});
