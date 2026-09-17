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

        public static string BoardToken() => WB_Env_BoardToken();
        public static void Init() => WB_Env_Init();
        public static string Drain() => WB_Env_Drain();
        public static void Download(byte[] bytes, string filename) => WB_Env_Download(bytes, bytes?.Length ?? 0, filename);
        public static string RelayWsUrl() => WB_Env_RelayWsUrl();
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
#endif
    }
}
