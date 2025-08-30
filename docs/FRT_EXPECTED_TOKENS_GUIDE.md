# Fixed Ratio Trading Expected Tokens Calculation Guide

## Overview

The Fixed Ratio Trading (FRT) system uses deterministic calculations for token swaps based on predetermined fixed ratios. This guide explains the mathematical foundation and implementation details for calculating expected output tokens.

## Core Formula

The FRT system uses a simple but precise formula for all swap calculations:

### For A→B Swaps:
```
Output_B = (Input_A × Ratio_B) ÷ Ratio_A
```

### For B→A Swaps:
```
Output_A = (Input_B × Ratio_A) ÷ Ratio_B
```

**Important:** All calculations use integer division, which means any fractional remainder is truncated (rounded down to zero).

## Understanding Ratios and Basis Points

### What are Basis Points?

- **Basis points** are the smallest unit of a token (similar to satoshis for Bitcoin)
- For a token with 6 decimals (like USDC): 1 USDC = 1,000,000 basis points
- For a token with 9 decimals (like SOL): 1 SOL = 1,000,000,000 basis points
- For a token with 0 decimals: 1 token = 1 basis point

### How Ratios are Stored

Ratios in FRT are stored as basis point values. For example:

- **1 SOL = 160 USDC** is stored as:
  - Ratio_A (SOL): 1,000,000,000 (1 SOL in basis points)
  - Ratio_B (USDC): 160,000,000 (160 USDC in basis points)

## Implementation Example

### C# Implementation (FRTExpectedTokens)

```csharp
public static ulong Calculate(
    ulong valueIn,
    int tokenADecimals,
    int tokenBDecimals,
    ulong tokenARatio,
    ulong tokenBRatio,
    bool aToB)
{
    if (aToB)
    {
        // A→B swap: output_B = input_A × ratio_B ÷ ratio_A
        return checked((valueIn * tokenBRatio) / tokenARatio);
    }
    else
    {
        // B→A swap: output_A = input_B × ratio_A ÷ ratio_B
        return checked((valueIn * tokenARatio) / tokenBRatio);
    }
}
```

### JavaScript Implementation

```javascript
function calculateExpectedTokens(valueIn, tokenADecimals, tokenBDecimals, tokenARatio, tokenBRatio, aToB) {
    if (aToB) {
        // A→B swap
        return Math.floor((valueIn * tokenBRatio) / tokenARatio);
    } else {
        // B→A swap
        return Math.floor((valueIn * tokenARatio) / tokenBRatio);
    }
}
```

## Practical Examples

### Example 1: SOL to USDC (1:160 ratio)

**Pool Configuration:**
- Token A: SOL (9 decimals)
- Token B: USDC (6 decimals)
- Ratio: 1 SOL = 160 USDC
- Ratio_A: 1,000,000,000 (1 SOL)
- Ratio_B: 160,000,000 (160 USDC)

**Swap 0.5 SOL to USDC:**
```
Input: 500,000,000 basis points (0.5 SOL)
Calculation: 500,000,000 × 160,000,000 ÷ 1,000,000,000 = 80,000,000
Output: 80,000,000 basis points = 80 USDC
```

### Example 2: USDC to SOL (160:1 ratio)

**Same pool, opposite direction:**

**Swap 80 USDC to SOL:**
```
Input: 80,000,000 basis points (80 USDC)
Calculation: 80,000,000 × 1,000,000,000 ÷ 160,000,000 = 500,000,000
Output: 500,000,000 basis points = 0.5 SOL
```

### Example 3: Tokens with Different Decimals

**Pool Configuration:**
- Token A: ABC (9 decimals)
- Token B: XYZ (2 decimals)
- Ratio: 1 ABC = 1 XYZ (1:1)
- Ratio_A: 1,000,000,000 (1 ABC)
- Ratio_B: 100 (1 XYZ)

**Swap 0.5 ABC to XYZ:**
```
Input: 500,000,000 basis points (0.5 ABC)
Calculation: 500,000,000 × 100 ÷ 1,000,000,000 = 50
Output: 50 basis points = 0.50 XYZ
```

## Common Pitfalls and Solutions

### 1. Dust Amounts (Zero Output)

**Problem:** Small input amounts may result in zero output due to integer division.

**Example:**
```
Pool: 1 ABC (9 decimals) = 1000 XYZ (0 decimals)
Input: 999,999 basis points (0.000999999 ABC)
Calculation: 999,999 × 1,000 ÷ 1,000,000,000 = 0.999... → 0
Output: 0 (dust eliminated)
```

**Solution:** Use `MinimumInputForOutput` to calculate the minimum viable trade:
```csharp
var minInput = FRTExpectedTokens.MinimumInputForOutput(
    tokenADecimals, tokenBDecimals, tokenARatio, tokenBRatio, aToB);
```

### 2. Overflow with Large Numbers

**Problem:** Multiplication of large numbers can cause overflow.

**Solution:** Use checked arithmetic or BigInteger for intermediate calculations:
```csharp
// C# with checked arithmetic
return checked((valueIn * tokenBRatio) / tokenARatio);

// JavaScript with BigInt
const output = (BigInt(valueIn) * BigInt(tokenBRatio)) / BigInt(tokenARatio);
return Number(output);
```

### 3. Wrong Direction Confusion

**Problem:** Mixing up which ratio to multiply vs divide.

**Remember:**
- A→B: Multiply by B ratio, divide by A ratio
- B→A: Multiply by A ratio, divide by B ratio

## Testing Your Implementation

### Test Case 1: Simple 1:1 Ratio
```
Tokens: Both 6 decimals
Ratio: 1:1
Input: 1,000,000 (1 token)
Expected: 1,000,000 (1 token)
```

### Test Case 2: Different Decimals
```
Token A: 9 decimals, Token B: 6 decimals
Ratio: 1:1000
Input A→B: 1,000,000,000 (1 A)
Expected: 1,000,000,000 (1000 B)
```

### Test Case 3: Fractional Amounts
```
Token A: 6 decimals, Token B: 6 decimals
Ratio: 3:2
Input A→B: 1,500,000 (1.5 A)
Expected: 1,000,000 (1.0 B)
```

## Integration with Smart Contract

The on-chain Fixed Ratio Trading contract validates that the `expected_amount_out` parameter matches its calculation exactly. Any mismatch results in error code `0x417` (AMOUNT_MISMATCH).

**Best Practice:** Always use the FRTExpectedTokens calculation to determine the exact output before submitting a swap transaction:

```csharp
// Calculate expected output
var expectedOutput = FRTExpectedTokens.Calculate(
    inputAmount, 
    pool.TokenADecimals, 
    pool.TokenBDecimals,
    pool.RatioANumerator, 
    pool.RatioBDenominator,
    isAtoB
);

// Submit swap with exact expected amount
await ExecuteSwapAsync(wallet, poolId, direction, inputAmount, expectedOutput);
```

## Debugging Tips

1. **Log All Values:** When debugging, log ratios, decimals, and intermediate calculations
2. **Check Units:** Ensure all values are in basis points, not whole tokens
3. **Validate Ratios:** Confirm ratios match the pool's intended exchange rate
4. **Test Edge Cases:** Try minimum amounts, maximum amounts, and amounts that should produce dust

## Summary

The FRTExpectedTokens calculation is deterministic and straightforward:
- Use the correct formula based on swap direction
- Work entirely in basis points
- Apply integer division (truncating remainders)
- Match the contract's calculation exactly for successful swaps

This approach ensures predictable, slippage-free trading with no surprises or failed transactions due to calculation mismatches.
