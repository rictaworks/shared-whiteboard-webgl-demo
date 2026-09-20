package ws

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/gorilla/websocket"

	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/appclient"
	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/hub"
	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/message"
	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/ratelimit"
)

// ---- Stub Rails application layer -----------------------------------------
//
// The gateway only ever talks to Rails through appclient.Client, so a tiny
// in-memory stub implementing the same three internal endpoints is enough
// to run the relay end-to-end without a real Rails process.

var stubColors = []string{"#1A1A1A", "#E53935", "#1E88E5", "#2E7D32", "#F9A825", "#8E24AA", "#FF6F00", "#546E7A"}

type stubParticipant struct {
	label           string
	color           string
	participationID string
}

type stubBoard struct {
	mu           sync.Mutex
	participants map[string]*stubParticipant // session_key -> participant
	order        []string
	lastSeq      int // overrides last_seq reported to the relay (0 = "fresh board")
}

type stubRails struct {
	mu     sync.Mutex
	boards map[string]*stubBoard // board_token (== board_id in this stub) -> board
}

func newStubRails() *stubRails {
	return &stubRails{boards: make(map[string]*stubBoard)}
}

func (s *stubRails) board(token string) *stubBoard {
	s.mu.Lock()
	defer s.mu.Unlock()
	b, ok := s.boards[token]
	if !ok {
		b = &stubBoard{participants: make(map[string]*stubParticipant)}
		s.boards[token] = b
	}
	return b
}

// setLastSeq lets a test simulate a board whose Rails-recorded last_seq is
// already ahead of anything the relay's own (fresh, empty) recent buffer
// could possibly hold -- i.e. the "outside the buffer" catch-up case.
func (s *stubRails) setLastSeq(token string, seq int) {
	s.board(token).lastSeq = seq
}

func (b *stubBoard) verify(sessionKey string) *stubParticipant {
	b.mu.Lock()
	defer b.mu.Unlock()
	if p, ok := b.participants[sessionKey]; ok {
		return p
	}
	idx := len(b.order)
	p := &stubParticipant{
		label:           fmt.Sprintf("参加者%d", idx),
		color:           stubColors[idx%len(stubColors)],
		participationID: sessionKey, // 1 session == 1 participation in this stub
	}
	b.participants[sessionKey] = p
	b.order = append(b.order, sessionKey)
	return p
}

func newStubServer(t *testing.T, rails *stubRails) *httptest.Server {
	t.Helper()
	r := gin.New()

	r.POST("/internal/verify_participation", func(c *gin.Context) {
		var body struct {
			SessionKey string `json:"session_key"`
			BoardToken string `json:"board_token"`
		}
		if err := c.BindJSON(&body); err != nil {
			c.JSON(http.StatusOK, gin.H{"ok": false, "reason": "verification_failed"})
			return
		}
		if body.BoardToken == "" || body.SessionKey == "" {
			c.JSON(http.StatusOK, gin.H{"ok": false, "reason": "verification_failed"})
			return
		}
		b := rails.board(body.BoardToken)
		p := b.verify(body.SessionKey)
		c.JSON(http.StatusOK, gin.H{
			"ok":               true,
			"board_id":         body.BoardToken,
			"label":            p.label,
			"color":            p.color,
			"participation_id": p.participationID,
		})
	})

	r.GET("/internal/boards/:board_id/last_seq", func(c *gin.Context) {
		boardID := c.Param("board_id")
		b := rails.board(boardID)
		b.mu.Lock()
		seq := b.lastSeq
		b.mu.Unlock()
		c.JSON(http.StatusOK, gin.H{"last_seq": seq})
	})

	r.POST("/internal/boards/:board_id/ops", func(c *gin.Context) {
		var body struct {
			Ops []appclient.PersistOp `json:"ops"`
		}
		_ = c.BindJSON(&body)
		c.JSON(http.StatusOK, gin.H{"accepted": len(body.Ops)})
	})

	r.POST("/internal/boards/:board_id/undo_flag", func(c *gin.Context) {
		c.JSON(http.StatusOK, gin.H{"ok": true})
	})

	srv := httptest.NewServer(r)
	t.Cleanup(srv.Close)
	return srv
}

// ---- Relay test harness -----------------------------------------------------

type testRelay struct {
	server *httptest.Server
	wsURL  string
	rails  *stubRails
}

