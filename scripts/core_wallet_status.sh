#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://localhost:8080}"
URL="$BASE_URL/api/jsonrpc"
ID=$(python - <<'PY'
import uuid; print(uuid.uuid4())
PY
)

echo "POST $URL (core_wallet_status)"
curl -sS -H 'Content-Type: application/json' \
  -d '{"method":"core_wallet_status","id":"'"$ID"'","params":{}}' \
  "$URL" | jq .

