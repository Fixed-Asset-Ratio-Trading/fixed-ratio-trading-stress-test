#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://localhost:8080}"
POOL_ID="${2:-}"
URL="$BASE_URL/api/jsonrpc"

if [[ -z "$POOL_ID" ]]; then
    echo "Usage: bash scripts/bash/get_pool.sh <base_url> <pool_id>"
    exit 1
fi

# Generate a simple UUID-like ID
ID="$(date +%s)-$(printf "%04x" $RANDOM)-$(printf "%04x" $RANDOM)-$(printf "%04x" $RANDOM)-$(printf "%012x" $RANDOM)"

echo "POST $URL (get_pool) - pool_id=$POOL_ID"
if command -v jq &> /dev/null; then
    curl -sS -H 'Content-Type: application/json' \
      -d '{"method":"get_pool","id":"'"$ID"'","params":{"pool_id":"'"$POOL_ID"'"}}' \
      "$URL" | jq .
else
    echo "jq not found, showing raw response:"
    curl -sS -H 'Content-Type: application/json' \
      -d '{"method":"get_pool","id":"'"$ID"'","params":{"pool_id":"'"$POOL_ID"'"}}' \
      "$URL"
    echo ""
fi


