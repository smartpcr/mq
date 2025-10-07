using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using MessageQueue.Core;
using MessageQueue.Core.Interfaces;
using MessageQueue.Core.Options;

namespace MessageQueue.Performance.Tests;

/// <summary>
/// Benchmarks for message enqueue operations.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class EnqueueBenchmarks
{
    private IQueueManager _queueManager = null!;
    private ICircularBuffer _buffer = null!;
    private DeduplicationIndex _deduplicationIndex = null!;

    [Params(1000, 10000, 100000)]
    public int MessageCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var options = new QueueOptions
        {
            Capacity = 1000000,
            EnablePersistence = false, // Disable for pure in-memory benchmarks
            EnableDeduplication = true
        };

        _buffer = new CircularBuffer(options.Capacity);
        _deduplicationIndex = new DeduplicationIndex();
        _queueManager = new QueueManager(_buffer, _deduplicationIndex, options);
    }

    [Benchmark(Description = "Enqueue without deduplication")]
    public async Task EnqueueWithoutDedup()
    {
        for (int i = 0; i < MessageCount; i++)
        {
            await _queueManager.EnqueueAsync(new TestMessage { Id = i, Data = $"Message {i}" });
        }
    }

    [Benchmark(Description = "Enqueue with unique deduplication keys")]
    public async Task EnqueueWithUniqueDedup()
    {
        for (int i = 0; i < MessageCount; i++)
        {
            await _queueManager.EnqueueAsync(
                new TestMessage { Id = i, Data = $"Message {i}" },
                deduplicationKey: $"key-{i}");
        }
    }

    [Benchmark(Description = "Enqueue with repeated deduplication keys")]
    public async Task EnqueueWithRepeatedDedup()
    {
        for (int i = 0; i < MessageCount; i++)
        {
            await _queueManager.EnqueueAsync(
                new TestMessage { Id = i, Data = $"Message {i}" },
                deduplicationKey: $"key-{i % 100}"); // Repeat every 100 messages
        }
    }

    [Benchmark(Description = "Concurrent enqueue (10 producers)")]
    public async Task ConcurrentEnqueue()
    {
        var tasks = new List<Task>();
        int messagesPerProducer = MessageCount / 10;

        for (int p = 0; p < 10; p++)
        {
            int producerId = p;
            tasks.Add(Task.Run(async () =>
            {
                for (int i = 0; i < messagesPerProducer; i++)
                {
                    await _queueManager.EnqueueAsync(
                        new TestMessage { Id = producerId * messagesPerProducer + i, Data = $"Message {i}" });
                }
            }));
        }

        await Task.WhenAll(tasks);
    }

    private class TestMessage
    {
        public int Id { get; set; }
        public string Data { get; set; } = string.Empty;
    }
}
