using System.Runtime.InteropServices;

namespace Whiteboard.Bridges
{
    /// <summary>
    /// requirements.md 7.3章の環境ブリッジ。URLからのボードトークン読み取り、
    /// visibilitychange/pagehide通知、PNGダウンロードを担う。
    /// WebGLビルド時は Assets/Plugins/WebGL/EnvBridge.jslib を呼び出す。
    /// Editor実行時は最小限のモックにフォールバックする。
    /// </summary>
    public static class EnvBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern string WB_Env_BoardToken();

        [DllImport("__Internal")]
        private static extern void WB_Env_Init();

        [DllImport("__Internal")]
        private static extern string WB_Env_Drain();

        [DllImport("__Internal")]
        private static extern void WB_Env_Download(byte[] bytes, int length, string filename);

        [DllImport("__Internal")]
        private static extern string WB_Env_RelayWsUrl();

        [DllImport("__Internal")]
        private static extern void WB_Env_FocusCanvas();

        [DllImport("__Internal")]
        private static extern void WB_Env_CopyParticipationUrl(string boardToken);

        public static string BoardToken() => WB_Env_BoardToken();
        public static void Init() => WB_Env_Init();
        public static string Drain() => WB_Env_Drain();
        public static void Download(byte[] bytes, string filename) => WB_Env_Download(bytes, bytes?.Length ?? 0, filename);
        public static string RelayWsUrl() => WB_Env_RelayWsUrl();
        public static void FocusCanvas() => WB_Env_FocusCanvas();
        // 実装（2026-09-21・Issue #31）：結果（成功/失敗）は同期では返らず、
        // 次回以降の Drain() のイベントキューに copy_succeeded/copy_failed として乗る。
        public static void CopyParticipationUrl(string boardToken) => WB_Env_CopyParticipationUrl(boardToken ?? "");
#else
        public static string BoardToken()
        {
            // Editorモック：URLが無いため常に空文字。
            return "";
        }

        public static void Init()
        {
            // Editorモック：no-op。
        }

        public static string Drain()
        {
            return "[]";
        }

        public static void Download(byte[] bytes, string filename)
        {
            // Editorモック：ダウンロードは発生しない。テストでは呼び出しの有無のみ確認する。
        }

        public static string RelayWsUrl()
        {
            // Editorモック：開発用ローカル中継サーバーを既定値とする。
            return "ws://localhost:8080/ws";
        }

        public static void FocusCanvas()
        {
            // Editorモック：no-op。
        }

        public static void CopyParticipationUrl(string boardToken)
        {
            // Editorモック：no-op。テストでは呼び出しの有無のみ確認する。
        }
#endif
    }
}
