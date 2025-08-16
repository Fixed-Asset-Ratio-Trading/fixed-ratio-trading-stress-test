#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://localhost:8080}"
SOL_AMOUNT="${2:-1.0}"
ID=$(python - <<'PY'
import uuid; print(uuid.uuid4())
PY
)

echo "POST $BASE_URL/api/jsonrpc (airdrop_sol) - $SOL_AMOUNT SOL"
curl -sS -H 'Content-Type: application/json' \
  -d '{"method":"airdrop_sol","id":"'"$ID"'","params":{"sol_amount":'"$SOL_AMOUNT"'}}' \
  "$BASE_URL/api/jsonrpc" | jq .
