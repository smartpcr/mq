using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using MessageQueue.Core;
using MessageQueue.Core.Interfaces;
using MessageQueue.Core.Options;

namespace MessageQueue.Performance.Tests;

/// <summary>
/// Benchmarks for message checkout operations.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class CheckoutBenchmarks
{
    private IQueueManager _queueManager = null!;
    private ICircularBuffer _buffer = null!;
    private DeduplicationIndex _deduplicationIndex = null!;

    [Params(1000, 10000)]
    public int MessageCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var options = new QueueOptions
        {
            Capacity = 1000000,
            EnablePersistence = false,
            EnableDeduplication = true,
            DefaultTimeout = TimeSpan.FromMinutes(5)
        };

        _buffer = new CircularBuffer(options.Capacity);
        _deduplicationIndex = new DeduplicationIndex();
        _queueManager = new QueueManager(_buffer, _deduplicationIndex, options);
    }

    [IterationSetup]
    public async Task IterationSetup()
    {
        // Populate queue with messages
        for (int i = 0; i < MessageCount; i++)
        {
            await _queueManager.EnqueueAsync(new TestMessage { Id = i, Data = $"Message {i}" });
        }
    }

    [Benchmark(Description = "Sequential checkout")]
    public async Task SequentialCheckout()
    {
        for (int i = 0; i < MessageCount; i++)
        {
            var message = await _queueManager.CheckoutAsync<TestMessage>("handler-1");
            if (message != null)
            {
                await _queueManager.AcknowledgeAsync(message.MessageId);
            }
        }
    }

    [Benchmark(Description = "Concurrent checkout (10 consumers)")]
    public async Task ConcurrentCheckout()
    {
        var tasks = new List<Task>();

        for (int c = 0; c < 10; c++)
        {
            int consumerId = c;
            tasks.Add(Task.Run(async () =>
            {
                while (true)
                {
                    var message = await _queueManager.CheckoutAsync<TestMessage>($"handler-{consumerId}");
                    if (message == null)
                        break;

                    await _queueManager.AcknowledgeAsync(message.MessageId);
                }
            }));
        }

        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "Checkout with lease extension")]
    public async Task CheckoutWithLeaseExtension()
    {
        for (int i = 0; i < Math.Min(MessageCount, 1000); i++)
        {
            var message = await _queueManager.CheckoutAsync<TestMessage>("handler-1");
            if (message != null)
            {
                // Simulate processing with lease extension
                await _queueManager.ExtendLeaseAsync(message.MessageId, TimeSpan.FromMinutes(1));
                await _queueManager.AcknowledgeAsync(message.MessageId);
            }
        }
    }

    private class TestMessage
    {
        public int Id { get; set; }
        public string Data { get; set; } = string.Empty;
    }
}
