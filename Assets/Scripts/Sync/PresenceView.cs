using System.Collections.Generic;
using UnityEngine;

namespace Whiteboard.Sync
{
    public class Participant
    {
        public string Label;
        public string Color;
        public Vector2 CursorPosition;
        public bool HasCursor;
    }

    /// <summary>
    /// requirements.md 15章。参加者一覧・カーソル表示。
    /// </summary>
    public class PresenceView
    {
        public readonly Dictionary<string, Participant> Participants = new Dictionary<string, Participant>();

        public void SetParticipants(IEnumerable<(string label, string color)> list)
        {
            Participants.Clear();
            foreach (var (label, color) in list)
            {
                Participants[label] = new Participant { Label = label, Color = color };
            }
        }

        public void OnJoin(string label, string color = null)
        {
            if (!Participants.ContainsKey(label))
            {
                Participants[label] = new Participant { Label = label, Color = color };
            }
        }

        public void OnLeave(string label)
        {
            // 離脱した参加者のラベルは再利用しないが、一覧からは除去する。
            Participants.Remove(label);
        }

        public void UpdateCursor(string label, Vector2 position)
        {
            if (Participants.TryGetValue(label, out var participant))
            {
                participant.CursorPosition = position;
                participant.HasCursor = true;
            }
        }
    }
}
