package hub

import (
	"sync"
	"time"
)

// DefaultIdleTimeout is how long a hub with zero connections is kept alive
// before the GC sweep removes it (bounds memory growth across the day; a
// board with active connections is never swept).
const DefaultIdleTimeout = 5 * time.Minute

// LastSeqFetcher fetches the current last_seq for a board from the
// application layer (Rails), used when a hub is created for the first time
// so sequence numbering resumes correctly after a relay restart
// (requirements.md 11.5節).
type LastSeqFetcher func(boardID string) (int, error)

// Manager owns every BoardHub, keyed by board ID.
type Manager struct {
	mu            sync.Mutex
	hubs          map[string]*BoardHub
	fetchLastSeq  LastSeqFetcher
	onHubRemoved  func(boardID string)
	stopGC        chan struct{}
	gcStartedOnce sync.Once
}

// NewManager creates an empty hub registry. onHubRemoved, if non-nil, is
// called (outside the manager's lock) whenever a hub is removed, whether by
// idle GC or CloseAll, so callers can release per-board resources they own
// (e.g. a persistence writer goroutine). It may be nil.
func NewManager(fetchLastSeq LastSeqFetcher, onHubRemoved func(boardID string)) *Manager {
	return &Manager{
		hubs:         make(map[string]*BoardHub),
		fetchLastSeq: fetchLastSeq,
		onHubRemoved: onHubRemoved,
		stopGC:       make(chan struct{}),
	}
}

// GetOrCreate returns the existing hub for boardID, or creates one, seeding
// its sequence counter from fetchLastSeq. Concurrent first-joins to the same
// new board may call fetchLastSeq more than once (harmless idempotent GET);
// only one hub is ever kept.
func (m *Manager) GetOrCreate(boardID string) (*BoardHub, error) {
	m.mu.Lock()
	if h, ok := m.hubs[boardID]; ok {
		m.mu.Unlock()
		return h, nil
	}
	m.mu.Unlock()

	lastSeq, err := m.fetchLastSeq(boardID)
	if err != nil {
		return nil, err
	}

	m.mu.Lock()
	defer m.mu.Unlock()
	if h, ok := m.hubs[boardID]; ok {
		return h, nil
	}
	h := NewBoardHub(boardID, lastSeq+1)
	m.hubs[boardID] = h
	return h, nil
}

// Get returns the existing hub for boardID, if any, without creating one.
func (m *Manager) Get(boardID string) (*BoardHub, bool) {
	m.mu.Lock()
	defer m.mu.Unlock()
	h, ok := m.hubs[boardID]
	return h, ok
}

// CloseAll broadcasts fatal(reason) to every hub and discards them all
// (used by the daily reset / POST /internal/reset). It returns the number of
// hubs that were closed.
func (m *Manager) CloseAll(reason string) int {
	m.mu.Lock()
	hubs := make([]*BoardHub, 0, len(m.hubs))
	ids := make([]string, 0, len(m.hubs))
	for id, h := range m.hubs {
		hubs = append(hubs, h)
		ids = append(ids, id)
	}
	m.hubs = make(map[string]*BoardHub)
	m.mu.Unlock()

	for _, h := range hubs {
		h.Close(reason)
	}
	if m.onHubRemoved != nil {
		for _, id := range ids {
			m.onHubRemoved(id)
		}
	}
	return len(hubs)
}

// StartGC launches a background goroutine that periodically removes hubs
// with zero connections that have been idle for longer than idleTimeout.
// Safe to call at most once; subsequent calls are no-ops.
func (m *Manager) StartGC(interval, idleTimeout time.Duration) {
	m.gcStartedOnce.Do(func() {
		go func() {
			ticker := time.NewTicker(interval)
			defer ticker.Stop()
			for {
				select {
				case <-m.stopGC:
					return
				case <-ticker.C:
					m.sweep(idleTimeout)
				}
			}
		}()
	})
}

// StopGC stops the background GC goroutine started by StartGC, if any.
func (m *Manager) StopGC() {
	close(m.stopGC)
}

func (m *Manager) sweep(idleTimeout time.Duration) {
	m.mu.Lock()
	now := time.Now()
	var removed []string
	for id, h := range m.hubs {
		if h.ConnectionCount() == 0 && now.Sub(h.LastActivity()) > idleTimeout {
			delete(m.hubs, id)
			removed = append(removed, id)
		}
	}
	m.mu.Unlock()

	if m.onHubRemoved != nil {
		for _, id := range removed {
			m.onHubRemoved(id)
		}
	}
}

// HubCount returns the number of currently registered hubs (test/debug use).
func (m *Manager) HubCount() int {
	m.mu.Lock()
	defer m.mu.Unlock()
	return len(m.hubs)
}
