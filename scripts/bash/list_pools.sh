#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://localhost:8080}"
URL="$BASE_URL/api/pool/list"

echo "GET $URL"
# Try to use jq if available, otherwise just output the raw response
if command -v jq &> /dev/null; then
    curl -sS "$URL" | jq .
else
    echo "jq not found, showing raw response:"
    curl -sS "$URL"
    echo ""
fi

