using System.Collections.Generic;

namespace Whiteboard.Sync
{
    /// <summary>EditModeテスト用のインメモリ実装（実PlayerPrefsに触れない）。</summary>
    public class InMemoryKeyValueStore : IKeyValueStore
    {
        private readonly Dictionary<string, string> _data = new Dictionary<string, string>();

        public bool HasKey(string key) => _data.ContainsKey(key);
        public string GetString(string key, string defaultValue = "") => _data.TryGetValue(key, out var v) ? v : defaultValue;
        public void SetString(string key, string value) => _data[key] = value;
        public void DeleteKey(string key) => _data.Remove(key);
        public void Save()
        {
            // no-op
        }
    }
}
