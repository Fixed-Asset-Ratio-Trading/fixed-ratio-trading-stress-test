# Fixed Ratio Trading Stress Test Service Design

**Document Version:** 1.0  
**Date:** January 2025  
**Purpose:** Multi-threaded stress testing service for liquidity operations and swaps  
**Target:** Linux daemon/service with RPC interface

---

## Application Purpose

### What This Application Does

The Fixed Ratio Trading Stress Test Service is designed to **bombard the Fixed Ratio Trading smart contract with high-volume, concurrent operations** to identify potential vulnerabilities, edge cases, and performance bottlenecks in the pool mathematics and contract logic before real-world deployment.

### Why This Testing Is Critical

Real-world DeFi protocols face extreme conditions that can expose flaws in:
- **Pool Mathematics**: Precision errors, overflow/underflow conditions, rounding inconsistencies
- **Liquidity Management**: Edge cases in deposit/withdrawal calculations, LP token distribution
- **Swap Logic**: Price impact calculations, slippage handling, ratio maintenance
- **Concurrency Issues**: Race conditions, state inconsistencies under high load
- **Resource Exhaustion**: Gas limit breaches, memory issues, transaction failures

### How This Application Tests the Contract

#### 1. **Mathematical Stress Testing**
The application creates scenarios designed to expose mathematical edge cases:

- **Extreme Ratios**: Creates pools with vastly different token decimal places (0 decimals vs 9 decimals)
- **Tiny Amounts**: Performs operations with minimal amounts (1 basis point) to test precision boundaries
- **Large Volumes**: Executes swaps up to 5% of pool liquidity to stress price impact calculations
- **Rapid Fluctuations**: Constant deposits/withdrawals create dynamic liquidity conditions

#### 2. **Concurrency Load Testing**
Multiple threads operate simultaneously to simulate real-world conditions:

- **Parallel Operations**: Multiple deposits, withdrawals, and swaps happening concurrently
- **Resource Contention**: Threads competing for the same pool liquidity
- **State Consistency**: Verifying that concurrent operations maintain pool invariants
- **Transaction Ordering**: Testing how the contract handles simultaneous transaction submission

#### 3. **Edge Case Discovery**
The random nature of operations creates unpredictable scenarios:

- **Empty Pool States**: What happens when liquidity is completely drained?
- **Minimal Liquidity**: How does the contract behave with extremely low liquidity?
- **Token Imbalances**: Testing extreme ratios between token A and token B reserves
- **Boundary Conditions**: Operations at the limits of allowed parameters

#### 4. **Economic Attack Simulation**
The token sharing mechanism simulates potential economic attacks:

- **Liquidity Manipulation**: Rapid large deposits followed by immediate withdrawals
- **Arbitrage Pressure**: Continuous swapping in both directions to test price stability
- **Flash-style Operations**: Quick sequences of operations to test state transitions
- **Resource Exhaustion**: Attempting to drain pools of specific tokens

### Testing Methodology

#### **Controlled Chaos Approach**
The application uses **controlled randomness** to create unpredictable but measurable stress:

```
Random Timing (750-2000ms) + Random Amounts (1bp-5%) + Concurrent Threads = Realistic Chaos
```

#### **Multi-Vector Testing**
- **Deposit Threads**: Continuously add liquidity with varying amounts and timing
- **Withdrawal Threads**: Constantly remove liquidity, creating dynamic conditions  
- **Swap Threads**: Generate trading activity that affects pool ratios and reserves
- **Token Circulation**: Automatic token sharing creates complex interaction patterns
- **Manual Intervention**: Direct token minting allows for controlled scenario creation and external pool manipulation

#### **Failure Detection Strategy**
The system is designed to **catch failures before they become exploits**:

- **Transaction Monitoring**: Every operation is logged and analyzed for unexpected results
- **Mathematical Verification**: Pool invariants can be checked after each operation
- **Error Categorization**: Distinguishes between expected failures (no liquidity) and unexpected errors
- **State Preservation**: When errors occur, full state is saved for analysis

#### **Realistic Load Simulation**
The threading model simulates realistic DeFi usage patterns:

- **Organic Timing**: Random intervals mimic human trading behavior
- **Proportional Sizing**: Operations sized relative to available balances/liquidity
- **Economic Incentives**: Token sharing creates realistic economic motivations
- **Resource Management**: SOL funding simulates gas fee management

### Expected Outcomes

#### **What Success Looks Like:**
- Contract handles all concurrent operations without state corruption
- Pool mathematics remain consistent under extreme conditions
- Transaction success/failure rates meet expected thresholds
- No exploitable edge cases discovered

#### **What Failures Might Reveal:**
- **Precision Errors**: Rounding issues in calculations leading to value leakage
- **Overflow/Underflow**: Mathematical operations exceeding safe boundaries
- **Race Conditions**: Concurrent operations causing inconsistent state
- **Economic Exploits**: Sequences of operations that drain pools improperly
- **Gas Issues**: Operations consuming excessive computation units

#### **Real-World Preparation:**
By discovering and fixing issues in this controlled environment, the contract becomes resilient against:
- High-frequency trading bots
- Sophisticated arbitrage strategies  
- Coordinated liquidity attacks
- Extreme market conditions
- Network congestion scenarios

This stress testing approach ensures the Fixed Ratio Trading protocol can handle the chaotic, high-stakes environment of production DeFi with confidence.

#### **Manual Token Distribution for Advanced Testing**
The core provides direct token minting capabilities for sophisticated testing scenarios:

- **Scenario Setup**: Create specific liquidity conditions before starting automated threads
- **External Pressure**: Simulate external traders adding liquidity while stress testing is running
- **Recovery Testing**: Manually add tokens to test pool recovery after extreme stress conditions
- **Imbalance Creation**: Deliberately create token imbalances to test rebalancing mechanisms
- **User Wallet Testing**: Fund personal wallets to manually interact with pools during stress testing
- **Baseline Establishment**: Set initial pool states for consistent testing environments

#### **Empty Command for Extreme Stress Testing**
The "empty" command enables controlled extreme scenarios by forcing threads to consume all resources immediately:

- **Liquidity Shock Testing**: Sudden massive deposits/withdrawals to test pool resilience under extreme conditions
- **Price Impact Analysis**: Large swaps that create maximum price impact to test slippage calculations
- **Resource Depletion**: Controlled draining of thread resources to test recovery mechanisms
- **Mathematical Boundaries**: Push operations to maximum sizes to discover calculation limits
- **Cleanup Operations**: Quickly reset thread states for controlled testing scenarios
- **Attack Simulation**: Mimic potential "rug pull" or large exit scenarios
- **Failure Testing**: Test contract behavior under extreme conditions where operations may fail
- **Resource Isolation**: Guarantee resource removal even when operations fail due to insufficient liquidity

---

## 1. Architecture Overview

### 1.1 Core Components
```
┌─────────────────────────────────────────────────────────────┐
│                    Stress Test Daemon                       │
├─────────────────────────────────────────────────────────────┤
│  RPC Server (HTTP JSON-RPC)                                 │
│  ├── Pool Management                                        │
│  ├── Thread Management                                      │
│  └── Status/Monitoring                                      │
├─────────────────────────────────────────────────────────────┤
│  Core Engine                                                │
│  ├── Thread Pool Manager                                    │
│  ├── Wallet Manager                                         │
│  ├── Token Distribution Controller                          │
│  └── SOL Balance Monitor                                    │
├─────────────────────────────────────────────────────────────┤
│  Worker Threads                                             │
│  ├── Deposit Threads (Token A/B specific)                   │
│  ├── Withdrawal Threads (Token A/B specific)                │
│  └── Swap Threads (A→B or B→A specific)                     │
├─────────────────────────────────────────────────────────────┤
│  State Persistence                                          │
│  ├── Thread State JSON Files                                │
│  ├── Wallet State JSON Storage                              │
│  └── Error History JSON Log                                 │
└─────────────────────────────────────────────────────────────┘
```

