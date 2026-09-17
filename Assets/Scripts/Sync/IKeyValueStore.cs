namespace Whiteboard.Sync
{
    /// <summary>
    /// PlayerPrefsへの薄い抽象化。EditModeテストでは実PlayerPrefsに触れず
    /// インメモリ実装に差し替えられるようにする。
    /// </summary>
    public interface IKeyValueStore
    {
        bool HasKey(string key);
        string GetString(string key, string defaultValue = "");
        void SetString(string key, string value);
        void DeleteKey(string key);
        void Save();
    }
}
