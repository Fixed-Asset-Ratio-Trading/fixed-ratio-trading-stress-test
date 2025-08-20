function Write-Usage {
  Write-Error "Usage: .\list_swap_threads.ps1 [port]"
}

# Parse arguments with optional leading [port]
$index = 0
if ($args.Length -ge 1 -and $args[0] -match '^[0-9]+$') {
  $Port = [int]$args[0]
  $index = 1
} else {
  $Port = 8080
}

Write-Host "Listing all swap threads..." -ForegroundColor Cyan

$body = @{ method = 'list_threads'; params = @{}; id = 1 } | ConvertTo-Json -Compress

try {
  $resp = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$Port/api/jsonrpc" -ContentType 'application/json' -Body $body
  
  # Filter for swap threads (threadType = 2)
  $swapThreads = $resp.result.threads | Where-Object { $_.threadType -eq 2 }
  
  if ($swapThreads.Count -eq 0) {
    Write-Host "No swap threads found" -ForegroundColor Yellow
    exit 0
  }
  
  Write-Host "`nFound $($swapThreads.Count) swap thread(s):`n" -ForegroundColor Green
  
  # Group by pool
  $groupedByPool = $swapThreads | Group-Object -Property poolId
  
  foreach ($poolGroup in $groupedByPool) {
    Write-Host "Pool: $($poolGroup.Name)" -ForegroundColor Yellow
    
    foreach ($thread in $poolGroup.Group) {
      $direction = if ($thread.swapDirection -eq 0) { "A→B" } else { "B→A" }
      $status = switch ($thread.status) {
        0 { "Created" }
        1 { "Running" }
        2 { "Stopped" }
        3 { "Paused" }
        4 { "Stopping" }
        5 { "Failed" }
        6 { "Error" }
        default { "Unknown" }
      }
      
      Write-Host "  Thread: $($thread.threadId)" -ForegroundColor Cyan
      Write-Host "    Direction: $direction"
      Write-Host "    Status: $status"
      Write-Host "    Wallet: $($thread.publicKey)"
      Write-Host "    Initial Amount: $($thread.initialAmount)"
      Write-Host "    Created: $($thread.createdAt)"
      Write-Host ""
    }
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
