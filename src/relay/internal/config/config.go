// Package config loads relay configuration from environment variables
// (INTEGRATION_CONTRACT.md 0章).
package config

import (
	"os"
	"strings"
)

// Config holds every environment-derived setting the relay needs.
type Config struct {
	Port           string
	AppInternalURL string
	InternalSecret string
	AllowedOrigins []string
}

// Load reads configuration from the process environment, applying the same
// development defaults INTEGRATION_CONTRACT.md documents.
func Load() Config {
	return Config{
		Port:           getEnv("PORT", "8080"),
		AppInternalURL: getEnv("APP_INTERNAL_URL", "http://localhost:3001"),
		InternalSecret: os.Getenv("INTERNAL_SHARED_SECRET"),
		AllowedOrigins: splitCSV(getEnv("ALLOWED_ORIGINS", "http://localhost:*")),
	}
}

func getEnv(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}

func splitCSV(s string) []string {
	parts := strings.Split(s, ",")
	out := make([]string, 0, len(parts))
	for _, p := range parts {
		p = strings.TrimSpace(p)
		if p != "" {
			out = append(out, p)
		}
	}
	return out
}
