using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using MessageQueue.Core;
using MessageQueue.Core.Interfaces;
using MessageQueue.Core.Options;
using Microsoft.Extensions.DependencyInjection;

namespace MessageQueue.Performance.Tests;

/// <summary>
/// End-to-end benchmarks simulating real-world usage patterns.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class EndToEndBenchmarks
{
    private IQueueManager _queueManager = null!;
    private ICircularBuffer _buffer = null!;
    private DeduplicationIndex _deduplicationIndex = null!;
    private IHandlerDispatcher _dispatcher = null!;
    private ServiceProvider _serviceProvider = null!;

    [Params(1000, 5000)]
    public int MessageCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();

        var options = new QueueOptions
        {
            Capacity = 1000000,
            EnablePersistence = false,
            EnableDeduplication = true,
            DefaultTimeout = TimeSpan.FromSeconds(30)
        };

        _buffer = new CircularBuffer(options.Capacity);
        _deduplicationIndex = new DeduplicationIndex();
        _queueManager = new QueueManager(_buffer, _deduplicationIndex, options);

        // Register test handler
        services.AddScoped<IMessageHandler<TestMessage>, TestMessageHandler>();
        services.Configure<HandlerOptions<TestMessage>>(opt =>
        {
            opt.MaxParallelism = 10;
            opt.MinParallelism = 1;
            opt.Timeout = TimeSpan.FromSeconds(30);
            opt.MaxRetries = 3;
        });

        _serviceProvider = services.BuildServiceProvider();

        var registry = new HandlerRegistry(_serviceProvider);
        registry.RegisterHandler<TestMessage, TestMessageHandler>(
            _serviceProvider.GetRequiredService<HandlerOptions<TestMessage>>());

        _dispatcher = new HandlerDispatcher(
            _queueManager,
            registry,
            _serviceProvider.GetRequiredService<IServiceScopeFactory>());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _dispatcher?.StopAsync().GetAwaiter().GetResult();
        _serviceProvider?.Dispose();
    }

    [Benchmark(Description = "Producer-Consumer throughput")]
    public async Task ProducerConsumerThroughput()
    {
        await _dispatcher.StartAsync();

        // Producer task
        var producerTask = Task.Run(async () =>
        {
            for (int i = 0; i < MessageCount; i++)
            {
                await _queueManager.EnqueueAsync(new TestMessage { Id = i, Data = $"Message {i}" });
                _dispatcher.SignalMessageReady(typeof(TestMessage));
            }
        });

        // Consumer is handled by dispatcher workers
        await producerTask;

        // Wait for all messages to be processed
        await Task.Delay(100); // Small delay to ensure processing completes

        await _dispatcher.StopAsync();
    }

    [Benchmark(Description = "Multi-producer multi-consumer")]
    public async Task MultiProducerMultiConsumer()
    {
        await _dispatcher.StartAsync();

        var producerTasks = new List<Task>();
        int messagesPerProducer = MessageCount / 5;

        for (int p = 0; p < 5; p++)
        {
            int producerId = p;
            producerTasks.Add(Task.Run(async () =>
            {
                for (int i = 0; i < messagesPerProducer; i++)
                {
                    await _queueManager.EnqueueAsync(
                        new TestMessage { Id = producerId * messagesPerProducer + i, Data = $"Message {i}" });
                    _dispatcher.SignalMessageReady(typeof(TestMessage));
                }
            }));
        }

        await Task.WhenAll(producerTasks);

        // Wait for processing
        await Task.Delay(100);

        await _dispatcher.StopAsync();
    }

    private class TestMessage
    {
        public int Id { get; set; }
        public string Data { get; set; } = string.Empty;
    }

    private class TestMessageHandler : IMessageHandler<TestMessage>
    {
        public Task HandleAsync(TestMessage message, CancellationToken cancellationToken)
        {
            // Minimal processing to benchmark infrastructure overhead
            return Task.CompletedTask;
        }
    }
}
