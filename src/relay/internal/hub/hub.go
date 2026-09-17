// Package hub implements the per-board relay hub: connection membership,
// monotonic sequence assignment, idempotent op dedup, the recent-ops buffer
// used for catch-up, and in-progress-stroke bookkeeping for disconnect
// cleanup (requirements.md 11章 / 24章 BoardHub).
package hub

import (
	"sync"
	"time"

	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/message"
)

const (
	// MaxRecentOps is the recent-buffer size cap (requirements.md 11.4節).
	MaxRecentOps = 500
	// RecentOpsTTL is the recent-buffer time cap (requirements.md 11.4節).
	RecentOpsTTL = 5 * time.Minute
	// MaxParticipants is the per-board concurrent connection cap (17章),
	// not counting extra connections that share an already-connected
	// participation_id (reconnects don't count against the limit).
	MaxParticipants = 10
	// MaxConnectionsPerParticipation bounds how many concurrent connections
	// a single participation (one session_key's join on this board) may
	// hold at once. A genuine reconnect race or a couple of browser tabs
	// legitimately overlap briefly, but nothing legitimate needs more than
	// a handful of simultaneous sockets. Without this cap, a single
	// already-verified session could open unbounded connections -- each
	// one excluded from the MaxParticipants count above by the reconnect
	// exemption -- and multiply every broadcast's fan-out without limit
	// (requirements.md 28章「1セッションあたりの…接続数に上限を設けること」).
	MaxConnectionsPerParticipation = 3
)

// Connection is the minimal surface BoardHub needs from a live WebSocket
// connection. The ws package implements this; hub stays free of any
// WebSocket/Gin dependency so it can be unit tested with fakes.
type Connection interface {
	ConnID() string
	Label() string
	ParticipationID() string
	Send(msg any)
	Close()
}

// bufferedOp is one entry in the recent-ops ring buffer. origin distinguishes
// genuine confirmed operations (which must also be persisted via the ops
// batch endpoint) from synthetic undo-flag-change entries (which exist only
// so a catching-up client's Op-shaped catch_up_ops array can carry the
// updated undone flag; they must never be sent to the Rails ops-batch
// endpoint, which only understands stroke_add/stroke_erase/clear).
type bufferedOp struct {
	op         message.Op
	sessionKey string
	origin     string // "op" | "undo"
	storedAt   time.Time
}

// BufferedOp is the subset of buffered-op data the persistence writer needs
// to refill a gap reported by Rails.
type BufferedOp struct {
	Op         message.Op
	SessionKey string
}

// BoardHub is the single relay hub for one board: connection set, sequence
// counter, idempotency map, and recent-ops buffer.
type BoardHub struct {
	mu sync.Mutex

	boardID string

	connections   map[string]Connection      // connID -> Connection
	activeStrokes map[string]map[string]bool // connID -> set of in-progress stroke IDs

	nextSeq   int
	opIDToSeq map[string]int // op_id -> assigned seq, for idempotent resend

	recentOps []bufferedOp

	lastActivity time.Time
}

// NewBoardHub creates a hub whose next assigned sequence number is startSeq
// (normally Rails' last_seq + 1; requirements.md 11.5節).
func NewBoardHub(boardID string, startSeq int) *BoardHub {
	if startSeq < 1 {
		startSeq = 1
	}
	return &BoardHub{
		boardID:       boardID,
		connections:   make(map[string]Connection),
		activeStrokes: make(map[string]map[string]bool),
		nextSeq:       startSeq,
		opIDToSeq:     make(map[string]int),
		lastActivity:  time.Now(),
	}
}

// BoardID returns the board this hub serves.
func (h *BoardHub) BoardID() string { return h.boardID }

// TryAdmit attempts to register a new connection, enforcing MaxParticipants.
// A reconnecting participation (same ParticipationID as an already-connected
// connection) never counts against MaxParticipants, so multi-tab / reconnect
// is allowed once verified -- but only up to MaxConnectionsPerParticipation
// concurrent sockets for that same participation, so a single session can't
// multiply broadcast fan-out without limit (28章). Returns false if the
// board is already full of *other* participants, or if this participation
// already holds its own connection cap.
func (h *BoardHub) TryAdmit(c Connection) bool {
	h.mu.Lock()
	defer h.mu.Unlock()

	others := 0
	sameParticipation := 0
	for _, existing := range h.connections {
		if existing.ParticipationID() != c.ParticipationID() {
			others++
		} else {
			sameParticipation++
		}
	}
	if others >= MaxParticipants {
		return false
	}
	if sameParticipation >= MaxConnectionsPerParticipation {
		return false
	}
	h.connections[c.ConnID()] = c
	h.lastActivity = time.Now()
	return true
}

// ConnectionCount returns the number of currently registered connections.
func (h *BoardHub) ConnectionCount() int {
	h.mu.Lock()
	defer h.mu.Unlock()
	return len(h.connections)
}

// LastActivity returns the last time a connection was added/removed or an op
// was confirmed; used by the manager's idle-hub GC.
func (h *BoardHub) LastActivity() time.Time {
	h.mu.Lock()
	defer h.mu.Unlock()
	return h.lastActivity
}

