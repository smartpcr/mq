// -----------------------------------------------------------------------
// <copyright file="PersistenceBenchmarks.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

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
    private IQueueManager queueManager = null!;
    private ICircularBuffer buffer = null!;
    private DeduplicationIndex deduplicationIndex = null!;
    private IPersister persister = null!;
    private string testDirectory = null!;

    [Params(100, 1000, 10000)]
    public int MessageCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        this.testDirectory = Path.Combine(Path.GetTempPath(), $"perf-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(this.testDirectory);

        var persistenceOptions = new PersistenceOptions
        {
            StoragePath = this.testDirectory,
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

        this.buffer = new CircularBuffer(queueOptions.Capacity);
        this.deduplicationIndex = new DeduplicationIndex();
        this.persister = new FilePersister(persistenceOptions);
        this.queueManager = new QueueManager(this.buffer, this.deduplicationIndex, queueOptions, this.persister);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (this.persister is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (Directory.Exists(this.testDirectory))
        {
            Directory.Delete(this.testDirectory, true);
        }
    }

    [Benchmark(Description = "Enqueue with journal persistence")]
    public async Task EnqueueWithPersistence()
    {
        for (int i = 0; i < MessageCount; i++)
        {
            await this.queueManager.EnqueueAsync(new TestMessage { Id = i, Data = $"Message {i}" });
        }
    }

    [Benchmark(Description = "Snapshot creation")]
    public async Task SnapshotCreation()
    {
        // First populate the queue
        for (int i = 0; i < MessageCount; i++)
        {
            await this.queueManager.EnqueueAsync(new TestMessage { Id = i, Data = $"Message {i}" });
        }

        // Benchmark snapshot creation
        var snapshot = await this.queueManager.CreateSnapshotAsync();
        await this.persister.CreateSnapshotAsync(snapshot);
    }

    [Benchmark(Description = "Journal replay")]
    public async Task JournalReplay()
    {
        // First create some journal entries
        for (int i = 0; i < MessageCount; i++)
        {
            await this.queueManager.EnqueueAsync(new TestMessage { Id = i, Data = $"Message {i}" });
        }

        // Benchmark journal replay
        var operations = await this.persister.ReplayJournalAsync(0);
        var count = operations.Count();
    }

    private class TestMessage
    {
        public int Id { get; set; }
        public string Data { get; set; } = string.Empty;
    }
}
