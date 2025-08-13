using System.Text.Json.Serialization;

namespace FixedRatioStressTest.Common.Models
{
    public class SolanaConfiguration
    {
        // Network RPC endpoints
        public const string LocalnetRpcUrl = "http://192.168.2.88:8899";
        public const string NgrokPublicRpcUrl = "https://fixed.ngrok.app";
        
        // Program IDs for different networks
        public const string LocalnetProgramId = "4aeVqtWhrUh6wpX8acNj2hpWXKEQwxjA3PYb2sHhNyCn";
        public const string DevnetProgramId = "9iqh69RqeG3RRrFBNZVoE77TMRvYboFUtC2sykaFVzB7";
        public const string MainnetProgramId = "quXSYkeZ8ByTCtYY1J1uxQmE36UZ3LmNGgE3CYMFixD";
        
        // Test wallet for localnet only
        public const string LocalnetTestWallet = "5GGZiMwU56rYL1L52q7Jz7ELkSN4iYyQqdv418hxPh6t";
        
        // Contract fee constants (in lamports/SOL)
        public const ulong REGISTRATION_FEE = 1_150_000_000; // 1.15 SOL
        public const ulong DEPOSIT_WITHDRAWAL_FEE = 1_300_000; // 0.0013 SOL
        public const ulong SWAP_CONTRACT_FEE = 27_150; // 0.00002715 SOL
        public const ulong MIN_DONATION_AMOUNT = 50_000; // 0.00005 SOL
        
        // Compute unit requirements
        public const uint DEPOSIT_COMPUTE_UNITS = 310_000;
        public const uint WITHDRAWAL_COMPUTE_UNITS = 290_000;
        public const uint SWAP_COMPUTE_UNITS = 250_000;
        
        // Operation limits
        public const double MAX_DEPOSIT_PERCENTAGE = 0.05; // 5% of balance
        public const double MAX_SWAP_PERCENTAGE = 0.02; // 2% of balance
        public const double AUTO_REFILL_THRESHOLD = 0.05; // 5% of initial amount
        
        // Timing constants (milliseconds)
        public const int MIN_OPERATION_DELAY_MS = 750;
        public const int MAX_OPERATION_DELAY_MS = 2000;
        
        // SOL balance thresholds (in lamports)
        public const ulong MIN_SOL_BALANCE = 100_000_000; // 0.1 SOL
        public const ulong SOL_AIRDROP_AMOUNT = 1_000_000_000; // 1 SOL
    }
    
    public class SolanaConfig
    {
        public string RpcUrl { get; set; } = SolanaConfiguration.LocalnetRpcUrl;
        public string ProgramId { get; set; } = SolanaConfiguration.LocalnetProgramId;
        public string Commitment { get; set; } = "confirmed";
        public int MaxRetries { get; set; } = 3;
        public int TimeoutMs { get; set; } = 30000;
        public bool UseNgrokEndpoint { get; set; } = false;
        
        public string GetActiveRpcUrl()
        {
            return UseNgrokEndpoint ? SolanaConfiguration.NgrokPublicRpcUrl : RpcUrl;
        }
    }
}
