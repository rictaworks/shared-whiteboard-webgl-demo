using System;
using NUnit.Framework;
using Whiteboard.Sync;

namespace Whiteboard.Tests.EditMode
{
    public class SendQueueTests
    {
        private static readonly DateTime FixedUtcNow = new DateTime(2026, 9, 17, 10, 0, 0, DateTimeKind.Utc);

        [Test]
        public void ResendAll_KeepsSameOpId_WhenAckNotYetReceived()
        {
            var prefs = new PrefsStore(new InMemoryKeyValueStore());
            var queue = new SendQueue(prefs, "board1", () => FixedUtcNow);
            var op = Op.NewClear("op-123", "参加者A");
            queue.Enqueue(op);

            var resent = queue.ResendAll();
            Assert.AreEqual(1, resent.Count);
            Assert.AreEqual("op-123", resent[0].OpId, "受領前の再送は同一op_idのまま行うこと");

            // 受領応答が来る前にもう一度再接続した想定：再度呼んでも同一IDのまま。
            var resentAgain = queue.ResendAll();
            Assert.AreEqual(1, resentAgain.Count);
            Assert.AreEqual("op-123", resentAgain[0].OpId);
        }

        [Test]
        public void Ack_RemovesOpFromPending()
        {
            var prefs = new PrefsStore(new InMemoryKeyValueStore());
            var queue = new SendQueue(prefs, "board1", () => FixedUtcNow);
            var op = Op.NewClear("op-123", "参加者A");
            queue.Enqueue(op);

            queue.Ack("op-123", 42);

            Assert.AreEqual(0, queue.ResendAll().Count, "受領応答後は再送対象から外れること");
        }

        [Test]
        public void PersistAndRestore_RoundTripsPendingOps()
        {
            var prefs = new PrefsStore(new InMemoryKeyValueStore());
            var queue = new SendQueue(prefs, "board1", () => FixedUtcNow);
            var op = Op.NewClear("op-abc", "参加者A");
            queue.Enqueue(op);
            queue.Persist();

            var restoredQueue = new SendQueue(prefs, "board1", () => FixedUtcNow);
            restoredQueue.Restore();

            Assert.AreEqual(1, restoredQueue.Pending.Count);
            Assert.AreEqual("op-abc", restoredQueue.Pending[0].OpId);
        }

        [Test]
        public void FlushOnPageHide_InvokesDirectSendForEveryPendingOp()
        {
            var prefs = new PrefsStore(new InMemoryKeyValueStore());
            var queue = new SendQueue(prefs, "board1", () => FixedUtcNow);
            queue.Enqueue(Op.NewClear("op-1", "参加者A"));
            queue.Enqueue(Op.NewClear("op-2", "参加者A"));

            int callCount = 0;
            queue.FlushOnPageHide(op => callCount++);

            Assert.AreEqual(2, callCount, "未送信の操作すべてが直接送出されること");
        }
    }
}
