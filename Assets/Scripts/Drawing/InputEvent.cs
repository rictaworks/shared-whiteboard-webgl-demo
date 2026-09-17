namespace Whiteboard.Drawing
{
    /// <summary>InputBridge.Drain() の1要素（requirements.md 7.1章）。</summary>
    public struct InputEvent
    {
        public int Id;
        public string Type; // "down" | "move" | "up" | "cancel" | "wheel"
        public string PointerType; // "mouse" | "pen" | "touch"
        public float X;
        public float Y;
        public int Button;
        public double T;
    }
}
