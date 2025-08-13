# Solana Transaction Building Guide for .NET Developers

## Overview

This guide documents the critical requirements for building Solana transactions in .NET applications when interacting with the Fixed Ratio Trading smart contract. These guidelines prevent common serialization issues and ensure reliable blockchain communication.

## ⚠️ Critical Issue: Solnet Transaction Serialization

### Problem
The `Solnet.Rpc.Builders.TransactionBuilder` class has known issues that can produce malformed transaction bytes, resulting in errors like:
```
failed to deserialize solana_transaction::versioned::VersionedTransaction: io error: failed to fill whole buffer
```

### Solution: Raw RPC Transaction Building

For production applications requiring reliable transaction execution, use **raw RPC calls** instead of Solnet's transaction builders.

## 🔧 Implementation Example

### 1. Manual Transaction Construction

```csharp
public class RawTransactionBuilder
{
    private readonly HttpClient _httpClient;
    private readonly string _rpcUrl;
    
    public async Task<string> BuildGetVersionTransaction(string programId)
    {
        var programIdBytes = DecodeBase58(programId);
        var feePayerBytes = new byte[32]; // Generate or provide fee payer
        var recentBlockhash = await GetLatestBlockhashAsync();
        
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        // 1. Number of signatures (1 byte)
        writer.Write((byte)1);

        // 2. Dummy signature (64 bytes) - ignored with sigVerify: false
        writer.Write(new byte[64]);

        // 3. Message header
        writer.Write((byte)1); // Number of required signatures
        writer.Write((byte)0); // Number of readonly signed accounts
        writer.Write((byte)1); // Number of readonly unsigned accounts

        // 4. Account addresses (compact array)
        writer.Write((byte)2); // 2 accounts: fee payer + program
        writer.Write(feePayerBytes); // Fee payer (32 bytes)
        writer.Write(programIdBytes); // Program ID (32 bytes)

        // 5. Recent blockhash (32 bytes)
        writer.Write(recentBlockhash);

        // 6. Instructions (compact array)
        writer.Write((byte)1); // 1 instruction

        // 7. GetVersion instruction
        writer.Write((byte)1); // Program ID index
        writer.Write((byte)0); // Accounts array length (GetVersion needs no accounts)
        writer.Write((byte)1); // Instruction data length
        writer.Write((byte)14); // GetVersion discriminator

        return Convert.ToBase64String(stream.ToArray());
    }
}
```

### 2. RPC Simulation Call

```csharp
public async Task<string> SimulateTransaction(string transactionBase64)
{
    var request = new
    {
        jsonrpc = "2.0",
        id = 1,
        method = "simulateTransaction",
        @params = new object[]
        {
            transactionBase64,
            new
            {
                sigVerify = false,
                replaceRecentBlockhash = true,
                encoding = "base64"
            }
        }
    };

    var jsonRequest = JsonSerializer.Serialize(request);
    var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
    var response = await _httpClient.PostAsync(_rpcUrl, content);
    
    return await response.Content.ReadAsStringAsync();
}
```

## 📋 Instruction Reference

### GetVersion (Discriminator: 14)
- **Purpose**: Retrieve contract version information
- **Accounts Required**: None
- **Instruction Data**: `[14]` (single byte)
- **Expected Response**: Program logs containing version information

**Example Transaction Format:**
```
Transaction Structure:
├── Signatures (65 bytes): [1 signature count] + [64-byte dummy signature]
├── Message Header (3 bytes): [1, 0, 1]
├── Accounts (65 bytes): [2 account count] + [32-byte fee payer] + [32-byte program ID]
├── Recent Blockhash (32 bytes)
└── Instructions (4 bytes): [1 instruction count] + [1 program index] + [0 accounts] + [1 data length] + [14 discriminator]
```

## 🔍 Validation Checklist

### Transaction Format Validation
- [ ] **Transaction serializes without errors**
- [ ] **Simulation returns `AccountNotFound` (not deserialization errors)**
- [ ] **Program ID is correctly encoded in Base58**
- [ ] **Recent blockhash is retrieved from RPC**
- [ ] **Instruction discriminator matches API specification**

