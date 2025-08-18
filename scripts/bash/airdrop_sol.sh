#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://localhost:8080}"
SOL_AMOUNT="${2:-1.0}"

# Generate a simple UUID-like ID without Python
ID="$(date +%s)-$(printf "%04x" $RANDOM)-$(printf "%04x" $RANDOM)-$(printf "%04x" $RANDOM)-$(printf "%012x" $RANDOM)"

echo "POST $BASE_URL/api/jsonrpc (airdrop_sol) - $SOL_AMOUNT SOL"
# Try to use jq if available, otherwise just output the raw response
if command -v jq &> /dev/null; then
    curl -sS -H 'Content-Type: application/json' \
      -d '{"method":"airdrop_sol","id":"'"$ID"'","params":{"sol_amount":'"$SOL_AMOUNT"'}}' \
      "$BASE_URL/api/jsonrpc" | jq .
else
    echo "jq not found, showing raw response:"
    curl -sS -H 'Content-Type: application/json' \
      -d '{"method":"airdrop_sol","id":"'"$ID"'","params":{"sol_amount":'"$SOL_AMOUNT"'}}' \
      "$BASE_URL/api/jsonrpc"
    echo ""
fi
