using Microsoft.Extensions.Logging;
using Solnet.Wallet;
using FixedRatioStressTest.Common.Models;

namespace FixedRatioStressTest.Core.Services
{
    /// <summary>
    /// Handles pool configuration normalization to prevent costly ratio mistakes
    /// </summary>
    public interface IPoolNormalizer
    {
        NormalizedPoolConfig NormalizePoolConfig(
            PublicKey multipleMint,    // Abundant token (e.g., USDT)
            PublicKey baseMint,        // Valuable token (e.g., SOL)
            ulong originalRatioA,
            ulong originalRatioB);
            
        void ValidatePoolRatio(NormalizedPoolConfig config, int tokenADecimals, int tokenBDecimals);
        
        string GetExchangeRateDisplay(NormalizedPoolConfig config);
    }
    
    public class PoolNormalizer : IPoolNormalizer
    {
        private readonly ILogger<PoolNormalizer> _logger;
        
        public PoolNormalizer(ILogger<PoolNormalizer> logger)
        {
            _logger = logger;
        }
        
        public NormalizedPoolConfig NormalizePoolConfig(
            PublicKey multipleMint,    // Abundant token (e.g., USDT)
            PublicKey baseMint,        // Valuable token (e.g., SOL)
            ulong originalRatioA,
            ulong originalRatioB)
        {
            // Token normalization (smaller pubkey = Token A)
            var shouldSwap = string.Compare(multipleMint.ToString(), baseMint.ToString()) > 0;
            
            _logger.LogInformation("Normalizing pool config: Multiple={Multiple}, Base={Base}, RatioA={RatioA}, RatioB={RatioB}",
                multipleMint, baseMint, originalRatioA, originalRatioB);
            
            if (shouldSwap)
            {
                _logger.LogWarning("Token order swap required! Swapping tokens AND ratios to maintain correct exchange rate");
                
                // Swap tokens AND ratios to maintain correct exchange rate
                var config = new NormalizedPoolConfig
                {
                    TokenAMint = baseMint,
                    TokenBMint = multipleMint,
                    RatioANumerator = originalRatioB,    // Swapped!
                    RatioBDenominator = originalRatioA,  // Swapped!
                    PoolStatePda = DerivePoolStatePda(baseMint, multipleMint),
                    WasSwapped = true
                };
                
                _logger.LogInformation("After normalization: TokenA={TokenA}, TokenB={TokenB}, RatioA={RatioA}, RatioB={RatioB}",
                    config.TokenAMint, config.TokenBMint, config.RatioANumerator, config.RatioBDenominator);
                
                return config;
            }
            
            return new NormalizedPoolConfig
            {
                TokenAMint = multipleMint,
                TokenBMint = baseMint,
                RatioANumerator = originalRatioA,
                RatioBDenominator = originalRatioB,
                PoolStatePda = DerivePoolStatePda(multipleMint, baseMint),
                WasSwapped = false
            };
        }
        
        public void ValidatePoolRatio(NormalizedPoolConfig config, int tokenADecimals, int tokenBDecimals)
        {
            // Verify one side equals exactly 10^decimals (anchored to 1)
            var expectedA = (ulong)Math.Pow(10, tokenADecimals);
            var expectedB = (ulong)Math.Pow(10, tokenBDecimals);
            
            var isAAnchored = config.RatioANumerator == expectedA;
            var isBAnchored = config.RatioBDenominator == expectedB;
            
            if (!isAAnchored && !isBAnchored)
            {
                var errorMsg = $"Invalid pool ratio: neither side is anchored to 1. " +
                    $"Expected A={expectedA} or B={expectedB}, " +
                    $"got A={config.RatioANumerator}, B={config.RatioBDenominator}";
                    
                _logger.LogError(errorMsg);
                throw new InvalidOperationException(errorMsg);
            }
            
            // Log the exchange rate for verification
            var rate = (double)config.RatioBDenominator / config.RatioANumerator;
            _logger.LogInformation("Pool ratio validated: 1 Token A = {Rate:F6} Token B", rate);
            
            // Additional validation for extreme ratios
            if (rate > 1_000_000 || rate < 0.000001)
            {
                _logger.LogWarning("Extreme exchange rate detected: {Rate:F6}. Please verify this is intentional.", rate);
            }
        }
        
        public string GetExchangeRateDisplay(NormalizedPoolConfig config)
        {
            var rate = (double)config.RatioBDenominator / config.RatioANumerator;
            return $"1 {config.TokenAMint} = {rate:F6} {config.TokenBMint}";
        }
        
        private PublicKey DerivePoolStatePda(PublicKey tokenA, PublicKey tokenB)
        {
            // This is a placeholder - in real implementation, this would use the actual PDA derivation
            // For now, return a deterministic key based on the tokens
            var combined = tokenA.ToString() + tokenB.ToString();
            return new PublicKey(System.Security.Cryptography.SHA256.Create()
                .ComputeHash(System.Text.Encoding.UTF8.GetBytes(combined))
                .Take(32)
                .ToArray());
        }
    }
    
    /// <summary>
    /// Normalized pool configuration after safety checks
    /// </summary>
    public class NormalizedPoolConfig
    {
        public PublicKey TokenAMint { get; set; } = null!;
        public PublicKey TokenBMint { get; set; } = null!;
        public ulong RatioANumerator { get; set; }
        public ulong RatioBDenominator { get; set; }
        public PublicKey PoolStatePda { get; set; } = null!;
        public bool WasSwapped { get; set; }
        
        public string GetRatioDisplay()
        {
            return $"{RatioANumerator}:{RatioBDenominator}";
        }
        
        public double GetExchangeRate()
        {
            return (double)RatioBDenominator / RatioANumerator;
        }
    }
}
