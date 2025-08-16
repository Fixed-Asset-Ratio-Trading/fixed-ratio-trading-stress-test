#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://localhost:8080}"
URL="$BASE_URL/api/pool/list"

echo "GET $URL"
curl -sS "$URL" | jq .

