using System;
using System.Collections.Generic;

namespace Whiteboard.Sync
{
    /// <summary>
    /// requirements.md 13章・14章。未送信op管理・PlayerPrefs永続化・ページ終了時の直接送出。
    /// 受領応答を得る前に再接続した場合、保留中の操作は同一op_idで再送する（冪等性の前提）。
    /// </summary>
    public class SendQueue
    {
        public readonly List<Op> Pending = new List<Op>();

        private readonly PrefsStore _prefs;
        private readonly string _boardId;
        private readonly Func<DateTime> _clock;

        public SendQueue(PrefsStore prefs, string boardId, Func<DateTime> clock = null)
        {
            _prefs = prefs;
            _boardId = boardId;
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        public void Enqueue(Op op)
        {
            Pending.Add(op);
        }

        /// <summary>受領応答を反映し、対応する保留opを取り除く。</summary>
        public void Ack(string opId, int seq)
        {
            Pending.RemoveAll(o => o.OpId == opId);
        }

        /// <summary>保留中の全操作を同一op_idのまま再送対象として返す。</summary>
        public List<Op> ResendAll()
        {
            return new List<Op>(Pending);
        }

        /// <summary>未送信キューをPlayerPrefsへ書き込む（保持日付も同時更新）。</summary>
        public void Persist()
        {
            var list = new List<Dictionary<string, object>>();
            foreach (var op in Pending)
            {
                list.Add(op.ToDict());
            }
            _prefs.SetUnsentQueue(_boardId, list, _clock());
        }

        /// <summary>PlayerPrefsから未送信キューを復元する。</summary>
        public void Restore()
        {
            Pending.Clear();
            foreach (var d in _prefs.GetUnsentQueue(_boardId))
            {
                Pending.Add(Op.FromDict(d));
            }
        }

        /// <summary>ページ終了通知を受けた場合、PlayerPrefsへの書き込みを待たず直接送出する。</summary>
        public void FlushOnPageHide(Action<Op> directSend)
        {
            if (directSend == null)
            {
                return;
            }
            foreach (var op in Pending)
            {
                directSend(op);
            }
        }
    }
}
