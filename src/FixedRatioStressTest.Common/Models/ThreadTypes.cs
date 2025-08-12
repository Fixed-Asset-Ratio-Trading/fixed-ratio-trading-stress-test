using System.Text.Json.Serialization;

namespace FixedRatioStressTest.Common.Models;

public enum ThreadType
{
    Deposit,
    Withdrawal,
    Swap
}

public enum TokenType
{
    A,
    B
}

public enum SwapDirection
{
    AToB,
    BToA
}

public enum ThreadStatus
{
    Created,
    Running,
    Stopped,
    Error
}

