#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://localhost:8080}"

# Generate a simple UUID-like ID without Python
ID="$(date +%s)-$(printf "%04x" $RANDOM)-$(printf "%04x" $RANDOM)-$(printf "%04x" $RANDOM)-$(printf "%012x" $RANDOM)"

echo "POST $BASE_URL/api/jsonrpc (create_pool_random)"
# Try to use jq if available, otherwise just output the raw response
if command -v jq &> /dev/null; then
    curl -sS -H 'Content-Type: application/json' \
      -d '{"method":"create_pool_random","id":"'"$ID"'","params":{}}' \
      "$BASE_URL/api/jsonrpc" | jq .
else
    echo "jq not found, showing raw response:"
    curl -sS -H 'Content-Type: application/json' \
      -d '{"method":"create_pool_random","id":"'"$ID"'","params":{}}' \
      "$BASE_URL/api/jsonrpc"
    echo ""
fi
