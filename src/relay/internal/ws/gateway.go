// Package ws wires WebSocket connections to the hub: the /ws upgrade,
// origin checking, the join handshake against Rails, and dispatch of every
// subsequent message type (requirements.md 7.2節 / 11章;
// INTEGRATION_CONTRACT.md 5章).
package ws

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"errors"
	"log"
	"net/http"
	"sync"

	"github.com/gin-gonic/gin"
	"github.com/gorilla/websocket"

	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/appclient"
	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/hub"
	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/message"
	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/persist"
	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/ratelimit"
)

// Gateway owns the /ws endpoint: upgrading connections, running the join
// handshake, and dispatching subsequent messages to the right BoardHub.
type Gateway struct {
	manager   *hub.Manager
	appClient *appclient.Client
	limiter   *ratelimit.Limiter
	allowed   []string
	upgrader  websocket.Upgrader

	writersMu sync.Mutex
	writers   map[string]*persist.Writer // boardID -> writer
}

// NewGateway creates a Gateway. allowedOrigins are patterns as accepted by
// matchOrigin (e.g. "http://localhost:*").
func NewGateway(manager *hub.Manager, appClient *appclient.Client, limiter *ratelimit.Limiter, allowedOrigins []string) *Gateway {
	g := &Gateway{
		manager:   manager,
		appClient: appClient,
		limiter:   limiter,
		allowed:   allowedOrigins,
		writers:   make(map[string]*persist.Writer),
	}
	g.upgrader = websocket.Upgrader{
		ReadBufferSize:  4096,
		WriteBufferSize: 4096,
		CheckOrigin:     g.checkOrigin,
	}
	return g
}

func (g *Gateway) checkOrigin(r *http.Request) bool {
	origin := r.Header.Get("Origin")
	if origin == "" {
		// No Origin header: same-origin / non-browser client (also covers
		// local integration tests using a bare ws dialer). Browsers always
		// send Origin on cross-origin WebSocket upgrades, so this cannot be
		// used to bypass the check from Unity Play's own page.
		return true
	}
	return originAllowed(g.allowed, origin)
}

// RemoveWriter stops and discards the persistence writer for boardID, if
// one exists. Intended to be wired as hub.Manager's onHubRemoved callback so
// a GC'd or reset hub doesn't leave its flush goroutine running forever.
func (g *Gateway) RemoveWriter(boardID string) {
	g.writersMu.Lock()
	w, ok := g.writers[boardID]
	if ok {
		delete(g.writers, boardID)
	}
	g.writersMu.Unlock()
	if ok {
		w.Stop()
	}
}

// StopAllWriters stops every persistence writer this gateway has started.
// Intended for graceful shutdown and test cleanup.
func (g *Gateway) StopAllWriters() {
	g.writersMu.Lock()
	writers := make([]*persist.Writer, 0, len(g.writers))
	for _, w := range g.writers {
		writers = append(writers, w)
	}
	g.writers = make(map[string]*persist.Writer)
	g.writersMu.Unlock()

	for _, w := range writers {
		w.Stop()
	}
}

func (g *Gateway) writerFor(h *hub.BoardHub) *persist.Writer {
	boardID := h.BoardID()
	g.writersMu.Lock()
	defer g.writersMu.Unlock()
	if w, ok := g.writers[boardID]; ok {
		return w
	}
	w := persist.NewWriter(boardID, g.appClient, h)
	w.Start()
	g.writers[boardID] = w
	return w
}

func newConnID() string {
	buf := make([]byte, 16)
	if _, err := rand.Read(buf); err != nil {
		// crypto/rand failing is effectively fatal for the process, but we
		// must not panic a request handler; fall back to a fixed prefix so
		// at least uniqueness within this process' map insertion order is
		// visibly broken rather than silently colliding forever. This path
		// is not expected to be hit in practice.
		log.Printf("ws: crypto/rand failed: %v", err)
	}
	return hex.EncodeToString(buf)
}

// HandleWS upgrades the request to a WebSocket and serves it. Registered as
// a Gin handler for GET /ws.
func (g *Gateway) HandleWS(c *gin.Context) {
	conn, err := g.upgrader.Upgrade(c.Writer, c.Request, nil)
	if err != nil {
		// Upgrader already wrote the HTTP error response.
		return
	}
	wsConn := newConnection(newConnID(), conn)
	go g.serve(wsConn)
}

