#!/usr/bin/env bash

set -euo pipefail

PORT="${1:-8080}"
POOL_ID="${2:-}"
INCLUDE_SWAPS="${3:-false}"

if [[ -z "$POOL_ID" ]]; then
  echo "Usage: $0 [port] <pool_id> [include_swaps:true|false]" >&2
  exit 1
fi

curl -sS -X POST "http://127.0.0.1:${PORT}/api/jsonrpc" \
  -H "Content-Type: application/json" \
  -d "{\"method\":\"stop_all_pool_threads\",\"params\":{\"pool_id\":\"${POOL_ID}\",\"include_swaps\":${INCLUDE_SWAPS}},\"id\":1}"


