function Write-Usage {
  Write-Host "Usage: .\get_thread_stats.ps1 [port] <thread_id>"
  Write-Host ""
  Write-Host "Parameters:"
  Write-Host "  port       Optional port number (default: 8080)"
  Write-Host "  thread_id  Required thread ID to get statistics for"
  Write-Host ""
  Write-Host "Examples:"
  Write-Host "  .\get_thread_stats.ps1 dep-12345678                    # Get stats for thread on default port"
  Write-Host "  .\get_thread_stats.ps1 8080 dep-12345678               # Get stats for thread on port 8080"
  Write-Host "  .\get_thread_stats.ps1 9000 swp-87654321               # Get stats for swap thread on port 9000"
}

# Parse arguments with optional leading [port]
$index = 0
if ($args.Length -ge 1 -and $args[0] -match '^[0-9]+$') {
  $Port = [int]$args[0]
  $index = 1
} else {
  $Port = 8080
}

# Extract thread_id (required)
if ($args.Length -le $index) {
  Write-Usage
  exit 1
}
$ThreadId = $args[$index]

if ([string]::IsNullOrWhiteSpace($ThreadId)) {
  Write-Error "thread_id is required"
  Write-Usage
  exit 1
}

$BaseUrl = "http://localhost:$Port"
$body = @{ 
  method = 'get_thread_stats'
  params = @{ thread_id = $ThreadId }
  id = 1 
} | ConvertTo-Json -Compress

try {
  $response = Invoke-RestMethod -Uri "$BaseUrl/api/jsonrpc" -Method Post -Body $body -ContentType "application/json"
  
  if ($response.error) {
    Write-Error "API Error: $($response.error.message)"
    exit 1
  }
  
  Write-Host "Thread Statistics for: $ThreadId" -ForegroundColor Green
  Write-Host "=================================" -ForegroundColor Green
  
  $stats = $response.result
  
  # Display basic info
  Write-Host "Status: $($stats.status)" -ForegroundColor Yellow
  Write-Host "Operations Count: $($stats.operations_count)" -ForegroundColor Cyan
  Write-Host "Successful Operations: $($stats.successful_operations)" -ForegroundColor Green
  Write-Host "Failed Operations: $($stats.failed_operations)" -ForegroundColor Red
  
  # Display balances if available
  if ($stats.current_sol_balance) {
    Write-Host "Current SOL Balance: $($stats.current_sol_balance)" -ForegroundColor Magenta
  }
  if ($stats.current_token_balance) {
    Write-Host "Current Token Balance: $($stats.current_token_balance)" -ForegroundColor Magenta
  }
  if ($stats.current_lp_token_balance) {
    Write-Host "Current LP Token Balance: $($stats.current_lp_token_balance)" -ForegroundColor Magenta
  }
  
  # Display timing info
  if ($stats.last_operation_time) {
    Write-Host "Last Operation: $($stats.last_operation_time)" -ForegroundColor Gray
  }
  if ($stats.total_runtime_seconds) {
    Write-Host "Total Runtime: $($stats.total_runtime_seconds) seconds" -ForegroundColor Gray
  }
  
  # Display error info if present
  if ($stats.last_error_message) {
    Write-Host "Last Error: $($stats.last_error_message)" -ForegroundColor Red
  }
  
} catch {
  Write-Error "Failed to get thread statistics: $($_.Exception.Message)"
  exit 1
}
