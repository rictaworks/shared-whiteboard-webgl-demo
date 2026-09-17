using System.Collections.Generic;
using NUnit.Framework;
using Whiteboard.Sync;

namespace Whiteboard.Tests.EditMode
{
    public class HistoryManagerTests
    {
        private static Op MakeOp(string id, string author)
        {
            return Op.NewClear(id, author);
        }

        [Test]
        public void Undo_MovesLatestOpFromUndoStackToRedoStack()
        {
            var h = new HistoryManager();
            var op1 = MakeOp("op1", "参加者A");
            var op2 = MakeOp("op2", "参加者A");
            h.Record(op1);
            h.Record(op2);

            var undone = h.Undo();

            Assert.AreSame(op2, undone, "直近の操作がUndo対象になること");
            Assert.AreEqual(1, h.UndoStack.Count);
            Assert.AreEqual(1, h.RedoStack.Count);
        }

        [Test]
        public void Redo_MovesOpBackFromRedoStackToUndoStack()
        {
            var h = new HistoryManager();
            var op1 = MakeOp("op1", "参加者A");
            h.Record(op1);
            h.Undo();

            var redone = h.Redo();

            Assert.AreSame(op1, redone);
            Assert.AreEqual(1, h.UndoStack.Count);
            Assert.AreEqual(0, h.RedoStack.Count);
        }

        [Test]
        public void Record_NewOpAfterUndo_ClearsRedoStack()
        {
            var h = new HistoryManager();
            var op1 = MakeOp("op1", "参加者A");
            var op2 = MakeOp("op2", "参加者A");
            h.Record(op1);
            h.Record(op2);
            h.Undo();
            Assert.AreEqual(1, h.RedoStack.Count, "Undo直後はRedoスタックに1件あるはず");

            var op3 = MakeOp("op3", "参加者A");
            h.Record(op3);

            Assert.AreEqual(0, h.RedoStack.Count, "新規操作の発生でRedoスタックは破棄されること");
        }

        [Test]
        public void Undo_OnEmptyStack_ReturnsNull()
        {
            var h = new HistoryManager();
            Assert.IsNull(h.Undo());
        }

        [Test]
        public void RebuildFromOwn_OnlyIncludesOwnLabelOpsInSeqOrder()
        {
            var h = new HistoryManager();
            var opsInSeqOrder = new List<Op>
            {
                MakeOp("a1", "参加者A"),
                MakeOp("b1", "参加者B"),
                MakeOp("a2", "参加者A"),
            };

            h.RebuildFromOwn(opsInSeqOrder, "参加者A");

            Assert.AreEqual(2, h.UndoStack.Count, "自分（参加者A）の操作のみが対象になること");
            Assert.AreEqual("a1", h.UndoStack[0].OpId);
            Assert.AreEqual("a2", h.UndoStack[1].OpId);
        }

        [Test]
        public void RebuildFromOwn_TrailingUndoneOpsGoToRedoStack()
        {
            var h = new HistoryManager();
            var op1 = MakeOp("a1", "参加者A");
            var op2 = MakeOp("a2", "参加者A");
            op2.Undone = true; // 直近がUndo済みの状態から再構成される場合

            h.RebuildFromOwn(new List<Op> { op1, op2 }, "参加者A");

            Assert.AreEqual(1, h.UndoStack.Count);
            Assert.AreEqual("a1", h.UndoStack[0].OpId);
            Assert.AreEqual(1, h.RedoStack.Count);
            Assert.AreEqual("a2", h.RedoStack[0].OpId);
        }
    }
}
