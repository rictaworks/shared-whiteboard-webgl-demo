#!/bin/sh
# Issue #11：JST 03:00 に Railway の Cron Schedule から起動され、backend の日次リセット用
# 内部エンドポイントを1回だけ叩いて終了する。SQLite の Volume は backend にしか付けられないため、
# このサービスは DB を持たず HTTP 経由でリセットを依頼するだけにする。
set -eu

: "${TARGET_URL:?TARGET_URL is required (backend public domain, no scheme)}"
: "${INTERNAL_SHARED_SECRET:?INTERNAL_SHARED_SECRET is required}"

status=$(curl -sS -m 60 -o /dev/null -w '%{http_code}' \
  -X POST "https://${TARGET_URL}/internal/system/daily_reset" \
  -H "X-Internal-Secret: ${INTERNAL_SHARED_SECRET}")

echo "daily_reset: POST https://${TARGET_URL}/internal/system/daily_reset -> HTTP ${status}"

[ "${status}" = "200" ]
