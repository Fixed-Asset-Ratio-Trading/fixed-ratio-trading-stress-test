## GUI CLI usage, logs, and data locations

This guide explains how to run the Windows Forms GUI in headless-friendly mode from the command line, how to stop it via JSON-RPC, and where logs and persisted data are stored.

### Prerequisites
- .NET 8 SDK installed
- Git Bash or PowerShell on Windows

### Build the GUI
Git Bash:
```
cd src/FixedRatioStressTest.Hosting.Gui
dotnet build -c Release | cat
```

PowerShell:
```
cd src/FixedRatioStressTest.Hosting.Gui
dotnet build -c Release
```

### Run the GUI with auto-start (Run as Administrator)
Passing `--start` will automatically press Start 1 second after the window displays. When the GUI later receives an RPC stop request, it will stop and then exit automatically.

Important:
- On Windows, you must run the GUI elevated (Run as Administrator) or the in-process API and logging can be blocked by the OS/firewall. If you see connection errors from `curl` or the script, launch the GUI elevated as shown below.

PowerShell (recommended, from `src/FixedRatioStressTest.Hosting.Gui`):
```
Start-Process -FilePath .\bin\Release\net8.0-windows\FixedRatioStressTest.Hosting.Gui.exe -ArgumentList '--start' -Verb RunAs
```

Git Bash (invoke PowerShell for elevation, from repo root):
```
powershell -NoProfile -NonInteractive -WindowStyle Hidden -Command "Start-Process -FilePath 'src/FixedRatioStressTest.Hosting.Gui/bin/Release/net8.0-windows/FixedRatioStressTest.Hosting.Gui.exe' -ArgumentList '--start' -Verb RunAs"
```

Notes:
- The in-process API listens on `http://localhost:8080` by default.
- The GUI currently forces data and logs into the repo root `data/` and `logs/` folders. If your local repo root path differs from the hard-coded path in `Program.cs`, update it accordingly.

### Stop the GUI via JSON-RPC
After the GUI starts, you can stop it with a JSON-RPC call. If started with `--start`, the GUI will exit after stopping.

Git Bash (curl):
```
curl -s -X POST http://localhost:8080/api/jsonrpc \
  -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","method":"stop_service","params":{},"id":1}'
```

PowerShell:
```
Invoke-RestMethod -Method Post -Uri http://localhost:8080/api/jsonrpc `
  -ContentType 'application/json' `
  -Body '{"jsonrpc":"2.0","method":"stop_service","params":{},"id":1}'
```

### Calling other JSON-RPC methods (for feature testing)
All feature endpoints exposed by `FixedRatioStressTest.Web` are available via the in-process API hosted by the GUI. Example methods:
- `core_wallet_status`
- `airdrop_sol` with params: `{ "lamports": 1000000000 }` or `{ "sol_amount": 1 }`
- `create_pool` with params: `{ "token_a_decimals": 9, "token_b_decimals": 9, "ratio_whole_number": 1000, "ratio_direction": "a_to_b" }`
- `create_pool_random`
- `list_pools`
- `get_pool` with params: `{ "pool_id": "..." }`
- `stop_service`

Template (Git Bash):
```
curl -s -X POST http://localhost:8080/api/jsonrpc \
  -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","method":"METHOD_NAME","params":PARAMS_JSON,"id":1}' | jq
```

### Quick validation: airdrop_sol script
From the repo root, after launching the GUI elevated and waiting ~2–3 seconds:
```
bash scripts/bash/airdrop_sol.sh http://localhost:8080 1.5 | cat
```
Expected response includes a non-empty `signature` and `status: "success"`.

### Logs location and viewing
- File: `logs/gui.log` (rotated with up to 5 backups)

Git Bash (tail):
```
tail -n 200 -f logs/gui.log
```

PowerShell:
```
Get-Content .\logs\gui.log -Wait -Tail 200
```

### Data location and key files
All persisted data is written under `data/` at the repo root.

Key files:
- `data/core_wallet.json`: Core wallet configuration (address, balances, timestamps)
- `data/real_pools.json`: List of real pools created
- `data/token_mints.json`: Token mint metadata used by tests
- `data/threads.json`: Thread configurations
- `data/statistics.json`: Aggregate thread statistics
- `data/active_pools.json`: IDs of currently active pools

You can inspect these JSON files directly:
Git Bash:
```
cat data/core_wallet.json | jq
cat data/real_pools.json | jq
```

PowerShell:
```
Get-Content .\data\core_wallet.json
Get-Content .\data\real_pools.json
```

### Typical workflow (CLI-only)
1) Start GUI in auto-start mode.
2) Run JSON-RPC calls to exercise new features.
3) Watch `logs/gui.log` for behavior and results.
4) Inspect `data/*.json` to verify persisted changes.
5) Send `stop_service` to stop, which will also exit the GUI when started with `--start`.


### Troubleshooting
- "No such file or directory" when running the EXE from Git Bash:
  - Ensure you are in `src/FixedRatioStressTest.Hosting.Gui` before running `./bin/Release/net8.0-windows/FixedRatioStressTest.Hosting.Gui.exe`, or use the repo-relative path shown in the elevated PowerShell command.
- `curl: (7) Failed to connect to localhost port 8080`:
  - The GUI is not running or not elevated. Launch it with admin privileges using the commands above and wait a couple of seconds.
  - Check `logs/gui.log` for binding/startup errors and confirm the port under `NetworkConfiguration:HttpPort` (defaults to `8080`).
- Windows prompts for elevation (UAC):
  - Accept the prompt; otherwise, the API may be blocked and RPC calls will fail.

