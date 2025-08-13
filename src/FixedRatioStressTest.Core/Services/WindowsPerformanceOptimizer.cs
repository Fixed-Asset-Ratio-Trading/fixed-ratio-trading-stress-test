using System.Diagnostics;
using System.Runtime;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Core.Services;

/// <summary>
/// Windows-specific performance optimizations for 32-core systems
/// </summary>
public static class WindowsPerformanceOptimizer
{
    public static void OptimizeForThreadripper(ILogger? logger = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            logger?.LogWarning("Windows performance optimizations skipped on non-Windows platform");
            return;
        }
        
        try
        {
            // Configure GC for high-core systems
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            logger?.LogInformation("Set GC latency mode to SustainedLowLatency");
            
            // Set process priority for maximum performance
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            logger?.LogInformation("Set process priority to High");
            
            // Configure ThreadPool for 32-core utilization
            ThreadPool.SetMinThreads(112, 112);
            ThreadPool.SetMaxThreads(112, 112);
            logger?.LogInformation("Configured ThreadPool for 32-core system: 112 worker threads");
            
            // Set processor affinity (reserve cores 0-3 for system)
            SetOptimalProcessorAffinity(logger);
            
            logger?.LogInformation("Windows performance optimizations completed");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to apply some Windows performance optimizations");
        }
    }
    
    private static void SetOptimalProcessorAffinity(ILogger? logger)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            logger?.LogWarning("Processor affinity is only supported on Windows and Linux");
            return;
        }
        
        try
        {
            var process = Process.GetCurrentProcess();
            // Reserve cores 0-3 for system, use 4-31 for workers
            var affinityMask = (IntPtr)((long)Math.Pow(2, 32) - 1 - 15);
            process.ProcessorAffinity = affinityMask;
            logger?.LogInformation("Set process affinity to use cores 4-31");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to set processor affinity");
        }
    }
}
