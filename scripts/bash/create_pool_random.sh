#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://localhost:8080}"
ID=$(python - <<'PY'
import uuid; print(uuid.uuid4())
PY
)

echo "POST $BASE_URL/api/jsonrpc (create_pool_random)"
curl -sS -H 'Content-Type: application/json' \
  -d '{"method":"create_pool_random","id":"'"$ID"'","params":{}}' \
  "$BASE_URL/api/jsonrpc" | jq .
