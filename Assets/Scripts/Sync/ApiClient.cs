using System;
using System.Collections.Generic;
using Whiteboard.Bridges;
using Whiteboard.Json;

namespace Whiteboard.Sync
{
    /// <summary>
    /// requirements.md 7.2章・INTEGRATION_CONTRACT.md 2章。HTTP経由でRailsの /api/v1/... を呼ぶ。
    /// NetBridge.Request は非同期のためリクエストIDでコールバックを管理し、
    /// PumpResponses を毎フレーム呼び出して完了応答を配る。
    /// </summary>
    public class ApiClient
    {
        private readonly PrefsStore _prefs;
        private int _nextRequestId = 1;
        private readonly Dictionary<int, Action<int, Dictionary<string, object>>> _callbacks = new Dictionary<int, Action<int, Dictionary<string, object>>>();

        public ApiClient(PrefsStore prefs)
        {
            _prefs = prefs;
        }

        public void IssueSession(Action<int, Dictionary<string, object>> callback)
        {
            Request("POST", "/api/v1/sessions", null, callback);
        }

        public void CreateBoard(string title, string honeypot, Action<int, Dictionary<string, object>> callback)
        {
            var body = new Dictionary<string, object>
            {
                ["title"] = title,
                ["website"] = honeypot ?? "",
            };
            Request("POST", "/api/v1/boards", body, callback);
        }

        public void ListBoards(Action<int, Dictionary<string, object>> callback)
        {
            Request("GET", "/api/v1/boards", null, callback);
        }

        public void RenameBoard(string boardId, string title, Action<int, Dictionary<string, object>> callback)
        {
            var body = new Dictionary<string, object> { ["title"] = title };
            Request("PATCH", "/api/v1/boards/" + boardId, body, callback);
        }

        public void PreviewByToken(string boardToken, Action<int, Dictionary<string, object>> callback)
        {
            Request("GET", "/api/v1/boards/by_token/" + boardToken, null, callback);
        }

        public void JoinByToken(string boardToken, Action<int, Dictionary<string, object>> callback)
        {
            Request("POST", "/api/v1/boards/by_token/" + boardToken + "/join", null, callback);
        }

        public void FetchOps(string boardId, int fromSeq, int? toSeq, Action<int, Dictionary<string, object>> callback)
        {
            string path = "/api/v1/boards/" + boardId + "/ops?from_seq=" + fromSeq;
            if (toSeq.HasValue)
            {
                path += "&to_seq=" + toSeq.Value;
            }
            Request("GET", path, null, callback);
        }

        private void Request(string method, string path, Dictionary<string, object> body, Action<int, Dictionary<string, object>> callback)
        {
            var headers = new Dictionary<string, object>();
            var sessionKey = _prefs?.GetSessionKey();
            if (!string.IsNullOrEmpty(sessionKey))
            {
                headers["X-Session-Key"] = sessionKey;
            }

            int id = _nextRequestId++;
            if (callback != null)
            {
                _callbacks[id] = callback;
            }

            string headersJson = MiniJson.Serialize(headers);
            string bodyJson = body != null ? MiniJson.Serialize(body) : "";
            NetBridge.Request(method, path, headersJson, bodyJson, id);
        }

        /// <summary>毎フレーム呼び出す。DrainResponses() を処理し、対応するコールバックへ配る。</summary>
        public void PumpResponses()
        {
            string json = NetBridge.DrainResponses();
            var arr = MiniJson.Deserialize(json) as List<object>;
            if (arr == null)
            {
                return;
            }

            foreach (var item in arr)
            {
                if (item is Dictionary<string, object> d)
                {
                    int requestId = (int)Convert.ToDouble(d.TryGetValue("requestId", out var rid) ? rid : 0.0);
                    int status = (int)Convert.ToDouble(d.TryGetValue("status", out var st) ? st : 0.0);
                    string bodyStr = d.TryGetValue("body", out var b) ? b?.ToString() : "";

                    if (_callbacks.TryGetValue(requestId, out var cb))
                    {
                        _callbacks.Remove(requestId);
                        Dictionary<string, object> parsedBody = null;
                        if (!string.IsNullOrEmpty(bodyStr))
                        {
                            parsedBody = MiniJson.Deserialize(bodyStr) as Dictionary<string, object>;
                        }
                        cb(status, parsedBody);
                    }
                }
            }
        }
    }
}
