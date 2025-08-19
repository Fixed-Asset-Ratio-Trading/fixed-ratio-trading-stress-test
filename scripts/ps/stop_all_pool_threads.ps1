$Port = if ($args.Length -ge 1) { $args[0] } else { 8080 }
$PoolId = if ($args.Length -ge 2) { $args[1] } else { '' }
$IncludeSwaps = if ($args.Length -ge 3) { [bool]::Parse($args[2]) } else { $false }

if ([string]::IsNullOrWhiteSpace($PoolId)) {
  Write-Error "Usage: .\stop_all_pool_threads.ps1 [port] <pool_id> [include_swaps:true|false]"
  exit 1
}

$body = @{ method = 'stop_all_pool_threads'; params = @{ pool_id = $PoolId; include_swaps = $IncludeSwaps }; id = 1 } | ConvertTo-Json -Compress

Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$Port/api/jsonrpc" -ContentType 'application/json' -Body $body | ConvertTo-Json
