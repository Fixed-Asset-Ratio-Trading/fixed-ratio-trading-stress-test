# Pool Creation Examples
# This script demonstrates various pool creation scenarios

param(
    [string]$BaseUrl = "http://localhost:8080"
)

$ErrorActionPreference = 'Stop'

Write-Host "🎯 Pool Creation Examples" -ForegroundColor Cyan
Write-Host "=========================" -ForegroundColor Cyan
Write-Host ""

# Example 1: Your specific request (1:2 ratio, decimals 3:0)
Write-Host "📋 Example 1: 1:2 ratio with 3:0 decimals" -ForegroundColor Yellow
Write-Host "   Command: .\create_pool.ps1 -TokenADecimals 3 -TokenBDecimals 0 -Ratio '1:2'" -ForegroundColor Gray
Write-Host ""

# Example 2: Standard tokens (9:6 decimals, 1:160 ratio)
Write-Host "📋 Example 2: Standard tokens with 1:160 ratio" -ForegroundColor Yellow
Write-Host "   Command: .\create_pool.ps1 -TokenADecimals 9 -TokenBDecimals 6 -Ratio '1:160'" -ForegroundColor Gray
Write-Host ""

# Example 3: High precision tokens (9:9 decimals, 10:1 ratio)
Write-Host "📋 Example 3: High precision tokens with 10:1 ratio" -ForegroundColor Yellow
Write-Host "   Command: .\create_pool.ps1 -TokenADecimals 9 -TokenBDecimals 9 -Ratio '10:1'" -ForegroundColor Gray
Write-Host ""

# Example 4: Integer tokens (0:0 decimals, 1:5 ratio)
Write-Host "📋 Example 4: Integer-only tokens with 1:5 ratio" -ForegroundColor Yellow
Write-Host "   Command: .\create_pool.ps1 -TokenADecimals 0 -TokenBDecimals 0 -Ratio '1:5'" -ForegroundColor Gray
Write-Host ""

# Example 5: Mixed precision (6:2 decimals, 1:100 ratio)
Write-Host "📋 Example 5: Mixed precision tokens with 1:100 ratio" -ForegroundColor Yellow
Write-Host "   Command: .\create_pool.ps1 -TokenADecimals 6 -TokenBDecimals 2 -Ratio '1:100'" -ForegroundColor Gray
Write-Host ""

Write-Host "💡 Usage Tips:" -ForegroundColor Green
Write-Host "   • One side of ratio must always be 1 (e.g., '1:2' or '10:1')" -ForegroundColor White
Write-Host "   • Decimals range from 0 to 9" -ForegroundColor White
Write-Host "   • Tokens have unlimited mint (core wallet is mint authority)" -ForegroundColor White
Write-Host "   • Pool creation costs ~1.15 SOL registration fee" -ForegroundColor White
Write-Host ""

$choice = Read-Host "Would you like to run one of these examples? Enter 1-5 or 'n' to skip"

switch ($choice) {
    "1" {
        Write-Host "🚀 Running Example 1..." -ForegroundColor Green
        & "$PSScriptRoot\create_pool.ps1" -BaseUrl $BaseUrl -TokenADecimals 3 -TokenBDecimals 0 -Ratio "1:2"
    }
    "2" {
        Write-Host "🚀 Running Example 2..." -ForegroundColor Green
        & "$PSScriptRoot\create_pool.ps1" -BaseUrl $BaseUrl -TokenADecimals 9 -TokenBDecimals 6 -Ratio "1:160"
    }
    "3" {
        Write-Host "🚀 Running Example 3..." -ForegroundColor Green
        & "$PSScriptRoot\create_pool.ps1" -BaseUrl $BaseUrl -TokenADecimals 9 -TokenBDecimals 9 -Ratio "10:1"
    }
    "4" {
        Write-Host "🚀 Running Example 4..." -ForegroundColor Green
        & "$PSScriptRoot\create_pool.ps1" -BaseUrl $BaseUrl -TokenADecimals 0 -TokenBDecimals 0 -Ratio "1:5"
    }
    "5" {
        Write-Host "🚀 Running Example 5..." -ForegroundColor Green
        & "$PSScriptRoot\create_pool.ps1" -BaseUrl $BaseUrl -TokenADecimals 6 -TokenBDecimals 2 -Ratio "1:100"
    }
    default {
        Write-Host "👋 Skipping examples. Use the individual create_pool.ps1 script with your desired parameters." -ForegroundColor Cyan
    }
}
