#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://localhost:8080}"
URL="$BASE_URL/api/jsonrpc"

# Generate a simple UUID-like ID without Python
ID="$(date +%s)-$(printf "%04x" $RANDOM)-$(printf "%04x" $RANDOM)-$(printf "%04x" $RANDOM)-$(printf "%012x" $RANDOM)"

echo "POST $URL (core_wallet_status)"
# Try to use jq if available, otherwise just output the raw response
if command -v jq &> /dev/null; then
    curl -sS -H 'Content-Type: application/json' \
      -d '{"method":"core_wallet_status","id":"'"$ID"'","params":{}}' \
      "$URL" | jq .
else
    echo "jq not found, showing raw response:"
    curl -sS -H 'Content-Type: application/json' \
      -d '{"method":"core_wallet_status","id":"'"$ID"'","params":{}}' \
      "$URL"
    echo ""
fi

