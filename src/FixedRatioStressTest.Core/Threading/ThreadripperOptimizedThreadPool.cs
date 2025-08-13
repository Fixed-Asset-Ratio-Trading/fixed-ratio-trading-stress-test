using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Core.Threading;

/// <summary>
/// Thread pool optimized for 32-core AMD Threadripper processors
/// </summary>
public class ThreadripperOptimizedThreadPool
{
    // Configuration optimized for 32-core Threadripper
    private const int RESERVED_CORES = 4;
    private const int WORKER_CORES = 28;
    private const int MAX_THREADS_PER_CORE = 4;  // Optimal for I/O-bound blockchain operations
    private const int TOTAL_WORKER_THREADS = WORKER_CORES * MAX_THREADS_PER_CORE; // 112 threads
    
    private readonly ILogger<ThreadripperOptimizedThreadPool> _logger;
    private readonly SemaphoreSlim _threadSemaphore;
    
    public ThreadripperOptimizedThreadPool(ILogger<ThreadripperOptimizedThreadPool> logger)
    {
        _logger = logger;
        _threadSemaphore = new SemaphoreSlim(TOTAL_WORKER_THREADS, TOTAL_WORKER_THREADS);
        
        ConfigureThreadPool();
    }
    
    private void ConfigureThreadPool()
    {
        // Configure thread pool for maximum concurrency
        ThreadPool.SetMinThreads(TOTAL_WORKER_THREADS, TOTAL_WORKER_THREADS);
        ThreadPool.SetMaxThreads(TOTAL_WORKER_THREADS, TOTAL_WORKER_THREADS);
        
        _logger.LogInformation("Configured thread pool for 32-core Threadripper: {MinThreads} worker threads", 
            TOTAL_WORKER_THREADS);
        
        // Log processor information
        _logger.LogInformation("Processor count: {ProcessorCount}, Reserved cores: {ReservedCores}, Worker cores: {WorkerCores}", 
            Environment.ProcessorCount, RESERVED_CORES, WORKER_CORES);
    }
    
    public async Task<T> RunWithOptimizedSchedulingAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        await _threadSemaphore.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(operation, cancellationToken);
        }
        finally
        {
            _threadSemaphore.Release();
        }
    }
    
    public async Task RunWithOptimizedSchedulingAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        await _threadSemaphore.WaitAsync(cancellationToken);
        try
        {
            await Task.Run(operation, cancellationToken);
        }
        finally
        {
            _threadSemaphore.Release();
        }
    }
    
    public void SetProcessAffinity()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                // Reserve cores 0-3 for system, use 4-31 for workers
                var process = System.Diagnostics.Process.GetCurrentProcess();
                var affinityMask = (IntPtr)((long)Math.Pow(2, 32) - 1 - 15); // All cores except 0-3
                process.ProcessorAffinity = affinityMask;
                
                _logger.LogInformation("Set process affinity to use cores 4-31 for worker threads");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set process affinity. Running with default affinity.");
            }
        }
        else
        {
            _logger.LogInformation("Processor affinity is not supported on this platform");
        }
    }
}