func newTestRelay(t *testing.T) *testRelay {
	t.Helper()
	gin.SetMode(gin.TestMode)

	rails := newStubRails()
	stub := newStubServer(t, rails)

	appClient := appclient.New(stub.URL, "test-secret")
	limiter := ratelimit.New(30)
	manager := hub.NewManager(func(boardID string) (int, error) {
		ctx := context.Background()
		return appClient.LastSeq(ctx, boardID)
	}, nil)
	gateway := NewGateway(manager, appClient, limiter, []string{"http://localhost:*"})
	t.Cleanup(gateway.StopAllWriters)

	router := gin.New()
	router.GET("/ws", gateway.HandleWS)
	server := httptest.NewServer(router)
	t.Cleanup(server.Close)

	wsURL := "ws" + strings.TrimPrefix(server.URL, "http")
	return &testRelay{server: server, wsURL: wsURL + "/ws", rails: rails}
}

// testClient wraps a WebSocket connection with helpers for the test
// assertions below.
type testClient struct {
	t    *testing.T
	conn *websocket.Conn
}

func dial(t *testing.T, relay *testRelay) *testClient {
	t.Helper()
	conn, _, err := websocket.DefaultDialer.Dial(relay.wsURL, nil)
	if err != nil {
		t.Fatalf("dial %s: %v", relay.wsURL, err)
	}
	t.Cleanup(func() { _ = conn.Close() })
	return &testClient{t: t, conn: conn}
}

func (c *testClient) send(v any) {
	c.t.Helper()
	if err := c.conn.WriteJSON(v); err != nil {
		c.t.Fatalf("send: %v", err)
	}
}

// tryReadTyped reads the next message with the given per-read timeout,
// returning ok=false on any read/decode error (including a plain timeout)
// instead of failing the test, so callers can keep polling within their own
// overall deadline.
func (c *testClient) tryReadTyped(timeout time.Duration) (typ string, raw []byte, ok bool) {
	c.t.Helper()
	_ = c.conn.SetReadDeadline(time.Now().Add(timeout))
	_, raw, err := c.conn.ReadMessage()
	if err != nil {
		return "", nil, false
	}
	var envelope struct {
		Type string `json:"type"`
	}
	if err := json.Unmarshal(raw, &envelope); err != nil {
		return "", nil, false
	}
	return envelope.Type, raw, true
}

// readUntil reads messages until one of the given wantTypes is seen (up to
// deadline overall), skipping any others (e.g. presence notifications from
// other clients joining/leaving). Fails the test if none arrives in time.
func (c *testClient) readUntil(deadline time.Duration, wantTypes ...string) (string, []byte) {
	c.t.Helper()
	end := time.Now().Add(deadline)
	step := 100 * time.Millisecond
	for time.Now().Before(end) {
		remaining := time.Until(end)
		readTimeout := step
		if remaining < readTimeout {
			readTimeout = remaining
		}
		if readTimeout <= 0 {
			break
		}
		typ, raw, ok := c.tryReadTyped(readTimeout)
		if !ok {
			continue
		}
		for _, want := range wantTypes {
			if typ == want {
				return typ, raw
			}
		}
	}
	c.t.Fatalf("readUntil: none of %v seen within %v", wantTypes, deadline)
	return "", nil
}

func decode[T any](t *testing.T, raw []byte) T {
	t.Helper()
	var v T
	if err := json.Unmarshal(raw, &v); err != nil {
		t.Fatalf("decode %T: %v", v, err)
	}
	return v
}

func joinAndAccept(t *testing.T, c *testClient, sessionKey, boardToken string, lastSeq int) message.JoinAccepted {
	t.Helper()
	c.send(message.ClientMessage{Type: "join", SessionKey: sessionKey, BoardToken: boardToken, LastSeq: lastSeq})
	typ, raw := c.readUntil(2*time.Second, "join_accepted", "fatal")
	if typ == "fatal" {
		f := decode[message.Fatal](t, raw)
		t.Fatalf("join rejected: fatal(%s)", f.Reason)
	}
	return decode[message.JoinAccepted](t, raw)
}

// ---- Required scenario 1: idempotent op resend -----------------------------

func TestWS_DuplicateOpID_SameSeqOnResend(t *testing.T) {
	relay := newTestRelay(t)
	client := dial(t, relay)
	joinAndAccept(t, client, "session-1", "board-dedup", 0)

	strokeOp := message.ClientMessage{
		Type: "op", OpID: "op-dup-1", Kind: "stroke_add",
		Stroke: &message.Stroke{ID: "s1", Tool: "pen", Color: "#1A1A1A", Width: "medium", Points: []message.Point{{0, 0}, {1, 1}}},
	}

	client.send(strokeOp)
	_, raw1 := client.readUntil(2*time.Second, "ack")
	ack1 := decode[message.Ack](t, raw1)
	if ack1.OpID != "op-dup-1" {
		t.Fatalf("first ack op_id = %q, want op-dup-1", ack1.OpID)
	}

	client.send(strokeOp) // resend the exact same op_id
	_, raw2 := client.readUntil(2*time.Second, "ack")
	ack2 := decode[message.Ack](t, raw2)

	if ack2.Seq != ack1.Seq {
		t.Fatalf("resend got seq=%d, want the same seq=%d as the first ack (idempotent)", ack2.Seq, ack1.Seq)
	}
}

