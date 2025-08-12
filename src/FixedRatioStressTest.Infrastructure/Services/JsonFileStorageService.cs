using System.Text.Json;
using FixedRatioStressTest.Common.Models;
using FixedRatioStressTest.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Infrastructure.Services;

public class JsonFileStorageService : IStorageService
{
    private readonly string _dataDirectory;
    private readonly ILogger<JsonFileStorageService> _logger;
    private readonly SemaphoreSlim _fileLock;
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonFileStorageService(IConfiguration configuration, ILogger<JsonFileStorageService> logger)
    {
        _dataDirectory = configuration.GetValue<string>("Storage:DataDirectory")
                        ?? Path.Combine(Environment.CurrentDirectory, "data");
        _logger = logger;
        _fileLock = new SemaphoreSlim(1, 1);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        EnsureDirectoriesExist();
    }

    public async Task SaveThreadConfigAsync(string threadId, ThreadConfig config)
    {
        await _fileLock.WaitAsync();
        try
        {
            var threadsFile = Path.Combine(_dataDirectory, "threads.json");
            var tempFile = $"{threadsFile}.tmp";

            var threads = await LoadAllThreadsAsync() ?? new List<ThreadConfig>();
            var existingIndex = threads.FindIndex(t => t.ThreadId == threadId);

            if (existingIndex >= 0)
                threads[existingIndex] = config;
            else
                threads.Add(config);

            var json = JsonSerializer.Serialize(new { threads }, _jsonOptions);
            await File.WriteAllTextAsync(tempFile, json);

            if (File.Exists(threadsFile))
                File.Replace(tempFile, threadsFile, $"{threadsFile}.backup");
            else
                File.Move(tempFile, threadsFile);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<ThreadConfig> LoadThreadConfigAsync(string threadId)
    {
        var threads = await LoadAllThreadsAsync();
        var config = threads?.FirstOrDefault(t => t.ThreadId == threadId);
        if (config is null)
        {
            throw new KeyNotFoundException($"Thread {threadId} not found");
        }
        return config;
    }

    public async Task<List<ThreadConfig>> LoadAllThreadsAsync()
    {
        var threadsFile = Path.Combine(_dataDirectory, "threads.json");
        if (!File.Exists(threadsFile))
            return new List<ThreadConfig>();

        var json = await File.ReadAllTextAsync(threadsFile);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("threads", out var threadsElement))
        {
            var threads = threadsElement.Deserialize<List<ThreadConfig>>(_jsonOptions);
            return threads ?? new List<ThreadConfig>();
        }
        return new List<ThreadConfig>();
    }

    public async Task SaveThreadStatisticsAsync(string threadId, ThreadStatistics statistics)
    {
        await _fileLock.WaitAsync();
        try
        {
            var statsFile = Path.Combine(_dataDirectory, "statistics.json");
            var tempFile = $"{statsFile}.tmp";

            var allStats = await LoadAllStatisticsAsync();
            allStats[threadId] = statistics;

            var json = JsonSerializer.Serialize(new { statistics = allStats }, _jsonOptions);
            await File.WriteAllTextAsync(tempFile, json);

            if (File.Exists(statsFile))
                File.Replace(tempFile, statsFile, $"{statsFile}.backup");
            else
                File.Move(tempFile, statsFile);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<ThreadStatistics> LoadThreadStatisticsAsync(string threadId)
    {
        var allStats = await LoadAllStatisticsAsync();
        return allStats.GetValueOrDefault(threadId, new ThreadStatistics());
    }

    public async Task AddThreadErrorAsync(string threadId, ThreadError error)
    {
        var statistics = await LoadThreadStatisticsAsync(threadId);
        statistics.RecentErrors.Add(error);

        if (statistics.RecentErrors.Count > 10)
        {
            statistics.RecentErrors = statistics.RecentErrors
                .OrderByDescending(e => e.Timestamp)
                .Take(10)
                .ToList();
        }

        await SaveThreadStatisticsAsync(threadId, statistics);
    }

    private async Task<Dictionary<string, ThreadStatistics>> LoadAllStatisticsAsync()
    {
        var statsFile = Path.Combine(_dataDirectory, "statistics.json");
        if (!File.Exists(statsFile))
            return new Dictionary<string, ThreadStatistics>();

        var json = await File.ReadAllTextAsync(statsFile);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("statistics", out var statsElement))
        {
            var dict = statsElement.Deserialize<Dictionary<string, ThreadStatistics>>(_jsonOptions);
            return dict ?? new Dictionary<string, ThreadStatistics>();
        }
        return new Dictionary<string, ThreadStatistics>();
    }

    private void EnsureDirectoriesExist()
    {
        if (!Directory.Exists(_dataDirectory))
        {
            Directory.CreateDirectory(_dataDirectory);
        }
    }
}

