using System.Collections.Generic;
using Whiteboard.Bridges;
using Whiteboard.Json;

namespace Whiteboard.Drawing
{
    /// <summary>
    /// InputBridge.Drain() を毎フレーム読み、パース済みの InputEvent 列として返す。
    /// </summary>
    public class InputReader
    {
        public List<InputEvent> ReadFrame()
        {
            string json = InputBridge.Drain();
            return Parse(json);
        }

        public static List<InputEvent> Parse(string json)
        {
            var result = new List<InputEvent>();
            var arr = MiniJson.Deserialize(json) as List<object>;
            if (arr == null)
            {
                return result;
            }

            foreach (var item in arr)
            {
                if (item is Dictionary<string, object> d)
                {
                    result.Add(new InputEvent
                    {
                        Id = ToInt(d, "id"),
                        Type = ToStr(d, "type"),
                        PointerType = ToStr(d, "pointerType"),
                        X = ToFloat(d, "x"),
                        Y = ToFloat(d, "y"),
                        Button = ToInt(d, "button"),
                        T = ToDouble(d, "t"),
                    });
                }
            }
            return result;
        }

        private static int ToInt(Dictionary<string, object> d, string key)
        {
            return d.TryGetValue(key, out var v) && v != null ? (int)System.Convert.ToDouble(v) : 0;
        }

        private static float ToFloat(Dictionary<string, object> d, string key)
        {
            return d.TryGetValue(key, out var v) && v != null ? (float)System.Convert.ToDouble(v) : 0f;
        }

        private static double ToDouble(Dictionary<string, object> d, string key)
        {
            return d.TryGetValue(key, out var v) && v != null ? System.Convert.ToDouble(v) : 0.0;
        }

        private static string ToStr(Dictionary<string, object> d, string key)
        {
            return d.TryGetValue(key, out var v) && v != null ? v.ToString() : "";
        }
    }
}
