using System.Collections.Concurrent;

namespace FixedRatioStressTest.Core.Threading;

/// <summary>
/// High-performance object pool optimized for 32-core systems to minimize GC pressure
/// </summary>
public class HighPerformanceObjectPool<T> where T : class, new()
{
    private readonly ConcurrentBag<T> _objects = new();
    private readonly Func<T> _objectGenerator;
    private readonly Action<T>? _resetAction;
    private readonly int _maxObjects;
    private int _currentCount;
    
    public HighPerformanceObjectPool(int maxObjects = 5000, Action<T>? resetAction = null)
    {
        _maxObjects = maxObjects;
        _objectGenerator = () => new T();
        _resetAction = resetAction;
        
        // Pre-warm the pool for 32-core systems
        PreWarmPool();
    }
    
    private void PreWarmPool()
    {
        var prewarmCount = Math.Min(_maxObjects, Environment.ProcessorCount * 4);
        Parallel.For(0, prewarmCount, _ =>
        {
            _objects.Add(_objectGenerator());
            Interlocked.Increment(ref _currentCount);
        });
    }
    
    public T Rent()
    {
        if (_objects.TryTake(out var item))
        {
            return item;
        }
        
        // Create new object if we haven't reached the limit
        if (_currentCount < _maxObjects)
        {
            Interlocked.Increment(ref _currentCount);
            return _objectGenerator();
        }
        
        // If at max capacity, create a non-pooled instance
        return _objectGenerator();
    }
    
    public void Return(T item)
    {
        if (item == null) return;
        
        // Reset the object if a reset action is provided
        _resetAction?.Invoke(item);
        
        // Only add back to pool if we're under the limit
        if (_objects.Count < _maxObjects)
        {
            _objects.Add(item);
        }
        // Otherwise, let GC collect it
    }
    
    public int AvailableCount => _objects.Count;
    public int TotalCount => _currentCount;
}
