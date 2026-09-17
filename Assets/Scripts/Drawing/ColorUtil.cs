using UnityEngine;

namespace Whiteboard.Drawing
{
    public static class ColorUtil
    {
        public static Color HexToColor(string hex, float alpha = 1f)
        {
            if (string.IsNullOrEmpty(hex))
            {
                return new Color(0f, 0f, 0f, alpha);
            }
            if (ColorUtility.TryParseHtmlString(hex, out var c))
            {
                c.a = alpha;
                return c;
            }
            return new Color(0f, 0f, 0f, alpha);
        }
    }
}
