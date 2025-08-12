namespace FixedRatioStressTest.Common.Models;

public class ThreadStatistics
{
    public int SuccessfulOperations { get; set; }
    public int FailedOperations { get; set; }
    public ulong TotalVolumeProcessed { get; set; }
    public ulong TotalFeesPaid { get; set; }
    public DateTime LastOperationAt { get; set; }
    public List<ThreadError> RecentErrors { get; set; } = new();
}

public class ThreadError
{
    public DateTime Timestamp { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
}

