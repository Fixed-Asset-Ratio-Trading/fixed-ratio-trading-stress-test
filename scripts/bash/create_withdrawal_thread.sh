#!/usr/bin/env bash

set -euo pipefail

PORT="${1:-8080}"
POOL_ID="${2:-}"
TOKEN_TYPE="${3:-A}"

if [[ -z "$POOL_ID" ]]; then
  echo "Usage: $0 [port] <pool_id> [A|B]" >&2
  exit 1
fi

curl -sS -X POST "http://127.0.0.1:${PORT}/api/jsonrpc" \
  -H "Content-Type: application/json" \
  -d "{\"method\":\"create_withdrawal_thread\",\"params\":{\"pool_id\":\"${POOL_ID}\",\"token_type\":\"${TOKEN_TYPE}\"},\"id\":1}"


