package message

import "testing"

func validStroke() *Stroke {
	return &Stroke{
		ID:     "s1",
		Tool:   "pen",
		Color:  "#1A1A1A",
		Width:  "medium",
		Points: []Point{{0, 0}, {1, 1}},
	}
}

func TestValidate_Join(t *testing.T) {
	ok := &ClientMessage{Type: "join", SessionKey: "sk", BoardToken: "bt", LastSeq: 0}
	if err := Validate(ok); err != nil {
		t.Fatalf("valid join rejected: %v", err)
	}

	missingSession := &ClientMessage{Type: "join", BoardToken: "bt"}
	if err := Validate(missingSession); err == nil {
		t.Fatalf("join without session_key: expected error")
	}

	negativeLastSeq := &ClientMessage{Type: "join", SessionKey: "sk", BoardToken: "bt", LastSeq: -1}
	if err := Validate(negativeLastSeq); err == nil {
		t.Fatalf("join with negative last_seq: expected error")
	}
}

func TestValidate_ActiveDelta(t *testing.T) {
	ok := &ClientMessage{
		Type: "active_delta", StrokeID: "s1", Tool: "pen", Color: "#1A1A1A", Width: "medium",
		Points: []Point{{0, 0}, {1, 1}},
	}
	if err := Validate(ok); err != nil {
		t.Fatalf("valid active_delta rejected: %v", err)
	}

	badColor := &ClientMessage{Type: "active_delta", StrokeID: "s1", Tool: "pen", Color: "#FFFFFF", Width: "medium"}
	if err := Validate(badColor); err == nil {
		t.Fatalf("active_delta with disallowed color: expected error")
	}

	tooManyPoints := make([]Point, MaxActivePoints+1)
	overLimit := &ClientMessage{Type: "active_delta", StrokeID: "s1", Tool: "pen", Color: "#1A1A1A", Width: "medium", Points: tooManyPoints}
	if err := Validate(overLimit); err == nil {
		t.Fatalf("active_delta with %d points (max %d): expected error", len(tooManyPoints), MaxActivePoints)
	}

	outOfRange := &ClientMessage{Type: "active_delta", StrokeID: "s1", Tool: "pen", Color: "#1A1A1A", Width: "medium", Points: []Point{{CoordLimit + 1, 0}}}
	if err := Validate(outOfRange); err == nil {
		t.Fatalf("active_delta with out-of-range coordinate: expected error")
	}
}

func TestValidate_Op(t *testing.T) {
	strokeAdd := &ClientMessage{Type: "op", OpID: "op-1", Kind: "stroke_add", Stroke: validStroke()}
	if err := Validate(strokeAdd); err != nil {
		t.Fatalf("valid op(stroke_add) rejected: %v", err)
	}

	strokeAddNoStroke := &ClientMessage{Type: "op", OpID: "op-1", Kind: "stroke_add"}
	if err := Validate(strokeAddNoStroke); err == nil {
		t.Fatalf("op(stroke_add) without stroke: expected error")
	}

	tooManyStrokePoints := make([]Point, MaxStrokePoints+1)
	overLimitStroke := validStroke()
	overLimitStroke.Points = tooManyStrokePoints
	strokeAddOverLimit := &ClientMessage{Type: "op", OpID: "op-1", Kind: "stroke_add", Stroke: overLimitStroke}
	if err := Validate(strokeAddOverLimit); err == nil {
		t.Fatalf("op(stroke_add) with %d points (max %d): expected error", len(tooManyStrokePoints), MaxStrokePoints)
	}

	strokeErase := &ClientMessage{Type: "op", OpID: "op-2", Kind: "stroke_erase", TargetStrokeIDs: []string{"s1"}}
	if err := Validate(strokeErase); err != nil {
		t.Fatalf("valid op(stroke_erase) rejected: %v", err)
	}

	strokeEraseNoTargets := &ClientMessage{Type: "op", OpID: "op-2", Kind: "stroke_erase"}
	if err := Validate(strokeEraseNoTargets); err == nil {
		t.Fatalf("op(stroke_erase) without target_stroke_ids: expected error")
	}

	clear := &ClientMessage{Type: "op", OpID: "op-3", Kind: "clear"}
	if err := Validate(clear); err != nil {
		t.Fatalf("valid op(clear) rejected: %v", err)
	}

	unknownKind := &ClientMessage{Type: "op", OpID: "op-4", Kind: "delete_everything"}
	if err := Validate(unknownKind); err == nil {
		t.Fatalf("op with unknown kind: expected error")
	}
}