### 1.2 Technology Stack
- **Language:** Rust (for performance and Solana ecosystem compatibility)
- **RPC Framework:** JSON-RPC over HTTP (using `jsonrpc-http-server`)
- **Threading:** Tokio async runtime with thread pool
- **Storage:** JSON files for thread state persistence
- **Solana Integration:** `solana-client`, `spl-token`

---

## 2. Thread Types and Behavior

### 2.1 Deposit Threads
**Purpose:** Continuously deposit tokens into pool liquidity to stress test liquidity management and LP token mathematics

#### **Thread Scaling and Pool Binding**
- **Pool Isolation:** Each deposit thread is bound to exactly **one specific pool ID** and operates exclusively on that pool
- **Unlimited Scaling:** A pool can have **0 or more deposit threads** - no maximum limit imposed
- **Independent Operation:** Multiple deposit threads on the same pool operate independently with their own wallets and timing
- **Token Specialization:** Each thread focuses on either Token A or Token B deposits (not both)

#### **Configuration Parameters**
- **Pool ID:** Specific pool binding (thread cannot switch pools)
- **Token Type:** Either "A" or "B" (determines which token this thread deposits)
- **Initial Token Amount:** User-defined amount in basis points (can be 0)
- **Auto Refill:** Optional boolean setting to enable automatic token refunding (only applicable if initial_amount > 0)
- **LP Token Sharing:** Optional boolean setting to share earned LP tokens with active withdrawal threads
- **Unique Keypair/Wallet:** Each thread maintains its own independent wallet

#### **Detailed Behavior**

##### **Deposit Operation Cycle:**
1. **Check Token Balance:** Verify sufficient tokens for deposit
2. **Generate Random Amount:** Calculate deposit size (1 basis point to 5% of current token balance)
3. **Execute Deposit:** Submit liquidity deposit transaction to the contract
4. **Receive LP Tokens:** Contract mints LP tokens proportional to liquidity provided
5. **Share LP Tokens:** If sharing is enabled, immediately distribute LP tokens to active withdrawal threads
6. **Update Statistics:** Record successful operation and volume processed
7. **Random Delay:** Wait 750-2000ms before next cycle

##### **Token Funding Logic:**
- **Initial Funding:**
  - If `initial_amount > 0`: Core mints and sends initial amount to wallet on thread creation
  - If `initial_amount = 0`: Thread waits for withdrawal threads to share tokens
- **Automatic Refunding (Optional):**
  - **Only available if `initial_amount > 0`**
  - **Controlled by `auto_refill` setting (user configurable)**
  - If enabled: Monitors token balance continuously
  - When balance drops below 5% of `initial_amount`, requests funding from core
  - Core mints and sends full `initial_amount` again (not just the deficit)
  - If disabled: Thread relies on token sharing from withdrawal threads when funds run low
  - If `initial_amount = 0`, auto refunding is not applicable (relies only on token sharing)

##### **LP Token Sharing Mechanism:**
```rust
pub struct DepositThread {
    pool_id: String,
    token_type: TokenType,  // A or B
    initial_amount: u64,    // Starting token amount
    auto_refill: bool,      // Optional auto refunding (only if initial_amount > 0)
    share_lp_tokens: bool,  // Optional sharing setting
    // ... other fields
}

impl DepositThread {
    async fn handle_earned_lp_tokens(&self, new_lp_tokens: u64) {
        if self.share_lp_tokens {
            let active_withdrawal_threads = self.get_active_withdrawal_threads_for_pool_and_token(
                &self.pool_id, 
                &self.token_type
            ).await;
            
            if !active_withdrawal_threads.is_empty() {
                let amount_per_thread = new_lp_tokens / active_withdrawal_threads.len() as u64;
                
                for withdrawal_thread in active_withdrawal_threads {
                    if amount_per_thread > 0 {
                        self.transfer_lp_tokens(
                            &self.wallet,
                            &withdrawal_thread.wallet,
                            amount_per_thread
                        ).await;
                    }
                }
                
                // Log the sharing operation
                self.log_lp_sharing(new_lp_tokens, active_withdrawal_threads.len()).await;
            } else {
                // No active withdrawal threads - keep all LP tokens
                self.log_lp_retention(new_lp_tokens).await;
            }
        } else {
            // Sharing disabled - retain all LP tokens
            self.log_lp_retention(new_lp_tokens).await;
        }
    }
}
```

#### **Thread Interaction Patterns**

##### **Multiple Deposit Threads on Same Pool:**
- **Competition:** Threads compete for pool liquidity slots, testing concurrent access
- **Cumulative Effect:** Multiple threads create sustained liquidity pressure
- **Varied Timing:** Different random intervals create irregular deposit patterns
- **Independent Funding:** Each thread manages its own token supply independently

##### **Cross-Thread Token Economy:**
- **Token Sources:** Threads receive tokens from:
  1. Initial core minting (if `initial_amount > 0`)
  2. Automatic refunding (when below 5% threshold)
  3. Token sharing from withdrawal threads
- **LP Token Distribution:** If sharing enabled:
  - **Target:** Only **ACTIVE** withdrawal threads (not stopped/error state)
  - **Scope:** Only withdrawal threads for the same pool and token type
  - **Timing:** Immediate distribution after each successful deposit
  - **Fairness:** Equal distribution among all eligible withdrawal threads

#### **Empty Command Functionality**
Deposit threads support an immediate "empty" operation that consumes all available tokens at once.

##### **Empty Operation Behavior:**
```rust
impl DepositThread {
    pub async fn execute_empty_command(&mut self) -> Result<EmptyResult> {
        // Get current token balance (excluding SOL)
        let available_tokens = self.get_token_balance().await?;
        
        if available_tokens == 0 {
            return Ok(EmptyResult::no_tokens_available());
        }
        
        // ALWAYS burn the input tokens first to ensure they're removed regardless of operation outcome
        self.burn_tokens(&self.get_token_mint(), available_tokens).await?;
        let mut result = EmptyResult {
            tokens_used: available_tokens,
            tokens_burned: available_tokens,
            operation_type: "deposit_empty",
            operation_successful: false,
            ..Default::default()
        };
        
        // Attempt the deposit operation
        match self.execute_deposit(available_tokens).await {
            Ok(lp_tokens_received) => {
                // Deposit successful - burn the received LP tokens as well
                self.burn_tokens(&self.pool_lp_mint, lp_tokens_received).await?;
                result.lp_tokens_received = lp_tokens_received;
                result.lp_tokens_burned = lp_tokens_received;
                result.operation_successful = true;
            }
            Err(e) => {
                // Deposit failed (insufficient liquidity, etc.) - tokens already burned
                result.error_message = Some(format!("Deposit failed: {}", e));
                // Input tokens still burned, operation recorded as failed
            }
        }
        
        // Update statistics
        self.record_empty_operation(&result).await;
        
        Ok(result)
    }
}
```

##### **Empty Command Characteristics:**
- **Immediate Execution:** Bypasses normal random timing and amounts
- **Total Consumption:** Uses 100% of available tokens (excluding SOL for fees)
- **Guaranteed Token Burn:** Input tokens are ALWAYS burned, regardless of operation success/failure
- **No Sharing:** Burns received LP tokens instead of sharing with withdrawal threads (if operation succeeds)
- **Status Independent:** Can execute whether thread is running or stopped
- **Failure Safe:** If deposit fails due to insufficient liquidity or other errors, input tokens are still burned
- **Statistics Tracking:** Records empty operations separately from normal operations, including failures
- **Resource Cleanup:** Effectively zeroes out the thread's token holdings regardless of outcome

