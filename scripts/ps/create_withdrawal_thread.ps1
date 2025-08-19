$Port = if ($args.Length -ge 1) { $args[0] } else { 8080 }
$PoolId = if ($args.Length -ge 2) { $args[1] } else { '' }
$TokenType = if ($args.Length -ge 3) { $args[2] } else { 'A' }

if ([string]::IsNullOrWhiteSpace($PoolId)) {
  Write-Error "Usage: .\create_withdrawal_thread.ps1 [port] <pool_id> [A|B]"
  exit 1
}

$body = @{ method = 'create_withdrawal_thread'; params = @{ pool_id = $PoolId; token_type = $TokenType }; id = 1 } | ConvertTo-Json -Compress

Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$Port/api/jsonrpc" -ContentType 'application/json' -Body $body | ConvertTo-Json