// ---- Required scenario 2: monotonic, non-colliding seq under concurrency ---

func TestWS_ConcurrentClients_MonotonicNonCollidingSeq(t *testing.T) {
	relay := newTestRelay(t)

	const numClients = 5
	const opsPerClient = 10

	clients := make([]*testClient, numClients)
	for i := 0; i < numClients; i++ {
		clients[i] = dial(t, relay)
		joinAndAccept(t, clients[i], fmt.Sprintf("session-concurrent-%d", i), "board-concurrent", 0)
	}

	var wg sync.WaitGroup
	seqsCh := make(chan int, numClients*opsPerClient)
	for i, c := range clients {
		wg.Add(1)
		go func(i int, c *testClient) {
			defer wg.Done()
			for j := 0; j < opsPerClient; j++ {
				c.send(message.ClientMessage{
					Type: "op", OpID: fmt.Sprintf("op-%d-%d", i, j), Kind: "clear",
				})
			}
		}(i, c)
	}
	wg.Wait()

	for _, c := range clients {
		for j := 0; j < opsPerClient; j++ {
			_, raw := c.readUntil(3*time.Second, "ack")
			ack := decode[message.Ack](t, raw)
			seqsCh <- ack.Seq
		}
	}
	close(seqsCh)

	seen := make(map[int]bool)
	count := 0
	for seq := range seqsCh {
		if seen[seq] {
			t.Fatalf("seq %d assigned more than once: concurrent ops collided", seq)
		}
		seen[seq] = true
		count++
	}
	if count != numClients*opsPerClient {
		t.Fatalf("got %d acked seqs, want %d", count, numClients*opsPerClient)
	}
	for i := 1; i <= count; i++ {
		if !seen[i] {
			t.Fatalf("seq range is not contiguous: missing seq %d", i)
		}
	}
}

// ---- Required scenario 3: 11th participant hits the limit ------------------

func TestWS_EleventhParticipant_FatalParticipantLimit(t *testing.T) {
	relay := newTestRelay(t)

	for i := 0; i < hub.MaxParticipants; i++ {
		c := dial(t, relay)
		joinAndAccept(t, c, fmt.Sprintf("session-limit-%d", i), "board-limit", 0)
	}

	eleventh := dial(t, relay)
	eleventh.send(message.ClientMessage{Type: "join", SessionKey: "session-limit-10", BoardToken: "board-limit", LastSeq: 0})
	typ, raw := eleventh.readUntil(2*time.Second, "fatal", "join_accepted")
	if typ != "fatal" {
		t.Fatalf("11th participant: got type=%q, want fatal", typ)
	}
	f := decode[message.Fatal](t, raw)
	if f.Reason != "participant_limit" {
		t.Fatalf("11th participant fatal reason = %q, want participant_limit", f.Reason)
	}
}

// ---- Required scenario 4: last_seq outside the buffer -> refetch_required --

func TestWS_LastSeqOutsideBuffer_RefetchRequired(t *testing.T) {
	relay := newTestRelay(t)
	// Simulate a relay restart scenario: Rails already has ops up to 100,
	// but this hub (created fresh in this test) has an empty recent buffer.
	relay.rails.setLastSeq("board-refetch", 100)

	client := dial(t, relay)
	client.send(message.ClientMessage{Type: "join", SessionKey: "session-refetch", BoardToken: "board-refetch", LastSeq: 50})

	// join_accepted must arrive first, then refetch_required.
	typ, raw := client.readUntil(2*time.Second, "join_accepted", "fatal")
	if typ != "join_accepted" {
		f := decode[message.Fatal](t, raw)
		t.Fatalf("expected join_accepted, got fatal(%s)", f.Reason)
	}

	typ, raw = client.readUntil(2*time.Second, "refetch_required")
	if typ != "refetch_required" {
		t.Fatalf("expected refetch_required, got %q", typ)
	}
	rr := decode[message.RefetchRequired](t, raw)
	if rr.FromSeq != 50 {
		t.Fatalf("refetch_required.from_seq = %d, want 50", rr.FromSeq)
	}
}

// ---- Required scenario 5: invalid messages are discarded, not fatal --------

