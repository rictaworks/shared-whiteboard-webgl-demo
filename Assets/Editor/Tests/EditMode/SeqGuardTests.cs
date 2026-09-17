using NUnit.Framework;
using Whiteboard.Sync;

namespace Whiteboard.Tests.EditMode
{
    public class SeqGuardTests
    {
        [Test]
        public void Accept_SequentialSeq_AdvancesExpected()
        {
            var guard = new SeqGuard();
            Assert.IsTrue(guard.Accept(1));
            Assert.AreEqual(2, guard.Expected);
            Assert.IsTrue(guard.Accept(2));
            Assert.AreEqual(3, guard.Expected);
        }

        [Test]
        public void Accept_GapDetected_RequestsRefetchAndDoesNotAdvanceExpected()
        {
            var guard = new SeqGuard();
            int capturedFrom = -1;
            int capturedTo = -1;
            bool refetchCalled = false;
            guard.RefetchRequested += (from, to) =>
            {
                refetchCalled = true;
                capturedFrom = from;
                capturedTo = to;
            };

            bool accepted = guard.Accept(5); // Expected=1のところ5番が来た＝2〜4が欠番

            Assert.IsFalse(accepted, "欠番がある場合は適用しないこと");
            Assert.IsTrue(refetchCalled, "再取得が要求されること");
            Assert.AreEqual(1, capturedFrom);
            Assert.AreEqual(4, capturedTo);
            Assert.AreEqual(1, guard.Expected, "欠番のまま後続を適用しないため、Expectedは進まないこと");
        }

        [Test]
        public void Accept_DuplicateOrPastSeq_IgnoredWithoutRefetch()
        {
            var guard = new SeqGuard();
            guard.Accept(1);
            guard.Accept(2); // Expected=3

            bool refetchCalled = false;
            guard.RefetchRequested += (f, t) => refetchCalled = true;

            bool accepted = guard.Accept(1); // 既に処理済みの過去分

            Assert.IsFalse(accepted);
            Assert.IsFalse(refetchCalled, "既知の重複では再取得を要求しないこと");
            Assert.AreEqual(3, guard.Expected);
        }

        [Test]
        public void SetExpected_AllowsResumingFromArbitraryPoint()
        {
            var guard = new SeqGuard();
            guard.SetExpected(101);
            Assert.AreEqual(101, guard.Expected);
            Assert.IsTrue(guard.Accept(101));
            Assert.AreEqual(102, guard.Expected);
        }
    }
}
