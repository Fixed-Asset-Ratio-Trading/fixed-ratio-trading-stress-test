$ErrorActionPreference = 'Stop'

param(
    [string]$BaseUrl = "http://localhost:8080"
)

$url = "$BaseUrl/api/pool/list"
Write-Host "GET $url" -ForegroundColor Cyan

try {
    $resp = Invoke-RestMethod -Method GET -Uri $url -TimeoutSec 30
    $resp | ConvertTo-Json -Depth 8
} catch {
    Write-Error $_
    exit 1
}