// CurrentLastSeq returns the most recently assigned sequence number (0 if
// none has been assigned yet).
func (h *BoardHub) CurrentLastSeq() int {
	h.mu.Lock()
	defer h.mu.Unlock()
	return h.nextSeq - 1
}

// ParticipantsSnapshot returns the distinct (label, color) pairs of
// currently connected participants, for join_accepted.participants.
func (h *BoardHub) ParticipantsSnapshot() []message.Participant {
	h.mu.Lock()
	defer h.mu.Unlock()
	seen := make(map[string]bool)
	out := make([]message.Participant, 0, len(h.connections))
	for _, c := range h.connections {
		label := c.Label()
		if seen[label] {
			continue
		}
		seen[label] = true
		out = append(out, message.Participant{Label: label})
	}
	return out
}

// AssignSeq assigns the next sequence number to opID, unless opID has
// already been assigned one (idempotent resend), in which case the existing
// seq is returned and isNew is false (requirements.md 11.2節).
func (h *BoardHub) AssignSeq(opID string) (seq int, isNew bool) {
	h.mu.Lock()
	defer h.mu.Unlock()
	if existing, ok := h.opIDToSeq[opID]; ok {
		return existing, false
	}
	seq = h.nextSeq
	h.nextSeq++
	h.opIDToSeq[opID] = seq
	h.lastActivity = time.Now()
	return seq, true
}

// AssignUndoSeq assigns a fresh sequence number to an undo-flag change
// event. Unlike AssignSeq, there is no idempotency key defined for
// undo_flag in the contract, so every valid undo_flag message consumes a
// new seq (documented simplification; see final report).
func (h *BoardHub) AssignUndoSeq() int {
	h.mu.Lock()
	defer h.mu.Unlock()
	seq := h.nextSeq
	h.nextSeq++
	h.lastActivity = time.Now()
	return seq
}

// FindKindByOpID searches the recent-ops buffer for the kind of the op
// identified by opID (used to build a synthetic catch-up entry for an undo
// flag change). ok is false if the op has already fallen out of the buffer.
func (h *BoardHub) FindKindByOpID(opID string) (kind string, ok bool) {
	h.mu.Lock()
	defer h.mu.Unlock()
	for i := len(h.recentOps) - 1; i >= 0; i-- {
		if h.recentOps[i].op.OpID == opID {
			return h.recentOps[i].op.Kind, true
		}
	}
	return "", false
}

// FindOwnerByOpID searches the recent-ops buffer for the session key that
// authored the (genuine, non-synthetic) op identified by opID. ok is false
// if that op has already fallen out of the buffer, in which case ownership
// cannot be determined locally and the caller must fall back to asking
// Rails (requirements.md 12章: Undo は自分の操作のみを対象とする).
func (h *BoardHub) FindOwnerByOpID(opID string) (sessionKey string, ok bool) {
	h.mu.Lock()
	defer h.mu.Unlock()
	for i := len(h.recentOps) - 1; i >= 0; i-- {
		if h.recentOps[i].origin == "op" && h.recentOps[i].op.OpID == opID {
			return h.recentOps[i].sessionKey, true
		}
	}
	return "", false
}

func (h *BoardHub) trimRecentOpsLocked() {
	cutoff := time.Now().Add(-RecentOpsTTL)
	start := 0
	for i, r := range h.recentOps {
		if r.storedAt.After(cutoff) {
			start = i
			break
		}
		start = i + 1
	}
	h.recentOps = h.recentOps[start:]
	if len(h.recentOps) > MaxRecentOps {
		h.recentOps = h.recentOps[len(h.recentOps)-MaxRecentOps:]
	}
}

// AppendRecentOp records a confirmed operation (kind stroke_add /
// stroke_erase / clear) in the recent buffer. It must be called once per
// newly-assigned seq (i.e. only when AssignSeq reported isNew).
func (h *BoardHub) AppendRecentOp(op message.Op, sessionKey string) {
	h.mu.Lock()
	defer h.mu.Unlock()
	h.recentOps = append(h.recentOps, bufferedOp{op: op, sessionKey: sessionKey, origin: "op", storedAt: time.Now()})
	h.trimRecentOpsLocked()
	h.lastActivity = time.Now()
}

// AppendUndoRecentOp records a synthetic catch-up entry for an undo-flag
// change (see bufferedOp doc comment). It is never returned by RecentSince,
// only by CatchUp.
func (h *BoardHub) AppendUndoRecentOp(op message.Op, sessionKey string) {
	h.mu.Lock()
	defer h.mu.Unlock()
	h.recentOps = append(h.recentOps, bufferedOp{op: op, sessionKey: sessionKey, origin: "undo", storedAt: time.Now()})
	h.trimRecentOpsLocked()
	h.lastActivity = time.Now()
}