func (g *Gateway) serve(conn *Connection) {
	defer conn.Close()
	conn.ws.SetReadLimit(message.MaxPayloadBytes)

	var joined bool
	var boardHub *hub.BoardHub
	var writer *persist.Writer

	for {
		_, raw, err := conn.ws.ReadMessage()
		if err != nil {
			break
		}

		var msg message.ClientMessage
		if err := json.Unmarshal(raw, &msg); err != nil {
			// Malformed JSON: discard, keep the connection open.
			continue
		}

		if !joined {
			if msg.Type != "join" {
				// Anything other than join before joining is ignored; the
				// client is expected to retry with a valid join.
				continue
			}
			if err := message.Validate(&msg); err != nil {
				// An invalid join leaves the connection alive but permanently
				// unusable: every later message is discarded because joined is
				// still false. Swallowing this silently hid a total sync
				// outage in production for days (Issue #30), so log it. The
				// error text carries no secrets - only which field was missing.
				log.Printf("ws: rejected join: %v", err)
				continue
			}
			h, ok := g.handleJoin(conn, &msg)
			if !ok {
				return
			}
			boardHub = h
			writer = g.writerFor(boardHub)
			joined = true
			continue
		}

		g.dispatch(conn, boardHub, writer, &msg)
	}

	if joined && boardHub != nil {
		boardHub.DropActiveOf(conn.id)
	}
}

// handleJoin runs the join handshake: verify participation with Rails,
// admit the connection to its board hub (enforcing the participant limit),
// and send join_accepted (+ refetch_required if the buffer can't cover the
// requested catch-up range). Returns the hub and true on success; on
// failure it has already sent fatal and closed the connection.
func (g *Gateway) handleJoin(conn *Connection, msg *message.ClientMessage) (*hub.BoardHub, bool) {
	ctx, cancel := context.WithTimeout(context.Background(), appclient.DefaultTimeout)
	result, err := g.appClient.VerifyParticipation(ctx, msg.SessionKey, msg.BoardToken)
	cancel()

	if err != nil || result == nil || !result.OK {
		conn.Send(message.Fatal{Type: "fatal", Reason: "verification_failed"})
		conn.Close()
		return nil, false
	}

	boardHub, err := g.manager.GetOrCreate(result.BoardID)
	if err != nil {
		conn.Send(message.Fatal{Type: "fatal", Reason: "verification_failed"})
		conn.Close()
		return nil, false
	}

	conn.sessionKey = msg.SessionKey
	conn.boardToken = msg.BoardToken
	conn.boardID = result.BoardID
	conn.label = result.Label
	conn.color = result.Color
	conn.participationID = result.ParticipationID

	if !boardHub.TryAdmit(conn) {
		conn.Send(message.Fatal{Type: "fatal", Reason: "participant_limit"})
		conn.Close()
		return nil, false
	}

	catchUpOps, needRefetch := boardHub.CatchUp(msg.LastSeq)
	lastSeq := boardHub.CurrentLastSeq()

	conn.Send(message.JoinAccepted{
		Type:         "join_accepted",
		Label:        result.Label,
		Color:        result.Color,
		LastSeq:      lastSeq,
		Participants: boardHub.ParticipantsSnapshot(),
		CatchUpOps:   catchUpOps,
	})
	if needRefetch {
		conn.Send(message.RefetchRequired{Type: "refetch_required", FromSeq: msg.LastSeq, ToSeq: lastSeq})
	}
	boardHub.Broadcast(message.Presence{Type: "presence", Joined: []string{result.Label}}, conn.id)

	return boardHub, true
}

// dispatch handles every message type once a connection has joined.
func (g *Gateway) dispatch(conn *Connection, h *hub.BoardHub, writer *persist.Writer, msg *message.ClientMessage) {
	if err := message.Validate(msg); err != nil {
		// Invalid message: discard, connection stays open
		// (INTEGRATION_CONTRACT.md 5章バリデーション).
		return
	}

	switch msg.Type {
	case "join":
		// Duplicate join after already joining: ignore.
		return

	case "active_delta":
		h.TrackActiveDelta(conn.id, msg.StrokeID)
		h.Broadcast(message.ActiveDeltaOut{
			Type:        "active_delta",
			AuthorLabel: conn.label,
			StrokeID:    msg.StrokeID,
			Tool:        msg.Tool,
			Color:       msg.Color,
			Width:       msg.Width,
			Points:      msg.Points,
		}, conn.id)

	case "active_abort":
		h.TrackActiveDone(conn.id, msg.StrokeID)
		h.Broadcast(message.ActiveAbortOut{
			Type:        "active_abort",
			AuthorLabel: conn.label,
			StrokeID:    msg.StrokeID,
		}, conn.id)

	case "op":
		g.handleOp(conn, h, writer, msg)

	case "undo_flag":
		g.handleUndoFlag(conn, h, msg)

	case "cursor":
		h.Broadcast(message.CursorOut{
			Type:  "cursor",
			Label: conn.label,
			Color: conn.color,
			X:     msg.X,
			Y:     msg.Y,
		}, conn.id)
	}
}

