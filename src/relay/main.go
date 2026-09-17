// Command relay is the shared-whiteboard-webgl-demo relay server (Go/Gin):
// it maintains WebSocket connections, assigns and rebroadcasts confirmed
// operations in order, relays in-progress strokes and cursors, and persists
// confirmed ops to the Rails application layer (requirements.md 2.2節).
package main

import (
	"context"
	"log"
	"time"

	"github.com/gin-gonic/gin"

	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/appclient"
	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/config"
	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/httpapi"
	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/hub"
	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/ratelimit"
	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/ws"
)

// opsPerSecondPerSession is the per-session rate limit for op/undo_flag
// messages (17章).
const opsPerSecondPerSession = 30

// gcInterval / gcIdleTimeout govern the idle-hub sweep (bounds memory for
// boards nobody has reconnected to in a while).
const (
	gcInterval    = 1 * time.Minute
	gcIdleTimeout = hub.DefaultIdleTimeout
)

func main() {
	cfg := config.Load()
	if cfg.InternalSecret == "" {
		log.Println("relay: WARNING: INTERNAL_SHARED_SECRET is empty; internal endpoints will reject every request")
	}

	appClient := appclient.New(cfg.AppInternalURL, cfg.InternalSecret)
	limiter := ratelimit.New(opsPerSecondPerSession)

	var gateway *ws.Gateway
	manager := hub.NewManager(
		func(boardID string) (int, error) {
			ctx, cancel := context.WithTimeout(context.Background(), appclient.DefaultTimeout)
			defer cancel()
			return appClient.LastSeq(ctx, boardID)
		},
		func(boardID string) {
			if gateway != nil {
				gateway.RemoveWriter(boardID)
			}
		},
	)
	gateway = ws.NewGateway(manager, appClient, limiter, cfg.AllowedOrigins)

	manager.StartGC(gcInterval, gcIdleTimeout)
	go sweepLimiterIdle(limiter)

	router := gin.Default()
	router.GET("/ws", gateway.HandleWS)

	internal := router.Group("/internal", httpapi.RequireInternalSecret(cfg.InternalSecret))
	internal.POST("/reset", httpapi.ResetHandler(manager))

	addr := ":" + cfg.Port
	log.Printf("relay: listening on %s (app internal url: %s)", addr, cfg.AppInternalURL)
	if err := router.Run(addr); err != nil {
		log.Fatalf("relay: server exited: %v", err)
	}
}

func sweepLimiterIdle(limiter *ratelimit.Limiter) {
	ticker := time.NewTicker(10 * time.Minute)
	defer ticker.Stop()
	for range ticker.C {
		limiter.SweepIdle(10 * time.Minute)
	}
}
