using System.Text.Json.Serialization;

namespace FixedRatioStressTest.Common.Models;

public class PoolInfo
{
    public string PoolId { get; set; } = string.Empty;
    public string TokenAMint { get; set; } = string.Empty;
    public string TokenBMint { get; set; } = string.Empty;
    public string LpTokenMint { get; set; } = string.Empty;
    public ulong TokenAReserve { get; set; }
    public ulong TokenBReserve { get; set; }
    public ulong LpTokenSupply { get; set; }
    public bool IsActive { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class TokenAccount
{
    public string Address { get; set; } = string.Empty;
    public string Mint { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public ulong Balance { get; set; }
    public byte Decimals { get; set; }
}

public class DepositOperation
{
    public string PoolId { get; set; } = string.Empty;
    public TokenType TokenType { get; set; }
    public ulong Amount { get; set; }
    public ulong ExpectedLpTokens { get; set; }
    public string TransactionSignature { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class WithdrawalOperation
{
    public string PoolId { get; set; } = string.Empty;
    public TokenType TokenType { get; set; }
    public ulong LpTokenAmount { get; set; }
    public ulong ExpectedTokenAmount { get; set; }
    public string TransactionSignature { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class SwapOperation
{
    public string PoolId { get; set; } = string.Empty;
    public SwapDirection Direction { get; set; }
    public ulong InputAmount { get; set; }
    public ulong ExpectedOutputAmount { get; set; }
    public ulong MinimumOutputAmount { get; set; }
    public string TransactionSignature { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

