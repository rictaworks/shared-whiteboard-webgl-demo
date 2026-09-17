using System;
using System.Collections.Generic;
using Whiteboard.Data;
using Whiteboard.Json;

namespace Whiteboard.Sync
{
    /// <summary>
    /// requirements.md 13章・INTEGRATION_CONTRACT.md 6章。PlayerPrefsキーアクセスをまとめる。
    /// 操作ログ・ストロークの全体は保持しない（未送信キューと直近ビューポート・設定のみ）。
    /// </summary>
    public class PrefsStore
    {
        private const string KeySessionKey = "wb_session_key";
        private const string KeyToolSettings = "wb_tool_settings";
        private const string KeyHeldDate = "wb_held_date";
        private const string KeyUnsentQueuePrefix = "wb_unsent_queue_";
        private const string KeyViewportPrefix = "wb_viewport_";

        private readonly IKeyValueStore _store;

        public PrefsStore(IKeyValueStore store)
        {
            _store = store;
        }

        // --- セッションキー ---
        public string GetSessionKey() => _store.GetString(KeySessionKey, "");

        public void SetSessionKey(string sessionKey)
        {
            _store.SetString(KeySessionKey, sessionKey);
            _store.Save();
        }

        // --- ツール設定 ---
        public (ToolKind tool, string color, StrokeWidth width) GetToolSettings()
        {
            if (!_store.HasKey(KeyToolSettings))
            {
                return (ToolKind.Pen, MasterData.DefaultColor, StrokeWidth.Medium);
            }
            var json = _store.GetString(KeyToolSettings, "{}");
            var dict = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (dict == null)
            {
                return (ToolKind.Pen, MasterData.DefaultColor, StrokeWidth.Medium);
            }
            var tool = MasterData.ToolFromString(dict.TryGetValue("tool", out var t) ? t?.ToString() : "pen");
            var color = dict.TryGetValue("color", out var c) ? c?.ToString() : MasterData.DefaultColor;
            var width = MasterData.WidthFromString(dict.TryGetValue("width", out var w) ? w?.ToString() : "medium");
            return (tool, color, width);
        }

        public void SetToolSettings(ToolKind tool, string color, StrokeWidth width)
        {
            var dict = new Dictionary<string, object>
            {
                ["tool"] = MasterData.ToolToString(tool),
                ["color"] = color,
                ["width"] = MasterData.WidthToString(width),
            };
            _store.SetString(KeyToolSettings, MiniJson.Serialize(dict));
            _store.Save();
        }

        // --- 未送信キュー（ボードごと） ---
        public List<Dictionary<string, object>> GetUnsentQueue(string boardId)
        {
            var key = KeyUnsentQueuePrefix + boardId;
            if (!_store.HasKey(key))
            {
                return new List<Dictionary<string, object>>();
            }
            var json = _store.GetString(key, "[]");
            var list = MiniJson.Deserialize(json) as List<object>;
            var result = new List<Dictionary<string, object>>();
            if (list != null)
            {
                foreach (var item in list)
                {
                    if (item is Dictionary<string, object> d)
                    {
                        result.Add(d);
                    }
                }
            }
            return result;
        }

        public void SetUnsentQueue(string boardId, List<Dictionary<string, object>> ops, DateTime utcNow)
        {
            var key = KeyUnsentQueuePrefix + boardId;
            var arr = new List<object>();
            foreach (var op in ops)
            {
                arr.Add(op);
            }
            _store.SetString(key, MiniJson.Serialize(arr));
            SetHeldDate(ComputeLogicalJstDate(utcNow));
            _store.Save();
        }

        public void ClearUnsentQueue(string boardId)
        {
            _store.DeleteKey(KeyUnsentQueuePrefix + boardId);
            _store.Save();
        }

        // --- ビューポート（ボードごと） ---
        public (float panX, float panY, float zoom) GetViewport(string boardId)
        {
            var key = KeyViewportPrefix + boardId;
            if (!_store.HasKey(key))
            {
                return (0f, 0f, 1f);
            }
            var json = _store.GetString(key, "{}");
            var dict = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (dict == null)
            {
                return (0f, 0f, 1f);
            }
            float panX = dict.TryGetValue("panX", out var px) ? (float)Convert.ToDouble(px) : 0f;
            float panY = dict.TryGetValue("panY", out var py) ? (float)Convert.ToDouble(py) : 0f;
            float zoom = dict.TryGetValue("zoom", out var z) ? (float)Convert.ToDouble(z) : 1f;
            return (panX, panY, zoom);
        }

        public void SetViewport(string boardId, float panX, float panY, float zoom)
        {
            var key = KeyViewportPrefix + boardId;
            var dict = new Dictionary<string, object>
            {
                ["panX"] = (double)panX,
                ["panY"] = (double)panY,
                ["zoom"] = (double)zoom,
            };
            _store.SetString(key, MiniJson.Serialize(dict));
            _store.Save();
        }

        // --- 保持日付（JST・03:00境界） ---
        public bool HasHeldDate() => _store.HasKey(KeyHeldDate);

        public string GetHeldDate() => _store.GetString(KeyHeldDate, "");

        public void SetHeldDate(string yyyyMmDd)
        {
            _store.SetString(KeyHeldDate, yyyyMmDd);
        }

        /// <summary>
        /// UTC時刻からJSTの「論理日付」を求める。JST 03:00 を日付境界とし、
        /// 03:00未満は前日の論理日付として扱う。
        /// </summary>
        public static string ComputeLogicalJstDate(DateTime utcNow)
        {
            DateTime jst = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc).ToUniversalTime().AddHours(9);
            DateTime logicalDate = jst.Hour < 3 ? jst.Date.AddDays(-1) : jst.Date;
            return logicalDate.ToString("yyyy-MM-dd");
        }

        /// <summary>
        /// 保持日付と現在のJST論理日付を比較する。異なる場合は未送信キューを
        /// 破棄すべき（true）ことを示す。保持日付が無ければ破棄不要（false）。
        /// </summary>
        public bool DiscardIfDateChanged(DateTime utcNow)
        {
            if (!HasHeldDate())
            {
                return false;
            }
            string current = ComputeLogicalJstDate(utcNow);
            string held = GetHeldDate();
            return held != current;
        }
    }
}