#### **Error Handling and Recovery**
- **Expected Errors:** Insufficient funds, temporary network issues (log and continue)
- **Unexpected Errors:** Contract panics, wallet corruption (stop thread, preserve state)
- **Recovery Strategy:** Thread can be restarted manually, resumes with saved wallet state
- **State Preservation:** All wallet keypairs and configuration saved to JSON on stop

#### **Performance Characteristics**
- **Throughput:** Limited by random timing (750-2000ms intervals) and Solana network capacity
- **Resource Usage:** Each thread maintains minimal memory footprint (wallet + statistics)
- **Scalability:** No hard limits on thread count per pool (bounded by system resources)
- **Monitoring:** Real-time statistics on deposits completed, volume processed, errors encountered

This design allows for flexible stress testing scenarios:
- **Single Thread:** Test basic deposit functionality
- **Multiple Threads:** Test concurrent deposit handling and resource contention
- **Zero Threads:** Test pools with only withdrawal/swap activity
- **Mixed Scenarios:** Combine deposit threads with/without LP sharing for complex interaction testing

### 2.2 Withdrawal Threads
**Purpose:** Continuously withdraw tokens from pool liquidity using LP tokens to stress test withdrawal mathematics and liquidity management

#### **Thread Scaling and Pool Binding**
- **Pool Isolation:** Each withdrawal thread is bound to exactly **one specific pool ID** and operates exclusively on that pool
- **Unlimited Scaling:** A pool can have **0 or more withdrawal threads** - no maximum limit imposed
- **Independent Operation:** Multiple withdrawal threads on the same pool operate independently with their own wallets and timing
- **Token Specialization:** Each thread focuses on withdrawing either Token A or Token B (not both)

#### **Configuration Parameters**
- **Pool ID:** Specific pool binding (thread cannot switch pools)
- **Token Type:** Either "A" or "B" (determines which token this thread withdraws)
- **Initial LP Token Amount:** Always 0 - LP tokens come exclusively from deposit threads
- **Unique Keypair/Wallet:** Each thread maintains its own independent wallet

#### **Detailed Behavior**

##### **Withdrawal Operation Cycle:**
1. **Check LP Token Availability:** Verify sufficient LP tokens for withdrawal operation
2. **LP Token Validation:** Ensure LP tokens are for the correct pool and compatible with target token type
3. **Generate Random Amount:** Calculate withdrawal size (1 basis point to 5% of current LP token holdings)
4. **Execute Withdrawal:** Submit liquidity withdrawal transaction to the contract
5. **Receive Tokens:** Contract burns LP tokens and returns proportional Token A or Token B
6. **Share Tokens:** Immediately distribute withdrawn tokens to active deposit threads of the same type
7. **Update Statistics:** Record successful operation and volume processed
8. **Random Delay:** Wait 750-2000ms before next cycle

##### **LP Token Management and Validation:**
- **Source Restriction:** Only accepts LP tokens from deposit threads operating on the same pool
- **Token Type Compatibility:** Only uses LP tokens that can withdraw the thread's specified token type (A or B)
- **Pool Verification:** Strict validation that LP tokens belong to the correct pool ID
- **Balance Monitoring:** Continuously tracks LP token balance from multiple deposit thread sources

##### **Active Waiting Behavior:**
```rust
pub struct WithdrawalThread {
    pool_id: String,
    token_type: TokenType,  // A or B
    // ... other fields
}

impl WithdrawalThread {
    async fn attempt_withdrawal_cycle(&self) -> Result<()> {
        // Check for valid LP tokens for this specific pool and token type
        let available_lp_tokens = self.get_valid_lp_token_balance().await?;
        
        if available_lp_tokens == 0 {
            // Thread remains active but performs no operation
            self.log_waiting_for_lp_tokens().await;
            return Ok(()); // Continue to next cycle after delay
        }
        
        // Verify LP tokens are for the correct pool
        if !self.validate_lp_tokens_for_pool(&self.pool_id).await? {
            self.log_invalid_lp_tokens().await;
            return Ok(()); // Skip this cycle
        }
        
        // Proceed with withdrawal operation
        let withdrawal_amount = self.calculate_random_withdrawal_amount(available_lp_tokens);
        self.execute_withdrawal(withdrawal_amount).await?;
        
        Ok(())
    }
    
    async fn get_valid_lp_token_balance(&self) -> Result<u64> {
        // Only count LP tokens that:
        // 1. Belong to this thread's specific pool
        // 2. Can be used to withdraw this thread's token type
        // 3. Are currently held in this thread's wallet
    }
}
```

##### **LP Token Source Tracking:**
- **Deposit Thread Sources:** Tracks which deposit threads have shared LP tokens
- **Pool Validation:** Verifies each LP token batch belongs to the correct pool
- **Token Type Compatibility:** Ensures LP tokens can withdraw the desired token type
- **Balance Reconciliation:** Maintains accurate count of usable LP tokens

##### **Token Sharing Logic:**
```rust
impl WithdrawalThread {
    async fn share_withdrawn_tokens(&self, withdrawn_tokens: u64) {
        let active_deposit_threads = self.get_active_deposit_threads_for_pool_and_token(
            &self.pool_id, 
            &self.token_type
        ).await;
        
        if !active_deposit_threads.is_empty() {
            let amount_per_thread = withdrawn_tokens / active_deposit_threads.len() as u64;
            
            for deposit_thread in active_deposit_threads {
                if amount_per_thread > 0 {
                    self.transfer_tokens(
                        &self.wallet,
                        &deposit_thread.wallet,
                        amount_per_thread
                    ).await;
                }
            }
            
            self.log_token_sharing(withdrawn_tokens, active_deposit_threads.len()).await;
        } else {
            // No active deposit threads - retain all tokens
            self.log_token_retention(withdrawn_tokens).await;
        }
    }
}
```

#### **Thread States and Waiting Behavior**

##### **Active Waiting State:**
- **Status:** Thread remains "running" but performs no withdrawals
- **Behavior:** Continues operation cycles at normal intervals (750-2000ms)
- **Monitoring:** Continuously checks for LP token availability
- **Logging:** Records waiting periods for analysis
- **Resource Usage:** Minimal - no blockchain transactions during waiting

##### **LP Token Availability Scenarios:**
1. **No LP Tokens:** Thread waits actively, performs no operations
2. **Wrong Pool LP Tokens:** Thread ignores LP tokens from other pools entirely
3. **Wrong Token Type:** Thread waits for LP tokens compatible with its token type
4. **Insufficient Amount:** Thread waits until enough LP tokens accumulate (minimum 1 basis point)
5. **Valid LP Tokens:** Thread proceeds with normal withdrawal operations

##### **Cross-Thread Coordination:**
- **Pool-Specific Operation:** Only interacts with deposit/withdrawal threads from the same pool
- **Token Type Matching:** Only shares tokens with deposit threads of the same token type
- **Non-Interference:** Cannot accidentally use resources from other pools or token types
- **Isolation:** Thread failures or waiting states don't affect other pools' operations

#### **Empty Command Functionality**
Withdrawal threads support an immediate "empty" operation that consumes all available LP tokens at once.

