param(
    [string]$BaseUrl = "http://localhost:8080"
)

$ErrorActionPreference = 'Stop'

# Normalize BaseUrl: accept numeric port or invalid string and fallback to default
if ($BaseUrl -notmatch '^https?://') {
    if ($BaseUrl -match '^[0-9]+$') {
        $BaseUrl = "http://localhost:$BaseUrl"
    } else {
        $BaseUrl = "http://localhost:8080"
    }
}

$url = "$BaseUrl/api/pool/list"
Write-Host "GET $url" -ForegroundColor Cyan

try {
    $resp = Invoke-RestMethod -Method GET -Uri $url -TimeoutSec 30
    $resp | ConvertTo-Json -Depth 8
} catch {
    Write-Error $_
    exit 1
}


