using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Core.Interfaces;
using FixedRatioStressTest.Common.Models;
using Solnet.Wallet;
using Solnet.Rpc.Models;
using Solnet.Rpc.Builders;
using Solnet.Programs;

namespace FixedRatioStressTest.Infrastructure.Services;

/// <summary>
/// Contract version service that uses raw RPC calls instead of Solnet
/// This bypasses the transaction serialization issues we've identified
/// </summary>
public class RawRpcContractVersionService : IContractVersionService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RawRpcContractVersionService> _logger;
    private readonly SolanaConfig _config;
    private readonly string _expectedVersion;
    private readonly string _maxSupportedVersion;
    private readonly ISolanaClientService _solanaClient;

    // GetVersion instruction discriminator as per API documentation
    private const byte GET_VERSION_DISCRIMINATOR = 14;
    private const string DEFAULT_MAX_SUPPORTED_VERSION = "0.19.9999";

    public RawRpcContractVersionService(
        IConfiguration configuration,
        ISolanaClientService solanaClient,
        ILogger<RawRpcContractVersionService> logger)
    {
        _config = configuration.GetSection("SolanaConfiguration").Get<SolanaConfig>() ?? new SolanaConfig();
        _solanaClient = solanaClient;
        _logger = logger;
        
        _httpClient = new HttpClient();
        
        // Get expected and max supported versions from configuration
        _expectedVersion = configuration["ContractVersion:Expected"] ?? "v0.15.1054";
        _maxSupportedVersion = configuration["ContractVersion:MaxSupported"] ?? DEFAULT_MAX_SUPPORTED_VERSION;
        
        _logger.LogInformation("RawRpcContractVersionService initialized. RPC: {RpcUrl}, Expected: {ExpectedVersion}, Max Supported: {MaxSupportedVersion}", 
            _config.GetActiveRpcUrl(), _expectedVersion, _maxSupportedVersion);
    }

    public async Task<ContractVersionResult> ValidateContractVersionAsync()
    {
        _logger.LogInformation("🔍 Validating deployed contract version using raw RPC...");
        
        try
        {
            var deployedVersion = await GetDeployedVersionAsync();
            
            var result = new ContractVersionResult
            {
                DeployedVersion = deployedVersion,
                ExpectedVersion = _expectedVersion,
                MaxSupportedVersion = _maxSupportedVersion
            };

            if (string.IsNullOrEmpty(deployedVersion))
            {
                // Cannot retrieve version - this is a critical failure that should stop the application
                result.IsValid = false;
                result.ShouldShutdown = true;
                result.ErrorMessage = "Cannot retrieve contract version from blockchain. This indicates RPC connection issues, contract deployment problems, or program ID configuration errors.";
                _logger.LogCritical("❌ Contract version validation FAILED - cannot retrieve version: {ErrorMessage}", result.ErrorMessage);
                return result;
            }

            // Normalize versions for comparison (remove 'v' prefix if present)
            var normalizedDeployed = NormalizeVersion(deployedVersion);
            var normalizedExpected = NormalizeVersion(_expectedVersion);
            var normalizedMaxSupported = NormalizeVersion(_maxSupportedVersion);

            // Check if deployed version is too high (>= 0.20.x)
            if (CompareVersions(normalizedDeployed, normalizedMaxSupported) > 0)
            {
                result.IsValid = false;
                result.IsVersionTooHigh = true;
                result.ShouldShutdown = true;
                result.ErrorMessage = $"Contract version {deployedVersion} is not supported. This service supports versions up to {_maxSupportedVersion}. Version 0.20.x+ contains breaking changes.";
                _logger.LogError("❌ Contract version validation FAILED - VERSION TOO HIGH: {ErrorMessage}", result.ErrorMessage);
                return result;
            }

            // Check if deployed version matches expected
            result.IsValid = string.Equals(normalizedDeployed, normalizedExpected, StringComparison.OrdinalIgnoreCase);

            if (result.IsValid)
            {
                _logger.LogInformation("✅ Contract version validation SUCCESS: Deployed={DeployedVersion}, Expected={ExpectedVersion}", 
                    deployedVersion, _expectedVersion);
            }
            else
            {
                result.ShouldShutdown = true;
                result.ErrorMessage = $"Contract version mismatch. Deployed: {deployedVersion}, Expected: {_expectedVersion}";
                _logger.LogError("❌ Contract version validation FAILED: {ErrorMessage}", result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            var result = new ContractVersionResult
            {
                DeployedVersion = null,
                ExpectedVersion = _expectedVersion,
                MaxSupportedVersion = _maxSupportedVersion,
                IsValid = false,
                ShouldShutdown = true,
                ErrorMessage = $"Contract version validation failed with exception: {ex.Message}"
            };

            _logger.LogError(ex, "❌ Contract version validation encountered an error");
            return result;
        }
    }

    public async Task<string?> GetDeployedVersionAsync()
    {
        try
        {
            _logger.LogInformation("🎯 GetVersion Test - Using Core Wallet");

            // Get the core wallet that was initialized during startup
            _logger.LogInformation("🔧 Step 1: Loading core wallet for version check...");
            var coreWallet = await _solanaClient.GetOrCreateCoreWalletAsync();
            
            _logger.LogInformation("💰 Using core wallet: {PublicKey} (Balance: {Balance} SOL)", 
                coreWallet.PublicKey, coreWallet.CurrentSolBalance / 1_000_000_000.0);
            
            // Check if core wallet has sufficient balance
            if (coreWallet.CurrentSolBalance == 0)
            {
                _logger.LogWarning("⚠️ Core wallet is empty - version check may fail");
            }

            // Create transaction using core wallet
            var (transactionBase64, _) = await CreateGetVersionTransactionWithCoreWalletAsync(coreWallet);
            var version = await TryGetVersionFromSimulation(transactionBase64, "core wallet");
            
            if (!string.IsNullOrEmpty(version))
            {
                _logger.LogInformation("🎉 SUCCESS! Contract version retrieved using core wallet: {Version}", version);
                return version;
            }

            _logger.LogError("❌ Failed to retrieve contract version using core wallet");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get deployed contract version via raw RPC");
            return null;
        }
    }

    private async Task<string?> TryGetVersionFromSimulation(string transactionBase64, string context)
    {
        try
        {
            // Create RPC request
            var rpcRequest = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "simulateTransaction",
                @params = new object[]
                {
                    transactionBase64,
                    new
                    {
                        sigVerify = false,
                        replaceRecentBlockhash = true,
                        commitment = "confirmed",
                        encoding = "base64"
                    }
                }
            };

            var jsonRequest = JsonSerializer.Serialize(rpcRequest);
            _logger.LogDebug("RPC request with {Context}: {Request}", context, jsonRequest);

            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_config.GetActiveRpcUrl(), content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("RPC request failed with status: {StatusCode}", response.StatusCode);
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("RPC response with {Context}: {Response}", context, responseContent);

            // Parse response
            using var document = JsonDocument.Parse(responseContent);
            var root = document.RootElement;

            if (root.TryGetProperty("error", out var errorElement))
            {
                var errorMessage = errorElement.GetProperty("message").GetString();
                _logger.LogError("RPC simulation error with {Context}: {Error}", context, errorMessage);
                return null;
            }

            if (!root.TryGetProperty("result", out var result))
            {
                _logger.LogError("No result in RPC response with {Context}", context);
                return null;
            }

            if (!result.TryGetProperty("value", out var value))
            {
                _logger.LogError("No value in RPC result with {Context}", context);
                return null;
            }

            // Check for errors - but AccountNotFound is expected with dummy fee payer
            if (value.TryGetProperty("err", out var err) && err.ValueKind != JsonValueKind.Null)
            {
                var errorStr = err.ToString();
                if (errorStr.Contains("AccountNotFound"))
                {
                    _logger.LogInformation("⚠️ Expected AccountNotFound with {Context} - transaction format is CORRECT!", context);
                }
                else
                {
                    _logger.LogWarning("Simulation returned error with {Context}: {Error}", context, errorStr);
                    return null;
                }
            }
            else
            {
                _logger.LogInformation("✅ Simulation succeeded with {Context}!", context);
            }

            // Check returnData first (some programs return data instead of logging)
            if (value.TryGetProperty("returnData", out var returnDataProperty) && returnDataProperty.ValueKind != JsonValueKind.Null)
            {
                if (returnDataProperty.TryGetProperty("data", out var dataProperty) && dataProperty.ValueKind == JsonValueKind.Array)
                {
                    var dataArray = dataProperty.EnumerateArray().ToArray();
                    if (dataArray.Length >= 2)
                    {
                        var base64Data = dataArray[0].GetString();
                        var encoding = dataArray[1].GetString();
                        
                        if (!string.IsNullOrEmpty(base64Data) && encoding == "base64")
                        {
                            try
                            {
                                var decodedData = Convert.FromBase64String(base64Data);
                                var dataString = System.Text.Encoding.UTF8.GetString(decodedData);
                                _logger.LogInformation("📦 GetVersion returned data with {Context}: {Data}", context, dataString);
                                
                                // Try to parse version from return data
                                var match = System.Text.RegularExpressions.Regex.Match(dataString, @"([0-9v.]+)");
                                if (match.Success)
                                {
                                    var version = match.Groups[1].Value;
                                    _logger.LogInformation("🎉 SUCCESS! Contract version from returnData with {Context}: {Version}", context, version);
                                    return version;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to decode returnData with {Context}", context);
                            }
                        }
                    }
                }
            }

            // Parse program logs for version information
            if (value.TryGetProperty("logs", out var logsProperty) && logsProperty.ValueKind == JsonValueKind.Array)
            {
                var logs = logsProperty.EnumerateArray().Select(log => log.GetString() ?? "").ToList();
                
                _logger.LogInformation("Program logs with {Context}:", context);
                foreach (var log in logs)
                {
                    _logger.LogInformation("  [{Index}] {Log}", logs.IndexOf(log), log);
                }
                
                // Look for version in logs
                var versionLog = logs.FirstOrDefault(log => log.Contains("Contract Version:", StringComparison.OrdinalIgnoreCase));
                if (versionLog != null)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(versionLog, @"Contract Version:\s*([0-9.]+)");
                    if (match.Success)
                    {
                        var version = match.Groups[1].Value;
                        _logger.LogInformation("🎉 SUCCESS! Contract version with {Context}: {Version}", context, version);
                        return version;
                    }
                }
                
                _logger.LogInformation("GetVersion instruction executed but no 'Contract Version:' found in logs with {Context}", context);
            }
            else
            {
                _logger.LogWarning("No logs found in simulation response with {Context}", context);
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Simulation failed with {Context}", context);
            return null;
        }
    }

    public string GetExpectedVersion()
    {
        return _expectedVersion;
    }

    private async Task<(string transactionBase64, string feePayerPubkey)> CreateGetVersionTransactionWithBlockhashAsync(string? feePayerPubkey = null)
    {
        // Create a transaction following the exact JavaScript pattern from API documentation
        // This uses Solnet to create a properly signed transaction like the working JS example
        
        var programId = new PublicKey(_config.ProgramId);
        
        // Use provided fee payer or generate ephemeral keypair
        Account feePayerKeypair;
        string feePayerPublicKey;
        
        if (!string.IsNullOrEmpty(feePayerPubkey))
        {
            // This should not happen in the current flow, but keeping for compatibility
            feePayerKeypair = new Account();
            feePayerPublicKey = feePayerKeypair.PublicKey.ToString();
        }
        else
        {
            // Generate ephemeral keypair for fee payer (like JS example)
            feePayerKeypair = new Account();
            feePayerPublicKey = feePayerKeypair.PublicKey.ToString();
        }
        
        _logger.LogInformation("✅ Transaction setup complete");
        _logger.LogInformation("   Program ID: {ProgramId}", _config.ProgramId);
        _logger.LogInformation("   Instruction discriminator: 14 (0x0E)");
        _logger.LogInformation("   Fee payer: {FeePayer}", feePayerPublicKey);
        
        // Create GetVersion instruction (no accounts required per API doc)
        var instructionData = new byte[] { GET_VERSION_DISCRIMINATOR }; // [14]
        var instruction = new TransactionInstruction
        {
            ProgramId = programId,
            Keys = new List<AccountMeta>(), // Empty - GetVersion needs no accounts
            Data = instructionData
        };
        
        // Build and sign transaction (required even for simulation per API doc)
        var recentBlockhash = await GetLatestBlockhashStringAsync();
        var transaction = new TransactionBuilder()
            .SetFeePayer(feePayerKeypair.PublicKey)
            .SetRecentBlockHash(recentBlockhash)
            .AddInstruction(instruction)
            .Build(feePayerKeypair);
        
        var base64Transaction = Convert.ToBase64String(transaction);
        
        _logger.LogDebug("Created signed GetVersion transaction: {Length} bytes, Base64: {Base64}", 
            transaction.Length, base64Transaction);
        
        return (base64Transaction, feePayerPublicKey);
    }

    private async Task<(string transactionBase64, string feePayerPubkey)> CreateGetVersionTransactionWithCoreWalletAsync(CoreWalletConfig coreWallet)
    {
        // Create a transaction using the core wallet as fee payer
        var programId = new PublicKey(_config.ProgramId);
        
        // Restore the core wallet from its private key (convert from Base64)
        var privateKeyBytes = Convert.FromBase64String(coreWallet.PrivateKey);
        var coreWalletKeypair = _solanaClient.RestoreWallet(privateKeyBytes);
        
        _logger.LogInformation("✅ Transaction setup complete using core wallet");
        _logger.LogInformation("   Program ID: {ProgramId}", _config.ProgramId);
        _logger.LogInformation("   Instruction discriminator: 14 (0x0E)");
        _logger.LogInformation("   Fee payer: {FeePayer} (Core Wallet)", coreWallet.PublicKey);
        
        // Create GetVersion instruction (no accounts required per API doc)
        var instructionData = new byte[] { GET_VERSION_DISCRIMINATOR }; // [14]
        var instruction = new TransactionInstruction
        {
            ProgramId = programId,
            Keys = new List<AccountMeta>(), // Empty - GetVersion needs no accounts
            Data = instructionData
        };
        
        // Build and sign transaction using core wallet
        var recentBlockhash = await GetLatestBlockhashStringAsync();
        var transaction = new TransactionBuilder()
            .SetFeePayer(coreWalletKeypair.Account.PublicKey)
            .SetRecentBlockHash(recentBlockhash)
            .AddInstruction(instruction)
            .Build(coreWalletKeypair.Account);
        
        var base64Transaction = Convert.ToBase64String(transaction);
        
        _logger.LogDebug("Created signed GetVersion transaction with core wallet: {Length} bytes, Base64: {Base64}", 
            transaction.Length, base64Transaction);
        
        return (base64Transaction, coreWallet.PublicKey);
    }

    private async Task<bool> TryFundAccountAsync(string publicKey)
    {
        try
        {
            _logger.LogInformation("💰 Requesting airdrop for ephemeral account: {PublicKey}", publicKey);
            
            // Request airdrop (works on localnet/devnet)
            var airdropRequest = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "requestAirdrop",
                @params = new object[] { publicKey, 1000000000 } // 1 SOL in lamports
            };

            var jsonRequest = JsonSerializer.Serialize(airdropRequest);
            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_config.GetActiveRpcUrl(), content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Airdrop request failed with status: {StatusCode}", response.StatusCode);
                return false;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseContent);
            var root = document.RootElement;

            if (root.TryGetProperty("error", out var errorElement))
            {
                var errorMessage = errorElement.GetProperty("message").GetString();
                _logger.LogWarning("Airdrop failed: {Error}", errorMessage);
                return false;
            }

            if (root.TryGetProperty("result", out var result))
            {
                var signature = result.GetString();
                _logger.LogInformation("✅ Airdrop successful, signature: {Signature}", signature);
                
                // Confirm the airdrop transaction like the working JavaScript code
                var confirmationSuccess = await ConfirmTransactionAsync(signature);
                if (confirmationSuccess)
                {
                    _logger.LogInformation("✅ Airdrop transaction confirmed");
                    return true;
                }
                else
                {
                    _logger.LogWarning("⚠️ Airdrop transaction confirmation failed, but continuing anyway");
                    await Task.Delay(2000); // Fallback delay
                    return true;
                }
            }

            _logger.LogWarning("Unexpected airdrop response format");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fund account {PublicKey}", publicKey);
            return false;
        }
    }

    private async Task<bool> ConfirmTransactionAsync(string signature)
    {
        try
        {
            _logger.LogDebug("Confirming transaction: {Signature}", signature);
            
            // Get latest blockhash for confirmation
            var latestBlockhash = await GetLatestBlockhashStringAsync();
            
            var confirmRequest = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "confirmTransaction",
                @params = new object[]
                {
                    new
                    {
                        signature = signature,
                        blockhash = latestBlockhash,
                        lastValidBlockHeight = (object)null // Let RPC determine this
                    },
                    "confirmed"
                }
            };

            var jsonRequest = JsonSerializer.Serialize(confirmRequest);
            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_config.GetActiveRpcUrl(), content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Transaction confirmation request failed with status: {StatusCode}", response.StatusCode);
                return false;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseContent);
            var root = document.RootElement;

            if (root.TryGetProperty("error", out var errorElement))
            {
                var errorMessage = errorElement.GetProperty("message").GetString();
                _logger.LogWarning("Transaction confirmation failed: {Error}", errorMessage);
                return false;
            }

            if (root.TryGetProperty("result", out var result))
            {
                var confirmed = result.TryGetProperty("value", out var value) && 
                               value.ValueKind == JsonValueKind.Array &&
                               value.GetArrayLength() > 0 &&
                               value[0].TryGetProperty("confirmationStatus", out var status) &&
                               status.GetString() == "confirmed";
                
                return confirmed;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to confirm transaction {Signature}", signature);
            return false;
        }
    }

    private async Task<string> GetLatestBlockhashStringAsync()
    {
        try
        {
            var request = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "getLatestBlockhash",
                @params = new object[] { new { commitment = "confirmed" } }
            };

            var jsonRequest = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_config.GetActiveRpcUrl(), content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get latest blockhash string, using dummy");
                return "11111111111111111111111111111111"; // Return dummy blockhash
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseContent);
            var root = document.RootElement;

            if (root.TryGetProperty("result", out var result) &&
                result.TryGetProperty("value", out var value) &&
                value.TryGetProperty("blockhash", out var blockhashProperty))
            {
                var blockhashBase58 = blockhashProperty.GetString();
                if (!string.IsNullOrEmpty(blockhashBase58))
                {
                    _logger.LogDebug("Got recent blockhash: {Blockhash}", blockhashBase58);
                    return blockhashBase58;
                }
            }

            _logger.LogWarning("Could not parse blockhash from response, using dummy");
            return "11111111111111111111111111111111";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest blockhash string, using dummy");
            return "11111111111111111111111111111111";
        }
    }

    private async Task<byte[]> GetLatestBlockhashAsync()
    {
        try
        {
            var request = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "getLatestBlockhash",
                @params = new object[] { new { commitment = "confirmed" } }
            };

            var jsonRequest = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_config.GetActiveRpcUrl(), content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get latest blockhash, using dummy");
                return new byte[32]; // Return dummy blockhash
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseContent);
            var root = document.RootElement;

            if (root.TryGetProperty("result", out var result) &&
                result.TryGetProperty("value", out var value) &&
                value.TryGetProperty("blockhash", out var blockhashProperty))
            {
                var blockhashBase58 = blockhashProperty.GetString();
                if (!string.IsNullOrEmpty(blockhashBase58))
                {
                    var blockhash = DecodeBase58(blockhashBase58);
                    _logger.LogDebug("Got recent blockhash: {Blockhash}", blockhashBase58);
                    return blockhash;
                }
            }

            _logger.LogWarning("Could not parse blockhash from response, using dummy");
            return new byte[32]; // Return dummy blockhash
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception getting latest blockhash, using dummy");
            return new byte[32]; // Return dummy blockhash
        }
    }

    private async Task<string> RequestAirdropAsync(string publicKeyBase58, ulong lamports)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "requestAirdrop",
            @params = new object[] { publicKeyBase58, lamports }
        };

        var jsonRequest = JsonSerializer.Serialize(request);
        var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(_config.GetActiveRpcUrl(), content);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Airdrop request failed with status: {response.StatusCode}");
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(responseContent);
        var root = document.RootElement;

        if (root.TryGetProperty("error", out var errorElement))
        {
            var errorMessage = errorElement.GetProperty("message").GetString();
            throw new Exception($"Airdrop failed: {errorMessage}");
        }

        if (root.TryGetProperty("result", out var result))
        {
            var signature = result.GetString();
            if (!string.IsNullOrEmpty(signature))
            {
                _logger.LogInformation("   ✅ Airdrop signature: {Signature}", signature);
                return signature;
            }
        }

        throw new Exception("Airdrop request succeeded but no signature returned");
    }

    private async Task<bool> ConfirmTransactionAsync(string signature, int maxRetries = 5)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var request = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "getSignatureStatuses",
                    @params = new object[] 
                    { 
                        new string[] { signature },
                        new { searchTransactionHistory = true }
                    }
                };

                var jsonRequest = JsonSerializer.Serialize(request);
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(_config.GetActiveRpcUrl(), content);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Attempt {Attempt}: Failed to check signature status: {StatusCode}", attempt, response.StatusCode);
                    await Task.Delay(1000);
                    continue;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(responseContent);
                var root = document.RootElement;

                if (root.TryGetProperty("result", out var result) &&
                    result.TryGetProperty("value", out var value) &&
                    value.ValueKind == JsonValueKind.Array)
                {
                    var statusArray = value.EnumerateArray().ToArray();
                    if (statusArray.Length > 0 && statusArray[0].ValueKind != JsonValueKind.Null)
                    {
                        var status = statusArray[0];
                        if (status.TryGetProperty("confirmationStatus", out var confirmationStatus))
                        {
                            var statusValue = confirmationStatus.GetString();
                            if (statusValue == "confirmed" || statusValue == "finalized")
                            {
                                _logger.LogDebug("Transaction {Signature} confirmed with status: {Status}", signature, statusValue);
                                return true;
                            }
                        }
                    }
                }

                _logger.LogDebug("Attempt {Attempt}: Transaction not yet confirmed, waiting...", attempt);
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Attempt {Attempt}: Error checking transaction confirmation", attempt);
                await Task.Delay(1000);
            }
        }

        _logger.LogWarning("Transaction confirmation failed after {MaxRetries} attempts", maxRetries);
        return false;
    }

    private static byte[] DecodeBase58(string base58)
    {
        // Simple Base58 decoder for Solana public keys
        const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        
        var decoded = new List<byte>();
        var num = System.Numerics.BigInteger.Zero;
        
        foreach (var c in base58)
        {
            var index = alphabet.IndexOf(c);
            if (index == -1) throw new ArgumentException($"Invalid character: {c}");
            
            num = num * 58 + index;
        }
        
        while (num > 0)
        {
            decoded.Insert(0, (byte)(num % 256));
            num /= 256;
        }
        
        // Add leading zeros
        var leadingZeros = 0;
        foreach (var c in base58)
        {
            if (c == '1') leadingZeros++;
            else break;
        }
        
        for (int i = 0; i < leadingZeros; i++)
        {
            decoded.Insert(0, 0);
        }
        
        return decoded.ToArray();
    }

    private static string EncodeBase58(byte[] bytes)
    {
        const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        
        if (bytes.Length == 0) return string.Empty;
        
        // Convert to big integer
        var num = System.Numerics.BigInteger.Zero;
        foreach (var b in bytes)
        {
            num = num * 256 + b;
        }
        
        // Convert to base58
        var result = new List<char>();
        while (num > 0)
        {
            var remainder = (int)(num % 58);
            result.Insert(0, alphabet[remainder]);
            num /= 58;
        }
        
        // Add leading zeros as '1' characters
        foreach (var b in bytes)
        {
            if (b == 0) result.Insert(0, '1');
            else break;
        }
        
        return new string(result.ToArray());
    }

    private static string NormalizeVersion(string version)
    {
        if (string.IsNullOrEmpty(version))
            return string.Empty;

        return version.Trim().ToLowerInvariant().TrimStart('v');
    }

    private static int CompareVersions(string version1, string version2)
    {
        if (string.IsNullOrEmpty(version1) && string.IsNullOrEmpty(version2))
            return 0;
        if (string.IsNullOrEmpty(version1))
            return -1;
        if (string.IsNullOrEmpty(version2))
            return 1;

        try
        {
            var v1Parts = version1.Split('.').Select(p => int.TryParse(p, out var num) ? num : 0).ToArray();
            var v2Parts = version2.Split('.').Select(p => int.TryParse(p, out var num) ? num : 0).ToArray();

            var v1 = new int[3];
            var v2 = new int[3];
            
            for (int i = 0; i < 3; i++)
            {
                v1[i] = i < v1Parts.Length ? v1Parts[i] : 0;
                v2[i] = i < v2Parts.Length ? v2Parts[i] : 0;
            }

            for (int i = 0; i < 3; i++)
            {
                if (v1[i] < v2[i]) return -1;
                if (v1[i] > v2[i]) return 1;
            }

            return 0;
        }
        catch
        {
            return string.Compare(version1, version2, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