##### **Empty Operation Behavior:**
```rust
impl WithdrawalThread {
    pub async fn execute_empty_command(&mut self) -> Result<EmptyResult> {
        // Get current LP token balance for this specific pool
        let available_lp_tokens = self.get_valid_lp_token_balance().await?;
        
        if available_lp_tokens == 0 {
            return Ok(EmptyResult::no_lp_tokens_available());
        }
        
        // ALWAYS burn the LP tokens first to ensure they're removed regardless of operation outcome
        self.burn_tokens(&self.pool_lp_mint, available_lp_tokens).await?;
        let mut result = EmptyResult {
            lp_tokens_used: available_lp_tokens,
            lp_tokens_burned: available_lp_tokens,
            operation_type: "withdrawal_empty",
            operation_successful: false,
            ..Default::default()
        };
        
        // Attempt the withdrawal operation
        match self.execute_withdrawal(available_lp_tokens).await {
            Ok(tokens_withdrawn) => {
                // Withdrawal successful - burn the received tokens as well
                self.burn_tokens(&self.get_token_mint(), tokens_withdrawn).await?;
                result.tokens_withdrawn = tokens_withdrawn;
                result.tokens_burned = tokens_withdrawn;
                result.operation_successful = true;
            }
            Err(e) => {
                // Withdrawal failed (insufficient pool liquidity, etc.) - LP tokens already burned
                result.error_message = Some(format!("Withdrawal failed: {}", e));
                // LP tokens still burned, operation recorded as failed
            }
        }
        
        // Update statistics
        self.record_empty_operation(&result).await;
        
        Ok(result)
    }
}
```

##### **Empty Command Characteristics:**
- **Immediate Execution:** Bypasses normal random timing and amounts
- **Total Consumption:** Uses 100% of available LP tokens for the specific pool/token type
- **Guaranteed LP Token Burn:** LP tokens are ALWAYS burned, regardless of operation success/failure
- **No Sharing:** Burns withdrawn tokens instead of sharing with deposit threads (if operation succeeds)
- **Status Independent:** Can execute whether thread is running or stopped
- **Failure Safe:** If withdrawal fails due to insufficient pool liquidity or other errors, LP tokens are still burned
- **Pool Validation:** Only uses LP tokens valid for this thread's pool and token type
- **Resource Cleanup:** Effectively zeroes out the thread's LP token holdings regardless of outcome

#### **Error Handling and Recovery**
- **Expected Waiting:** No LP tokens available (normal operation, not an error)
- **Pool Mismatch:** Wrong pool LP tokens detected (log warning, continue waiting)
- **Token Type Mismatch:** Incompatible LP tokens (log warning, continue waiting)
- **Insufficient Pool Liquidity:** Pool cannot fulfill withdrawal (log warning, retry later)
- **Unexpected Errors:** Contract failures, wallet corruption (stop thread, preserve state)

#### **Performance Characteristics**
- **Efficiency:** Only operates when valid LP tokens are available
- **Patience:** Can wait indefinitely without consuming resources
- **Precision:** Strict validation prevents cross-contamination between pools
- **Scalability:** Multiple threads can wait simultaneously without interference

This design ensures withdrawal threads are:
- **Patient:** Will wait as long as needed for valid LP tokens
- **Precise:** Only use LP tokens from the correct pool and token type
- **Safe:** Cannot accidentally interfere with other pools or token types
- **Efficient:** Remain active but consume minimal resources while waiting

### 2.3 Swap Threads
**Purpose:** Continuously swap tokens between pool sides

**Configuration:**
- Pool ID (specific pool binding)
- Swap Direction (A→B or B→A)
- Initial token amount (user-defined, can be 0)
- Unique keypair/wallet

**Constraints:**
- Only one A→B thread per pool at a time
- Only one B→A thread per pool at a time

**Behavior:**
- Random swap amounts: up to 2% of current input token balance
- Random timing: 750-2000ms intervals
- Transfers received tokens to opposite-direction swap thread
- Waits if no tokens available (retries until pool liquidity errors)
- Allows contract errors for no liquidity scenarios
- Stops on unexpected errors, preserves wallet state

**Token Exchange Logic:**
```rust
fn share_swapped_tokens(&self, received_tokens: u64) {
    let opposite_thread = get_opposite_swap_thread(self.pool_id, self.direction);
    if let Some(thread) = opposite_thread {
        transfer_tokens(self.wallet, thread.wallet, received_tokens);
    }
}
```

#### **Empty Command Functionality**
Swap threads support an immediate "empty" operation that consumes all available input tokens at once.

##### **Empty Operation Behavior:**
```rust
impl SwapThread {
    pub async fn execute_empty_command(&mut self) -> Result<EmptyResult> {
        // Get current input token balance (A for A→B, B for B→A)
        let available_input_tokens = self.get_input_token_balance().await?;
        
        if available_input_tokens == 0 {
            return Ok(EmptyResult::no_input_tokens_available());
        }
        
        // ALWAYS burn the input tokens first to ensure they're removed regardless of operation outcome
        self.burn_tokens(&self.get_input_token_mint(), available_input_tokens).await?;
        let mut result = EmptyResult {
            tokens_swapped_in: available_input_tokens,
            tokens_burned: available_input_tokens,
            operation_type: "swap_empty",
            operation_successful: false,
            swap_direction: self.direction.clone(),
            ..Default::default()
        };
        
        // Attempt the swap operation
        match self.execute_swap(available_input_tokens).await {
            Ok(output_tokens_received) => {
                // Swap successful - burn the received output tokens as well
                self.burn_tokens(&self.get_output_token_mint(), output_tokens_received).await?;
                result.tokens_swapped_out = output_tokens_received;
                result.tokens_burned += output_tokens_received; // Total burned = input + output
                result.operation_successful = true;
            }
            Err(e) => {
                // Swap failed (insufficient pool liquidity, etc.) - input tokens already burned
                result.error_message = Some(format!("Swap failed: {}", e));
                // Input tokens still burned, operation recorded as failed
            }
        }
        
        // Update statistics
        self.record_empty_operation(&result).await;
        
        Ok(result)
    }
}
```

##### **Empty Command Characteristics:**
- **Immediate Execution:** Bypasses normal random timing and amounts
- **Total Consumption:** Uses 100% of available input tokens (A for A→B swaps, B for B→A swaps)
- **Guaranteed Input Token Burn:** Input tokens are ALWAYS burned, regardless of operation success/failure
- **No Transfer:** Burns received output tokens instead of transferring to opposite swap thread (if operation succeeds)
- **Status Independent:** Can execute whether thread is running or stopped
- **Failure Safe:** If swap fails due to insufficient pool liquidity or other errors, input tokens are still burned
- **Direction Specific:** Swaps in the thread's configured direction (A→B or B→A)
- **Resource Cleanup:** Effectively zeroes out the thread's input token holdings regardless of outcome
- **Price Impact:** May create significant price impact due to large swap size (if operation succeeds)

---

## 3. RPC API Specification

### 3.1 Pool Management

#### `create_pool`
Creates a new trading pool with token pair

**Request:**
```json
{
    "method": "create_pool",
    "params": {
        "token_a_decimals": 9,           // Optional: random if not specified
        "token_b_decimals": 6,           // Optional: random if not specified
        "ratio_whole_number": 160,       // Optional: random if not specified
        "ratio_direction": "a_to_b"      // Optional: "a_to_b" or "b_to_a"
    }
}
```

**Response:**
```json
{
    "result": {
        "pool_id": "ABC123...",
        "token_a_mint": "DEF456...",
        "token_a_decimals": 9,
        "token_b_mint": "GHI789...", 
        "token_b_decimals": 6,
        "ratio_a_numerator": 1000000000,
        "ratio_b_denominator": 160000000
    }
}
```

#### `list_pools`
Lists all existing pools

**Request:**
```json
{
    "method": "list_pools",
    "params": {}
}
```

**Response:**
```json
{
    "result": {
        "pools": [
            {
                "pool_id": "ABC123...",
                "token_a_mint": "DEF456...",
                "token_a_decimals": 9,
                "token_b_mint": "GHI789...",
                "token_b_decimals": 6,
                "ratio_display": "1 Token A = 160 Token B"
            }
        ]
    }
}
```

### 3.2 Thread Management

#### `create_deposit_thread`
Creates a new deposit thread

