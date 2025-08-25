# Pool Creation Scripts

This directory contains scripts for creating trading pools with the Fixed Ratio Trading stress test system.

## 🎯 Available Scripts

### PowerShell Scripts (`ps/`)

#### `create_pool.ps1` - Custom Pool Creation
Create pools with specific parameters.

**Usage:**
```powershell
.\create_pool.ps1 [-BaseUrl <url>] [-TokenADecimals <0-9>] [-TokenBDecimals <0-9>] [-Ratio <X:Y>]
```

**Parameters:**
- `BaseUrl`: API endpoint (default: `http://localhost:8080`)
- `TokenADecimals`: Token A decimal places 0-9 (default: `9`)
- `TokenBDecimals`: Token B decimal places 0-9 (default: `6`)
- `Ratio`: Trading ratio in `X:Y` format, one side must be 1 (default: `1:2`)

**Examples:**
```powershell
# Your specific request: 1:2 ratio with 3:0 decimals
.\create_pool.ps1 -TokenADecimals 3 -TokenBDecimals 0 -Ratio "1:2"

# Standard tokens with 1:160 ratio
.\create_pool.ps1 -TokenADecimals 9 -TokenBDecimals 6 -Ratio "1:160"

# High precision tokens with 10:1 ratio
.\create_pool.ps1 -TokenADecimals 9 -TokenBDecimals 9 -Ratio "10:1"

# Integer-only tokens
.\create_pool.ps1 -TokenADecimals 0 -TokenBDecimals 0 -Ratio "1:5"
```

#### `create_pool_random.ps1` - Random Pool Creation
Create pools with random parameters (decimals 6-9, random ratios).

**Usage:**
```powershell
.\create_pool_random.ps1 [-BaseUrl <url>]
```

#### `create_pool_examples.ps1` - Interactive Examples
Interactive script that demonstrates various pool creation scenarios.

**Usage:**
```powershell
.\create_pool_examples.ps1 [-BaseUrl <url>]
```

### Bash Scripts (`bash/`)

#### `create_pool.sh` - Custom Pool Creation (Linux/macOS)
Cross-platform pool creation with the same functionality as the PowerShell version.

**Usage:**
```bash
./create_pool.sh [OPTIONS]

Options:
  --url URL                 Base URL (default: http://localhost:8080)
  --token-a-decimals NUM    Token A decimals 0-9 (default: 9)
  --token-b-decimals NUM    Token B decimals 0-9 (default: 6)
  --ratio RATIO            Ratio in X:Y format (default: 1:2)
  -h, --help               Show help message
```

**Examples:**
```bash
# Your specific request
./create_pool.sh --token-a-decimals 3 --token-b-decimals 0 --ratio 1:2

# High ratio pool
./create_pool.sh --ratio 1:160

# Reverse ratio
./create_pool.sh --ratio 10:1
```

## 📋 API Reference

### Request Format
```json
{
    "jsonrpc": "2.0",
    "method": "create_pool",
    "params": {
        "token_a_decimals": 3,
        "token_b_decimals": 0,
        "ratio": "1:2"
    },
    "id": "unique-request-id"
}
```

### Response Format
```json
{
    "result": {
        "pool_id": "ABC123...",
        "token_a_mint": "DEF456...",
        "token_a_decimals": 3,
        "token_b_mint": "GHI789...",
        "token_b_decimals": 0,
        "ratio_display": "1 Token A = 2.000000 Token B",
        "creation_signature": "XYZ789...",
        "status": "created",
        "is_blockchain_pool": true
    }
}
```

## 🔧 Parameter Rules

### Decimals
- **Range**: 0-9 decimal places
- **Token A**: Controls precision of first token
- **Token B**: Controls precision of second token
- **Examples**: 
  - `0` = integer only (1, 2, 3)
  - `3` = three decimal places (1.000, 2.500)
  - `9` = nine decimal places (standard Solana token)

### Ratios
- **Format**: `"X:Y"` where X and Y are whole numbers
- **Requirement**: Exactly one side must equal 1
- **Valid Examples**:
  - `"1:2"` → 1 Token A = 2 Token B
  - `"1:160"` → 1 Token A = 160 Token B
  - `"10:1"` → 10 Token A = 1 Token B
  - `"50:1"` → 50 Token A = 1 Token B
- **Invalid Examples**:
  - `"2:4"` ❌ (use `"1:2"` instead)
  - `"1.5:2"` ❌ (no decimals allowed)
  - `"1:1"` ❌ (both sides cannot be 1)

## 💰 Cost & Requirements

- **Pool Creation Fee**: ~1.15 SOL registration fee
- **Core Wallet**: Must have sufficient SOL balance
- **Mint Authority**: Core wallet becomes mint authority for both tokens
- **Unlimited Supply**: Tokens can be minted as needed for testing

## 🚨 Common Issues

1. **Insufficient SOL**: Ensure core wallet has >1.15 SOL
2. **Invalid Ratio**: One side must be exactly 1
3. **Network Issues**: Check if API service is running on specified URL
4. **Decimal Range**: Decimals must be 0-9

## 📊 Output

All scripts provide:
- ✅ **Success indicators** with pool details
- ❌ **Error messages** with specific failure reasons
- 📄 **Full JSON response** for scripting/automation
- 🎯 **Pool ID** for use in thread creation scripts

## 🔗 Related Scripts

After creating pools, use these scripts for testing:
- `create_deposit_thread.ps1` - Create deposit threads for the pool
- `create_withdrawal_thread.ps1` - Create withdrawal threads for the pool
- `create_swap_thread.ps1` - Create swap threads for the pool
- `list_pools.ps1` - List all created pools
- `get_pool.ps1` - Get specific pool details
