package ws

import "strings"

// matchOrigin reports whether origin is allowed by pattern. A pattern
// ending in "*" matches any origin sharing its prefix (e.g.
// "http://localhost:*" matches "http://localhost:3000"); otherwise an exact
// match is required.
func matchOrigin(pattern, origin string) bool {
	if pattern == origin {
		return true
	}
	if strings.HasSuffix(pattern, "*") {
		prefix := strings.TrimSuffix(pattern, "*")
		return strings.HasPrefix(origin, prefix)
	}
	return false
}

// originAllowed reports whether origin matches any of the configured
// allowed-origin patterns (28章: 配信元として Unity Play の配信元のみを受け入れる).
func originAllowed(allowed []string, origin string) bool {
	for _, pattern := range allowed {
		if matchOrigin(pattern, origin) {
			return true
		}
	}
	return false
}
