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
        
        [JsonIgnore]
        public string RatioDisplay => $"1 Token A = {(double)RatioBDenominator / RatioANumerator:F6} Token B";
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
        
        public string RatioDirection { get; set; } = "a_to_b";
    }
    
    public class SwapCalculation
    {
        public ulong InputAmount { get; set; }
        public ulong OutputAmount { get; set; }
        public ulong MinimumOutputAmount { get; set; }
        public double PriceImpact { get; set; }
        public string SwapDirection { get; set; } = string.Empty;
        
        public static SwapCalculation Calculate(PoolState pool, SwapDirection direction, ulong inputAmount, double slippageTolerance = 0.01)
        {
            // Fixed ratio swap formula: output = (input × output_ratio) ÷ input_ratio
            ulong outputAmount = direction switch
            {
                SwapDirection.AToB => (inputAmount * pool.RatioBDenominator) / pool.RatioANumerator,
                SwapDirection.BToA => (inputAmount * pool.RatioANumerator) / pool.RatioBDenominator,
                _ => throw new ArgumentException("Invalid swap direction")
            };
            
            // Apply slippage tolerance
            var minimumOutput = (ulong)(outputAmount * (1 - slippageTolerance));
            
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
}