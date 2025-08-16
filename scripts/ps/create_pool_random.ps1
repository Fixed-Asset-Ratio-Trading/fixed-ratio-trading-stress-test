param(
    [string]$BaseUrl = "http://localhost:8080"
)

$ErrorActionPreference = 'Stop'

$url = "$BaseUrl/api/jsonrpc"
$body = @{ 
    method = 'create_pool_random'; 
    id = [guid]::NewGuid().ToString(); 
    params = @{} 
} | ConvertTo-Json

Write-Host "POST $url (create_pool_random)" -ForegroundColor Cyan

try {
    $resp = Invoke-RestMethod -Method POST -Uri $url -ContentType 'application/json' -Body $body -TimeoutSec 30
    $resp | ConvertTo-Json -Depth 8
} catch {
    Write-Error $_
    exit 1
}
