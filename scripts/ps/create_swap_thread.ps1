function Write-Usage {
  Write-Error "Usage: .\create_swap_thread.ps1 [port] <pool_id> <direction:a_to_b|b_to_a> [initial_amount]"
}

# Parse arguments with optional leading [port]
$index = 0
if ($args.Length -ge 1 -and $args[0] -match '^[0-9]+$') {
  $Port = [int]$args[0]
  $index = 1
} else {
  $Port = 8080
}

if ($args.Length -lt ($index + 2)) { Write-Usage; exit 1 }
$PoolId = $args[$index]; $index++
$Direction = $args[$index]; $index++
$InitialAmount = if ($args.Length -ge ($index + 1)) { 
  try { [uint64]$args[$index] } catch { Write-Error "initial_amount must be an unsigned integer"; exit 1 } 
} else { 0 }

if ([string]::IsNullOrWhiteSpace($PoolId)) { Write-Usage; exit 1 }
if ($Direction -ne 'a_to_b' -and $Direction -ne 'b_to_a') { 
  Write-Error "direction must be 'a_to_b' or 'b_to_a'"
  exit 1 
}

$body = @{ 
  method = 'create_swap_thread'
  params = @{ 
    pool_id = $PoolId
    direction = $Direction
    initial_amount = $InitialAmount
  }
  id = 1 
} | ConvertTo-Json -Compress

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
