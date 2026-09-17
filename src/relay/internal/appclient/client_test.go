package appclient

import (
	"context"
	"errors"
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestWriteUndoFlag_Success(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusOK)
		_, _ = w.Write([]byte(`{"ok":true}`))
	}))
	defer srv.Close()

	c := New(srv.URL, "secret")
	if err := c.WriteUndoFlag(context.Background(), "board-1", "op-1", true, "session-1", 5); err != nil {
		t.Fatalf("WriteUndoFlag: unexpected error: %v", err)
	}
}

func TestWriteUndoFlag_NotOwner(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusForbidden)
		_, _ = w.Write([]byte(`{"error":"not_owner"}`))
	}))
	defer srv.Close()

	c := New(srv.URL, "secret")
	err := c.WriteUndoFlag(context.Background(), "board-1", "op-1", true, "session-not-owner", 5)
	if err == nil {
		t.Fatalf("WriteUndoFlag: expected an error on 403, got nil")
	}
	var notOwner *NotOwnerError
	if !errors.As(err, &notOwner) {
		t.Fatalf("WriteUndoFlag: got err=%v (%T), want a *NotOwnerError", err, err)
	}
}

func TestWriteUndoFlag_OtherServerError(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusInternalServerError)
	}))
	defer srv.Close()

	c := New(srv.URL, "secret")
	err := c.WriteUndoFlag(context.Background(), "board-1", "op-1", true, "session-1", 5)
	if err == nil {
		t.Fatalf("WriteUndoFlag: expected an error on 500, got nil")
	}
	var notOwner *NotOwnerError
	if errors.As(err, &notOwner) {
		t.Fatalf("WriteUndoFlag: a plain 500 must not be reported as NotOwnerError")
	}
}
