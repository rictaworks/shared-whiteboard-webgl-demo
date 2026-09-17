package hub

import (
	"fmt"
	"sync"
	"testing"

	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/message"
)

// fakeConn is a minimal hub.Connection test double that records every
// message sent to it.
type fakeConn struct {
	id              string
	label           string
	participationID string

	mu     sync.Mutex
	sent   []any
	closed bool
}

func newFakeConn(id, label, participationID string) *fakeConn {
	return &fakeConn{id: id, label: label, participationID: participationID}
}

func (c *fakeConn) ConnID() string          { return c.id }
func (c *fakeConn) Label() string           { return c.label }
func (c *fakeConn) ParticipationID() string { return c.participationID }
func (c *fakeConn) Send(msg any) {
	c.mu.Lock()
	defer c.mu.Unlock()
	c.sent = append(c.sent, msg)
}
func (c *fakeConn) Close() {
	c.mu.Lock()
	defer c.mu.Unlock()
	c.closed = true
}

func (c *fakeConn) sentCount() int {
	c.mu.Lock()
	defer c.mu.Unlock()
	return len(c.sent)
}

func (c *fakeConn) sentOf(t *testing.T, index int) any {
	c.mu.Lock()
	defer c.mu.Unlock()
	if index >= len(c.sent) {
		t.Fatalf("fakeConn %s: expected at least %d sent messages, got %d", c.id, index+1, len(c.sent))
	}
	return c.sent[index]
}

func TestAssignSeq_DedupSameOpID(t *testing.T) {
	h := NewBoardHub("board1", 1)

	seq1, isNew1 := h.AssignSeq("op-1")
	if !isNew1 || seq1 != 1 {
		t.Fatalf("first assignment: got seq=%d isNew=%v, want seq=1 isNew=true", seq1, isNew1)
	}

	seq2, isNew2 := h.AssignSeq("op-1")
	if isNew2 {
		t.Fatalf("resend of op-1: expected isNew=false, got true")
	}
	if seq2 != seq1 {
		t.Fatalf("resend of op-1: got seq=%d, want %d (same as first)", seq2, seq1)
	}

	seq3, isNew3 := h.AssignSeq("op-2")
	if !isNew3 || seq3 != 2 {
		t.Fatalf("second distinct op: got seq=%d isNew=%v, want seq=2 isNew=true", seq3, isNew3)
	}
}

func TestAssignSeq_MonotonicUnderConcurrency(t *testing.T) {
	h := NewBoardHub("board1", 1)

	const n = 200
	seqs := make([]int, n)
	var wg sync.WaitGroup
	for i := 0; i < n; i++ {
		wg.Add(1)
		go func(i int) {
			defer wg.Done()
			seq, isNew := h.AssignSeq(fmt.Sprintf("op-%d", i))
			if !isNew {
				t.Errorf("op-%d: expected isNew=true", i)
			}
			seqs[i] = seq
		}(i)
	}
	wg.Wait()

	seen := make(map[int]bool, n)
	min, max := seqs[0], seqs[0]
	for _, s := range seqs {
		if seen[s] {
			t.Fatalf("duplicate seq %d assigned to two different ops", s)
		}
		seen[s] = true
		if s < min {
			min = s
		}
		if s > max {
			max = s
		}
	}
	if min != 1 || max != n {
		t.Fatalf("expected seqs to be exactly the contiguous range [1,%d], got min=%d max=%d", n, min, max)
	}
	if h.CurrentLastSeq() != n {
		t.Fatalf("CurrentLastSeq() = %d, want %d", h.CurrentLastSeq(), n)
	}
}

func TestTryAdmit_ParticipantLimit(t *testing.T) {
	h := NewBoardHub("board1", 1)

	for i := 0; i < MaxParticipants; i++ {
		c := newFakeConn(fmt.Sprintf("conn-%d", i), fmt.Sprintf("参加者%d", i), fmt.Sprintf("participation-%d", i))
		if !h.TryAdmit(c) {
			t.Fatalf("participant %d: expected admission within the limit", i)
		}
	}

	eleventh := newFakeConn("conn-10", "参加者10", "participation-10")
	if h.TryAdmit(eleventh) {
		t.Fatalf("11th distinct participant: expected TryAdmit to return false")
	}

	// A second connection from an already-admitted participation (e.g. a
	// reconnect or second tab) must not count against the limit.
	reconnect := newFakeConn("conn-0-again", "参加者0", "participation-0")
	if !h.TryAdmit(reconnect) {
		t.Fatalf("reconnect of an already-admitted participation_id: expected TryAdmit to return true even though the board is at MaxParticipants distinct participants")
	}
}

