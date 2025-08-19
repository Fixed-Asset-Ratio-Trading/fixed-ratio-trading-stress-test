$Port = if ($args.Length -ge 1) { $args[0] } else { 8080 }

$body = @{ method = 'list_threads'; params = @{}; id = 1 } | ConvertTo-Json -Compress

Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$Port/api/jsonrpc" -ContentType 'application/json' -Body $body | ConvertTo-Json
