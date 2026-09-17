// Package appclient calls the Rails application layer's internal API
// (INTEGRATION_CONTRACT.md 3章). Every request carries the shared internal
// secret in X-Internal-Secret; this is never logged or exposed elsewhere.
package appclient

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"time"

	"github.com/rictaworks/shared-whiteboard-webgl-demo/relay/internal/message"
)

// DefaultTimeout bounds every internal HTTP call so a slow/unreachable Rails
// instance cannot stall the relay's hub goroutines indefinitely.
const DefaultTimeout = 5 * time.Second

// Client calls the Rails internal API.
type Client struct {
	baseURL    string
	secret     string
	httpClient *http.Client
}

// New creates a Client. baseURL should not have a trailing slash (e.g.
// "http://localhost:3001"); secret is the shared INTERNAL_SHARED_SECRET.
func New(baseURL, secret string) *Client {
	return &Client{
		baseURL: baseURL,
		secret:  secret,
		httpClient: &http.Client{
			Timeout: DefaultTimeout,
		},
	}
}

func (c *Client) newRequest(ctx context.Context, method, path string, body any) (*http.Request, error) {
	var reader io.Reader
	if body != nil {
		data, err := json.Marshal(body)
		if err != nil {
			return nil, err
		}
		reader = bytes.NewReader(data)
	}
	req, err := http.NewRequestWithContext(ctx, method, c.baseURL+path, reader)
	if err != nil {
		return nil, err
	}
	req.Header.Set("X-Internal-Secret", c.secret)
	if body != nil {
		req.Header.Set("Content-Type", "application/json")
	}
	return req, nil
}

// VerifyResult is the decoded response of POST /internal/verify_participation.
type VerifyResult struct {
	OK              bool
	BoardID         string
	Label           string
	Color           string
	ParticipationID string
	Reason          string // "not_found" | "verification_failed", only when OK is false
}

// VerifyParticipation checks that sessionKey has a participation record on
// the board identified by boardToken (requirements.md 6章 / 28章).
func (c *Client) VerifyParticipation(ctx context.Context, sessionKey, boardToken string) (*VerifyResult, error) {
	req, err := c.newRequest(ctx, http.MethodPost, "/internal/verify_participation", map[string]string{
		"session_key": sessionKey,
		"board_token": boardToken,
	})
	if err != nil {
		return nil, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("appclient: verify_participation: unexpected status %d", resp.StatusCode)
	}
	var out struct {
		OK              bool   `json:"ok"`
		BoardID         string `json:"board_id"`
		Label           string `json:"label"`
		Color           string `json:"color"`
		ParticipationID string `json:"participation_id"`
		Reason          string `json:"reason"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return nil, err
	}
	return &VerifyResult{
		OK:              out.OK,
		BoardID:         out.BoardID,
		Label:           out.Label,
		Color:           out.Color,
		ParticipationID: out.ParticipationID,
		Reason:          out.Reason,
	}, nil
}

// LastSeq fetches the board's last confirmed sequence number (used to
// resume numbering after a relay restart; requirements.md 11.5節).
func (c *Client) LastSeq(ctx context.Context, boardID string) (int, error) {
	req, err := c.newRequest(ctx, http.MethodGet, "/internal/boards/"+boardID+"/last_seq", nil)
	if err != nil {
		return 0, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return 0, err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return 0, fmt.Errorf("appclient: last_seq: unexpected status %d", resp.StatusCode)
	}
	var out struct {
		LastSeq int `json:"last_seq"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return 0, err
	}
	return out.LastSeq, nil
}

// PersistOp is one item of the ops batch persisted to Rails.
type PersistOp struct {
	OpID            string          `json:"op_id"`
	SessionKey      string          `json:"session_key"`
	Seq             int             `json:"seq"`
	Kind            string          `json:"kind"`
	Stroke          *message.Stroke `json:"stroke,omitempty"`
	TargetStrokeIDs []string        `json:"target_stroke_ids,omitempty"`
}

// GapError is returned by WriteOps when Rails reports a seq gap (409).
type GapError struct {
	ExpectedFrom int
}

func (e *GapError) Error() string {
	return fmt.Sprintf("appclient: gap detected, expected_from=%d", e.ExpectedFrom)
}

// WriteOps persists a batch of confirmed ops. accepted is the number Rails
// accepted (duplicates by seq are skipped, not an error). A *GapError is
// returned (as err) when Rails detects a missing range before this batch.
func (c *Client) WriteOps(ctx context.Context, boardID string, ops []PersistOp) (accepted int, err error) {
	req, err := c.newRequest(ctx, http.MethodPost, "/internal/boards/"+boardID+"/ops", map[string]any{
		"ops": ops,
	})
	if err != nil {
		return 0, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return 0, err
	}
	defer resp.Body.Close()

	if resp.StatusCode == http.StatusConflict {
		var gap struct {
			Error        string `json:"error"`
			ExpectedFrom int    `json:"expected_from"`
		}
		if decErr := json.NewDecoder(resp.Body).Decode(&gap); decErr != nil {
			return 0, decErr
		}
		return 0, &GapError{ExpectedFrom: gap.ExpectedFrom}
	}
	if resp.StatusCode != http.StatusOK {
		return 0, fmt.Errorf("appclient: write_ops: unexpected status %d", resp.StatusCode)
	}
	var out struct {
		Accepted int `json:"accepted"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return 0, err
	}
	return out.Accepted, nil
}

// NotOwnerError is returned by WriteUndoFlag when Rails rejects the change
// with 403 not_owner: sessionKey does not own the target op. The relay
// itself is expected to catch most of these before ever calling Rails, by
// checking the recent-ops buffer (hub.BoardHub.FindOwnerByOpID); this is the
// fallback for a target op old enough to have already left that buffer.
type NotOwnerError struct{}

func (e *NotOwnerError) Error() string {
	return "appclient: undo_flag rejected: not_owner"
}

// WriteUndoFlag persists a single undo-flag change
// (POST /internal/boards/:board_id/undo_flag). This endpoint is not
// batched, unlike WriteOps. A *NotOwnerError is returned (as err) when Rails
// rejects the change because sessionKey does not own the target op (403).
func (c *Client) WriteUndoFlag(ctx context.Context, boardID, opID string, undone bool, sessionKey string, seq int) error {
	req, err := c.newRequest(ctx, http.MethodPost, "/internal/boards/"+boardID+"/undo_flag", map[string]any{
		"op_id":       opID,
		"undone":      undone,
		"session_key": sessionKey,
		"seq":         seq,
	})
	if err != nil {
		return err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()

	if resp.StatusCode == http.StatusForbidden {
		return &NotOwnerError{}
	}
	if resp.StatusCode != http.StatusOK {
		return fmt.Errorf("appclient: write_undo_flag: unexpected status %d", resp.StatusCode)
	}
	return nil
}
