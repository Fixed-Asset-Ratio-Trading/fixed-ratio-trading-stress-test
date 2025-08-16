param(
    [string]$BaseUrl = "http://localhost:8080",
    [double]$SolAmount = 1.0
)

$ErrorActionPreference = 'Stop'

$url = "$BaseUrl/api/jsonrpc"
$lamports = [ulong]($SolAmount * 1000000000)
$body = @{ 
    method = 'airdrop_sol'; 
    id = [guid]::NewGuid().ToString(); 
    params = @{ sol_amount = $SolAmount } 
} | ConvertTo-Json

Write-Host "POST $url (airdrop_sol) - $SolAmount SOL" -ForegroundColor Cyan

try {
    $resp = Invoke-RestMethod -Method POST -Uri $url -ContentType 'application/json' -Body $body -TimeoutSec 30
    $resp | ConvertTo-Json -Depth 8
} catch {
    Write-Error $_
    exit 1
}
