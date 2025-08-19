#!/usr/bin/env bash

set -euo pipefail

PORT="${1:-8080}"
THREAD_ID="${2:-}"

if [[ -z "$THREAD_ID" ]]; then
  echo "Usage: $0 [port] <thread_id>" >&2
  exit 1
fi

curl -sS -X POST "http://127.0.0.1:${PORT}/api/jsonrpc" \
  -H "Content-Type: application/json" \
  -d "{\"method\":\"start_thread\",\"params\":{\"thread_id\":\"${THREAD_ID}\"},\"id\":1}"


