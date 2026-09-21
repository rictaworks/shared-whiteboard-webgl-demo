using System.Runtime.InteropServices;

namespace Whiteboard.Bridges
{
    /// <summary>
    /// requirements.md 7.1章の入力ブリッジ。Pointer Events を購読し、入力バッファへ蓄積する。
    /// WebGLビルド時は Assets/Plugins/WebGL/InputBridge.jslib を呼び出す。
    /// Editor実行時（#else 分岐）は常に空を返す最小限のモックにフォールバックする。
    /// </summary>
    public static class InputBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void WB_Input_Init(string canvasSelector);

        [DllImport("__Internal")]
        private static extern string WB_Input_Drain();

        [DllImport("__Internal")]
        private static extern void WB_Input_Capture(int pointerId);

        [DllImport("__Internal")]
        private static extern void WB_Input_Flush();

        public static void Init(string canvasSelector) => WB_Input_Init(canvasSelector);
        public static string Drain() => WB_Input_Drain();
        public static void Capture(int pointerId) => WB_Input_Capture(pointerId);
        // 実機バグ修正（2026-09-21・Issue #37）：画面遷移の直前に呼び、
        // 滞留している未処理イベントを読み捨てる。
        public static void Flush() => WB_Input_Flush();
#else
        public static void Init(string canvasSelector)
        {
            // Editorモック：入力ブリッジは存在しないため何もしない。
        }

        public static string Drain()
        {
            // Editorモック：常に空配列を返す。
            return "[]";
        }

        public static void Capture(int pointerId)
        {
            // Editorモック：no-op。
        }

        public static void Flush()
        {
            // Editorモック：no-op。
        }
#endif
    }
}
