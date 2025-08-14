# Solana Transaction Building Guide

## 🚨 Critical Issue #1: Basis Points Calculation for Different Token Decimals

### **Problem Identified**

When creating pools with tokens that have different decimal places, developers commonly make an error in basis points calculation that leads to smart contract rejection.

**Date Discovered**: December 2024  
**Error**: `"Program failed to complete"` during pool creation  
**Root Cause**: Incorrect ratio calculation for tokens with different decimals

#### ❌ **Common Mistake**
```csharp
// WRONG: Treating both tokens the same regardless of decimals
var ratioANumerator = 1000000;    // Hardcoded value
var ratioBDenominator = 1000000;  // Same hardcoded value

// This creates incorrect ratios like 1000:1000 instead of proper basis points
```

#### ✅ **Correct Implementation**
```csharp
// CORRECT: Calculate basis points using actual token decimals
public (ulong ratioANumerator, ulong ratioBDenominator) CalculateBasisPoints(
    string tokenAMint, string tokenBMint, 
    int tokenADecimals, int tokenBDecimals, 
    ulong ratioWholeNumber, string ratioDirection)
{
    // Get properly ordered tokens (lexicographic byte order)
    var (orderedTokenA, orderedTokenB) = GetOrderedTokens(tokenAMint, tokenBMint);
    
    // Determine if we need to invert the ratio based on ordering
    bool needsInversion = (orderedTokenA != tokenAMint);
    if (ratioDirection == "b_to_a") needsInversion = !needsInversion;
    
    // Determine which token has which decimals after ordering
    var orderedDecimalsA = needsInversion ? tokenBDecimals : tokenADecimals;
    var orderedDecimalsB = needsInversion ? tokenADecimals : tokenBDecimals;
    
    // Convert display ratio to basis points correctly
    // For 1:N ratio: one side = 1.0 * 10^decimals, other side = N * 10^decimals
    ulong ratioANumerator = (ulong)(1 * Math.Pow(10, orderedDecimalsA));  // Always anchor A to 1
    ulong ratioBDenominator = (ulong)(ratioWholeNumber * Math.Pow(10, orderedDecimalsB));
    
    return (ratioANumerator, ratioBDenominator);
}
```

### **Real-World Example**

For a 1:2 ratio between Token A (9 decimals) and Token B (6 decimals):

```csharp
// Input: 1:2 ratio, Token A = 9 decimals, Token B = 6 decimals
// Expected: 1 Token A = 2 Token B

// WRONG calculation (common mistake):
ratioA = 1000000;     // Random value
ratioB = 2000000;     // Random value  
// Result: Incorrect ratio, smart contract rejects

// CORRECT calculation:
ratioA = 1 * 10^9 = 1,000,000,000;  // 1.0 Token A in basis points
ratioB = 2 * 10^6 = 2,000,000;      // 2.0 Token B in basis points
// Result: Perfect 1:2 ratio, smart contract accepts
```

### **Smart Contract Validation**

The Fixed Ratio Trading contract validates that ratios meet specific requirements:

1. **"One Equals 1" Rule**: Either `display_ratio_a == 1` OR `display_ratio_b == 1`
2. **SimpleRatio Type**: Both sides are whole numbers, one equals 1 (e.g., 1:2, 1:100)
3. **DecimalRatio Type**: One equals 1, other can have decimals (e.g., 1:1.01)
4. **No EngineeringRatio**: Ratios where neither side equals 1 are rejected

### **Common Errors and Solutions**

| Error | Cause | Solution |
|-------|-------|----------|
| `Program failed to complete` | Wrong basis points calculation | Use token decimals in calculation |
| `EngineeringRatio not supported` | Neither side equals 1 | Ensure one side anchors to 1.0 |
| `Invalid pool ratio` | Both sides equal 1 | Use 1:N ratio, not 1:1 for different decimals |

### **Registration Fee Requirement**

Pool creation requires a **1.15 SOL registration fee** paid to the smart contract:

```csharp
// Ensure sufficient SOL balance BEFORE pool creation
var requiredBalance = 2_500_000_000UL; // 2.5 SOL (1.15 SOL fee + 1.35 SOL buffer)
var currentBalance = await GetSolBalanceAsync(wallet.PublicKey);

if (currentBalance < requiredBalance)
{
    // Request airdrop on localnet or fund wallet manually
    await RequestAirdropAsync(wallet.PublicKey, requiredBalance - currentBalance);
}
```

