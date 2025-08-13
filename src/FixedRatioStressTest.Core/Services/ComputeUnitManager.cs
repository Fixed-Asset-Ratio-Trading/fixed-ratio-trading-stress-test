using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Common.Models;

namespace FixedRatioStressTest.Core.Services
{
    /// <summary>
    /// Manages compute unit allocation for Solana transactions based on production-tested values
    /// </summary>
    public interface IComputeUnitManager
    {
        uint GetComputeUnits(string operation, TransactionContext? context = null);
    }

    public class ComputeUnitManager : IComputeUnitManager
    {
        private readonly ILogger<ComputeUnitManager> _logger;
        
        // Production-tested CU requirements from dashboard
        private readonly Dictionary<string, uint> _computeUnits = new()
        {
            ["process_liquidity_deposit"] = 310_000,  // Min observed: 249K
            ["process_liquidity_withdraw"] = 290_000, // Min observed: 227K
            ["process_swap_execute"] = 250_000,       // Min observed: 202K
            ["process_pool_initialize"] = 150_000,    // Min observed: 91K
            ["process_consolidate_pool_fees"] = 150_000,
            ["process_treasury_donate_sol"] = 150_000,
            ["process_system_pause"] = 150_000,
            ["process_system_unpause"] = 150_000,
            ["process_treasury_withdraw_fees"] = 150_000,
            ["process_treasury_get_info"] = 150_000,
            ["process_pool_pause"] = 150_000,
            ["process_pool_unpause"] = 150_000,
            ["process_pool_update_fees"] = 150_000,
            ["process_swap_set_owner_only"] = 150_000
        };
        
        public ComputeUnitManager(ILogger<ComputeUnitManager> logger)
        {
            _logger = logger;
        }
        
        public uint GetComputeUnits(string operation, TransactionContext? context = null)
        {
            // Dynamic calculation for consolidation
            if (operation == "process_consolidate_pool_fees" && context != null)
            {
                var units = CalculateConsolidationCU(context.PoolCount);
                _logger.LogDebug("Calculated {Units} CUs for consolidation of {PoolCount} pools", 
                    units, context.PoolCount);
                return units;
            }
            
            // Dynamic calculation for donations
            if (operation == "process_treasury_donate_sol" && context != null)
            {
                var units = CalculateDonationCU(context.DonationAmount);
                _logger.LogDebug("Calculated {Units} CUs for donation of {Amount} lamports", 
                    units, context.DonationAmount);
                return units;
            }
            
            if (_computeUnits.TryGetValue(operation, out var computeUnits))
            {
                _logger.LogDebug("Using {Units} CUs for operation {Operation}", 
                    computeUnits, operation);
                return computeUnits;
            }
            
            // Default to 150K for unknown operations
            _logger.LogWarning("Unknown operation {Operation}, defaulting to 150K CUs", operation);
            return 150_000;
        }
        
        private uint CalculateConsolidationCU(int poolCount)
        {
            // Formula: Base_CUs = 4,000 + (pool_count × 5,000)
            const uint BASE_CU = 4_000;
            const uint PER_POOL_CU = 5_000;
            const uint MAX_CU = 150_000;
            
            var calculated = BASE_CU + (uint)(poolCount * PER_POOL_CU);
            return Math.Min(calculated, MAX_CU);
        }
        
        private uint CalculateDonationCU(ulong donationLamports)
        {
            const ulong SMALL_DONATION_THRESHOLD = 1000L * 1_000_000_000L; // 1,000 SOL
            return donationLamports <= SMALL_DONATION_THRESHOLD ? 25_000u : 120_000u;
        }
    }
    
    /// <summary>
    /// Context for dynamic compute unit calculation
    /// </summary>
    public class TransactionContext
    {
        public int PoolCount { get; set; }
        public ulong DonationAmount { get; set; }
        public string Operation { get; set; } = string.Empty;
        public Dictionary<string, object> AdditionalData { get; set; } = new();
    }
}
