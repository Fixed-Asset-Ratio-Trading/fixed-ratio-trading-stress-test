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

    // Fixed Ratio Trading contract error codes
    public static class ContractErrorCodes
    {
        public const int InvalidTokenPair = 1001;
        public const int InvalidRatio = 1002;
        public const int InsufficientFunds = 1003;
        public const int InvalidTokenAccount = 1004;
        public const int InvalidSwapAmount = 1005;
        public const int RentExemptError = 1006;
        public const int PoolPaused = 1007;
        public const int InvalidLpMint = 1008;
        public const int InvalidPoolState = 1009;
        public const int InvalidDonationAmount = 1010;
        public const int InvalidMintAuthority = 1011;
        public const int Unauthorized = 1012;
        public const int InvalidVaultAccount = 1013;
        public const int InvalidMainTreasury = 1014;
        public const int InvalidSystemStateAccount = 1015;
        public const int InvalidTreasuryVaultAccount = 1016;
        public const int InvalidAdmin = 1017;
        public const int ArithmeticError = 1018;
        public const int ArithmeticOverflow = 1019;
        public const int ArithmeticUnderflow = 1020;
        public const int DivisionByZero = 1021;
        public const int InvalidProgram = 1022;
        public const int SystemPaused = 1023;
        public const int SystemAlreadyPaused = 1024;
        public const int SystemNotPaused = 1025;
        public const int UnauthorizedAccess = 1026;
        public const int PoolSwapsPaused = 1027;
        public const int InvalidSwapDirection = 1028;
        public const int PoolSwapsAlreadyPaused = 1029;
        public const int PoolSwapsNotPaused = 1030;
        public const int InvalidMinimumOutput = 1031;
        public const int SlippageExceeded = 1032;
        public const int InvalidDepositAmount = 1033;
        public const int InvalidWithdrawalAmount = 1034;
        public const int InsufficientLiquidity = 1035;
        public const int InvalidLpTokenType = 1036;
        public const int InvalidOutputAmount = 1037;
        public const int InvalidSwapToTheSameToken = 1038;
        public const int PoolDoesNotExist = 1039;
        public const int PoolAlreadyExists = 1040;
        public const int InvalidBurnAuthority = 1041;
        public const int RatioCalculationError = 1042;
    }
    
    public static class ContractErrorMessages
    {
        public static string GetErrorMessage(int errorCode)
        {
            return errorCode switch
            {
                ContractErrorCodes.InvalidTokenPair => "Invalid token pair",
                ContractErrorCodes.InvalidRatio => "Invalid ratio",
                ContractErrorCodes.InsufficientFunds => "Insufficient funds",
                ContractErrorCodes.InvalidTokenAccount => "Invalid token account",
                ContractErrorCodes.InvalidSwapAmount => "Invalid swap amount",
                ContractErrorCodes.RentExemptError => "Rent exempt error",
                ContractErrorCodes.PoolPaused => "Pool is paused",
                ContractErrorCodes.InvalidLpMint => "Invalid LP mint",
                ContractErrorCodes.InvalidPoolState => "Invalid pool state",
                ContractErrorCodes.InvalidDonationAmount => "Invalid donation amount",
                ContractErrorCodes.InvalidMintAuthority => "Invalid mint authority",
                ContractErrorCodes.Unauthorized => "Unauthorized",
                ContractErrorCodes.InvalidVaultAccount => "Invalid vault account",
                ContractErrorCodes.InvalidMainTreasury => "Invalid main treasury",
                ContractErrorCodes.InvalidSystemStateAccount => "Invalid system state account",
                ContractErrorCodes.InvalidTreasuryVaultAccount => "Invalid treasury vault account",
                ContractErrorCodes.InvalidAdmin => "Invalid admin",
                ContractErrorCodes.ArithmeticError => "Arithmetic error",
                ContractErrorCodes.ArithmeticOverflow => "Arithmetic overflow",
                ContractErrorCodes.ArithmeticUnderflow => "Arithmetic underflow",
                ContractErrorCodes.DivisionByZero => "Division by zero",
                ContractErrorCodes.InvalidProgram => "Invalid program",
                ContractErrorCodes.SystemPaused => "System is paused",
                ContractErrorCodes.SystemAlreadyPaused => "System is already paused",
                ContractErrorCodes.SystemNotPaused => "System is not paused",
                ContractErrorCodes.UnauthorizedAccess => "Unauthorized access",
                ContractErrorCodes.PoolSwapsPaused => "Pool swaps are paused",
                ContractErrorCodes.InvalidSwapDirection => "Invalid swap direction",
                ContractErrorCodes.PoolSwapsAlreadyPaused => "Pool swaps are already paused",
                ContractErrorCodes.PoolSwapsNotPaused => "Pool swaps are not paused",
                ContractErrorCodes.InvalidMinimumOutput => "Invalid minimum output",
                ContractErrorCodes.SlippageExceeded => "Slippage exceeded",
                ContractErrorCodes.InvalidDepositAmount => "Invalid deposit amount",
                ContractErrorCodes.InvalidWithdrawalAmount => "Invalid withdrawal amount",
                ContractErrorCodes.InsufficientLiquidity => "Insufficient liquidity",
                ContractErrorCodes.InvalidLpTokenType => "Invalid LP token type",
                ContractErrorCodes.InvalidOutputAmount => "Invalid output amount",
                ContractErrorCodes.InvalidSwapToTheSameToken => "Invalid swap to the same token",
                ContractErrorCodes.PoolDoesNotExist => "Pool does not exist",
                ContractErrorCodes.PoolAlreadyExists => "Pool already exists",
                ContractErrorCodes.InvalidBurnAuthority => "Invalid burn authority",
                ContractErrorCodes.RatioCalculationError => "Ratio calculation error",
                _ => $"Unknown error code: {errorCode}"
            };
        }
    }
}