**Important**: The registration fee is charged **during** pool creation, not before. Ensure your wallet has sufficient balance to cover both the fee and transaction costs.

## 🚨 Critical Issue #2: Transaction Serialization Methods

### **Problem Identified**

When building Solana transactions with Solnet 6.1.0, there are **two different submission patterns** that behave differently:

1. **✅ Working Method (Transaction Object)**
2. **❌ Failing Method (Byte Array)**

### **Root Cause Analysis**

**Date Discovered**: December 2024  
**Solnet Version**: 6.1.0  
**Error**: `failed to deserialize solana_transaction::versioned::VersionedTransaction: io error: failed to fill whole buffer`

#### ✅ **Working Pattern (Token Creation)**
```csharp
// Step 1: Build transaction returning Transaction object
var transaction = new TransactionBuilder()
    .SetFeePayer(wallet.Account.PublicKey)
    .SetRecentBlockHash(blockHash)
    .AddInstruction(instruction)
    .Build(new[] { wallet.Account, mintKeypair.Account }); // Returns Transaction object

// Step 2: Submit directly to RPC client
var result = await _rpcClient.SendTransactionAsync(transaction); // Transaction object
```

#### ❌ **Failing Pattern (Pool Creation)**
```csharp
// Step 1: Build transaction returning byte array
var transaction = new TransactionBuilder()
    .SetFeePayer(wallet.Account.PublicKey)
    .SetRecentBlockHash(blockHash)
    .AddInstruction(instruction)
    .Build(wallet.Account); // Returns byte[]

// Step 2: Submit via wrapper method
var result = await SendTransactionAsync(transaction); // byte[] wrapper
```

### **Technical Details**

The issue occurs in the **transaction submission layer**, not the transaction building logic:

- **Transaction Building**: ✅ Works correctly for both patterns
- **Instruction Data**: ✅ Discriminator `1`, proper 17-byte structure
- **Account Ordering**: ✅ Correct lexicographic byte comparison
- **Basis Points**: ✅ Proper calculation using token decimals
- **Transaction Serialization**: ❌ **Only fails with byte[] submission**

### **Evidence From Testing**

Our comprehensive diagnostic testing revealed:

1. **✅ Basic Solnet Works**: Built 231-byte transactions successfully
2. **✅ Token Creation Works**: Uses `Transaction` object submission
3. **✅ Airdrops Work**: Successfully funded wallets with 1.5 SOL
4. **✅ All Logic Correct**: Pool creation logic is sound
5. **❌ Only Pool Submission Fails**: Due to byte[] serialization issue

### **Test Results Example**

```
Token Creation (Transaction object):
✅ F9AXB2jn3GS5qQuRDjvggLmgSXuDGUAWV4VBVF2h9qWf created successfully
✅ Signature: 31ijh1QJcoiSCDFBQpso4fZP3GJfBw6meM6pLsTT3KBfWEk6fHHrUt3FFGaFiTG77Q...

Pool Creation (byte[] wrapper):
❌ failed to deserialize solana_transaction::versioned::VersionedTransaction
❌ Pool creation transaction built correctly but submission failed
```

## 🛠️ **Solutions**

### **Solution 1: Use Transaction Object Pattern (Recommended)**

Modify pool creation to use the same pattern as token creation:

```csharp
// Before (failing)
public async Task<byte[]> BuildCreatePoolTransactionAsync(Wallet payer, PoolConfig poolConfig)
{
    var builder = new TransactionBuilder()
        .SetFeePayer(payer.Account.PublicKey)
        .SetRecentBlockHash(blockHash)
        .AddInstruction(instruction);
    
    return builder.Build(payer.Account); // Returns byte[]
}

// After (working)
public async Task<Transaction> BuildCreatePoolTransactionObjectAsync(Wallet payer, PoolConfig poolConfig)
{
    var builder = new TransactionBuilder()
        .SetFeePayer(payer.Account.PublicKey)
        .SetRecentBlockHash(blockHash)
        .AddInstruction(instruction);
    
    return builder.Build(new[] { payer.Account }); // Returns Transaction object
}

// Usage
var transaction = await BuildCreatePoolTransactionObjectAsync(wallet, poolConfig);
var result = await _rpcClient.SendTransactionAsync(transaction); // Direct RPC call
```

### **Solution 2: Fix Byte Array Wrapper (Alternative)**

If you must use byte arrays, investigate the wrapper serialization:

