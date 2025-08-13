using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Core.Interfaces;

namespace FixedRatioStressTest.Core.Services;

/// <summary>
/// Performance monitoring service for 32-core systems
/// </summary>
public class PerformanceMonitorService : BackgroundService
{
    private readonly ILogger<PerformanceMonitorService> _logger;
    private readonly IThreadManager _threadManager;
    private readonly PerformanceCounter? _cpuCounter;
    private readonly PerformanceCounter? _memoryCounter;
    private readonly Process _currentProcess;
    
    public PerformanceMonitorService(
        ILogger<PerformanceMonitorService> logger,
        IThreadManager threadManager)
    {
        _logger = logger;
        _threadManager = threadManager;
        _currentProcess = Process.GetCurrentProcess();
        
        // Initialize performance counters on Windows
        if (OperatingSystem.IsWindows())
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Process", "% Processor Time", _currentProcess.ProcessName);
                _memoryCounter = new PerformanceCounter("Process", "Working Set - Private", _currentProcess.ProcessName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize performance counters");
            }
        }
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Performance monitoring service started");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectAndLogMetrics();
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting performance metrics");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
        
        _logger.LogInformation("Performance monitoring service stopped");
    }
    
    private async Task CollectAndLogMetrics()
    {
        // Process metrics
        _currentProcess.Refresh();
        var threadCount = _currentProcess.Threads.Count;
        var handleCount = _currentProcess.HandleCount;
        var workingSet = _currentProcess.WorkingSet64 / (1024 * 1024); // MB
        var gcMemory = GC.GetTotalMemory(false) / (1024 * 1024); // MB
        
        // CPU usage (Windows only)
        float cpuUsage = 0;
        if (OperatingSystem.IsWindows() && _cpuCounter != null)
        {
            cpuUsage = _cpuCounter.NextValue();
        }
        
        // Thread statistics
        var threads = await _threadManager.GetAllThreadsAsync();
        var runningThreads = threads.Count(t => t.Status == Common.Models.ThreadStatus.Running);
        var totalOperations = 0L;
        var totalVolume = 0UL;
        
        foreach (var thread in threads)
        {
            var stats = await _threadManager.GetThreadStatisticsAsync(thread.ThreadId);
            totalOperations += stats.SuccessfulOperations + stats.FailedOperations;
            totalVolume += stats.TotalVolumeProcessed;
        }
        
        // Log metrics
        _logger.LogInformation(
            "Performance Metrics - CPU: {CpuUsage:F1}%, Memory: {MemoryMB}MB, GC Memory: {GcMemoryMB}MB, " +
            "Threads: {ThreadCount}, Handles: {HandleCount}, Running Stress Threads: {RunningThreads}/{TotalThreads}, " +
            "Total Operations: {TotalOperations}, Total Volume: {TotalVolume}",
            cpuUsage, workingSet, gcMemory, threadCount, handleCount, 
            runningThreads, threads.Count, totalOperations, totalVolume);
    }
    
    public override void Dispose()
    {
        _cpuCounter?.Dispose();
        _memoryCounter?.Dispose();
        base.Dispose();
    }
}
