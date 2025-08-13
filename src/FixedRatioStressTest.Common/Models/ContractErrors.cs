namespace FixedRatioStressTest.Common.Models
{
    /// <summary>
    /// Contract error codes from the Fixed Ratio Trading API
    /// </summary>
    public static class ContractErrorCodes
    {
        // System and Configuration Errors
        public const int Unauthorized = 1001;
        public const int InvalidTokenMints = 1002;
        public const int InvalidRatio = 1003;
        public const int SystemPaused = 1004;
        public const int PoolPaused = 1005;
        public const int AlreadyPaused = 1006;
        public const int NotPaused = 1007;
        public const int InvalidOwner = 1008;
        public const int InvalidSystem = 1009;
        public const int InvalidTokenDecimals = 1010;
        
        // Pool State Errors
        public const int PoolAlreadyExists = 1011;
        public const int PoolNotFound = 1012;
        public const int InvalidPoolState = 1013;
        public const int InvalidTokenAccount = 1014;
        
        // Fee Errors
        public const int InsufficientFunds = 1015;
        public const int InvalidFeeRate = 1016;
        public const int FeeTooHigh = 1017;
        public const int InvalidTreasury = 1018;
        
        // Liquidity Operation Errors
        public const int InvalidAmount = 1019;
        public const int InsufficientLiquidity = 1020;
        public const int InvalidLpTokenType = 1021;
        public const int InsufficientLpTokens = 1022;
        public const int DepositTooSmall = 1023;
        public const int WithdrawalTooSmall = 1024;
        
        // Swap Errors
        public const int SwapAmountTooSmall = 1025;
        public const int SlippageExceeded = 1026;
        public const int InvalidSwapDirection = 1027;
        public const int InvalidInputAmount = 1028;
        public const int InvalidMinimumOutput = 1029;
        public const int PoolSwapsPaused = 1030;
        
        // Account and PDA Errors
        public const int InvalidAccountOwner = 1031;
        public const int InvalidMintAuthority = 1032;
        public const int InvalidPda = 1033;
        public const int AccountAlreadyInitialized = 1034;
        public const int AccountNotInitialized = 1035;
        public const int InvalidSigner = 1036;
        
        // Program Errors
        public const int InvalidInstruction = 1037;
        public const int MissingRequiredSignature = 1038;
        public const int InvalidProgramId = 1039;
        public const int InvalidAccountData = 1040;
        public const int AccountBorrowFailed = 1041;
        public const int InstructionPackError = 1042;
    }
    
    /// <summary>
    /// Contract error messages from the Fixed Ratio Trading API
    /// </summary>
    public static class ContractErrorMessages
    {
        private static readonly Dictionary<int, string> ErrorMessages = new()
        {
            // System and Configuration Errors
            [ContractErrorCodes.Unauthorized] = "Unauthorized access",
            [ContractErrorCodes.InvalidTokenMints] = "Invalid token mints - ensure correct ordering (smaller pubkey = Token A)",
            [ContractErrorCodes.InvalidRatio] = "Invalid pool ratio - ensure one side equals 10^decimals",
            [ContractErrorCodes.SystemPaused] = "System is paused - no operations allowed",
            [ContractErrorCodes.PoolPaused] = "Pool is paused - no liquidity operations allowed",
            [ContractErrorCodes.AlreadyPaused] = "Already paused",
            [ContractErrorCodes.NotPaused] = "Not paused",
            [ContractErrorCodes.InvalidOwner] = "Invalid owner",
            [ContractErrorCodes.InvalidSystem] = "Invalid system account",
            [ContractErrorCodes.InvalidTokenDecimals] = "Invalid token decimals",
            
            // Pool State Errors
            [ContractErrorCodes.PoolAlreadyExists] = "Pool already exists for this token pair",
            [ContractErrorCodes.PoolNotFound] = "Pool not found",
            [ContractErrorCodes.InvalidPoolState] = "Invalid pool state",
            [ContractErrorCodes.InvalidTokenAccount] = "Invalid token account",
            
            // Fee Errors
            [ContractErrorCodes.InsufficientFunds] = "Insufficient funds for operation",
            [ContractErrorCodes.InvalidFeeRate] = "Invalid fee rate",
            [ContractErrorCodes.FeeTooHigh] = "Fee exceeds maximum allowed",
            [ContractErrorCodes.InvalidTreasury] = "Invalid treasury account",
            
            // Liquidity Operation Errors
            [ContractErrorCodes.InvalidAmount] = "Invalid amount - must be greater than 0",
            [ContractErrorCodes.InsufficientLiquidity] = "Insufficient liquidity in pool",
            [ContractErrorCodes.InvalidLpTokenType] = "Invalid LP token type for this operation",
            [ContractErrorCodes.InsufficientLpTokens] = "Insufficient LP tokens for withdrawal",
            [ContractErrorCodes.DepositTooSmall] = "Deposit amount too small",
            [ContractErrorCodes.WithdrawalTooSmall] = "Withdrawal amount too small",
            
            // Swap Errors
            [ContractErrorCodes.SwapAmountTooSmall] = "Swap amount too small",
            [ContractErrorCodes.SlippageExceeded] = "Slippage tolerance exceeded",
            [ContractErrorCodes.InvalidSwapDirection] = "Invalid swap direction",
            [ContractErrorCodes.InvalidInputAmount] = "Invalid input amount",
            [ContractErrorCodes.InvalidMinimumOutput] = "Invalid minimum output amount",
            [ContractErrorCodes.PoolSwapsPaused] = "Pool swaps are paused",
            
            // Account and PDA Errors
            [ContractErrorCodes.InvalidAccountOwner] = "Invalid account owner",
            [ContractErrorCodes.InvalidMintAuthority] = "Invalid mint authority",
            [ContractErrorCodes.InvalidPda] = "Invalid PDA derivation",
            [ContractErrorCodes.AccountAlreadyInitialized] = "Account already initialized",
            [ContractErrorCodes.AccountNotInitialized] = "Account not initialized",
            [ContractErrorCodes.InvalidSigner] = "Invalid signer",
            
            // Program Errors
            [ContractErrorCodes.InvalidInstruction] = "Invalid instruction",
            [ContractErrorCodes.MissingRequiredSignature] = "Missing required signature",
            [ContractErrorCodes.InvalidProgramId] = "Invalid program ID",
            [ContractErrorCodes.InvalidAccountData] = "Invalid account data",
            [ContractErrorCodes.AccountBorrowFailed] = "Account borrow failed",
            [ContractErrorCodes.InstructionPackError] = "Instruction pack error"
        };
        
        public static string GetErrorMessage(int errorCode)
        {
            return ErrorMessages.TryGetValue(errorCode, out var message) 
                ? message 
                : $"Unknown error code: {errorCode}";
        }
    }
}
