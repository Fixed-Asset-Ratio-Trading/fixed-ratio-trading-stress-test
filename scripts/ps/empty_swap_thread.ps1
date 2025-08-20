function Write-Usage {
  Write-Error "Usage: .\empty_swap_thread.ps1 [port] <thread_id>"
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
$ThreadId = $args[$index]

if ([string]::IsNullOrWhiteSpace($ThreadId)) { Write-Usage; exit 1 }

Write-Host "Emptying swap thread: $ThreadId" -ForegroundColor Yellow
Write-Host "This will burn all input tokens and any received output tokens..." -ForegroundColor Red

$body = @{ 
  method = 'empty_thread'
  params = @{ thread_id = $ThreadId }
  id = 1 
} | ConvertTo-Json -Compress

try {
  $resp = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$Port/api/jsonrpc" -ContentType 'application/json' -Body $body
  
  if ($resp.result) {
    $result = $resp.result
    Write-Host "`nEmpty operation completed:" -ForegroundColor Green
    Write-Host "  Thread Type: $($result.thread_type)"
    
    if ($result.empty_operation) {
      $op = $result.empty_operation
      Write-Host "  Swap Direction: $($op.swap_direction)"
      Write-Host "  Input Tokens Swapped: $($op.tokens_swapped_in)"
      Write-Host "  Output Tokens Received: $($op.tokens_swapped_out)"
      Write-Host "  Total Tokens Burned: $($op.tokens_burned)"
      Write-Host "  Operation Successful: $($op.operation_successful)"
      
      if ($op.error_message) {
        Write-Host "  Error: $($op.error_message)" -ForegroundColor Red
      }
      
      if ($op.transaction_signature) {
        Write-Host "  Transaction: $($op.transaction_signature)" -ForegroundColor Cyan
      }
    }
    
    if ($result.post_empty_balances) {
      Write-Host "`nPost-Empty Balances:" -ForegroundColor Cyan
      Write-Host "  SOL: $($result.post_empty_balances.sol_balance)"
      Write-Host "  Token A: $($result.post_empty_balances.token_a_balance)"
      Write-Host "  Token B: $($result.post_empty_balances.token_b_balance)"
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
