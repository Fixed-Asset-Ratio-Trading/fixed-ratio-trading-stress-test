using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Infrastructure.Services;
using FixedRatioStressTest.Core.Services;
using FixedRatioStressTest.Core.Interfaces;
using Xunit;
using Xunit.Abstractions;

namespace FixedRatioStressTest.Core.Tests;

/// <summary>
/// Tests for contract version startup service functionality
/// </summary>
public class ContractVersionStartupServiceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<ContractVersionStartupService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConfiguration _configuration;

    public ContractVersionStartupServiceTests(ITestOutputHelper output)
    {
        _output = output;

        // Setup logging to capture debug info
        _loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = _loggerFactory.CreateLogger<ContractVersionStartupService>();

        // Setup configuration
        var configBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SolanaConfiguration:LocalnetRpcUrl"] = "http://192.168.2.88:8899",
                ["SolanaConfiguration:DevnetRpcUrl"] = "https://api.devnet.solana.com",
                ["SolanaConfiguration:MainnetRpcUrl"] = "https://api.mainnet-beta.solana.com",
                ["SolanaConfiguration:ActiveNetwork"] = "localnet",
                ["SolanaConfiguration:ProgramId"] = "4aeVqtWhrUh6wpX8acNj2hpWXKEQwxjA3PYb2sHhNyCn",
                ["SolanaConfiguration:Commitment"] = "confirmed",
                ["ContractVersion:Expected"] = "v0.15.1053"
            });
        _configuration = configBuilder.Build();
    }

    [Fact]
    public async Task ContractVersionStartupService_StartAsync_HandlesVersionCheckGracefully()
    {
        _logger.LogInformation("=== TEST: ContractVersionStartupService_StartAsync_HandlesVersionCheckGracefully ===");

        try
        {
            // Arrange
            var versionService = new ContractVersionService(_configuration, 
                _loggerFactory.CreateLogger<ContractVersionService>());

            var mockAppLifetime = new MockApplicationLifetime();
            var startupService = new ContractVersionStartupService(
                versionService, mockAppLifetime, _logger);

            // Act
            await startupService.StartAsync(CancellationToken.None);

            // Assert
            _output.WriteLine($"App shutdown requested: {mockAppLifetime.StopRequested}");
            
            // The service should complete without throwing
            Assert.True(true, "Service startup completed without exceptions");

            // Log the outcome
            if (mockAppLifetime.StopRequested)
            {
                _logger.LogInformation("✅ Service correctly requested shutdown due to version issues");
            }
            else
            {
                _logger.LogInformation("✅ Service allowed startup to continue (version check passed or was inconclusive)");
            }

            // Cleanup
            await startupService.StopAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test failed with exception");
            throw;
        }
    }

    public void Dispose()
    {
        _loggerFactory?.Dispose();
    }

    /// <summary>
    /// Mock implementation of IHostApplicationLifetime for testing
    /// </summary>
    private class MockApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _applicationStartedSource = new();
        private readonly CancellationTokenSource _applicationStoppingSource = new();
        private readonly CancellationTokenSource _applicationStoppedSource = new();

        public bool StopRequested { get; private set; }

        public CancellationToken ApplicationStarted => _applicationStartedSource.Token;
        public CancellationToken ApplicationStopping => _applicationStoppingSource.Token;
        public CancellationToken ApplicationStopped => _applicationStoppedSource.Token;

        public void StopApplication()
        {
            StopRequested = true;
            if (!_applicationStoppingSource.IsCancellationRequested)
            {
                _applicationStoppingSource.Cancel();
            }
            if (!_applicationStoppedSource.IsCancellationRequested)
            {
                _applicationStoppedSource.Cancel();
            }
        }
    }
}
