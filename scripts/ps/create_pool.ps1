param(
    [string]$BaseUrl = "http://localhost:8080",
    [int]$TokenADecimals = 9,
    [int]$TokenBDecimals = 6,
    [string]$Ratio = "1:2"
)

$ErrorActionPreference = 'Stop'

# Validate parameters
if ($TokenADecimals -lt 0 -or $TokenADecimals -gt 9) {
    Write-Error "TokenADecimals must be between 0 and 9"
    exit 1
}

if ($TokenBDecimals -lt 0 -or $TokenBDecimals -gt 9) {
    Write-Error "TokenBDecimals must be between 0 and 9"
    exit 1
}

# Validate ratio format
if ($Ratio -notmatch '^\d+:\d+$') {
    Write-Error "Ratio must be in format 'X:Y' where X and Y are whole numbers (e.g., '1:2', '10:1')"
    exit 1
}

$ratioParts = $Ratio.Split(':')
$left = [int]$ratioParts[0]
$right = [int]$ratioParts[1]

if (($left -ne 1 -and $right -ne 1) -or ($left -eq 1 -and $right -eq 1)) {
    Write-Error "Ratio must have exactly one side equal to 1 (e.g., '1:10' or '10:1')"
    exit 1
}

$url = "$BaseUrl/api/jsonrpc"
$body = @{ 
    jsonrpc = "2.0"
    method = 'create_pool'
    id = [guid]::NewGuid().ToString()
    params = @{
        token_a_decimals = $TokenADecimals
        token_b_decimals = $TokenBDecimals
        ratio = $Ratio
    }
} | ConvertTo-Json -Depth 3

Write-Host "Creating pool with parameters:" -ForegroundColor Cyan
Write-Host "  Token A Decimals: $TokenADecimals" -ForegroundColor White
Write-Host "  Token B Decimals: $TokenBDecimals" -ForegroundColor White
Write-Host "  Ratio: $Ratio" -ForegroundColor White
Write-Host "  URL: $url" -ForegroundColor Gray
Write-Host ""

try {
    $resp = Invoke-RestMethod -Method POST -Uri $url -ContentType 'application/json' -Body $body -TimeoutSec 60
    
    if ($resp.error) {
        Write-Host "❌ Pool creation failed:" -ForegroundColor Red
        Write-Host "   Code: $($resp.error.code)" -ForegroundColor Red
        Write-Host "   Message: $($resp.error.message)" -ForegroundColor Red
        exit 1
    }
    
    if ($resp.result) {
        Write-Host "✅ Pool created successfully!" -ForegroundColor Green
        Write-Host "   Pool ID: $($resp.result.pool_id)" -ForegroundColor White
        Write-Host "   Token A Mint: $($resp.result.token_a_mint)" -ForegroundColor White
        Write-Host "   Token B Mint: $($resp.result.token_b_mint)" -ForegroundColor White
        Write-Host "   Ratio Display: $($resp.result.ratio_display)" -ForegroundColor White
        Write-Host "   Creation Signature: $($resp.result.creation_signature)" -ForegroundColor Gray
        Write-Host ""
        
        # Output full response for scripting
        $resp | ConvertTo-Json -Depth 8
    } else {
        Write-Host "⚠️ Unexpected response format" -ForegroundColor Yellow
        $resp | ConvertTo-Json -Depth 8
    }
} catch {
    Write-Host "❌ Request failed:" -ForegroundColor Red
    Write-Host "   $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response) {
        Write-Host "   Status: $($_.Exception.Response.StatusCode)" -ForegroundColor Red
    }
    exit 1
}
