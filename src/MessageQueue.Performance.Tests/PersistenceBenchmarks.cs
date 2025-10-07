using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using MessageQueue.Core;
using MessageQueue.Core.Interfaces;
using MessageQueue.Core.Options;
using MessageQueue.Persistence;

namespace MessageQueue.Performance.Tests;

/// <summary>
/// Benchmarks for persistence operations.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class PersistenceBenchmarks
{
    private IQueueManager _queueManager = null!;
    private ICircularBuffer _buffer = null!;
    private DeduplicationIndex _deduplicationIndex = null!;
    private IPersister _persister = null!;
    private string _testDirectory = null!;

    [Params(100, 1000, 10000)]
    public int MessageCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"perf-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);

        var persistenceOptions = new PersistenceOptions
        {
            StoragePath = _testDirectory,
            JournalFileName = "journal.dat",
            SnapshotInterval = TimeSpan.FromHours(1), // Don't trigger during benchmark
            SnapshotThreshold = 1000000
        };

        var queueOptions = new QueueOptions
        {
            Capacity = 1000000,
            EnablePersistence = true,
            EnableDeduplication = true
        };

        _buffer = new CircularBuffer(queueOptions.Capacity);
        _deduplicationIndex = new DeduplicationIndex();
        _persister = new FilePersister(persistenceOptions);
        _queueManager = new QueueManager(_buffer, _deduplicationIndex, queueOptions, _persister);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_persister is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Benchmark(Description = "Enqueue with journal persistence")]
    public async Task EnqueueWithPersistence()
    {
        for (int i = 0; i < MessageCount; i++)
        {
            await _queueManager.EnqueueAsync(new TestMessage { Id = i, Data = $"Message {i}" });
        }
    }

    [Benchmark(Description = "Snapshot creation")]
    public async Task SnapshotCreation()
    {
        // First populate the queue
        for (int i = 0; i < MessageCount; i++)
        {
            await _queueManager.EnqueueAsync(new TestMessage { Id = i, Data = $"Message {i}" });
        }

        // Benchmark snapshot creation
        var snapshot = await _queueManager.CreateSnapshotAsync();
        await _persister.CreateSnapshotAsync(snapshot);
    }

    [Benchmark(Description = "Journal replay")]
    public async Task JournalReplay()
    {
        // First create some journal entries
        for (int i = 0; i < MessageCount; i++)
        {
            await _queueManager.EnqueueAsync(new TestMessage { Id = i, Data = $"Message {i}" });
        }

        // Benchmark journal replay
        var operations = await _persister.ReplayJournalAsync(0);
        var count = operations.Count();
    }

    private class TestMessage
    {
        public int Id { get; set; }
        public string Data { get; set; } = string.Empty;
    }
}
