param(
    [string]$BaseUrl = "http://localhost:8080",
    [string]$PoolId
)

$ErrorActionPreference = 'Stop'

if (-not $PoolId) {
    Write-Host "Usage: ./scripts/ps/get_pool.ps1 -BaseUrl http://localhost:8080 -PoolId <POOL_ID>" -ForegroundColor Yellow
    exit 1
}

$url = "$BaseUrl/api/jsonrpc"
$body = @{ 
    method = 'get_pool'; 
    id = [guid]::NewGuid().ToString(); 
    params = @{ pool_id = $PoolId } 
} | ConvertTo-Json

Write-Host "POST $url (get_pool) - pool_id=$PoolId" -ForegroundColor Cyan

try {
    $resp = Invoke-RestMethod -Method POST -Uri $url -ContentType 'application/json' -Body $body -TimeoutSec 30
    $resp | ConvertTo-Json -Depth 8
} catch {
    Write-Error $_
    exit 1
}