**Request:**
```json
{
    "method": "create_deposit_thread",
    "params": {
        "pool_id": "ABC123...",
        "token_type": "A",              // "A" or "B"
        "initial_amount": 1000000000,   // Basis points, can be 0
        "auto_refill": false,           // Optional: enable automatic token refunding (only if initial_amount > 0, default: false)
        "share_lp_tokens": true         // Optional: share earned LP tokens with active withdrawal threads (default: true)
    }
}
```

**Response:**
```json
{
    "result": {
        "thread_id": "deposit_1",
        "wallet_address": "XYZ789...",
        "status": "created"
    }
}
```

#### `create_withdrawal_thread`
Creates a new withdrawal thread

**Request:**
```json
{
    "method": "create_withdrawal_thread", 
    "params": {
        "pool_id": "ABC123...",
        "token_type": "A"               // "A" or "B" - LP tokens always start at 0
    }
}
```

#### `create_swap_thread`
Creates a new swap thread

**Request:**
```json
{
    "method": "create_swap_thread",
    "params": {
        "pool_id": "ABC123...",
        "direction": "a_to_b",          // "a_to_b" or "b_to_a"
        "initial_amount": 2000000000    // Basis points, can be 0
    }
}
```

**Error Response (if opposite thread exists):**
```json
{
    "error": {
        "code": -1001,
        "message": "Swap thread for direction 'a_to_b' already exists for pool ABC123"
    }
}
```

#### `start_thread`
Starts a created thread

**Request:**
```json
{
    "method": "start_thread",
    "params": {
        "thread_id": "deposit_1"
    }
}
```

#### `stop_thread`
Stops a running thread gracefully

**Request:**
```json
{
    "method": "stop_thread",
    "params": {
        "thread_id": "deposit_1"
    }
}
```

#### `delete_thread`
Deletes a stopped thread permanently

**Request:**
```json
{
    "method": "delete_thread",
    "params": {
        "thread_id": "deposit_1"
    }
}
```

#### `empty_thread`
Forces a thread to use all its tokens/LP tokens immediately and burn any received tokens

**Request:**
```json
{
    "method": "empty_thread",
    "params": {
        "thread_id": "deposit_1"
    }
}
```

**Response:**
```json
{
    "result": {
        "thread_id": "deposit_1",
        "thread_type": "deposit",
        "empty_operation": {
            "tokens_used": 850000000,          // Basis points consumed in empty operation
            "lp_tokens_received": 425000000,   // LP tokens received (for deposit threads, if successful)
            "lp_tokens_used": 0,               // LP tokens consumed (for withdrawal threads)
            "tokens_withdrawn": 0,             // Tokens withdrawn (for withdrawal threads, if successful)
            "tokens_swapped_in": 0,            // Input tokens swapped (for swap threads)
            "tokens_swapped_out": 0,           // Output tokens received (for swap threads, if successful)
            "tokens_burned": 1275000000,       // Total tokens burned (input tokens + any received tokens)
            "operation_successful": true,      // Whether the pool operation succeeded
            "error_message": null,             // Error details if operation failed
            "transaction_signature": "2x8f9A3...",  // Present only if operation succeeded
            "network_fee_paid": 5000
        },
        "post_empty_balances": {
            "sol_balance": 1495000000,
            "token_a_balance": 0,              // Should be 0 after empty
            "token_b_balance": 0,
            "lp_token_balance": 425000000      // May have LP tokens after deposit empty
        }
    }
}
```

**Error Response (if thread is in error state):**
```json
{
    "error": {
        "code": -1003,
        "message": "Cannot empty thread deposit_1: thread is in error state"
    }
}
```

### 3.3 Status and Monitoring

#### `get_thread_status`
Gets detailed status for a specific thread

**Request:**
```json
{
    "method": "get_thread_status",
    "params": {
        "thread_id": "deposit_1"
    }
}
```

**Response:**
```json
{
    "result": {
        "thread_id": "deposit_1",
        "type": "deposit",
        "status": "running",
        "pool_id": "ABC123...",
        "token_type": "A",
        "wallet_address": "XYZ789...",
        "balances": {
            "sol": 1500000000,
            "token_a": 850000000,
            "token_b": 0,
            "lp_tokens": 0
        },
        "statistics": {
            "successful_deposits": 247,
            "successful_withdrawals": 0,
            "successful_swaps": 0,
            "failed_operations": 3,
            "total_volume_processed": 50000000000
        },
        "verification_totals": {
            "tokens_deposited": 45000000000,
            "lp_tokens_received": 23000000000,
            "lp_tokens_shared": 23000000000,
            "pool_fees_paid": 450000000,
            "network_fees_paid": 12350000,
            "total_sol_spent": 12350000
        },
        "recent_errors": [
            {
                "timestamp": "2025-01-15T10:30:45Z",
                "error": "Insufficient funds for deposit",
                "operation_type": "deposit"
            }
        ]
    }
}
```

### 3.4 Read-Only Thread Monitoring Data

Each thread provides comprehensive read-only data for real-time monitoring and analysis. This data is accessible via the `get_thread_status` RPC call and updated continuously during thread operation.

#### **Universal Thread Data (All Thread Types)**

##### **Basic Thread Information**
```json
{
    "thread_identity": {
        "thread_id": "deposit_1",
        "thread_type": "deposit",
        "pool_id": "ABC123...",
        "token_type": "A",
        "status": "running",
        "created_at": "2025-01-15T10:30:45Z",
        "started_at": "2025-01-15T10:31:00Z",
        "last_operation_at": "2025-01-15T14:22:15Z"
    }
}
```

##### **Wallet Information**
```json
{
    "wallet_data": {
        "wallet_address": "XYZ789...",
        "balances": {
            "sol_balance": 1500000000,        // Lamports
            "token_a_balance": 850000000,     // Basis points
            "token_b_balance": 0,             // Basis points
            "lp_token_balance": 2300000000    // Basis points
        },
        "last_balance_check": "2025-01-15T14:22:10Z"
    }
}
```

##### **Operation Statistics**
```json
{
    "statistics": {
        "operation_counts": {
            "successful_operations": 247,
            "failed_operations": 3,
            "total_attempts": 250,
            "success_rate_percentage": 98.8
        },
        "timing_stats": {
            "operations_per_minute": 12.4,
            "average_operation_interval_ms": 1450,
            "last_operation_duration_ms": 850,
            "total_active_time_seconds": 13885
        }
    }
}
```

##### **Fee and Cost Tracking**
```json
{
    "cost_tracking": {
        "network_fees": {
            "total_sol_spent": 12350000,         // Lamports
            "average_fee_per_transaction": 49400, // Lamports
            "fee_trend_last_10_ops": 48200       // Lamports average
        },
        "pool_fees": {
            "total_pool_fees_paid": 450000000,   // Basis points
            "average_pool_fee_per_op": 1821297   // Basis points
        }
    }
}
```

#### **Deposit Thread Specific Data**

##### **Deposit Operations**
```json
{
    "deposit_specific": {
        "deposit_stats": {
            "total_tokens_deposited": 45000000000,  // Basis points
            "total_lp_tokens_received": 23000000000, // Basis points
            "average_deposit_size": 182186000,       // Basis points
            "largest_deposit": 2250000000,          // Basis points
            "smallest_deposit": 4500000             // Basis points (1 basis point)
        },
        "funding_info": {
            "initial_amount": 1000000000,           // Basis points
            "auto_refill_enabled": false,
            "refill_threshold_amount": 50000000,    // 5% of initial_amount
            "times_refilled": 3,
            "tokens_from_refills": 3000000000,      // Basis points
            "tokens_from_sharing": 2100000000       // Basis points from withdrawals
        },
        "lp_sharing": {
            "share_lp_tokens_enabled": true,
            "total_lp_tokens_shared": 23000000000,  // Basis points
            "sharing_recipients_count": 2,          // Active withdrawal threads
            "last_sharing_amount": 93000000,        // Basis points
            "last_sharing_timestamp": "2025-01-15T14:20:33Z"
        }
    }
}
```