func TestWS_InvalidMessages_DiscardedConnectionStaysAlive(t *testing.T) {
	relay := newTestRelay(t)
	sender := dial(t, relay)
	joinAndAccept(t, sender, "session-invalid-sender", "board-invalid", 0)

	observer := dial(t, relay)
	joinAndAccept(t, observer, "session-invalid-observer", "board-invalid", 0)

	// 1. Malformed JSON.
	if err := sender.conn.WriteMessage(websocket.TextMessage, []byte("{not valid json")); err != nil {
		t.Fatalf("write malformed JSON: %v", err)
	}
	// 2. Unknown type.
	sender.send(map[string]string{"type": "teleport"})
	// 3. Out-of-range cursor coordinate.
	sender.send(message.ClientMessage{Type: "cursor", X: 9_999_999, Y: 0})
	// 4. active_delta with a disallowed color.
	sender.send(message.ClientMessage{Type: "active_delta", StrokeID: "s1", Tool: "pen", Color: "#FFFFFF", Width: "medium"})

	// The connection must still be alive: a valid cursor sent afterwards
	// must reach the observer.
	sender.send(message.ClientMessage{Type: "cursor", X: 3, Y: 4})
	typ, raw := observer.readUntil(2*time.Second, "cursor")
	if typ != "cursor" {
		t.Fatalf("expected cursor to still be delivered after invalid messages, got %q", typ)
	}
	cur := decode[message.CursorOut](t, raw)
	if cur.X != 3 || cur.Y != 4 {
		t.Fatalf("cursor = %+v, want X=3 Y=4", cur)
	}
}

// TestWS_JoinWithEmptyBoardToken_NothingIsPersisted reproduces Issue #30: a
// join whose board_token is empty fails validation, so the connection never
// becomes joined and every later message is discarded. The client sees no
// join_accepted and no op_confirmed, which in production looked like "drawing
// works but nothing is saved and Undo does nothing" - the relay's only job
// here is to not pretend the session is healthy.
func TestWS_JoinWithEmptyBoardToken_NothingIsPersisted(t *testing.T) {
	relay := newTestRelay(t)
	client := dial(t, relay)

	client.send(message.ClientMessage{Type: "join", SessionKey: "session-no-token", BoardToken: "", LastSeq: 0})
	if typ, _, ok := client.tryReadTyped(500 * time.Millisecond); ok {
		t.Fatalf("expected no reply to an invalid join, got %q", typ)
	}

	// An op sent on the un-joined connection must not be confirmed.
	client.send(message.ClientMessage{
		Type:   "op",
		OpID:   "op-after-invalid-join",
		Kind:   "stroke_add",
		Stroke: &message.Stroke{ID: "s1", Tool: "pen", Color: "#1A1A1A", Width: "medium", Points: []message.Point{{0, 0}, {1, 1}}},
	})
	if typ, _, ok := client.tryReadTyped(500 * time.Millisecond); ok {
		t.Fatalf("expected no reply for an op before joining, got %q", typ)
	}

	// A join carrying the token does succeed on the same connection, which is
	// what the client-side fix restores.
	accepted := joinAndAccept(t, client, "session-no-token", "board-issue-30", 0)
	if accepted.Label == "" {
		t.Fatalf("join_accepted carried no label: %+v", accepted)
	}
}

// ---- Required scenario 6: disconnect drops the active stroke ---------------

func TestWS_Disconnect_DropsActiveStrokeAndBroadcastsPresence(t *testing.T) {
	relay := newTestRelay(t)

	author := dial(t, relay)
	authorJoin := joinAndAccept(t, author, "session-disconnect-author", "board-disconnect", 0)

	observer := dial(t, relay)
	joinAndAccept(t, observer, "session-disconnect-observer", "board-disconnect", 0)
	// Drain the presence(joined) notification for the observer's own join
	// that the author would have seen; not relevant to the observer itself.

	author.send(message.ClientMessage{
		Type: "active_delta", StrokeID: "stroke-disc-1", Tool: "pen", Color: "#1A1A1A", Width: "medium",
		Points: []message.Point{{0, 0}},
	})
	// Let the observer see the in-progress delta first (proves tracking
	// happened before we close the author's connection).
	typ, _ := observer.readUntil(2*time.Second, "active_delta")
	if typ != "active_delta" {
		t.Fatalf("expected active_delta before disconnect, got %q", typ)
	}

	_ = author.conn.Close()

	typ, raw := observer.readUntil(2*time.Second, "active_abort")
	if typ != "active_abort" {
		t.Fatalf("expected active_abort after author disconnect, got %q", typ)
	}
	abort := decode[message.ActiveAbortOut](t, raw)
	if abort.StrokeID != "stroke-disc-1" || abort.AuthorLabel != authorJoin.Label {
		t.Fatalf("active_abort = %+v, want StrokeID=stroke-disc-1 AuthorLabel=%s", abort, authorJoin.Label)
	}

	typ, raw = observer.readUntil(2*time.Second, "presence")
	if typ != "presence" {
		t.Fatalf("expected presence after author disconnect, got %q", typ)
	}
	presence := decode[message.Presence](t, raw)
	if len(presence.Left) != 1 || presence.Left[0] != authorJoin.Label {
		t.Fatalf("presence = %+v, want Left=[%s]", presence, authorJoin.Label)
	}
}

