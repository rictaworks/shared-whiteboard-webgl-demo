// Package persist batches confirmed operations and writes them to the
// application layer's internal ops endpoint, 200ms or 50 items at a time
// (requirements.md 11.5節), retrying on transient failure and refilling from
// the hub's recent-ops buffer on a gap (409) response.
package persist

import (
	"context"
	"errors"
	"log"
	"sync"
	"time"

	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/appclient"
	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/hub"
	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/message"
)

// FlushInterval is the max time an op waits in the buffer before being sent.
const FlushInterval = 200 * time.Millisecond

// MaxBatch is the max number of ops accumulated before an immediate flush.
const MaxBatch = 50

type item struct {
	op         message.Op
	sessionKey string
}

// Writer batches and persists confirmed ops for a single board.
type Writer struct {
	boardID string
	client  *appclient.Client
	hub     *hub.BoardHub

	mu  sync.Mutex
	buf []item

	stopCh chan struct{}
	doneCh chan struct{}
}

// NewWriter creates a Writer for boardID. Call Start to begin the flush
// loop and Stop to end it.
func NewWriter(boardID string, client *appclient.Client, h *hub.BoardHub) *Writer {
	return &Writer{
		boardID: boardID,
		client:  client,
		hub:     h,
		stopCh:  make(chan struct{}),
		doneCh:  make(chan struct{}),
	}
}

// Enqueue adds a confirmed op to the pending batch, flushing immediately if
// MaxBatch is reached.
func (w *Writer) Enqueue(op message.Op, sessionKey string) {
	w.mu.Lock()
	w.buf = append(w.buf, item{op: op, sessionKey: sessionKey})
	shouldFlush := len(w.buf) >= MaxBatch
	w.mu.Unlock()
	if shouldFlush {
		w.Flush()
	}
}

// Start launches the periodic flush loop in a background goroutine.
func (w *Writer) Start() {
	go func() {
		defer close(w.doneCh)
		ticker := time.NewTicker(FlushInterval)
		defer ticker.Stop()
		for {
			select {
			case <-w.stopCh:
				w.Flush()
				return
			case <-ticker.C:
				w.Flush()
			}
		}
	}()
}

// Stop ends the flush loop after a final flush. It blocks until the
// goroutine started by Start has exited.
func (w *Writer) Stop() {
	close(w.stopCh)
	<-w.doneCh
}

// Flush sends the currently buffered ops to Rails, retrying the batch later
// on transient error and refilling from the hub's recent buffer on a
// reported gap.
func (w *Writer) Flush() {
	w.mu.Lock()
	if len(w.buf) == 0 {
		w.mu.Unlock()
		return
	}
	items := w.buf
	w.buf = nil
	w.mu.Unlock()

	w.send(items)
}

func (w *Writer) send(items []item) {
	ctx, cancel := context.WithTimeout(context.Background(), appclient.DefaultTimeout)
	defer cancel()

	ops := make([]appclient.PersistOp, len(items))
	for i, it := range items {
		ops[i] = appclient.PersistOp{
			OpID:            it.op.OpID,
			SessionKey:      it.sessionKey,
			Seq:             it.op.Seq,
			Kind:            it.op.Kind,
			Stroke:          it.op.Stroke,
			TargetStrokeIDs: it.op.TargetStrokeIDs,
		}
	}

	_, err := w.client.WriteOps(ctx, w.boardID, ops)
	if err == nil {
		return
	}

	var gapErr *appclient.GapError
	if errors.As(err, &gapErr) {
		w.resendFromBuffer(gapErr.ExpectedFrom)
		return
	}

	// Transient error: put the batch back at the front of the queue for the
	// next flush tick, so nothing already-confirmed is silently dropped.
	log.Printf("persist: board %s: write_ops failed, will retry: %v", w.boardID, err)
	w.mu.Lock()
	w.buf = append(items, w.buf...)
	w.mu.Unlock()
}

// resendFromBuffer handles a 409 gap_detected response by asking the hub's
// recent-ops buffer for everything from expectedFrom onward and resending
// that as a fresh batch, once. If the hub buffer itself cannot prove
// continuity (already trimmed past the gap), the batch is dropped and the
// gap is logged as unrecoverable at the relay layer (documented
// limitation: this can only happen after a relay-process crash mid-batch,
// which a demo-scope relay does not attempt to fully heal).
func (w *Writer) resendFromBuffer(expectedFrom int) {
	buffered, ok := w.hub.RecentSince(expectedFrom - 1)
	if !ok || len(buffered) == 0 {
		log.Printf("persist: board %s: gap at seq %d could not be refilled from the recent buffer; dropping batch", w.boardID, expectedFrom)
		return
	}

	ctx, cancel := context.WithTimeout(context.Background(), appclient.DefaultTimeout)
	defer cancel()

	ops := make([]appclient.PersistOp, len(buffered))
	for i, b := range buffered {
		ops[i] = appclient.PersistOp{
			OpID:            b.Op.OpID,
			SessionKey:      b.SessionKey,
			Seq:             b.Op.Seq,
			Kind:            b.Op.Kind,
			Stroke:          b.Op.Stroke,
			TargetStrokeIDs: b.Op.TargetStrokeIDs,
		}
	}
	if _, err := w.client.WriteOps(ctx, w.boardID, ops); err != nil {
		log.Printf("persist: board %s: gap resend failed, dropping: %v", w.boardID, err)
	}
}
