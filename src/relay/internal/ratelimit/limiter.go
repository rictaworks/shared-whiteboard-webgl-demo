// Package ratelimit implements a simple per-session-key token bucket used
// to cap "op"/"undo_flag" throughput at 30/sec per session (17章 / 28章).
package ratelimit

import (
	"math"
	"sync"
	"time"
)

type bucket struct {
	mu         sync.Mutex
	tokens     float64
	lastRefill time.Time
	lastUsed   time.Time
}

// Limiter is a token bucket rate limiter keyed by session key.
type Limiter struct {
	mu         sync.Mutex
	buckets    map[string]*bucket
	ratePerSec float64
	burst      float64
}

// New creates a Limiter allowing ratePerSec sustained operations per second
// per session key, with a burst capacity equal to ratePerSec.
func New(ratePerSec float64) *Limiter {
	return &Limiter{
		buckets:    make(map[string]*bucket),
		ratePerSec: ratePerSec,
		burst:      ratePerSec,
	}
}

// Allow reports whether an operation from sessionKey may proceed right now,
// consuming one token if so.
func (l *Limiter) Allow(sessionKey string) bool {
	l.mu.Lock()
	b, ok := l.buckets[sessionKey]
	if !ok {
		b = &bucket{tokens: l.burst, lastRefill: time.Now()}
		l.buckets[sessionKey] = b
	}
	l.mu.Unlock()

	b.mu.Lock()
	defer b.mu.Unlock()
	now := time.Now()
	elapsed := now.Sub(b.lastRefill).Seconds()
	b.tokens = math.Min(l.burst, b.tokens+elapsed*l.ratePerSec)
	b.lastRefill = now
	b.lastUsed = now
	if b.tokens >= 1 {
		b.tokens--
		return true
	}
	return false
}

// SweepIdle removes buckets that have not been used for longer than idle,
// bounding memory growth across a day of many short-lived sessions.
func (l *Limiter) SweepIdle(idle time.Duration) {
	l.mu.Lock()
	defer l.mu.Unlock()
	now := time.Now()
	for key, b := range l.buckets {
		b.mu.Lock()
		stale := now.Sub(b.lastUsed) > idle
		b.mu.Unlock()
		if stale {
			delete(l.buckets, key)
		}
	}
}
