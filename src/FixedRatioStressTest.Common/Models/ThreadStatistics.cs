namespace FixedRatioStressTest.Common.Models;

public class ThreadError
{
    public DateTime Timestamp { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
}

public class ThreadStatistics
{
    public int SuccessfulOperations { get; set; }
    public int FailedOperations { get; set; }
    public ulong TotalVolumeProcessed { get; set; }
    public ulong TotalFeesPaid { get; set; }
    public DateTime LastOperationAt { get; set; }
    public List<ThreadError> RecentErrors { get; set; } = new();
    
    // Deposit thread specific statistics
    public int SuccessfulDeposits { get; set; }
    public ulong TotalTokensDeposited { get; set; }
    public ulong TotalLpTokensReceived { get; set; }
    public ulong TotalLpTokensShared { get; set; }
    public int TimesRefilled { get; set; }
    public ulong TokensFromRefills { get; set; }
    public ulong TokensFromSharing { get; set; }
    
    // Withdrawal thread specific statistics
    public int SuccessfulWithdrawals { get; set; }
    public ulong TotalLpTokensUsed { get; set; }
    public ulong TotalTokensWithdrawn { get; set; }
    public ulong TotalTokensShared { get; set; }
    public ulong LpTokensFromDeposits { get; set; }
    
    // Swap thread specific statistics
    public int SuccessfulSwaps { get; set; }
    public ulong TotalInputTokens { get; set; }
    public ulong TotalOutputTokens { get; set; }
    public ulong TokensSentToOpposite { get; set; }
    public ulong TokensReceivedFromOpposite { get; set; }
    
    // Fee tracking
    public ulong TotalPoolFeesPaid { get; set; }
    public ulong TotalNetworkFeesPaid { get; set; }
    public ulong TotalSolSpent { get; set; }
    
    // Current balances
    public ulong CurrentSolBalance { get; set; }
    public ulong CurrentTokenABalance { get; set; }
    public ulong CurrentTokenBBalance { get; set; }
    public ulong CurrentLpTokenBalance { get; set; }
}