// ---- Ownership check on undo_flag (requirements.md 12章: Undo は自分の操作のみを対象とする) --

func TestWS_UndoFlag_NonOwnerDiscardedSilently(t *testing.T) {
	relay := newTestRelay(t)

	owner := dial(t, relay)
	joinAndAccept(t, owner, "session-undo-owner", "board-undo-ownership", 0)

	other := dial(t, relay)
	joinAndAccept(t, other, "session-undo-other", "board-undo-ownership", 0)

	owner.send(message.ClientMessage{
		Type: "op", OpID: "op-undo-owned-1", Kind: "stroke_add",
		Stroke: &message.Stroke{ID: "s1", Tool: "pen", Color: "#1A1A1A", Width: "medium", Points: []message.Point{{0, 0}}},
	})
	owner.readUntil(2*time.Second, "ack")

	// The non-owner tries to undo the owner's op: the relay must know
	// op-undo-owned-1's real author from its own recent buffer and discard
	// this silently (no undo_flag_confirmed to anyone, no seq assigned,
	// connection stays open) without ever asking Rails.
	undone := true
	other.send(message.ClientMessage{Type: "undo_flag", OpID: "op-undo-owned-1", Undone: &undone})

	// Prove liveness (a later, unrelated message from the same connection
	// still goes through) and, along the way, confirm no
	// undo_flag_confirmed was ever broadcast for the rejected undo.
	other.send(message.ClientMessage{Type: "cursor", X: 7, Y: 8})

	end := time.Now().Add(1 * time.Second)
	for time.Now().Before(end) {
		typ, raw, ok := owner.tryReadTyped(200 * time.Millisecond)
		if !ok {
			continue
		}
		if typ == "undo_flag_confirmed" {
			t.Fatalf("non-owner's undo_flag must not be confirmed, got: %s", raw)
		}
		if typ == "cursor" {
			cur := decode[message.CursorOut](t, raw)
			if cur.X == 7 && cur.Y == 8 {
				return // liveness confirmed; no undo_flag_confirmed was seen before it
			}
		}
	}
	t.Fatalf("expected the non-owner's later cursor message to still be delivered to the owner")
}

func TestWS_UndoFlag_OwnerSucceeds(t *testing.T) {
	relay := newTestRelay(t)

	owner := dial(t, relay)
	joinAndAccept(t, owner, "session-undo-owner-ok", "board-undo-owner-ok", 0)

	observer := dial(t, relay)
	joinAndAccept(t, observer, "session-undo-observer-ok", "board-undo-owner-ok", 0)

	owner.send(message.ClientMessage{
		Type: "op", OpID: "op-undo-owned-2", Kind: "stroke_add",
		Stroke: &message.Stroke{ID: "s2", Tool: "pen", Color: "#1A1A1A", Width: "medium", Points: []message.Point{{0, 0}}},
	})
	owner.readUntil(2*time.Second, "ack")

	undone := true
	owner.send(message.ClientMessage{Type: "undo_flag", OpID: "op-undo-owned-2", Undone: &undone})

	// The owner's own undo_flag_confirmed (undo_flag has no separate ack).
	typ, raw := owner.readUntil(2*time.Second, "undo_flag_confirmed")
	if typ != "undo_flag_confirmed" {
		t.Fatalf("owner: expected undo_flag_confirmed, got %q", typ)
	}
	ownerConfirm := decode[message.UndoFlagConfirmed](t, raw)
	if ownerConfirm.OpID != "op-undo-owned-2" || !ownerConfirm.Undone {
		t.Fatalf("owner's undo_flag_confirmed = %+v, want OpID=op-undo-owned-2 Undone=true", ownerConfirm)
	}

	// The observer also sees it (broadcast to everyone).
	typ, _ = observer.readUntil(2*time.Second, "undo_flag_confirmed")
	if typ != "undo_flag_confirmed" {
		t.Fatalf("observer: expected undo_flag_confirmed, got %q", typ)
	}
}
