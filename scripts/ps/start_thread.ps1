$Port = if ($args.Length -ge 1) { $args[0] } else { 8080 }
$ThreadId = if ($args.Length -ge 2) { $args[1] } else { '' }

if ([string]::IsNullOrWhiteSpace($ThreadId)) {
  Write-Error "Usage: .\start_thread.ps1 [port] <thread_id>"
  exit 1
}

$body = @{ method = 'start_thread'; params = @{ thread_id = $ThreadId }; id = 1 } | ConvertTo-Json -Compress

Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$Port/api/jsonrpc" -ContentType 'application/json' -Body $body | ConvertTo-Json
