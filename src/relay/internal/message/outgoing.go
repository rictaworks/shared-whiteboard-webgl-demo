package message

// The types below are the relay -> client WebSocket message shapes
// (INTEGRATION_CONTRACT.md 5章). Each embeds/sets its own "type" literal.

type Participant struct {
	Label string `json:"label"`
	Color string `json:"color"`
}

type JoinAccepted struct {
	Type         string        `json:"type"`
	Label        string        `json:"label"`
	Color        string        `json:"color"`
	LastSeq      int           `json:"last_seq"`
	Participants []Participant `json:"participants"`
	CatchUpOps   []Op          `json:"catch_up_ops,omitempty"`
}

type RefetchRequired struct {
	Type    string `json:"type"`
	FromSeq int    `json:"from_seq"`
	ToSeq   int    `json:"to_seq"`
}

// Fatal reasons: board_lost | participant_limit | verification_failed | op_limit_exceeded | rate_limited
type Fatal struct {
	Type   string `json:"type"`
	Reason string `json:"reason"`
}

type ActiveDeltaOut struct {
	Type        string  `json:"type"`
	AuthorLabel string  `json:"author_label"`
	StrokeID    string  `json:"stroke_id"`
	Tool        string  `json:"tool"`
	Color       string  `json:"color"`
	Width       string  `json:"width"`
	Points      []Point `json:"points"`
}

type ActiveAbortOut struct {
	Type        string `json:"type"`
	AuthorLabel string `json:"author_label"`
	StrokeID    string `json:"stroke_id"`
}

type OpConfirmed struct {
	Type string `json:"type"`
	Op
}

type UndoFlagConfirmed struct {
	Type   string `json:"type"`
	Seq    int    `json:"seq"`
	OpID   string `json:"op_id"`
	Undone bool   `json:"undone"`
}

type Ack struct {
	Type string `json:"type"`
	OpID string `json:"op_id"`
	Seq  int    `json:"seq"`
}

type CursorOut struct {
	Type  string  `json:"type"`
	Label string  `json:"label"`
	Color string  `json:"color"`
	X     float64 `json:"x"`
	Y     float64 `json:"y"`
}

type Presence struct {
	Type   string   `json:"type"`
	Joined []string `json:"joined,omitempty"`
	Left   []string `json:"left,omitempty"`
}
