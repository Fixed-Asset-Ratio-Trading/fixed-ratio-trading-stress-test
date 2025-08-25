param(
    [string]$BaseUrl = "http://localhost:8080"
)

$ErrorActionPreference = 'Stop'

$url = "$BaseUrl/api/jsonrpc"
$body = @{ 
    jsonrpc = "2.0"
    method = 'create_pool_random'
    id = [guid]::NewGuid().ToString()
    params = @{} 
} | ConvertTo-Json -Depth 3

Write-Host "🎲 Creating random pool..." -ForegroundColor Cyan
Write-Host "   URL: $url" -ForegroundColor Gray
Write-Host ""

try {
    $resp = Invoke-RestMethod -Method POST -Uri $url -ContentType 'application/json' -Body $body -TimeoutSec 60
    
    if ($resp.error) {
        Write-Host "❌ Random pool creation failed:" -ForegroundColor Red
        Write-Host "   Code: $($resp.error.code)" -ForegroundColor Red
        Write-Host "   Message: $($resp.error.message)" -ForegroundColor Red
        exit 1
    }
    
    if ($resp.result) {
        Write-Host "✅ Random pool created successfully!" -ForegroundColor Green
        Write-Host "   Pool ID: $($resp.result.pool_id)" -ForegroundColor White
        Write-Host "   Token A Mint: $($resp.result.token_a_mint) (Decimals: $($resp.result.token_a_decimals))" -ForegroundColor White
        Write-Host "   Token B Mint: $($resp.result.token_b_mint) (Decimals: $($resp.result.token_b_decimals))" -ForegroundColor White
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
