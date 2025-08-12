namespace FixedRatioStressTest.Common.Models;

public class ThreadConfig
{
    public string ThreadId { get; set; } = string.Empty;
    public ThreadType ThreadType { get; set; }
    public string PoolId { get; set; } = string.Empty;
    public TokenType TokenType { get; set; }
    public SwapDirection? SwapDirection { get; set; }
    public ulong InitialAmount { get; set; }
    public bool AutoRefill { get; set; }
    public bool ShareTokens { get; set; }
    public ThreadStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastOperationAt { get; set; }
    
    // Phase 2: Solana wallet information
    public string? PublicKey { get; set; }
    public byte[]? PrivateKey { get; set; }  // Store encrypted in production
    public string? WalletMnemonic { get; set; }  // Store encrypted in production
}

