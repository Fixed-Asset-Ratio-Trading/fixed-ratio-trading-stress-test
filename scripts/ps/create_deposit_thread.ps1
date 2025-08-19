function Write-Usage {
  Write-Error "Usage: .\create_deposit_thread.ps1 [port] <pool_id> [A|B] [initial_amount] [auto_refill:true|false] [share_lp_tokens:true|false]"
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
$TokenType = if ($args.Length -ge ($index + 1)) { $args[$index] } else { 'A' }; if ($args.Length -ge ($index + 1)) { $index++ }
$InitialAmount = if ($args.Length -ge ($index + 1)) { try { [uint64]$args[$index] } catch { Write-Error "initial_amount must be an unsigned integer"; exit 1 } } else { 0 }; if ($args.Length -ge ($index + 1)) { $index++ }
$AutoRefill = if ($args.Length -ge ($index + 1)) { try { [bool]::Parse($args[$index]) } catch { Write-Error "auto_refill must be true or false"; exit 1 } } else { $false }; if ($args.Length -ge ($index + 1)) { $index++ }
$ShareLp = if ($args.Length -ge ($index + 1)) { try { [bool]::Parse($args[$index]) } catch { Write-Error "share_lp_tokens must be true or false"; exit 1 } } else { $true }

if ([string]::IsNullOrWhiteSpace($PoolId)) { Write-Usage; exit 1 }

$body = @{ method = 'create_deposit_thread'; params = @{ pool_id = $PoolId; token_type = $TokenType; initial_amount = $InitialAmount; auto_refill = $AutoRefill; share_lp_tokens = $ShareLp }; id = 1 } | ConvertTo-Json -Compress

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