```csharp
public async Task<string> SendTransactionAsync(byte[] transaction)
{
    try
    {
        // Add transaction format validation
        if (transaction.Length < 10)
        {
            throw new InvalidOperationException("Transaction too short");
        }
        
        // Log transaction details for debugging
        _logger.LogDebug("Sending transaction: {Length} bytes", transaction.Length);
        
        var result = await _rpcClient.SendTransactionAsync(transaction);
        // ... rest of method
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Transaction submission failed. Length: {Length}", transaction.Length);
        throw;
    }
}
```

## 🔧 **Implementation Steps**

### Phase 1: Immediate Fix
1. Create new `BuildCreatePoolTransactionObjectAsync` method
2. Update `CreateRealPoolAsync` to use Transaction object pattern
3. Test with existing pool creation flows

### Phase 2: Comprehensive Update
1. Update all transaction building methods to return Transaction objects
2. Remove byte[] wrapper methods
3. Standardize on direct RPC client usage

### Phase 3: Validation
1. Test all transaction types (deposits, withdrawals, swaps)
2. Verify no regression in existing functionality
3. Update unit tests to use new patterns

## 📋 **Best Practices**

### **DO:**
- ✅ Use `TransactionBuilder().Build(Account[])` → `Transaction` object
- ✅ Call `_rpcClient.SendTransactionAsync(Transaction)` directly
- ✅ Test transaction building separately from submission
- ✅ Use proper instruction discriminators (`1` for pool creation)
- ✅ Implement proper token ordering (lexicographic byte comparison)
- ✅ **Calculate basis points using actual token decimals** (CRITICAL)
- ✅ Ensure one side of ratio equals 1.0 in display units (SimpleRatio requirement)
- ✅ Verify wallet has sufficient SOL for registration fee (1.15 SOL + buffer)
- ✅ Fetch token decimals from mint accounts before ratio calculation

### **DON'T:**
- ❌ Use `TransactionBuilder().Build(Account)` → `byte[]`
- ❌ Create custom transaction submission wrappers
- ❌ Mix transaction object and byte array patterns
- ❌ Assume byte[] and Transaction object are equivalent
- ❌ **Use hardcoded values for ratio calculations** (CRITICAL)
- ❌ Ignore token decimal differences in basis points calculation
- ❌ Create EngineeringRatio pools (neither side equals 1)
- ❌ Attempt pool creation without sufficient SOL balance

## 🔍 **Debugging Guide**

### Symptoms of Basis Points Issue:
- Pool creation fails with "Program failed to complete"
- Transaction reaches smart contract but gets rejected
- Ratio calculation seems logical but contract rejects it
- Display ratios look correct (e.g., 1:2) but contract validation fails

### Symptoms of Serialization Issue:
- Token creation works fine
- Pool creation fails with deserialization error
- Transaction building succeeds but submission fails
- Error: "failed to fill whole buffer"

### How to Diagnose Basis Points Issue:
1. **Check ratio calculation**: Verify you're using token decimals, not hardcoded values
2. **Validate ratio type**: Ensure one side equals 1.0 in display units
3. **Log basis points**: Print calculated basis points and verify they make sense
4. **Test with simple ratio**: Try 1:2 ratio with different decimal tokens

### How to Diagnose Serialization Issue:
1. Check transaction submission pattern
2. Verify return type of `Build()` method  
3. Test with minimal transaction first
4. Compare working vs failing code paths

### Quick Test:
```csharp
// This should work
var workingTx = builder.Build(new[] { account }); // Transaction object
var result1 = await _rpcClient.SendTransactionAsync(workingTx);

// This might fail
var possibleFailTx = builder.Build(account); // byte[]
var result2 = await _rpcClient.SendTransactionAsync(possibleFailTx);
```

## 📚 **Related Information**

- **Solnet Version**: 6.1.0 (latest as of December 2024)
- **Solana RPC**: Compatible with localnet and mainnet
- **Known Issue**: May be related to versioned transaction support
- **Workaround**: Use Transaction object pattern consistently

## 🔄 **Version History**

| Date | Version | Changes |
|------|---------|---------|
| Dec 2024 | 1.1 | Added basis points calculation guide for different token decimals |
| Dec 2024 | 1.0 | Initial documentation of transaction serialization issue |

---

**⚠️ Critical Note**: These issues cost significant development time to identify. The most common issue (#1) is **incorrect basis points calculation for different token decimals** - this is a fundamental mathematical error that causes smart contract rejection. The second issue (#2) is transaction submission method incompatibility. Always verify ratio calculations first, then check transaction submission patterns when debugging Solana transaction issues.