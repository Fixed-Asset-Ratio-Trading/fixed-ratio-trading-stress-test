function Write-Usage {
  Write-Host "Usage: .\list_threads.ps1 [port] [pool_id] [thread_type]"
  Write-Host ""
  Write-Host "Parameters:"
  Write-Host "  port        Optional port number (default: 8080)"
  Write-Host "  pool_id     Optional pool ID filter"
  Write-Host "  thread_type Optional thread type filter (deposit, withdrawal, swap)"
  Write-Host ""
  Write-Host "Examples:"
  Write-Host "  .\list_threads.ps1                           # List all threads"
  Write-Host "  .\list_threads.ps1 8080                      # List all threads on port 8080"
  Write-Host "  .\list_threads.ps1 8080 pool123              # List threads for pool123"
  Write-Host "  .\list_threads.ps1 8080 '' deposit           # List all deposit threads"
  Write-Host "  .\list_threads.ps1 8080 pool123 withdrawal   # List withdrawal threads for pool123"
}

# Parse arguments with optional leading [port] [pool_id] [thread_type]
$index = 0
if ($args.Length -ge 1 -and $args[0] -match '^[0-9]+$') {
  $Port = [int]$args[0]
  $index = 1
} else {
  $Port = 8080
}

$PoolId = $null
$ThreadType = $null

# Parse pool_id if provided
if ($args.Length -gt $index -and $args[$index] -ne '' -and $args[$index] -ne $null) {
  $PoolId = $args[$index]
}
$index++

# Parse thread_type if provided
if ($args.Length -gt $index -and $args[$index] -ne '' -and $args[$index] -ne $null) {
  $ThreadType = $args[$index].ToLower()
  # Validate thread type
  if ($ThreadType -notin @('deposit', 'withdrawal', 'swap')) {
    Write-Error "Invalid thread_type. Must be one of: deposit, withdrawal, swap"
    Write-Usage
    exit 1
  }
}

# Build request parameters
$params = @{}
if ($PoolId) {
  $params.pool_id = $PoolId
}
if ($ThreadType) {
  $params.thread_type = $ThreadType
}

$body = @{ method = 'list_threads'; params = $params; id = 1 } | ConvertTo-Json -Compress

try {
  $resp = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$Port/api/jsonrpc" -ContentType 'application/json' -Body $body
  $resp | ConvertTo-Json -Depth 10
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
