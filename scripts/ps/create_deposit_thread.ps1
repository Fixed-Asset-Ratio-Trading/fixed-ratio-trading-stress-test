$Port = if ($args.Length -ge 1) { $args[0] } else { 8080 }
$PoolId = if ($args.Length -ge 2) { $args[1] } else { '' }
$TokenType = if ($args.Length -ge 3) { $args[2] } else { 'A' }
$InitialAmount = if ($args.Length -ge 4) { [uint64]$args[3] } else { 0 }
$AutoRefill = if ($args.Length -ge 5) { [bool]::Parse($args[4]) } else { $false }
$ShareLp = if ($args.Length -ge 6) { [bool]::Parse($args[5]) } else { $true }

if ([string]::IsNullOrWhiteSpace($PoolId)) {
  Write-Error "Usage: .\create_deposit_thread.ps1 [port] <pool_id> [A|B] [initial_amount] [auto_refill:true|false] [share_lp_tokens:true|false]"
  exit 1
}

$body = @{ method = 'create_deposit_thread'; params = @{ pool_id = $PoolId; token_type = $TokenType; initial_amount = $InitialAmount; auto_refill = $AutoRefill; share_lp_tokens = $ShareLp }; id = 1 } | ConvertTo-Json -Compress

Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$Port/api/jsonrpc" -ContentType 'application/json' -Body $body | ConvertTo-Json
