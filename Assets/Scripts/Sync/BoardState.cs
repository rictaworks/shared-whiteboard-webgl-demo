using System.Collections.Generic;
using Whiteboard.Drawing;

namespace Whiteboard.Sync
{
    /// <summary>
    /// requirements.md 24章クラス図。ボードの内容（ストローク・操作ログ・最新連番）を保持し、
    /// 操作ログから見える状態（visibleStrokes）を導出する。状態遷移は25.3節に従う：
    /// RemoteActive → Committed → (Erased ⇄ Committed via undo_flag) / (Hidden ⇄ Committed via undo_flag)。
    /// </summary>
    public class BoardState
    {
        public readonly Dictionary<string, Stroke> Strokes = new Dictionary<string, Stroke>();
        public readonly List<Op> Ops = new List<Op>();
        public int LastSeq { get; private set; }

        private readonly Dictionary<string, Op> _opsById = new Dictionary<string, Op>();
        private readonly Dictionary<string, bool> _strokeAddUndone = new Dictionary<string, bool>();
        private readonly Dictionary<string, int> _strokeAddSeq = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _erasedCount = new Dictionary<string, int>();
        private readonly HashSet<int> _activeClearSeqs = new HashSet<int>();

        /// <summary>連番付きの確定操作を状態へ反映する。同一op_idの再適用は無視する（冪等）。</summary>
        public void ApplyOp(Op op, int seq)
        {
            if (op == null)
            {
                return;
            }
            if (_opsById.ContainsKey(op.OpId))
            {
                // 冪等性：同一操作IDは再適用しない。
                return;
            }

            op.Seq = seq;
            if (seq > LastSeq)
            {
                LastSeq = seq;
            }

            Ops.Add(op);
            _opsById[op.OpId] = op;

            switch (op.Kind)
            {
                case OpKind.StrokeAdd:
                    if (op.StrokeData != null)
                    {
                        Strokes[op.StrokeData.Id] = op.StrokeData;
                    }
                    _strokeAddUndone[op.StrokeId] = op.Undone;
                    _strokeAddSeq[op.StrokeId] = seq;
                    break;

                case OpKind.StrokeErase:
                    if (!op.Undone)
                    {
                        SetErasureActive(op, true);
                    }
                    break;

                case OpKind.Clear:
                    if (!op.Undone)
                    {
                        _activeClearSeqs.Add(seq);
                    }
                    break;
            }
        }

        /// <summary>取消フラグ変更を反映する。対象操作が未知の場合は無視する。</summary>
        public void ApplyUndoFlag(string opId, bool undone)
        {
            if (!_opsById.TryGetValue(opId, out var op))
            {
                return;
            }

            bool prevUndone = op.Undone;
            if (prevUndone == undone)
            {
                return;
            }
            op.Undone = undone;

            switch (op.Kind)
            {
                case OpKind.StrokeAdd:
                    _strokeAddUndone[op.StrokeId] = undone;
                    break;

                case OpKind.StrokeErase:
                    // undone=true になった（非活性化） / undone=false に戻った（再活性化）
                    SetErasureActive(op, !undone);
                    break;

                case OpKind.Clear:
                    if (undone)
                    {
                        _activeClearSeqs.Remove(op.Seq);
                    }
                    else
                    {
                        _activeClearSeqs.Add(op.Seq);
                    }
                    break;
            }
        }

        private void SetErasureActive(Op op, bool active)
        {
            if (op.TargetStrokeIds == null)
            {
                return;
            }
            foreach (var id in op.TargetStrokeIds)
            {
                _erasedCount.TryGetValue(id, out int count);
                count += active ? 1 : -1;
                if (count < 0)
                {
                    count = 0;
                }
                _erasedCount[id] = count;
            }
        }

        private bool IsClearedBy(string strokeId)
        {
            if (!_strokeAddSeq.TryGetValue(strokeId, out int addSeq))
            {
                return false;
            }
            foreach (var clearSeq in _activeClearSeqs)
            {
                if (clearSeq >= addSeq)
                {
                    return true;
                }
            }
            return false;
        }

        public bool IsVisible(string strokeId)
        {
            if (!Strokes.ContainsKey(strokeId))
            {
                return false;
            }
            if (_strokeAddUndone.TryGetValue(strokeId, out bool addUndone) && addUndone)
            {
                return false;
            }
            if (_erasedCount.TryGetValue(strokeId, out int erasedCount) && erasedCount > 0)
            {
                return false;
            }
            if (IsClearedBy(strokeId))
            {
                return false;
            }
            return true;
        }

        public List<Stroke> VisibleStrokes()
        {
            var result = new List<Stroke>();
            foreach (var kv in Strokes)
            {
                if (IsVisible(kv.Key))
                {
                    result.Add(kv.Value);
                }
            }
            return result;
        }

        /// <summary>
        /// 自分の操作を送信と同時に自画面へ適用した後、受領応答（ack）で連番を確定する。
        /// </summary>
        public void ConfirmSeq(string opId, int seq)
        {
            if (_opsById.TryGetValue(opId, out var op))
            {
                op.Seq = seq;
                if (seq > LastSeq)
                {
                    LastSeq = seq;
                }
            }
        }

        public Op FindOp(string opId)
        {
            return _opsById.TryGetValue(opId, out var op) ? op : null;
        }

        public bool HasOp(string opId)
        {
            return _opsById.ContainsKey(opId);
        }
    }
}
