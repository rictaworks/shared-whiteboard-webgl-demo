using System.Runtime.InteropServices;

namespace Whiteboard.Bridges
{
    /// <summary>
    /// requirements.md 7.2章の通信ブリッジ。WebSocketの接続・送受信と、
    /// アプリケーション層へのHTTP要求を担う。
    /// WebGLビルド時は Assets/Plugins/WebGL/NetBridge.jslib を呼び出す。
    /// Editor実行時は「常に空を返す・接続済みとみなす」最小限のモックにフォールバックする。
    /// </summary>
    public static class NetBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void WB_Net_Connect(string url);

        [DllImport("__Internal")]
        private static extern void WB_Net_Send(string json);

        [DllImport("__Internal")]
        private static extern string WB_Net_Drain();

        [DllImport("__Internal")]
        private static extern int WB_Net_Overflowed();

        [DllImport("__Internal")]
        private static extern void WB_Net_Request(string method, string path, string headersJson, string bodyJson, int requestId);

        [DllImport("__Internal")]
        private static extern string WB_Net_DrainResponses();

        [DllImport("__Internal")]
        private static extern void WB_Net_SendOnPageHide(string jsonArray);

        [DllImport("__Internal")]
        private static extern void WB_Net_SetPendingSnapshot(string jsonArray);

        public static void Connect(string url) => WB_Net_Connect(url);
        public static void Send(string json) => WB_Net_Send(json);
        public static string Drain() => WB_Net_Drain();
        public static bool Overflowed() => WB_Net_Overflowed() != 0;
        public static void Request(string method, string path, string headersJson, string bodyJson, int requestId) => WB_Net_Request(method, path, headersJson, bodyJson, requestId);
        public static string DrainResponses() => WB_Net_DrainResponses();
        public static void SendOnPageHide(string jsonArray) => WB_Net_SendOnPageHide(jsonArray);
        public static void SetPendingSnapshot(string jsonArray) => WB_Net_SetPendingSnapshot(jsonArray);
#else
        public static void Connect(string url)
        {
            // Editorモック：no-op。
        }

        public static void Send(string json)
        {
            // Editorモック：no-op。
        }

        public static string Drain()
        {
            return "[]";
        }

        public static bool Overflowed()
        {
            return false;
        }

        public static void Request(string method, string path, string headersJson, string bodyJson, int requestId)
        {
            // Editorモック：応答を返さない（DrainResponsesは常に空）。
        }

        public static string DrainResponses()
        {
            return "[]";
        }

        public static void SendOnPageHide(string jsonArray)
        {
            // Editorモック：no-op。
        }

        public static void SetPendingSnapshot(string jsonArray)
        {
            // Editorモック：no-op。
        }
#endif
    }
}
