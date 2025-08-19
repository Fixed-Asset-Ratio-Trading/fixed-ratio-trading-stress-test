#!/usr/bin/env bash

set -euo pipefail

PORT="${1:-8080}"
POOL_ID="${2:-}"
TOKEN_TYPE="${3:-A}"
INITIAL_AMOUNT="${4:-0}"
AUTO_REFILL="${5:-false}"
SHARE_LP="${6:-true}"

if [[ -z "$POOL_ID" ]]; then
  echo "Usage: $0 [port] <pool_id> [A|B] [initial_amount] [auto_refill:true|false] [share_lp_tokens:true|false]" >&2
  exit 1
fi

curl -sS -X POST "http://127.0.0.1:${PORT}/api/jsonrpc" \
  -H "Content-Type: application/json" \
  -d "{\"method\":\"create_deposit_thread\",\"params\":{\"pool_id\":\"${POOL_ID}\",\"token_type\":\"${TOKEN_TYPE}\",\"initial_amount\":${INITIAL_AMOUNT},\"auto_refill\":${AUTO_REFILL},\"share_lp_tokens\":${SHARE_LP}},\"id\":1}"