func (g *Gateway) handleOp(conn *Connection, h *hub.BoardHub, writer *persist.Writer, msg *message.ClientMessage) {
	if !g.limiter.Allow(conn.sessionKey) {
		// Silently dropped per contract: no fatal, connection stays open.
		return
	}

	seq, isNew := h.AssignSeq(msg.OpID)
	op := message.Op{
		OpID:            msg.OpID,
		Seq:             seq,
		Kind:            msg.Kind,
		AuthorLabel:     conn.label,
		Undone:          false,
		Stroke:          msg.Stroke,
		TargetStrokeIDs: msg.TargetStrokeIDs,
	}

	if isNew {
		h.AppendRecentOp(op, conn.sessionKey)
		if msg.Kind == "stroke_add" && msg.Stroke != nil {
			h.TrackActiveDone(conn.id, msg.Stroke.ID)
		}
		// Broadcast to everyone but the author: the author already applied
		// this op optimistically and only needs the ack below
		// (requirements.md 23.3節シーケンス図).
		h.Broadcast(message.OpConfirmed{Type: "op_confirmed", Op: op}, conn.id)
		if writer != nil {
			writer.Enqueue(op, conn.sessionKey)
		}
	}

	conn.Send(message.Ack{Type: "ack", OpID: msg.OpID, Seq: seq})
}

func (g *Gateway) handleUndoFlag(conn *Connection, h *hub.BoardHub, msg *message.ClientMessage) {
	if !g.limiter.Allow(conn.sessionKey) {
		return
	}

	// Ownership check (requirements.md 12章: Undo は自分の操作のみを対象と
	// する). When the target op is still in the recent buffer we can decide
	// this locally and for free; a mismatch is treated like any other
	// invalid message (silently discarded, connection stays open) without
	// ever assigning a seq or contacting Rails. Only when the target op has
	// already fallen out of the buffer do we fall back to letting Rails'
	// own ownership check (403 not_owner) be the backstop.
	if owner, ok := h.FindOwnerByOpID(msg.OpID); ok && owner != conn.sessionKey {
		return
	}

	undone := *msg.Undone
	seq := h.AssignUndoSeq()

	// Record a synthetic catch-up entry so a client that reconnects within
	// the recent-buffer window learns about this toggle too (see
	// hub.bufferedOp doc comment). Best-effort: if the original op has
	// already fallen out of the buffer we skip the synthetic entry, but the
	// live broadcast below still reaches every currently-connected client.
	if kind, ok := h.FindKindByOpID(msg.OpID); ok {
		h.AppendUndoRecentOp(message.Op{
			OpID:        msg.OpID,
			Seq:         seq,
			Kind:        kind,
			AuthorLabel: conn.label,
			Undone:      undone,
		}, conn.sessionKey)
	}

	// Broadcast to everyone including the sender: unlike "op", undo_flag has
	// no separate ack type, so the sender also relies on
	// undo_flag_confirmed.
	h.Broadcast(message.UndoFlagConfirmed{
		Type:   "undo_flag_confirmed",
		Seq:    seq,
		OpID:   msg.OpID,
		Undone: undone,
	}, "")

	sessionKey, boardID := conn.sessionKey, conn.boardID
	go func() {
		ctx, cancel := context.WithTimeout(context.Background(), appclient.DefaultTimeout)
		defer cancel()
		err := g.appClient.WriteUndoFlag(ctx, boardID, msg.OpID, undone, sessionKey, seq)
		if err == nil {
			return
		}
		var notOwner *appclient.NotOwnerError
		if errors.As(err, &notOwner) {
			// We already broadcast undo_flag_confirmed optimistically
			// before this call returned. This can only happen when the
			// target op was already outside our recent buffer (checked
			// above), so we could not have caught the mismatch locally.
			// The persisted op log is unaffected (Rails rejected the
			// write), so the board's source of truth is still correct;
			// this is a known, accepted demo-scale limitation rather than
			// a data-integrity bug, and needs no corrective broadcast
			// (none is defined by the contract).
			log.Printf("ws: board %s: undo_flag for op %s rejected by Rails as not_owner (target op was outside the relay's recent buffer, so this could not be checked locally): %v", boardID, msg.OpID, err)
			return
		}
		log.Printf("ws: board %s: write_undo_flag failed: %v", boardID, err)
	}()
}
