using System;
using System.Collections.Generic;

namespace Whiteboard.Drawing
{
    public enum GestureMode
    {
        Idle,
        Drawing,
        Navigating,
    }

    /// <summary>
    /// requirements.md 8.1章の表に従い、ポインタ種別・本数に応じて描画/パン/ズームを振り分ける。
    /// </summary>
    public class GestureRouter
    {
        private readonly Dictionary<int, string> _activePointers = new Dictionary<int, string>();

        public GestureMode CurrentMode { get; private set; } = GestureMode.Idle;

        /// <summary>描画中に2本目のタッチが検出された（進行中ストローク破棄が必要）ときに発火する。</summary>
        public event Action SecondTouchDetected;

        public GestureMode Route(InputEvent ev)
        {
            switch (ev.Type)
            {
                case "down":
                    HandleDown(ev);
                    break;
                case "up":
                case "cancel":
                    _activePointers.Remove(ev.Id);
                    if (_activePointers.Count == 0)
                    {
                        CurrentMode = GestureMode.Idle;
                    }
                    break;
                default:
                    break;
            }
            return CurrentMode;
        }

        private void HandleDown(InputEvent ev)
        {
            bool wasDrawing = CurrentMode == GestureMode.Drawing;
            _activePointers[ev.Id] = ev.PointerType;

            if (ev.PointerType == "touch")
            {
                if (_activePointers.Count >= 2)
                {
                    if (wasDrawing)
                    {
                        OnSecondTouch();
                    }
                    CurrentMode = GestureMode.Navigating;
                }
                else
                {
                    CurrentMode = GestureMode.Drawing;
                }
            }
            else if (ev.PointerType == "mouse" && ev.Button == 1)
            {
                // 中ボタンでパン
                CurrentMode = GestureMode.Navigating;
            }
            else
            {
                // マウス主ボタン・ペン・トラックパッド主ボタン：描画
                CurrentMode = GestureMode.Drawing;
            }
        }

        /// <summary>描画中に2本目のポインタ（タッチ）が検出された場合、進行中のストロークを破棄する。</summary>
        public void OnSecondTouch()
        {
            SecondTouchDetected?.Invoke();
        }

        public void Reset()
        {
            _activePointers.Clear();
            CurrentMode = GestureMode.Idle;
        }
    }
}