// TestValidate_Op_RejectsOffKindFields guards against a stroke_erase/clear op
// smuggling an unvalidated Stroke (or a stroke_add smuggling
// target_stroke_ids) through to the caller: handleOp forwards whatever
// Validate lets through straight into broadcast + persistence regardless of
// Kind, so these off-kind fields must be rejected here, not merely ignored.
func TestValidate_Op_RejectsOffKindFields(t *testing.T) {
	unvalidatedStroke := &Stroke{
		ID:     "s1",
		Tool:   "not-a-real-tool",
		Color:  "#FFFFFF",
		Width:  "not-a-real-width",
		Points: make([]Point, MaxStrokePoints+1), // exceeds the stroke_add cap
	}

	strokeErase := &ClientMessage{Type: "op", OpID: "op-5", Kind: "stroke_erase", TargetStrokeIDs: []string{"s1"}, Stroke: unvalidatedStroke}
	if err := Validate(strokeErase); err == nil {
		t.Fatalf("op(stroke_erase) with an unvalidated stroke field: expected error")
	}

	clearWithStroke := &ClientMessage{Type: "op", OpID: "op-6", Kind: "clear", Stroke: unvalidatedStroke}
	if err := Validate(clearWithStroke); err == nil {
		t.Fatalf("op(clear) with a stroke field: expected error")
	}

	clearWithTargets := &ClientMessage{Type: "op", OpID: "op-7", Kind: "clear", TargetStrokeIDs: []string{"s1"}}
	if err := Validate(clearWithTargets); err == nil {
		t.Fatalf("op(clear) with target_stroke_ids: expected error")
	}

	strokeAddWithTargets := &ClientMessage{Type: "op", OpID: "op-8", Kind: "stroke_add", Stroke: validStroke(), TargetStrokeIDs: []string{"s1"}}
	if err := Validate(strokeAddWithTargets); err == nil {
		t.Fatalf("op(stroke_add) with target_stroke_ids: expected error")
	}

	tooManyTargets := make([]string, MaxTargetStrokeIDs+1)
	for i := range tooManyTargets {
		tooManyTargets[i] = "s1"
	}
	strokeEraseTooManyTargets := &ClientMessage{Type: "op", OpID: "op-9", Kind: "stroke_erase", TargetStrokeIDs: tooManyTargets}
	if err := Validate(strokeEraseTooManyTargets); err == nil {
		t.Fatalf("op(stroke_erase) with %d target_stroke_ids (max %d): expected error", len(tooManyTargets), MaxTargetStrokeIDs)
	}

	strokeEraseEmptyTarget := &ClientMessage{Type: "op", OpID: "op-10", Kind: "stroke_erase", TargetStrokeIDs: []string{""}}
	if err := Validate(strokeEraseEmptyTarget); err == nil {
		t.Fatalf("op(stroke_erase) with an empty target_stroke_id: expected error")
	}
}

func TestValidate_UndoFlag(t *testing.T) {
	undone := true
	ok := &ClientMessage{Type: "undo_flag", OpID: "op-1", Undone: &undone}
	if err := Validate(ok); err != nil {
		t.Fatalf("valid undo_flag rejected: %v", err)
	}

	missingUndone := &ClientMessage{Type: "undo_flag", OpID: "op-1"}
	if err := Validate(missingUndone); err == nil {
		t.Fatalf("undo_flag without undone: expected error")
	}
}

func TestValidate_Cursor(t *testing.T) {
	ok := &ClientMessage{Type: "cursor", X: 1.5, Y: -2.5}
	if err := Validate(ok); err != nil {
		t.Fatalf("valid cursor rejected: %v", err)
	}

	outOfRange := &ClientMessage{Type: "cursor", X: CoordLimit * 2, Y: 0}
	if err := Validate(outOfRange); err == nil {
		t.Fatalf("cursor with out-of-range x: expected error")
	}
}

func TestValidate_UnknownType(t *testing.T) {
	unknown := &ClientMessage{Type: "teleport"}
	if err := Validate(unknown); err != ErrUnknownType {
		t.Fatalf("unknown type: got err=%v, want ErrUnknownType", err)
	}
}
