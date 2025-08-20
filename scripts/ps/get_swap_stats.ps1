function Write-Usage {
  Write-Error "Usage: .\get_swap_stats.ps1 [port] <pool_id>"
}

# Parse arguments with optional leading [port]
$index = 0
if ($args.Length -ge 1 -and $args[0] -match '^[0-9]+$') {
  $Port = [int]$args[0]
  $index = 1
} else {
  $Port = 8080
}

if ($args.Length -lt ($index + 1)) { Write-Usage; exit 1 }
$PoolId = $args[$index]

if ([string]::IsNullOrWhiteSpace($PoolId)) { Write-Usage; exit 1 }

Write-Host "Getting swap thread statistics for pool: $PoolId" -ForegroundColor Cyan

# Get all threads for the pool
$listBody = @{ method = 'list_threads'; params = @{}; id = 1 } | ConvertTo-Json -Compress

try {
  $listResp = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$Port/api/jsonrpc" -ContentType 'application/json' -Body $listBody
  
  # Filter for swap threads on this pool
  $swapThreads = $listResp.result.threads | Where-Object { 
    $_.threadType -eq 2 -and $_.poolId -eq $PoolId 
  }
  
  if ($swapThreads.Count -eq 0) {
    Write-Host "No swap threads found for pool $PoolId" -ForegroundColor Yellow
    exit 0
  }
  
  Write-Host "`nFound $($swapThreads.Count) swap thread(s):`n" -ForegroundColor Green
  
  foreach ($thread in $swapThreads) {
    $direction = if ($thread.swapDirection -eq 0) { "A→B" } else { "B→A" }
    Write-Host "Thread: $($thread.threadId) [$direction]" -ForegroundColor Yellow
    Write-Host "  Status: $($thread.status)"
    Write-Host "  Wallet: $($thread.publicKey)"
    
    # Get detailed thread info
    $getBody = @{ method = 'get_thread'; params = @{ thread_id = $thread.threadId }; id = 2 } | ConvertTo-Json -Compress
    $threadInfo = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$Port/api/jsonrpc" -ContentType 'application/json' -Body $getBody
    
    if ($threadInfo.result.statistics) {
      $stats = $threadInfo.result.statistics
      Write-Host "  Statistics:" -ForegroundColor Cyan
      Write-Host "    Successful Swaps: $($stats.successfulSwaps)"
      Write-Host "    Failed Operations: $($stats.failedOperations)"
      Write-Host "    Total Input Tokens: $($stats.totalInputTokens)"
      Write-Host "    Total Output Tokens: $($stats.totalOutputTokens)"
      Write-Host "    Tokens Sent to Opposite: $($stats.tokensSentToOpposite)"
      Write-Host "    Tokens Received from Opposite: $($stats.tokensReceivedFromOpposite)"
      Write-Host "    Current Token A Balance: $($stats.currentTokenABalance)"
      Write-Host "    Current Token B Balance: $($stats.currentTokenBBalance)"
      Write-Host "    Last Operation: $($stats.lastOperationAt)"
    }
    Write-Host ""
  }
  
} catch {
  $err = $_
  if ($err.Exception.Response -and $err.Exception.Response.GetResponseStream) {
    $reader = New-Object System.IO.StreamReader($err.Exception.Response.GetResponseStream())
    $content = $reader.ReadToEnd()
    Write-Error "HTTP $($err.Exception.Response.StatusCode) $($err.Exception.Response.StatusDescription): $content"
  } else {
    Write-Error $err
  }
  exit 1
}