// TestTryAdmit_PerParticipationConnectionLimit guards against a single
// already-verified session opening unbounded concurrent connections to the
// same board: the MaxParticipants check above exempts same-participation
// reconnects entirely, so without a separate cap one session could multiply
// every broadcast's fan-out without limit (requirements.md 28章).
func TestTryAdmit_PerParticipationConnectionLimit(t *testing.T) {
	h := NewBoardHub("board1", 1)

	for i := 0; i < MaxConnectionsPerParticipation; i++ {
		c := newFakeConn(fmt.Sprintf("conn-p0-%d", i), "参加者0", "participation-0")
		if !h.TryAdmit(c) {
			t.Fatalf("connection %d for the same participation: expected admission within MaxConnectionsPerParticipation=%d", i, MaxConnectionsPerParticipation)
		}
	}

	extra := newFakeConn("conn-p0-extra", "参加者0", "participation-0")
	if h.TryAdmit(extra) {
		t.Fatalf("connection beyond MaxConnectionsPerParticipation for the same participation: expected TryAdmit to return false")
	}

	// A different participation must be unaffected by participation-0's cap.
	other := newFakeConn("conn-p1-0", "参加者1", "participation-1")
	if !h.TryAdmit(other) {
		t.Fatalf("first connection for a different participation: expected admission")
	}
}

func TestCatchUp_WithinAndOutsideBuffer(t *testing.T) {
	h := NewBoardHub("board1", 1)

	for i := 1; i <= 5; i++ {
		seq, _ := h.AssignSeq(fmt.Sprintf("op-%d", i))
		h.AppendRecentOp(message.Op{OpID: fmt.Sprintf("op-%d", i), Seq: seq, Kind: "clear"}, "session-1")
	}

	ops, needRefetch := h.CatchUp(2)
	if needRefetch {
		t.Fatalf("CatchUp(2): expected needRefetch=false (within buffer)")
	}
	if len(ops) != 3 {
		t.Fatalf("CatchUp(2): expected 3 ops (seq 3,4,5), got %d", len(ops))
	}
	for i, op := range ops {
		wantSeq := 3 + i
		if op.Seq != wantSeq {
			t.Fatalf("CatchUp(2)[%d]: seq=%d, want %d", i, op.Seq, wantSeq)
		}
	}

	// Already fully caught up: no ops, no refetch.
	ops, needRefetch = h.CatchUp(5)
	if needRefetch || len(ops) != 0 {
		t.Fatalf("CatchUp(5): expected empty/no-refetch when already caught up, got ops=%v needRefetch=%v", ops, needRefetch)
	}
}

func TestCatchUp_OutsideBufferRequestsRefetch(t *testing.T) {
	// Simulate a hub whose starting seq reflects history the in-memory
	// buffer never held (e.g. after a relay restart): nextSeq starts high,
	// recentOps starts empty.
	h := NewBoardHub("board1", 101)

	ops, needRefetch := h.CatchUp(50)
	if !needRefetch {
		t.Fatalf("CatchUp(50) with empty buffer but nextSeq=101: expected needRefetch=true")
	}
	if len(ops) != 0 {
		t.Fatalf("CatchUp(50): expected no ops when refetch is required, got %d", len(ops))
	}
}