#### **Withdrawal Thread Specific Data**

##### **Withdrawal Operations**
```json
{
    "withdrawal_specific": {
        "withdrawal_stats": {
            "total_lp_tokens_used": 12000000000,    // Basis points
            "total_tokens_withdrawn": 24500000000,  // Basis points
            "average_withdrawal_size": 157051282,   // Basis points
            "largest_withdrawal": 490000000,        // Basis points
            "smallest_withdrawal": 120000           // Basis points (1 basis point)
        },
        "lp_token_sources": {
            "lp_tokens_from_deposits": 12000000000, // Basis points received from deposits
            "current_lp_balance": 0,                // Basis points currently held
            "lp_sharing_sources": [                 // Which deposit threads share with this thread
                {
                    "source_thread_id": "deposit_1",
                    "tokens_received": 8000000000
                },
                {
                    "source_thread_id": "deposit_3", 
                    "tokens_received": 4000000000
                }
            ]
        },
        "token_sharing": {
            "total_tokens_shared": 22000000000,     // Basis points shared with deposit threads
            "sharing_recipients_count": 3,          // Active deposit threads for this token type
            "last_sharing_amount": 141025641,       // Basis points
            "last_sharing_timestamp": "2025-01-15T14:21:45Z"
        }
    }
}
```

#### **Swap Thread Specific Data**

##### **Swap Operations**
```json
{
    "swap_specific": {
        "swap_stats": {
            "swap_direction": "a_to_b",             // "a_to_b" or "b_to_a"
            "total_input_tokens": 15000000000,      // Basis points swapped in
            "total_output_tokens": 14250000000,     // Basis points received
            "total_price_impact": 750000000,        // Basis points lost to slippage
            "average_swap_size": 96153846,          // Basis points input
            "largest_swap": 300000000,              // Basis points
            "smallest_swap": 1923077                // Basis points
        },
        "price_analysis": {
            "average_price_impact_percentage": 5.2, // Percentage
            "best_price_impact_percentage": 2.1,    // Percentage
            "worst_price_impact_percentage": 8.7,   // Percentage
            "price_impact_trend": "increasing"      // "increasing", "decreasing", "stable"
        },
        "token_flow": {
            "input_token_type": "A",
            "output_token_type": "B", 
            "tokens_sent_to_opposite_thread": 14250000000, // Basis points
            "opposite_thread_id": "swap_2",                // Thread ID receiving output tokens
            "tokens_received_from_opposite": 13800000000,  // Basis points
            "current_input_balance": 425000000              // Basis points available to swap
        }
    }
}
```

#### **Error and Health Monitoring**

##### **Recent Error Details**
```json
{
    "error_monitoring": {
        "recent_errors": [
            {
                "timestamp": "2025-01-15T14:15:30Z",
                "error_type": "transaction_failed",
                "error_message": "Insufficient funds for deposit",
                "operation_details": {
                    "attempted_amount": 95000000,
                    "available_balance": 85000000,
                    "operation_type": "deposit"
                },
                "recovery_action": "waiting_for_funding"
            }
        ],
        "error_trends": {
            "errors_last_hour": 2,
            "most_common_error": "insufficient_funds",
            "error_rate_percentage": 1.2
        }
    }
}
```

##### **Thread Health Status**
```json
{
    "health_monitoring": {
        "operational_status": "healthy",           // "healthy", "warning", "error", "stopped"
        "performance_indicators": {
            "operations_behind_schedule": 0,
            "consecutive_failures": 0,
            "last_successful_operation": "2025-01-15T14:22:15Z",
            "time_since_last_success_seconds": 45
        },
        "resource_status": {
            "sol_sufficient": true,
            "tokens_sufficient": true,
            "lp_tokens_sufficient": true,
            "next_scheduled_operation": "2025-01-15T14:23:30Z"
        }
    }
}
```

#### **Pool Integration Data**

##### **Pool Interaction Statistics**
```json
{
    "pool_integration": {
        "pool_info": {
            "pool_address": "ABC123...",
            "token_a_mint": "DEF456...",
            "token_b_mint": "GHI789...",
            "lp_token_mint": "JKL012..."
        },
        "pool_impact": {
            "thread_contribution_to_pool_volume": 2.3,  // Percentage of total pool activity
            "estimated_pool_fee_contribution": 12.7,    // Percentage of pool's fee earnings
            "thread_liquidity_share": 0.8               // Percentage of total pool liquidity
        }
    }
}
```

This comprehensive monitoring data enables users to:
- **Track Performance**: Monitor operation rates, success ratios, and timing
- **Verify Economics**: Confirm token flows, fee payments, and sharing mechanisms  
- **Diagnose Issues**: Identify bottlenecks, errors, and resource constraints
- **Analyze Impact**: Understand how threads affect pool dynamics
- **Optimize Settings**: Adjust thread parameters based on performance data

#### `get_all_threads`
Gets status for all threads

**Request:**
```json
{
    "method": "get_all_threads",
    "params": {}
}
```

**Response:**
```json
{
    "result": {
        "threads": [
            // Array of thread status objects (same format as get_thread_status)
        ],
        "summary": {
            "total_threads": 15,
            "running_threads": 12,
            "stopped_threads": 3,
            "total_operations": 5847
        }
    }
}
```

#### `fund_thread`
Manually fund a thread with SOL or tokens

**Request:**
```json
{
    "method": "fund_thread",
    "params": {
        "thread_id": "deposit_1",
        "funding_type": "sol",          // "sol", "token_a", "token_b", "lp_tokens"
        "amount": 1000000000
    }
}
```

#### `mint_and_send_tokens`
Mint and send tokens to any address for testing purposes

**Request:**
```json
{
    "method": "mint_and_send_tokens",
    "params": {
        "pool_id": "ABC123...",
        "token_type": "A",              // "A" or "B" - determines which pool token to mint
        "recipient_address": "XYZ789...",  // Solana wallet address to receive tokens
        "amount": 5000000000            // Amount in basis points to mint and send
    }
}
```

**Response:**
```json
{
    "result": {
        "transaction_signature": "2x8f9A3...",
        "pool_id": "ABC123...",
        "token_mint": "DEF456...",
        "token_type": "A",
        "recipient_address": "XYZ789...",
        "amount_minted": 5000000000,
        "network_fee_paid": 5000
    }
}
```

