// Package httpapi implements the relay's own plain-HTTP endpoints (as
// opposed to the /ws WebSocket endpoint): the internal reset hook Rails
// calls during the daily reset (INTEGRATION_CONTRACT.md 4章).
package httpapi

import (
	"crypto/subtle"
	"net/http"

	"github.com/gin-gonic/gin"

	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/hub"
)

// RequireInternalSecret is Gin middleware that rejects any request whose
// X-Internal-Secret header does not match secret.
func RequireInternalSecret(secret string) gin.HandlerFunc {
	return func(c *gin.Context) {
		got := c.GetHeader("X-Internal-Secret")
		if subtle.ConstantTimeCompare([]byte(got), []byte(secret)) != 1 || secret == "" {
			c.AbortWithStatusJSON(http.StatusUnauthorized, gin.H{"error": "unauthorized"})
			return
		}
		c.Next()
	}
}

// ResetHandler handles POST /internal/reset: broadcast fatal(board_lost) to
// every connection on every hub, then discard all hubs and their recent
// buffers (requirements.md 23.7節 / 29章).
func ResetHandler(manager *hub.Manager) gin.HandlerFunc {
	return func(c *gin.Context) {
		closed := manager.CloseAll("board_lost")
		c.JSON(http.StatusOK, gin.H{"ok": true, "hubs_closed": closed})
	}
}
