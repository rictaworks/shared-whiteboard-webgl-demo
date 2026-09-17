// Package message defines the WebSocket JSON message shapes exchanged between
// Unity WebGL clients and the relay, and the validation rules from
// requirements.md 11章 / INTEGRATION_CONTRACT.md 5章.
package message

import "math"

// Limits from INTEGRATION_CONTRACT.md 5章.
const (
	MaxPayloadBytes = 64 * 1024
	MaxActivePoints = 200
	MaxStrokePoints = 1000
	CoordLimit      = 1_000_000
	// MaxTargetStrokeIDs bounds a stroke_erase op's target list. Not an
	// explicit contract number, but the same order of magnitude as
	// MaxStrokePoints and cheap to enforce; without it a stroke_erase op
	// could otherwise carry an unbounded (up to MaxPayloadBytes worth of)
	// target_stroke_ids array through to broadcast + persistence.
	MaxTargetStrokeIDs = 1000
)

// AllowedColors is the fixed 8-color palette (20.3節 / 9章 master data).
var AllowedColors = map[string]bool{
	"#1A1A1A": true,
	"#E53935": true,
	"#1E88E5": true,
	"#2E7D32": true,
	"#F9A825": true,
	"#8E24AA": true,
	"#FF6F00": true,
	"#546E7A": true,
}

// AllowedTools is the set of tool values valid on a Stroke (消しゴムは stroke_erase 操作になるため含まない).
var AllowedTools = map[string]bool{
	"pen":    true,
	"marker": true,
}

// AllowedWidths is the set of valid stroke widths.
var AllowedWidths = map[string]bool{
	"thin":   true,
	"medium": true,
	"thick":  true,
}

// AllowedOpKinds is the set of valid Op.Kind values.
var AllowedOpKinds = map[string]bool{
	"stroke_add":   true,
	"stroke_erase": true,
	"clear":        true,
}

// Point is a single [x, y] coordinate in world space.
type Point [2]float64

// Stroke is the body of a stroke_add operation.
type Stroke struct {
	ID     string  `json:"id"`
	Tool   string  `json:"tool"`
	Color  string  `json:"color"`
	Width  string  `json:"width"`
	Points []Point `json:"points"`
}

// Op is the canonical confirmed-operation shape (INTEGRATION_CONTRACT.md 2章 "Opの形").
// It is used both for the Rails HTTP API and for WebSocket op_confirmed / catch_up_ops.
type Op struct {
	OpID            string   `json:"op_id"`
	Seq             int      `json:"seq"`
	Kind            string   `json:"kind"`
	AuthorLabel     string   `json:"author_label"`
	Undone          bool     `json:"undone"`
	Stroke          *Stroke  `json:"stroke,omitempty"`
	TargetStrokeIDs []string `json:"target_stroke_ids,omitempty"`
}

// ClientMessage is the raw envelope for every client -> relay WebSocket message.
// Only the fields relevant to msg.Type are populated by the client.
type ClientMessage struct {
	Type string `json:"type"`

	// join
	SessionKey string `json:"session_key,omitempty"`
	BoardToken string `json:"board_token,omitempty"`
	LastSeq    int    `json:"last_seq,omitempty"`

	// active_delta / active_abort
	StrokeID string  `json:"stroke_id,omitempty"`
	Tool     string  `json:"tool,omitempty"`
	Color    string  `json:"color,omitempty"`
	Width    string  `json:"width,omitempty"`
	Points   []Point `json:"points,omitempty"`

	// op
	OpID            string   `json:"op_id,omitempty"`
	Kind            string   `json:"kind,omitempty"`
	Stroke          *Stroke  `json:"stroke,omitempty"`
	TargetStrokeIDs []string `json:"target_stroke_ids,omitempty"`

	// undo_flag (OpID above holds the target op id)
	Undone *bool `json:"undone,omitempty"`

	// cursor
	X float64 `json:"x,omitempty"`
	Y float64 `json:"y,omitempty"`
}

func validPoint(p Point) bool {
	x, y := p[0], p[1]
	if math.IsNaN(x) || math.IsInf(x, 0) || math.IsNaN(y) || math.IsInf(y, 0) {
		return false
	}
	if math.Abs(x) > CoordLimit || math.Abs(y) > CoordLimit {
		return false
	}
	return true
}

func validPoints(points []Point, max int) bool {
	if len(points) > max {
		return false
	}
	for _, p := range points {
		if !validPoint(p) {
			return false
		}
	}
	return true
}

// ValidateStroke checks a stroke body (used for op kind=stroke_add).
func ValidateStroke(s *Stroke) bool {
	if s == nil {
		return false
	}
	if s.ID == "" {
		return false
	}
	if !AllowedTools[s.Tool] {
		return false
	}
	if !AllowedColors[s.Color] {
		return false
	}
	if !AllowedWidths[s.Width] {
		return false
	}
	if !validPoints(s.Points, MaxStrokePoints) {
		return false
	}
	return true
}
