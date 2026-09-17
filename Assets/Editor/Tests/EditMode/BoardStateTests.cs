using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Whiteboard.Data;
using Whiteboard.Drawing;
using Whiteboard.Sync;

namespace Whiteboard.Tests.EditMode
{
    public class BoardStateTests
    {
        private static Stroke MakeStroke(string id)
        {
            return new Stroke
            {
                Id = id,
                Tool = ToolKind.Pen,
                Color = "#1A1A1A",
                Width = StrokeWidth.Medium,
                Points = new List<Vector2> { Vector2.zero, Vector2.one },
                AuthorLabel = "参加者A",
            };
        }

        [Test]
        public void ApplyOp_StrokeAdd_MakesStrokeVisible()
        {
            var state = new BoardState();
            var op = Op.NewStrokeAdd("op1", MakeStroke("s1"), "参加者A");
            state.ApplyOp(op, 1);

            Assert.IsTrue(state.IsVisible("s1"));
            Assert.AreEqual(1, state.LastSeq);
        }

        [Test]
        public void ApplyOp_SameOpIdTwice_IsIgnoredSecondTime()
        {
            var state = new BoardState();
            var op = Op.NewStrokeAdd("op1", MakeStroke("s1"), "参加者A");
            state.ApplyOp(op, 1);
            state.ApplyOp(op, 2); // 冪等性：再適用は無視

            Assert.AreEqual(1, state.LastSeq);
            Assert.AreEqual(1, state.Ops.Count);
        }

        [Test]
        public void StrokeErase_HidesStroke_AndUndoFlagRestoresIt()
        {
            var state = new BoardState();
            state.ApplyOp(Op.NewStrokeAdd("add1", MakeStroke("s1"), "参加者A"), 1);
            var eraseOp = Op.NewStrokeErase("erase1", new List<string> { "s1" }, "参加者B");
            state.ApplyOp(eraseOp, 2);

            Assert.IsFalse(state.IsVisible("s1"), "消去後は非表示になること");

            state.ApplyUndoFlag("erase1", true);
            Assert.IsTrue(state.IsVisible("s1"), "消去操作のUndoで再表示されること");
        }

        [Test]
        public void ConcurrentErase_BothActive_UndoingOneStillErased()
        {
            var state = new BoardState();
            state.ApplyOp(Op.NewStrokeAdd("add1", MakeStroke("s1"), "参加者A"), 1);
            state.ApplyOp(Op.NewStrokeErase("eraseA", new List<string> { "s1" }, "参加者A"), 2);
            state.ApplyOp(Op.NewStrokeErase("eraseB", new List<string> { "s1" }, "参加者B"), 3);

            Assert.IsFalse(state.IsVisible("s1"));

            // 参加者Aが自分の消去をUndoしても、参加者Bの消去がまだ有効なので消えたままであること。
            state.ApplyUndoFlag("eraseA", true);
            Assert.IsFalse(state.IsVisible("s1"), "他者の消去が有効な限り非表示のままであること");

            state.ApplyUndoFlag("eraseB", true);
            Assert.IsTrue(state.IsVisible("s1"), "両方Undoされれば再表示されること");
        }

        [Test]
        public void Clear_HidesStrokesAddedBeforeIt_ButNotStrokesAddedAfter()
        {
            var state = new BoardState();
            state.ApplyOp(Op.NewStrokeAdd("add1", MakeStroke("s1"), "参加者A"), 1);
            state.ApplyOp(Op.NewClear("clear1", "参加者A"), 2);
            state.ApplyOp(Op.NewStrokeAdd("add2", MakeStroke("s2"), "参加者A"), 3);

            Assert.IsFalse(state.IsVisible("s1"), "全消去より前のストロークは非表示");
            Assert.IsTrue(state.IsVisible("s2"), "全消去より後のストロークは表示されたままであること");
        }

        [Test]
        public void StrokeAddUndone_HidesOwnStroke()
        {
            var state = new BoardState();
            state.ApplyOp(Op.NewStrokeAdd("add1", MakeStroke("s1"), "参加者A"), 1);
            state.ApplyUndoFlag("add1", true);

            Assert.IsFalse(state.IsVisible("s1"), "追加自体がUndoされたストロークは非表示になること");
        }
    }
}
