using System;
using NUnit.Framework;
using Whiteboard.Sync;

namespace Whiteboard.Tests.EditMode
{
    public class PrefsStoreTests
    {
        [Test]
        public void ComputeLogicalJstDate_BeforeThreeAm_BelongsToPreviousDay()
        {
            // UTC 2026-09-16 17:30 -> JST 2026-09-17 02:30（03:00未満） -> 論理日付は2026-09-16
            var utc = new DateTime(2026, 9, 16, 17, 30, 0, DateTimeKind.Utc);
            Assert.AreEqual("2026-09-16", PrefsStore.ComputeLogicalJstDate(utc));
        }

        [Test]
        public void ComputeLogicalJstDate_AtOrAfterThreeAm_BelongsToSameDay()
        {
            // UTC 2026-09-16 18:00 -> JST 2026-09-17 03:00（境界ちょうど） -> 論理日付は2026-09-17
            var utc = new DateTime(2026, 9, 16, 18, 0, 0, DateTimeKind.Utc);
            Assert.AreEqual("2026-09-17", PrefsStore.ComputeLogicalJstDate(utc));
        }

        [Test]
        public void ComputeLogicalJstDate_WellAfterBoundary_SameDay()
        {
            var utc = new DateTime(2026, 9, 16, 20, 0, 0, DateTimeKind.Utc); // JST 05:00
            Assert.AreEqual("2026-09-17", PrefsStore.ComputeLogicalJstDate(utc));
        }

        [Test]
        public void DiscardIfDateChanged_ReturnsFalse_WhenNoHeldDateYet()
        {
            var store = new PrefsStore(new InMemoryKeyValueStore());
            Assert.IsFalse(store.DiscardIfDateChanged(DateTime.UtcNow));
        }

        [Test]
        public void DiscardIfDateChanged_ReturnsTrue_WhenLogicalDateDiffers()
        {
            var store = new PrefsStore(new InMemoryKeyValueStore());
            store.SetHeldDate("2026-09-15");

            var utcNextLogicalDay = new DateTime(2026, 9, 16, 18, 30, 0, DateTimeKind.Utc); // JST 2026-09-17 03:30
            Assert.IsTrue(store.DiscardIfDateChanged(utcNextLogicalDay));
        }

        [Test]
        public void DiscardIfDateChanged_ReturnsFalse_WhenSameLogicalDate()
        {
            var store = new PrefsStore(new InMemoryKeyValueStore());
            store.SetHeldDate("2026-09-17");

            var utcSameLogicalDay = new DateTime(2026, 9, 16, 20, 0, 0, DateTimeKind.Utc); // JST 2026-09-17 05:00
            Assert.IsFalse(store.DiscardIfDateChanged(utcSameLogicalDay));
        }

        [Test]
        public void SetUnsentQueue_AlsoUpdatesHeldDate()
        {
            var store = new PrefsStore(new InMemoryKeyValueStore());
            var ops = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>();
            var utc = new DateTime(2026, 9, 16, 20, 0, 0, DateTimeKind.Utc); // JST 2026-09-17 05:00

            store.SetUnsentQueue("board1", ops, utc);

            Assert.AreEqual("2026-09-17", store.GetHeldDate());
        }

        [Test]
        public void GetUnsentQueue_ReturnsEmptyList_WhenNothingStored()
        {
            var store = new PrefsStore(new InMemoryKeyValueStore());
            var result = store.GetUnsentQueue("no-such-board");
            Assert.AreEqual(0, result.Count);
        }
    }
}
