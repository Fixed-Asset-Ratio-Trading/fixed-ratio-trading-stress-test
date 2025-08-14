using System.Text.Json.Serialization;

namespace FixedRatioStressTest.Common.Models;

/// <summary>
/// Core wallet configuration that manages all token minting and pool creation
/// This wallet acts as the mint authority for all tokens created by the stress test service
/// </summary>
public class CoreWalletConfig
{
    /// <summary>
    /// Master wallet private key (Base58 encoded)
    /// This wallet is the authority for all token mints created by the service
    /// </summary>
    public string PrivateKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Public key of the core wallet
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Minimum SOL balance to maintain in core wallet
    /// Service will log warnings if balance drops below this threshold
    /// </summary>
    public ulong MinimumSolBalance { get; set; } = 10_000_000_000; // 10 SOL
    
    /// <summary>
    /// SOL amount to fund new thread wallets with
    /// </summary>
    public ulong ThreadSolFunding { get; set; } = 1_000_000_000; // 1 SOL
    
    /// <summary>
    /// When was this core wallet created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Last time the wallet balance was checked
    /// </summary>
    public DateTime LastBalanceCheck { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Current SOL balance (cached, updated during balance checks)
    /// </summary>
    public ulong CurrentSolBalance { get; set; }
}

/// <summary>
/// Real pool data that exists on the blockchain via the smart contract
/// </summary>
public class RealPoolData
{
    /// <summary>
    /// Pool ID (Program Derived Address of the pool state)
    /// </summary>
    public string PoolId { get; set; } = string.Empty;
    
    /// <summary>
    /// Token A mint address (created by core wallet as mint authority)
    /// </summary>
    public string TokenAMint { get; set; } = string.Empty;
    
    /// <summary>
    /// Token B mint address (created by core wallet as mint authority)
    /// </summary>
    public string TokenBMint { get; set; } = string.Empty;
    
    /// <summary>
    /// Token A decimals
    /// </summary>
    public int TokenADecimals { get; set; }
    
    /// <summary>
    /// Token B decimals
    /// </summary>
    public int TokenBDecimals { get; set; }
    
    /// <summary>
    /// Ratio A numerator (normalized)
    /// </summary>
    public ulong RatioANumerator { get; set; }
    
    /// <summary>
    /// Ratio B denominator (normalized)
    /// </summary>
    public ulong RatioBDenominator { get; set; }
    
    /// <summary>
    /// Transaction signature of the pool creation
    /// </summary>
    public string CreationSignature { get; set; } = string.Empty;
    
    /// <summary>
    /// When was this pool created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Last time this pool was validated against blockchain
    /// </summary>
    public DateTime LastValidated { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Is this pool still valid on the blockchain
    /// </summary>
    public bool IsValid { get; set; } = true;
    
    /// <summary>
    /// Display string for the ratio
    /// </summary>
    [JsonIgnore]
    public string RatioDisplay => $"1 Token A = {(double)RatioBDenominator / RatioANumerator:F6} Token B";
}

/// <summary>
/// Token mint information created by the core wallet
/// </summary>
public class StressTestTokenMint
{
    /// <summary>
    /// Token mint address
    /// </summary>
    public string MintAddress { get; set; } = string.Empty;
    
    /// <summary>
    /// Token decimals
    /// </summary>
    public int Decimals { get; set; }
    
    /// <summary>
    /// Core wallet is the mint authority
    /// </summary>
    public string MintAuthority { get; set; } = string.Empty;
    
    /// <summary>
    /// Transaction signature of token creation
    /// </summary>
    public string CreationSignature { get; set; } = string.Empty;
    
    /// <summary>
    /// When was this token created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Total amount minted so far
    /// </summary>
    public ulong TotalMinted { get; set; }
}