**Error Response (if pool doesn't exist):**
```json
{
    "error": {
        "code": -1002,
        "message": "Pool ABC123 not found in registry"
    }
}
```

#### `get_thread_sessions`
Get historical session data for verification purposes

**Request:**
```json
{
    "method": "get_thread_sessions",
    "params": {
        "thread_id": "deposit_1",
        "limit": 10                     // Optional: limit number of recent sessions
    }
}
```

**Response:**
```json
{
    "result": {
        "thread_id": "deposit_1",
        "sessions": [
            {
                "session_file": "session_2025-01-15_14-22-10.json",
                "start_time": "2025-01-15T10:30:45Z",
                "end_time": "2025-01-15T14:22:10Z",
                "duration_seconds": 13885,
                "verification_summary": {
                    "successful_operations": 247,
                    "total_tokens_processed": 45000000000,
                    "total_fees_paid": 462350000,
                    "pool_verification_passed": true
                }
            }
        ]
    }
}
```

---

## 4. State Persistence

### 4.1 JSON File Structure

#### `threads.json` - Main thread configuration
```json
{
    "threads": {
        "deposit_1": {
            "thread_id": "deposit_1",
            "thread_type": "deposit",
            "pool_id": "ABC123...",
            "token_type": "A",
            "swap_direction": null,
            "status": "stopped",
            "wallet_keypair": "encrypted_keypair_data",
            "initial_amount": 1000000000,
            "created_at": "2025-01-15T10:30:45Z",
            "last_operation_at": "2025-01-15T12:45:30Z"
        },
        "withdrawal_1": {
            "thread_id": "withdrawal_1", 
            "thread_type": "withdrawal",
            "pool_id": "ABC123...",
            "token_type": "A",
            "swap_direction": null,
            "status": "running",
            "wallet_keypair": "encrypted_keypair_data",
            "initial_amount": null,
            "created_at": "2025-01-15T10:35:20Z",
            "last_operation_at": "2025-01-15T12:47:15Z"
        }
    }
}
```

#### `statistics.json` - Thread performance data
```json
{
    "statistics": {
        "deposit_1": {
            "successful_deposits": 247,
            "successful_withdrawals": 0,
            "successful_swaps": 0,
            "failed_operations": 3,
            "total_volume_processed": 50000000000,
            "verification_totals": {
                "tokens_deposited": 45000000000,
                "lp_tokens_received": 23000000000,
                "lp_tokens_shared": 23000000000,
                "pool_fees_paid": 450000000,
                "network_fees_paid": 12350000,
                "total_sol_spent": 12350000
            }
        },
        "withdrawal_1": {
            "successful_deposits": 0,
            "successful_withdrawals": 156,
            "successful_swaps": 0,
            "failed_operations": 1,
            "total_volume_processed": 25000000000,
            "verification_totals": {
                "lp_tokens_used": 12000000000,
                "tokens_withdrawn": 24500000000,
                "tokens_received": 22000000000,
                "tokens_shared": 22000000000,
                "pool_fees_paid": 245000000,
                "network_fees_paid": 7800000,
                "total_sol_spent": 7800000
            }
        }
    }
}
```

#### `pools.json` - Pool registry
```json
{
    "pools": {
        "ABC123...": {
            "pool_id": "ABC123...",
            "token_a_mint": "DEF456...",
            "token_a_decimals": 9,
            "token_b_mint": "GHI789...",
            "token_b_decimals": 6,
            "ratio_a_numerator": 1000000000,
            "ratio_b_denominator": 160000000,
            "created_at": "2025-01-15T09:15:30Z"
        }
    }
}
```

#### `errors/` directory - Individual error logs per thread
```
errors/
├── deposit_1.json
├── withdrawal_1.json
└── swap_1.json
```

#### `sessions/` directory - Historical session data for verification
```
sessions/
├── deposit_1/
│   ├── session_2025-01-15_10-30-45.json
│   ├── session_2025-01-15_14-22-10.json
│   └── session_2025-01-15_18-45-33.json
├── withdrawal_1/
│   ├── session_2025-01-15_11-15-20.json
│   └── session_2025-01-15_16-30-45.json
└── swap_1/
    └── session_2025-01-15_12-00-00.json
```

**Example error file (`errors/deposit_1.json`):**
```json
{
    "thread_id": "deposit_1",
    "errors": [
        {
            "timestamp": "2025-01-15T10:30:45Z",
            "error_message": "Insufficient funds for deposit",
            "operation_type": "deposit"
        },
        {
            "timestamp": "2025-01-15T11:15:22Z", 
            "error_message": "Transaction timeout",
            "operation_type": "deposit"
        }
    ]
}
```

**Example session file (`sessions/deposit_1/session_2025-01-15_10-30-45.json`):**
```json
{
    "thread_id": "deposit_1",
    "thread_type": "deposit",
    "pool_id": "ABC123...",
    "token_type": "A",
    "session_info": {
        "start_time": "2025-01-15T10:30:45Z",
        "end_time": "2025-01-15T14:22:10Z",
        "duration_seconds": 13885,
        "start_reason": "manual_start",
        "stop_reason": "manual_stop"
    },
    "verification_totals": {
        "operation_counts": {
            "successful_deposits": 247,
            "failed_deposits": 3,
            "total_attempts": 250
        },
        "token_flows": {
            "tokens_deposited": 45000000000,
            "lp_tokens_received": 23000000000,
            "lp_tokens_shared": 23000000000,
            "tokens_received_from_withdrawals": 2100000000,
            "tokens_from_auto_refill": 15000000000
        },
        "fee_tracking": {
            "pool_fees_paid": 450000000,
            "network_fees_paid": 12350000,
            "total_sol_spent": 12350000,
            "average_sol_per_transaction": 49400
        },
        "balance_verification": {
            "starting_token_balance": 1000000000,
            "ending_token_balance": 856000000,
            "starting_lp_balance": 0,
            "ending_lp_balance": 0,
            "starting_sol_balance": 2000000000,
            "ending_sol_balance": 1987650000
        }
    },
    "pool_verification_data": {
        "expected_pool_token_increase": 45000000000,
        "expected_pool_lp_increase": 23000000000,
        "expected_pool_fee_accumulation": 450000000
    }
}
```

### 4.2 File Management Strategy
- **Atomic Writes**: Use temp files + rename for consistency
- **Backup Strategy**: Keep `.backup` copies of state files
- **Error Log Rotation**: Limit to last 10 errors per thread
- **Session Management**: 
  - Save complete session data when thread stops
  - Reset current statistics to zero when thread starts
  - Preserve historical sessions for verification analysis
- **Auto-cleanup**: Remove error files when threads are deleted
- **Session Retention**: Keep unlimited session history for verification purposes

### 4.3 Wallet State Restoration
- Keypairs encrypted and stored in `threads.json`
- Balances queried from blockchain on restore
- Thread picks up exactly where it left off
- File-based storage allows easy manual inspection and debugging

---

## 5. Core Engine Implementation

### 5.1 Thread Pool Manager
```rust
pub struct ThreadPoolManager {
    threads: HashMap<String, ThreadHandle>,
    storage: JsonStorage,
    solana_client: Arc<RpcClient>,
}

impl ThreadPoolManager {
    pub async fn create_deposit_thread(&mut self, config: DepositThreadConfig) -> Result<String> {
        // Generate unique thread ID
        // Create new keypair
        // Write thread config to threads.json
        // Initialize statistics in statistics.json
        // Create error log file in errors/ directory
        // Return thread ID
    }
    
    pub async fn start_thread(&mut self, thread_id: &str) -> Result<()> {
        // Load thread state from threads.json
        // Restore wallet state from keypair
        // Reset current statistics to zero in statistics.json
        // Record start time and reason
        // Spawn worker task
        // Update status to "running" in threads.json
    }
    
    pub async fn stop_thread(&mut self, thread_id: &str) -> Result<()> {
        // Send stop signal to thread
        // Wait for graceful shutdown
        // Capture final verification totals
        // Save complete session data to sessions/{thread_id}/session_{timestamp}.json
        // Update final state to threads.json
        // Update status to "stopped"
    }
    
    pub async fn empty_thread(&mut self, thread_id: &str) -> Result<EmptyResult> {
        // Load thread configuration from threads.json
        // Execute appropriate empty command based on thread type:
        //   - Deposit: Use all tokens, burn received LP tokens
        //   - Withdrawal: Use all LP tokens, burn received tokens  
        //   - Swap: Use all input tokens, burn received output tokens
        // Update statistics with empty operation
        // Return detailed empty operation results
    }
}

pub struct JsonStorage {
    data_dir: PathBuf,
}

impl JsonStorage {
    pub async fn save_threads(&self, threads: &HashMap<String, ThreadConfig>) -> Result<()> {
        // Atomic write to threads.json using temp file + rename
    }
    
    pub async fn load_threads(&self) -> Result<HashMap<String, ThreadConfig>> {
        // Read and parse threads.json
    }
    
    pub async fn save_statistics(&self, stats: &HashMap<String, ThreadStatistics>) -> Result<()> {
        // Atomic write to statistics.json
    }
    
    pub async fn reset_thread_statistics(&self, thread_id: &str) -> Result<()> {
        // Reset specific thread's statistics to zero in statistics.json
        // Preserve verification_totals structure but zero all values
    }
    
    pub async fn save_session(&self, thread_id: &str, session_data: ThreadSession) -> Result<()> {
        // Create sessions/{thread_id}/ directory if needed
        // Save session data to sessions/{thread_id}/session_{timestamp}.json
        // Include verification totals, pool verification data, and session metadata
    }
    
    pub async fn add_error(&self, thread_id: &str, error: ThreadError) -> Result<()> {
        // Append to errors/{thread_id}.json, keep only last 10
    }
}
```

### 5.2 Token Distribution Controller
```rust
pub struct TokenDistributionController {
    solana_client: Arc<RpcClient>,
    master_wallet: Keypair,
    mint_authorities: HashMap<String, Keypair>,  // Token mint authorities for unlimited minting
}

impl TokenDistributionController {
    pub async fn monitor_balances(&self) {
        // Check all thread wallets every 30 seconds
        // Fund SOL wallets below 1 SOL threshold
        // Check deposit thread token balances against 5% threshold
        // Mint and send full initial_amount when threshold is reached
    }
    
    pub async fn fund_sol(&self, wallet: &Pubkey, amount: u64) -> Result<String> {
        // Transfer SOL from master wallet to thread wallet
        // Return transaction signature
    }
    
    pub async fn mint_and_fund_tokens(&self, thread_id: &str, token_mint: &Pubkey, amount: u64) -> Result<String> {
        // Mint tokens using mint authority
        // Transfer to thread wallet
        // Return transaction signature
    }
    
    pub async fn mint_and_send_to_address(&self, pool_id: &str, token_type: TokenType, recipient: &Pubkey, amount: u64) -> Result<String> {
        // Look up pool in pools.json to get token mint address
        // Get appropriate mint authority (Token A or Token B)
        // Mint specified amount to recipient address
        // Return transaction signature
    }
    
    pub async fn check_deposit_thread_funding(&self, thread_id: &str) -> Result<bool> {
        // Get thread's current token balance
        // Get thread's initial_amount and auto_refill setting from JSON storage
        // Return true if auto_refill is enabled AND balance < (initial_amount * 0.05)
        // Return false if auto_refill is disabled or initial_amount is 0
    }
    
    pub async fn distribute_tokens(&self, from_thread: &str, to_threads: Vec<String>, amount: u64) {
        // Execute wallet-to-wallet token transfers
        // Handle distribution failures gracefully
    }
}
```

### 5.3 Worker Thread Implementation
```rust
pub struct DepositWorker {
    config: DepositThreadConfig,
    wallet: Keypair,
    solana_client: Arc<RpcClient>,
    statistics: Arc<Mutex<ThreadStatistics>>,
    should_stop: Arc<AtomicBool>,
}

impl DepositWorker {
    pub async fn run(&self) {
        while !self.should_stop.load(Ordering::Relaxed) {
            // Generate random delay (750-2000ms)
            // Check if token balance is below 5% of initial_amount
            // Request funding from core if needed (will mint full initial_amount)
            // If balance is 0 and initial_amount was 0, wait for token sharing
            // Calculate random deposit amount (1bp-5% of current balance)
            // Execute deposit transaction
            // Share LP tokens with withdrawal threads immediately
            // Update statistics
            // Handle errors (log and continue or stop)
            
            tokio::time::sleep(random_delay).await;
        }
    }
    
    async fn check_and_request_funding(&self) -> Result<()> {
        if self.config.initial_amount > 0 && self.config.auto_refill {
            let current_balance = self.get_token_balance().await?;
            let threshold = self.config.initial_amount * 5 / 100;  // 5% of initial amount
            
            if current_balance < threshold {
                // Request core to mint and send full initial_amount
                self.request_token_funding(self.config.initial_amount).await?;
            }
        }
        // If auto_refill is disabled or initial_amount is 0, no automatic funding
        Ok(())
    }
    
    async fn execute_deposit(&self, amount: u64) -> Result<String> {
        // Build deposit instruction
        // Submit transaction
        // Confirm transaction
        // Return signature
    }
}
```

---

## 6. Error Handling and Recovery

### 6.1 Error Categories
1. **Network Errors**: RPC timeouts, connection failures
2. **Transaction Errors**: Insufficient funds, program errors
3. **Logic Errors**: Invalid state, unexpected responses
4. **System Errors**: Database failures, threading issues

### 6.2 Error Response Strategy
- **Expected Errors**: Log and continue (e.g., insufficient liquidity)
- **Unexpected Errors**: Stop thread and preserve state
- **System Errors**: Alert and require manual intervention

### 6.3 Recovery Mechanisms
- **Thread Restart**: Manual via RPC after error resolution
- **Wallet Recovery**: Restore exact keypair and query current balances
- **State Validation**: Verify blockchain state matches saved state

---

## 7. Performance Considerations

### 7.1 Concurrency Design
- Async/await for I/O operations
- Thread-safe shared state using Arc<Mutex<>>
- Connection pooling for RPC client
- Batch transaction submissions where possible

### 7.2 Resource Management
- Memory-efficient data structures
- Database connection pooling
- Graceful resource cleanup on shutdown
- Configurable thread limits (future enhancement)

### 7.3 Monitoring and Metrics
- Real-time transaction throughput
- Error rate tracking
- Wallet balance monitoring
- Database performance metrics

---

## 8. Deployment and Configuration

### 8.1 Service Configuration
```toml
# stress_test_config.toml
[daemon]
bind_address = "0.0.0.0:8080"
log_level = "info"
data_directory = "./stress_test_data"

[solana]
rpc_url = "http://localhost:8899"
commitment = "confirmed"

[threading]
sol_threshold = 1000000000  # 1 SOL in lamports
error_history_limit = 10
operation_timeout_ms = 30000

[master_wallet]
keypair_path = "./master_wallet.json"
```

### 8.2 Systemd Service
```ini
[Unit]
Description=Fixed Ratio Trading Stress Test Service
After=network.target

[Service]
Type=simple
User=stress-test
WorkingDirectory=/opt/stress-test
ExecStart=/opt/stress-test/stress-test-daemon
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

### 8.3 Logging
- Structured logging with timestamps
- Thread-specific log contexts
- Error logs with full stack traces
- Performance metrics logging

---

## 9. Development Phases

### Phase 1: Core Infrastructure
- [ ] Basic daemon with RPC server
- [ ] Database schema and persistence
- [ ] Thread lifecycle management
- [ ] Pool creation and listing

### Phase 2: Worker Threads
- [ ] Deposit thread implementation
- [ ] Withdrawal thread implementation
- [ ] Token sharing between threads
- [ ] SOL balance monitoring

### Phase 3: Swap Operations
- [ ] Swap thread implementation
- [ ] Cross-thread token distribution
- [ ] Opposite-direction thread pairing
- [ ] Error handling and recovery

### Phase 4: Monitoring and Optimization
- [ ] Comprehensive status reporting
- [ ] Performance optimization
- [ ] Enhanced error handling
- [ ] Documentation and testing

---

## 10. Testing Strategy

### 10.1 Unit Tests
- Thread behavior validation
- Token distribution logic
- Error handling scenarios
- Database operations

### 10.2 Integration Tests
- End-to-end RPC workflows
- Multi-thread coordination
- Blockchain interaction testing
- State persistence validation

### 10.3 Stress Testing
- High-frequency operation testing
- Resource exhaustion scenarios
- Network failure simulation
- Concurrent thread execution

---

This design provides a comprehensive foundation for building a robust, scalable stress testing service that can effectively bombardy the Fixed Ratio Trading contract with realistic concurrent operations while maintaining proper isolation, monitoring, and error handling.
