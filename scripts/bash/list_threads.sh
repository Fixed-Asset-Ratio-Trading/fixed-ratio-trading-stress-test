#!/usr/bin/env bash

set -euo pipefail

PORT="${1:-8080}"

curl -sS -X POST "http://127.0.0.1:${PORT}/api/jsonrpc" \
  -H "Content-Type: application/json" \
  -d '{"method":"list_threads","params":{},"id":1}'


