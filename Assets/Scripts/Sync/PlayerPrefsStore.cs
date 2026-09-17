using UnityEngine;

namespace Whiteboard.Sync
{
    /// <summary>実行時（Editor実行含む）に使う UnityEngine.PlayerPrefs のラッパー。</summary>
    public class PlayerPrefsStore : IKeyValueStore
    {
        public bool HasKey(string key) => PlayerPrefs.HasKey(key);
        public string GetString(string key, string defaultValue = "") => PlayerPrefs.GetString(key, defaultValue);
        public void SetString(string key, string value) => PlayerPrefs.SetString(key, value);
        public void DeleteKey(string key) => PlayerPrefs.DeleteKey(key);
        public void Save() => PlayerPrefs.Save();
    }
}
