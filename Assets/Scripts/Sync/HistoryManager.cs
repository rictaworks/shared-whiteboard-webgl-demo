using System.Collections.Generic;

namespace Whiteboard.Sync
{
    /// <summary>
    /// requirements.md 12章。Undo/Redoは自分の操作のみを対象とし、他の参加者の操作には作用しない。
    /// </summary>
    public class HistoryManager
    {
        public readonly List<Op> UndoStack = new List<Op>();
        public readonly List<Op> RedoStack = new List<Op>();

        /// <summary>自分の新規操作を記録する。Redo可能だった操作はRedo不能となる。</summary>
        public void Record(Op op)
        {
            UndoStack.Add(op);
            RedoStack.Clear();
        }

        /// <summary>直近の自分の操作をUndo対象として取り出す（呼び出し側がundo_flag送信を行う）。</summary>
        public Op Undo()
        {
            if (UndoStack.Count == 0)
            {
                return null;
            }
            var op = UndoStack[UndoStack.Count - 1];
            UndoStack.RemoveAt(UndoStack.Count - 1);
            RedoStack.Add(op);
            return op;
        }

        /// <summary>直近Undoした自分の操作をRedo対象として取り出す。</summary>
        public Op Redo()
        {
            if (RedoStack.Count == 0)
            {
                return null;
            }
            var op = RedoStack[RedoStack.Count - 1];
            RedoStack.RemoveAt(RedoStack.Count - 1);
            UndoStack.Add(op);
            return op;
        }

        /// <summary>
        /// ボードを開いた時、操作ログを連番順に走査し、自分のセッションが発信した操作のみで
        /// Undo/Redoスタックを再構成する。
        /// </summary>
        public void RebuildFromOwn(IEnumerable<Op> orderedOps, string myLabel)
        {
            UndoStack.Clear();
            RedoStack.Clear();

            var mine = new List<Op>();
            foreach (var op in orderedOps)
            {
                if (op.AuthorLabel == myLabel)
                {
                    mine.Add(op);
                }
            }

            // 末尾から undone=true が連続する区間はRedo対象、それ以前の undone=false はUndo対象。
            int i = mine.Count - 1;
            while (i >= 0 && mine[i].Undone)
            {
                RedoStack.Insert(0, mine[i]);
                i--;
            }
            for (int j = 0; j <= i; j++)
            {
                if (!mine[j].Undone)
                {
                    UndoStack.Add(mine[j]);
                }
            }
        }
    }
}
