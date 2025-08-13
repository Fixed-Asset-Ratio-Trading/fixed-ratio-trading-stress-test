Find the API document located:
"C:\Users\Davinci\code\fixed-ratio-trading\docs\api\FIXED_RATIO_TRADING_API.md"

## Important: .NET Developer Requirements

⚠️ **If you are developing in .NET/C#**, please read the [Solana Transaction Building Guide](SOLANA_TRANSACTION_BUILDING_GUIDE.md) **BEFORE** implementing transaction logic.

This guide covers critical requirements for:
- Avoiding Solnet transaction serialization issues
- Building reliable raw RPC transactions
- Proper instruction formatting for the Fixed Ratio Trading contract
- Testing and validation procedures

**Key Point**: The standard Solnet `TransactionBuilder` can produce malformed transactions that fail with deserialization errors. Use the raw RPC approach documented in the guide for production applications.