func TestDropActiveOf_BroadcastsAbortAndPresence(t *testing.T) {
	h := NewBoardHub("board1", 1)

	author := newFakeConn("conn-author", "参加者A", "participation-A")
	other := newFakeConn("conn-other", "参加者B", "participation-B")
	if !h.TryAdmit(author) || !h.TryAdmit(other) {
		t.Fatalf("setup: expected both connections to be admitted")
	}

	h.TrackActiveDelta(author.ConnID(), "stroke-1")

	h.DropActiveOf(author.ConnID())

	if h.ConnectionCount() != 1 {
		t.Fatalf("ConnectionCount() after drop = %d, want 1", h.ConnectionCount())
	}

	if other.sentCount() != 2 {
		t.Fatalf("other connection: expected 2 broadcasts (active_abort, presence), got %d", other.sentCount())
	}
	abort, ok := other.sentOf(t, 0).(message.ActiveAbortOut)
	if !ok {
		t.Fatalf("other connection's first message: got %T, want message.ActiveAbortOut", other.sentOf(t, 0))
	}
	if abort.AuthorLabel != "参加者A" || abort.StrokeID != "stroke-1" {
		t.Fatalf("active_abort = %+v, want AuthorLabel=参加者A StrokeID=stroke-1", abort)
	}
	presence, ok := other.sentOf(t, 1).(message.Presence)
	if !ok {
		t.Fatalf("other connection's second message: got %T, want message.Presence", other.sentOf(t, 1))
	}
	if len(presence.Left) != 1 || presence.Left[0] != "参加者A" {
		t.Fatalf("presence = %+v, want Left=[参加者A]", presence)
	}
}

func TestFindOwnerByOpID(t *testing.T) {
	h := NewBoardHub("board1", 1)

	seq1, _ := h.AssignSeq("op-1")
	h.AppendRecentOp(message.Op{OpID: "op-1", Seq: seq1, Kind: "stroke_add"}, "session-owner")

	owner, ok := h.FindOwnerByOpID("op-1")
	if !ok || owner != "session-owner" {
		t.Fatalf("FindOwnerByOpID(op-1) = (%q, %v), want (session-owner, true)", owner, ok)
	}

	_, ok = h.FindOwnerByOpID("op-never-seen")
	if ok {
		t.Fatalf("FindOwnerByOpID(op-never-seen): expected ok=false")
	}
}

func TestFindOwnerByOpID_IgnoresSyntheticUndoEntries(t *testing.T) {
	h := NewBoardHub("board1", 1)

	seq1, _ := h.AssignSeq("op-1")
	h.AppendRecentOp(message.Op{OpID: "op-1", Seq: seq1, Kind: "stroke_add"}, "session-owner")

	// A synthetic undo entry recorded under a *different* session must not
	// shadow the genuine op's real owner.
	undoSeq := h.AssignUndoSeq()
	h.AppendUndoRecentOp(message.Op{OpID: "op-1", Seq: undoSeq, Kind: "stroke_add", Undone: true}, "session-someone-else")

	owner, ok := h.FindOwnerByOpID("op-1")
	if !ok || owner != "session-owner" {
		t.Fatalf("FindOwnerByOpID(op-1) after an undo by another session = (%q, %v), want (session-owner, true)", owner, ok)
	}
}

func TestRecentSince_ExcludesSyntheticUndoEntries(t *testing.T) {
	h := NewBoardHub("board1", 1)

	seq1, _ := h.AssignSeq("op-1")
	h.AppendRecentOp(message.Op{OpID: "op-1", Seq: seq1, Kind: "stroke_add"}, "session-1")

	undoSeq := h.AssignUndoSeq()
	h.AppendUndoRecentOp(message.Op{OpID: "op-1", Seq: undoSeq, Kind: "stroke_add", Undone: true}, "session-1")

	buffered, ok := h.RecentSince(0)
	if !ok {
		t.Fatalf("RecentSince(0): expected ok=true")
	}
	if len(buffered) != 1 {
		t.Fatalf("RecentSince(0): expected only the genuine op (undo entry excluded), got %d entries", len(buffered))
	}
	if buffered[0].Op.Seq != seq1 {
		t.Fatalf("RecentSince(0)[0].Op.Seq = %d, want %d", buffered[0].Op.Seq, seq1)
	}

	// But CatchUp (used for client catch-up) must include both.
	ops, needRefetch := h.CatchUp(0)
	if needRefetch {
		t.Fatalf("CatchUp(0): expected needRefetch=false")
	}
	if len(ops) != 2 {
		t.Fatalf("CatchUp(0): expected both the op and the undo entry, got %d", len(ops))
	}
}
