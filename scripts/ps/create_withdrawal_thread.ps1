function Write-Usage {
  Write-Error "Usage: .\create_withdrawal_thread.ps1 [port] <pool_id> [A|B]"
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
$PoolId = $args[$index]; $index++
$TokenType = if ($args.Length -ge ($index + 1)) { $args[$index] } else { 'A' }

if ([string]::IsNullOrWhiteSpace($PoolId)) { Write-Usage; exit 1 }

$body = @{ method = 'create_withdrawal_thread'; params = @{ pool_id = $PoolId; token_type = $TokenType }; id = 1 } | ConvertTo-Json -Compress

try {
  Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$Port/api/jsonrpc" -ContentType 'application/json' -Body $body | ConvertTo-Json
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