### Expected Simulation Results

#### ✅ Success Indicators
```json
{
  "result": {
    "value": {
      "err": "AccountNotFound",  // Expected for unfunded fee payer
      "logs": [],               // May be empty for AccountNotFound
      "accounts": null
    }
  }
}
```

#### ❌ Failure Indicators
```json
{
  "error": {
    "message": "failed to deserialize solana_transaction::versioned::VersionedTransaction"
  }
}
```

## 🛠️ Development Tools

### Base58 Encoding/Decoding
```csharp
private static byte[] DecodeBase58(string base58)
{
    const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
    
    var decoded = new List<byte>();
    var num = System.Numerics.BigInteger.Zero;
    
    foreach (var c in base58)
    {
        var index = alphabet.IndexOf(c);
        if (index == -1) throw new ArgumentException($"Invalid character: {c}");
        num = num * 58 + index;
    }
    
    while (num > 0)
    {
        decoded.Insert(0, (byte)(num % 256));
        num /= 256;
    }
    
    // Handle leading zeros
    var leadingZeros = 0;
    foreach (var c in base58)
    {
        if (c == '1') leadingZeros++;
        else break;
    }
    
    for (int i = 0; i < leadingZeros; i++)
    {
        decoded.Insert(0, 0);
    }
    
    return decoded.ToArray();
}
```

### RPC Helper Methods
```csharp
private async Task<byte[]> GetLatestBlockhashAsync()
{
    var request = new
    {
        jsonrpc = "2.0",
        id = 1,
        method = "getLatestBlockhash",
        @params = new object[] { new { commitment = "confirmed" } }
    };

    // Implementation details...
    // Returns 32-byte blockhash
}
```

## 🚨 Common Pitfalls

### 1. **Using Solnet TransactionBuilder**
```csharp
// ❌ AVOID - Can produce malformed transactions
var transaction = new TransactionBuilder()
    .SetRecentBlockHash(blockhash)
    .SetFeePayer(feePayer)
    .AddInstruction(instruction)
    .Build();
```

### 2. **Incorrect Account Ordering**
- Fee payer must be first account (index 0)
- Program ID must be correctly indexed in instruction
- Account indices must match the accounts array

### 3. **Missing Recent Blockhash**
- Always fetch a real blockhash from RPC
- Don't use dummy/zero blockhashes for production transactions

### 4. **Instruction Data Format**
- Use exact discriminator values from API documentation
- Ensure proper Borsh serialization for complex data structures

## 🔗 Related Resources

- [Fixed Ratio Trading API Documentation](../api/FIXED_RATIO_TRADING_API.md)
- [Solana Transaction Format Specification](https://docs.solana.com/developing/programming-model/transactions)
- [RPC API Reference](https://docs.solana.com/api/http)

## 📝 Testing Guidelines

### Unit Test Example
```csharp
[Fact]
public async Task BuildGetVersionTransaction_ShouldProduceValidFormat()
{
    // Arrange
    var builder = new RawTransactionBuilder(rpcUrl);
    
    // Act
    var transactionBase64 = await builder.BuildGetVersionTransaction(programId);
    var response = await builder.SimulateTransaction(transactionBase64);
    
    // Assert
    // Should NOT contain deserialization errors
    Assert.DoesNotContain("failed to deserialize", response);
    
    // Should indicate proper transaction format (AccountNotFound is OK)
    Assert.Contains("AccountNotFound", response);
}
```

## 🏷️ Version Compatibility

- **Solnet**: Avoid transaction builders in versions that exhibit serialization issues
- **Fixed Ratio Trading Contract**: 0.15.1053 (confirmed compatible)
- **Solana RPC**: Standard JSON-RPC API (v1.14+)

---

**Last Updated**: December 2024  
**Tested Against**: Fixed Ratio Trading Contract v0.15.1053 on Solana Localnet
