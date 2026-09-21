using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Whiteboard.Data;
using Whiteboard.Drawing;
using Whiteboard.Sync;

namespace Whiteboard.Tests.EditMode
{
    /// <summary>
    /// Issue #26（Redoで復元されない）・#27（消しゴムで消えない）の切り分け。
    /// 単体テストは各クラスを個別に検証済みだが、実機で失敗したのは
    /// Boot.cs が組み合わせて呼び出す一連の流れなので、その順序どおりに再現する。
    /// </summary>
    public class EraseUndoRedoFlowTests
    {
        private const float EraserRadiusWorld = 10f; // Boot.EraserRadiusWorld と同値

        private static Stroke MakeHorizontalStroke(string id)
        {
            return new Stroke
            {
                Id = id,
                Tool = ToolKind.Pen,
                Color = "#1A1A1A",
                Width = StrokeWidth.Medium,
                Points = new List<Vector2> { new Vector2(0f, 0f), new Vector2(100f, 0f) },
                AuthorLabel = "参加者A",
            };
        }

        /// <summary>Boot.FinishLocalStroke() と同じ手順（履歴記録→楽観適用）。</summary>
        private static Op DrawStroke(BoardState state, HistoryManager history, string strokeId)
        {
            var op = Op.NewStrokeAdd(strokeId, MakeHorizontalStroke(strokeId), "参加者A");
            history.Record(op);
            state.ApplyOp(op, state.LastSeq);
            return op;
        }

        [Test]
        public void Eraser_PathOverStroke_HitsAndErases()
        {
            var state = new BoardState();
            var history = new HistoryManager();
            DrawStroke(state, history, "s1");

            // Boot.HandleDown：押下点1点のみの経路で当たり判定を行う。
            var eraserPath = new List<Vector2> { new Vector2(50f, 0f) };
            var hits = EraserHitTester.Hit(eraserPath, EraserRadiusWorld, state.VisibleStrokes());

            Assert.AreEqual(1, hits.Count, "線の真上を押下したら当たること");

            // Boot.TryEraseAt：消去opを楽観適用する。
            var eraseOp = Op.NewStrokeErase(Guid.NewGuid().ToString("N"), hits, "参加者A");
            state.ApplyOp(eraseOp, state.LastSeq);

            Assert.IsFalse(state.IsVisible("s1"), "消去opの適用でストロークが不可視になること");
        }

        [Test]
        public void Eraser_DragAcrossStroke_HitsAndErases()
        {
            var state = new BoardState();
            var history = new HistoryManager();
            DrawStroke(state, history, "s1");

            // 実機の drag は down と move の2点しか届かないことがあるため、その形を再現する。
            var eraserPath = new List<Vector2> { new Vector2(20f, 0f), new Vector2(80f, 0f) };
            var hits = EraserHitTester.Hit(eraserPath, EraserRadiusWorld, state.VisibleStrokes());

            Assert.AreEqual(1, hits.Count, "線に沿ってなぞったら当たること");
        }

        [Test]
        public void Eraser_EraseIsUndoable_AndDoesNotTargetThePreviousDraw()
        {
            var state = new BoardState();
            var history = new HistoryManager();
            DrawStroke(state, history, "s1");

            // Boot.TryEraseAt と同じ手順（履歴記録→楽観適用）。Issue #28 以前は
            // 履歴記録が漏れており、Undoが直前の描画opを取り消していた。
            var hits = new List<string> { "s1" };
            var eraseOp = Op.NewStrokeErase(Guid.NewGuid().ToString("N"), hits, "参加者A");
            history.Record(eraseOp);
            state.ApplyOp(eraseOp, state.LastSeq);
            Assert.IsFalse(state.IsVisible("s1"));

            var undone = history.Undo();

            Assert.AreSame(eraseOp, undone, "Undoの対象が消去opであること");

            state.ApplyUndoFlag(undone.OpId, true);
            Assert.IsTrue(state.IsVisible("s1"), "消去をUndoするとストロークが戻ること");
        }