// CatchUp returns every buffered op/undo-flag-change with seq > fromSeq, in
// order. needRefetch is true if the buffer cannot prove continuity from
// fromSeq (i.e. the requested range may have gaps), in which case the caller
// must fetch the range from Rails instead (requirements.md 11.4節).
func (h *BoardHub) CatchUp(fromSeq int) (ops []message.Op, needRefetch bool) {
	h.mu.Lock()
	defer h.mu.Unlock()
	h.trimRecentOpsLocked()

	if fromSeq >= h.nextSeq-1 {
		// Caller already has everything ever assigned.
		return nil, false
	}
	if len(h.recentOps) == 0 {
		// Something was assigned (fromSeq < nextSeq-1) but the buffer is
		// empty (trimmed away): we cannot prove continuity.
		return nil, true
	}
	oldest := h.recentOps[0].op.Seq
	if fromSeq < oldest-1 {
		return nil, true
	}
	result := make([]message.Op, 0, len(h.recentOps))
	for _, r := range h.recentOps {
		if r.op.Seq > fromSeq {
			result = append(result, r.op)
		}
	}
	return result, false
}

// RecentSince returns the genuine (non-synthetic) confirmed ops with
// seq > fromSeqExclusive, for use by the persistence writer when refilling a
// gap reported by Rails. ok is false if the buffer cannot prove continuity.
func (h *BoardHub) RecentSince(fromSeqExclusive int) (ops []BufferedOp, ok bool) {
	h.mu.Lock()
	defer h.mu.Unlock()
	h.trimRecentOpsLocked()

	if fromSeqExclusive >= h.nextSeq-1 {
		return nil, true
	}
	if len(h.recentOps) == 0 {
		return nil, false
	}
	oldest := h.recentOps[0].op.Seq
	if fromSeqExclusive < oldest-1 {
		return nil, false
	}
	result := make([]BufferedOp, 0, len(h.recentOps))
	for _, r := range h.recentOps {
		if r.origin == "op" && r.op.Seq > fromSeqExclusive {
			result = append(result, BufferedOp{Op: r.op, SessionKey: r.sessionKey})
		}
	}
	return result, true
}

// TrackActiveDelta records that connID currently has an in-progress stroke
// strokeID, so it can be dropped on disconnect.
func (h *BoardHub) TrackActiveDelta(connID, strokeID string) {
	h.mu.Lock()
	defer h.mu.Unlock()
	set, ok := h.activeStrokes[connID]
	if !ok {
		set = make(map[string]bool)
		h.activeStrokes[connID] = set
	}
	set[strokeID] = true
}

// TrackActiveDone removes strokeID from connID's in-progress set, either
// because it was explicitly aborted or because it was committed as an op.
func (h *BoardHub) TrackActiveDone(connID, strokeID string) {
	h.mu.Lock()
	defer h.mu.Unlock()
	if set, ok := h.activeStrokes[connID]; ok {
		delete(set, strokeID)
	}
}

// Broadcast sends msg to every connection except the one whose ConnID equals
// exceptConnID (pass "" to include everyone).
func (h *BoardHub) Broadcast(msg any, exceptConnID string) {
	h.mu.Lock()
	targets := make([]Connection, 0, len(h.connections))
	for id, c := range h.connections {
		if id == exceptConnID {
			continue
		}
		targets = append(targets, c)
	}
	h.mu.Unlock()

	for _, c := range targets {
		c.Send(msg)
	}
}

// DropActiveOf removes connID from the hub, broadcasting an active_abort for
// each stroke it had in progress and a presence(left) for its label
// (requirements.md 11.3節 / 14章). It is a no-op if connID is not registered.
func (h *BoardHub) DropActiveOf(connID string) {
	h.mu.Lock()
	c, ok := h.connections[connID]
	var strokeIDs []string
	if set, exists := h.activeStrokes[connID]; exists {
		for id := range set {
			strokeIDs = append(strokeIDs, id)
		}
		delete(h.activeStrokes, connID)
	}
	delete(h.connections, connID)
	h.lastActivity = time.Now()
	h.mu.Unlock()

	if !ok {
		return
	}
	label := c.Label()
	for _, sid := range strokeIDs {
		h.Broadcast(message.ActiveAbortOut{Type: "active_abort", AuthorLabel: label, StrokeID: sid}, "")
	}
	if label != "" {
		h.Broadcast(message.Presence{Type: "presence", Left: []string{label}}, "")
	}
}

// Close broadcasts a fatal(reason) to every connection and clears all hub
// state (connections, active-stroke tracking, recent buffer, idempotency
// map). It returns the connections that were registered at the time of the
// call so the caller (Manager) can close the underlying transport.
func (h *BoardHub) Close(reason string) []Connection {
	h.mu.Lock()
	targets := make([]Connection, 0, len(h.connections))
	for _, c := range h.connections {
		targets = append(targets, c)
	}
	h.connections = make(map[string]Connection)
	h.activeStrokes = make(map[string]map[string]bool)
	h.recentOps = nil
	h.opIDToSeq = make(map[string]int)
	h.lastActivity = time.Now()
	h.mu.Unlock()

	for _, c := range targets {
		c.Send(message.Fatal{Type: "fatal", Reason: reason})
		c.Close()
	}
	return targets
}
