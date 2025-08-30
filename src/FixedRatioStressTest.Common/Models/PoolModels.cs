using System.Text.Json.Serialization;

namespace FixedRatioStressTest.Common.Models
{
    public class PoolState
    {
        public string PoolId { get; set; } = string.Empty;
        public string TokenAMint { get; set; } = string.Empty;
        public string TokenBMint { get; set; } = string.Empty;
        public int TokenADecimals { get; set; }
        public int TokenBDecimals { get; set; }
        public ulong RatioANumerator { get; set; }
        public ulong RatioBDenominator { get; set; }
        public string VaultA { get; set; } = string.Empty;
        public string VaultB { get; set; } = string.Empty;
        public string LpMintA { get; set; } = string.Empty;
        public string LpMintB { get; set; } = string.Empty;
        public string MainTreasury { get; set; } = string.Empty;
        public string PoolTreasury { get; set; } = string.Empty;
        public bool PoolPaused { get; set; }
        public bool SwapsPaused { get; set; }
        public DateTime CreatedAt { get; set; }
        
        // Fee percentage fields (basis points)
        public ulong SwapFeeNumerator { get; set; }  // e.g., 30 = 0.3%
        public ulong SwapFeeDenominator { get; set; } = 10000;  // Default 10000 = 100%
        public ulong LiquidityFeeNumerator { get; set; }  // e.g., 30 = 0.3%
        public ulong LiquidityFeeDenominator { get; set; } = 10000;  // Default 10000 = 100%
        
        // Blockchain pool creation tracking
        public string? CreationSignature { get; set; }
        public string? PayerWallet { get; set; }
        
        [JsonIgnore]
        public string RatioDisplay => $"1 Token A = {(double)RatioBDenominator / RatioANumerator:F6} Token B";
        
        [JsonIgnore]
        public bool IsBlockchainPool => !string.IsNullOrEmpty(CreationSignature);
    }
    
    public class PoolCreationParams
    {
        public int? TokenADecimals { get; set; }
        public int? TokenBDecimals { get; set; }
        public ulong? RatioWholeNumber { get; set; }
        public string? RatioDirection { get; set; } // "a_to_b" or "b_to_a"
    }
    
    public class PoolConfig
    {
        public string TokenAMint { get; set; } = string.Empty;
        public string TokenBMint { get; set; } = string.Empty;
        public int TokenADecimals { get; set; }
        public int TokenBDecimals { get; set; }
        public ulong RatioANumerator { get; set; }
        public ulong RatioBDenominator { get; set; }
        
        // Additional fields needed for proper ratio calculation (matching JavaScript)
        public ulong RatioWholeNumber { get; set; }
        public string RatioDirection { get; set; } = "a_to_b";
        
        // Normalize pool configuration according to contract rules
        public void Normalize()
        {
            // Rule: Token ordering - smaller pubkey is Token A
            // This is done during pool creation based on generated mints
            
            // Rule: One side must equal exactly 10^decimals (anchored to 1)
            // If Token A is anchored to 1
            if (RatioDirection == "a_to_b")
            {
                RatioANumerator = (ulong)Math.Pow(10, TokenADecimals);
                // RatioBDenominator is set based on the whole number ratio
            }
            // If Token B is anchored to 1
            else if (RatioDirection == "b_to_a")
            {
                RatioBDenominator = (ulong)Math.Pow(10, TokenBDecimals);
                // RatioANumerator is set based on the whole number ratio
            }
        }
    }
    
    public class SwapCalculation
    {
        public ulong InputAmount { get; set; }
        public ulong OutputAmount { get; set; }
        public ulong MinimumOutputAmount { get; set; }
        public double PriceImpact { get; set; }
        public string SwapDirection { get; set; } = string.Empty;
        public ulong FeeAmount { get; set; }  // Fee deducted from output
        
        public static SwapCalculation Calculate(PoolState pool, Common.Models.SwapDirection direction, ulong inputAmount, double slippageTolerance = 0.01)
        {
            // FIXED: Use the correct Fixed Ratio Trading formulas
            // The contract calculates output based on the ratio between tokens
            ulong outputAmount = direction switch
            {
                // For A→B swaps: output = (input × ratioB) ÷ ratioA
                Common.Models.SwapDirection.AToB => (inputAmount * pool.RatioBDenominator) / pool.RatioANumerator,
                // For B→A swaps: output = (input × ratioA) ÷ ratioB
                Common.Models.SwapDirection.BToA => (inputAmount * pool.RatioANumerator) / pool.RatioBDenominator,
                _ => throw new ArgumentException("Invalid swap direction")
            };
            
            // For Fixed Ratio Trading, use exact output (no slippage tolerance)
            // The contract enforces exact calculations
            var minimumOutput = outputAmount;
            
            return new SwapCalculation
            {
                InputAmount = inputAmount,
                OutputAmount = outputAmount,
                MinimumOutputAmount = minimumOutput,
                PriceImpact = 0.0, // No price impact in fixed ratio pools
                SwapDirection = direction.ToString()
            };
        }
    }
    
    public class DepositResult
    {
        public string TransactionSignature { get; set; } = string.Empty;
        public ulong TokensDeposited { get; set; }
        public ulong LpTokensReceived { get; set; }
        public ulong PoolFeePaid { get; set; }
        public ulong NetworkFeePaid { get; set; }
    }
    
    public class WithdrawalResult
    {
        public string TransactionSignature { get; set; } = string.Empty;
        public ulong LpTokensBurned { get; set; }
        public ulong TokensWithdrawn { get; set; }
        public ulong PoolFeePaid { get; set; }
        public ulong NetworkFeePaid { get; set; }
    }
    
    public class SwapResult
    {
        public string TransactionSignature { get; set; } = string.Empty;
        public ulong InputTokens { get; set; }
        public ulong OutputTokens { get; set; }
        public ulong PoolFeePaid { get; set; }
        public ulong NetworkFeePaid { get; set; }
        public string SwapDirection { get; set; } = string.Empty;
    }

    /// <summary>
    /// Result of transaction simulation
    /// </summary>
    public class TransactionSimulationResult
    {
        public bool IsSuccessful { get; set; }
        public string? ErrorMessage { get; set; }
        public List<string> Logs { get; set; } = new();
        public ulong ComputeUnitsConsumed { get; set; }
        public Dictionary<string, object> Accounts { get; set; } = new();
        public string? TransactionSignature { get; set; }
        public bool WouldSucceed => IsSuccessful && string.IsNullOrEmpty(ErrorMessage);
        
        /// <summary>
        /// Detailed analysis of the simulation
        /// </summary>
        public string SimulationSummary { get; set; } = string.Empty;
    }
}