        [Test]
        public void UndoThenRedo_RestoresStroke()
        {
            var state = new BoardState();
            var history = new HistoryManager();
            var op = DrawStroke(state, history, "s1");
            Assert.IsTrue(state.IsVisible("s1"));

            // Undo：Boot.OnUndoClicked → サーバーの undo_flag_confirmed 受信まで。
            var undoTarget = history.Undo();
            Assert.IsNotNull(undoTarget, "Undo対象が取り出せること");
            state.ApplyUndoFlag(undoTarget.OpId, true);
            Assert.IsFalse(state.IsVisible("s1"), "Undoでストロークが消えること");

            // Redo：Boot.OnRedoClicked → undo_flag_confirmed(undone=false) 受信まで。
            var redoTarget = history.Redo();
            Assert.IsNotNull(redoTarget, "Redo対象が取り出せること（RedoStackが空でないこと）");
            state.ApplyUndoFlag(redoTarget.OpId, false);

            Assert.IsTrue(state.IsVisible("s1"), "Redoでストロークが復元されること");
        }

        /// <summary>
        /// 本番実機（2026-09-21）で発見：stroke_add → Undo → Redo → 別のstroke_add →
        /// Undo → 全消去、という順序で全消去を行うと、最初に描いた（Undo→Redoで復元済みの）
        /// 線だけが消えずに残る現象を再現する。ConfirmSeq が op.Seq は更新するのに
        /// _strokeAddSeq/_activeClearSeqs（楽観適用時の"暫定seq"を保持する内部辞書）を
        /// 更新しないため、本物のサーバーseqではなく暫定値のまま比較され続けることを疑う。
        /// </summary>
        [Test]
        public void ClearAfterUndoRedoInterleaved_HidesTheEarlierRestoredStroke()
        {
            var state = new BoardState();
            var history = new HistoryManager();

            // 1. stroke1を描く（楽観適用：暫定seq=LastSeq=0）→ ackでseq=1に確定。
            var op1 = DrawStroke(state, history, "s1");
            state.ConfirmSeq(op1.OpId, 1);
            Assert.IsTrue(state.IsVisible("s1"), "確定直後は表示されていること");

            // 2. stroke1をUndo（サーバーseq=2）→Redo（サーバーseq=3）。
            state.ApplyUndoFlag(op1.OpId, true);
            Assert.IsFalse(state.IsVisible("s1"));
            state.ApplyUndoFlag(op1.OpId, false);
            Assert.IsTrue(state.IsVisible("s1"), "Redoで復元されていること");

            // 3. stroke2を描く（楽観適用：暫定seq=LastSeq=1）→ ackでseq=4に確定。
            var op2 = DrawStroke(state, history, "s2");
            state.ConfirmSeq(op2.OpId, 4);
            Assert.IsTrue(state.IsVisible("s2"));

            // 4. stroke2をUndo（サーバーseq=5）。
            state.ApplyUndoFlag(op2.OpId, true);
            Assert.IsFalse(state.IsVisible("s2"));

            // 5. 全消去（楽観適用：暫定seq=LastSeq=4）→ ackでseq=6に確定。
            var clearOp = Op.NewClear(Guid.NewGuid().ToString("N"), "参加者A");
            history.Record(clearOp);
            state.ApplyOp(clearOp, state.LastSeq);
            state.ConfirmSeq(clearOp.OpId, 6);

            // 本番実機での観測：stroke1（サーバーseq=1、全消去seq=6より前）が
            // 消えずに残ってしまう。
            Assert.IsFalse(state.IsVisible("s1"),
                "全消去（サーバーseq=6）はそれより前のstroke1（サーバーseq=1）を消すはず");
        }

        [Test]
        public void UndoThenRedo_AfterRejoin_StillRestoresStroke()
        {
            // 実機では再接続で join_accepted が再発火し、RebuildFromOwn が履歴を作り直す。
            var state = new BoardState();
            var history = new HistoryManager();
            var op = DrawStroke(state, history, "s1");

            var undoTarget = history.Undo();
            state.ApplyUndoFlag(undoTarget.OpId, true);

            // 再接続相当：自分の操作ログから履歴を再構成する。
            history.RebuildFromOwn(state.Ops, "参加者A");

            var redoTarget = history.Redo();
            Assert.IsNotNull(redoTarget, "再接続後もRedo対象が残ること");
            state.ApplyUndoFlag(redoTarget.OpId, false);

            Assert.IsTrue(state.IsVisible("s1"), "再接続を挟んでもRedoで復元されること");
        }
    }
}
