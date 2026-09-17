package ws

import (
	"encoding/json"
	"log"
	"sync"
	"sync/atomic"
	"time"

	"github.com/gorilla/websocket"
)

// writeTimeout bounds a single WriteMessage call so a stalled client can't
// block the hub goroutine broadcasting to it.
const writeTimeout = 5 * time.Second

// Connection wraps one WebSocket connection and implements hub.Connection.
// Fields set at construction (id) are immutable; fields set once at join
// time (label/color/etc.) are only ever written by the single read-loop
// goroutine that owns this Connection, before it is registered with any
// hub, so no lock is needed for them.
type Connection struct {
	id string
	ws *websocket.Conn

	writeMu sync.Mutex
	closed  atomic.Bool

	sessionKey      string
	boardToken      string
	boardID         string
	label           string
	color           string
	participationID string
}

func newConnection(id string, wsConn *websocket.Conn) *Connection {
	return &Connection{id: id, ws: wsConn}
}

// ConnID implements hub.Connection.
func (c *Connection) ConnID() string { return c.id }

// Label implements hub.Connection.
func (c *Connection) Label() string { return c.label }

// ParticipationID implements hub.Connection.
func (c *Connection) ParticipationID() string { return c.participationID }

// Send implements hub.Connection. Marshal/write errors are logged and
// otherwise swallowed: a broken connection is detected and cleaned up by
// its own read loop, not by writers broadcasting to it.
func (c *Connection) Send(msg any) {
	if c.closed.Load() {
		return
	}
	data, err := json.Marshal(msg)
	if err != nil {
		log.Printf("ws: marshal outgoing message failed: %v", err)
		return
	}
	c.writeMu.Lock()
	defer c.writeMu.Unlock()
	if c.closed.Load() {
		return
	}
	_ = c.ws.SetWriteDeadline(time.Now().Add(writeTimeout))
	if err := c.ws.WriteMessage(websocket.TextMessage, data); err != nil {
		log.Printf("ws: conn %s: write failed: %v", c.id, err)
	}
}

// Close closes the underlying transport exactly once.
func (c *Connection) Close() {
	if c.closed.CompareAndSwap(false, true) {
		_ = c.ws.Close()
	}
}
