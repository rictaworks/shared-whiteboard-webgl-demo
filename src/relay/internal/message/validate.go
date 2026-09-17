package message

import "errors"

// ErrUnknownType is returned by Validate when msg.Type is not one of the
// known client -> relay message types. Per INTEGRATION_CONTRACT.md 5章, an
// unknown type is discarded, not a disconnect.
var ErrUnknownType = errors.New("message: unknown type")

// Validate checks a decoded ClientMessage against the field-level rules for
// its type (INTEGRATION_CONTRACT.md 5章 バリデーション). It does not check
// payload size (that is enforced on the raw bytes before JSON decoding) and
// does not check rate limits (handled separately per session).
func Validate(msg *ClientMessage) error {
	switch msg.Type {
	case "join":
		if msg.SessionKey == "" {
			return errors.New("join: missing session_key")
		}
		if msg.BoardToken == "" {
			return errors.New("join: missing board_token")
		}
		if msg.LastSeq < 0 {
			return errors.New("join: invalid last_seq")
		}
		return nil

	case "active_delta":
		if msg.StrokeID == "" {
			return errors.New("active_delta: missing stroke_id")
		}
		if !AllowedTools[msg.Tool] {
			return errors.New("active_delta: invalid tool")
		}
		if !AllowedColors[msg.Color] {
			return errors.New("active_delta: invalid color")
		}
		if !AllowedWidths[msg.Width] {
			return errors.New("active_delta: invalid width")
		}
		if !validPoints(msg.Points, MaxActivePoints) {
			return errors.New("active_delta: invalid points")
		}
		return nil

	case "active_abort":
		if msg.StrokeID == "" {
			return errors.New("active_abort: missing stroke_id")
		}
		return nil

	case "op":
		if msg.OpID == "" {
			return errors.New("op: missing op_id")
		}
		if !AllowedOpKinds[msg.Kind] {
			return errors.New("op: invalid kind")
		}
		// Every kind's off-kind field must be absent, not merely unchecked:
		// handleOp copies msg.Stroke / msg.TargetStrokeIDs straight into the
		// broadcast Op and into the persisted batch regardless of msg.Kind,
		// so an unvalidated Stroke (unbounded points / out-of-range
		// coordinates) smuggled in under kind=stroke_erase|clear would
		// otherwise skip ValidateStroke entirely and still reach every
		// participant and Rails (INTEGRATION_CONTRACT.md 5章).
		switch msg.Kind {
		case "stroke_add":
			if !ValidateStroke(msg.Stroke) {
				return errors.New("op: invalid stroke")
			}
			if len(msg.TargetStrokeIDs) != 0 {
				return errors.New("op: unexpected target_stroke_ids for stroke_add")
			}
		case "stroke_erase":
			if msg.Stroke != nil {
				return errors.New("op: unexpected stroke for stroke_erase")
			}
			if len(msg.TargetStrokeIDs) == 0 {
				return errors.New("op: missing target_stroke_ids")
			}
			if len(msg.TargetStrokeIDs) > MaxTargetStrokeIDs {
				return errors.New("op: too many target_stroke_ids")
			}
			for _, id := range msg.TargetStrokeIDs {
				if id == "" {
					return errors.New("op: empty target_stroke_id")
				}
			}
		case "clear":
			if msg.Stroke != nil {
				return errors.New("op: unexpected stroke for clear")
			}
			if len(msg.TargetStrokeIDs) != 0 {
				return errors.New("op: unexpected target_stroke_ids for clear")
			}
		}
		return nil

	case "undo_flag":
		if msg.OpID == "" {
			return errors.New("undo_flag: missing op_id")
		}
		if msg.Undone == nil {
			return errors.New("undo_flag: missing undone")
		}
		return nil

	case "cursor":
		if !validPoint(Point{msg.X, msg.Y}) {
			return errors.New("cursor: invalid coordinates")
		}
		return nil

	default:
		return ErrUnknownType
	}
}
