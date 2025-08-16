$ErrorActionPreference = 'Stop'

param(
    [string]$BaseUrl = "http://localhost:8080"
)

$url = "$BaseUrl/api/jsonrpc"
$body = @{ method = 'core_wallet_status'; id = [guid]::NewGuid().ToString(); params = @{} } | ConvertTo-Json

Write-Host "POST $url (core_wallet_status)" -ForegroundColor Cyan

try {
    $resp = Invoke-RestMethod -Method POST -Uri $url -ContentType 'application/json' -Body $body -TimeoutSec 30
    $resp | ConvertTo-Json -Depth 8
} catch {
    Write-Error $_
    exit 1
}


