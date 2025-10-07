// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using MessageQueue.Core;
using MessageQueue.Core.Interfaces;
using MessageQueue.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Diagnostics;

namespace MessageQueue.SoakTests;

using MessageQueue.Core.Persistence;

/// <summary>
/// 24-hour soak test to validate system stability under sustained load.
/// </summary>
public class Program
{
    private static long TotalEnqueued = 0;
    private static long TotalProcessed = 0;
    private static long TotalFailed = 0;
    private static readonly object StatsLock = new object();

    public static async Task Main(string[] args)
    {
        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File("soak-test-.log", rollingInterval: RollingInterval.Hour)
            .CreateLogger();

        try
        {
            Log.Information("=== MessageQueue Soak Test Starting ===");

            var duration = TimeSpan.FromHours(24);
            if (args.Length > 0 && int.TryParse(args[0], out var hours))
            {
                duration = TimeSpan.FromHours(hours);
            }

            Log.Information("Test duration: {Duration}", duration);

            await RunSoakTest(duration);

            Log.Information("=== MessageQueue Soak Test Completed Successfully ===");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Soak test failed with exception");
            Environment.ExitCode = 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static async Task RunSoakTest(TimeSpan duration)
    {
        var services = new ServiceCollection();

        // Configure queue options
        var queueOptions = new QueueOptions
        {
            Capacity = 100000,
            EnablePersistence = true,
            EnableDeduplication = true,
            PersistencePath = "./soak-test-data",
            SnapshotIntervalSeconds = 60, // Snapshot every minute
            SnapshotThreshold = 10000,
            DefaultMaxRetries = 5,
            DefaultTimeout = TimeSpan.FromSeconds(30)
        };

        var persistenceOptions = new PersistenceOptions
        {
            StoragePath = queueOptions.PersistencePath,
            JournalFileName = "journal.dat",
            SnapshotInterval = TimeSpan.FromSeconds(queueOptions.SnapshotIntervalSeconds),
            SnapshotThreshold = queueOptions.SnapshotThreshold
        };

        // Ensure storage directory exists
        Directory.CreateDirectory(queueOptions.PersistencePath);

        // Build queue components
        var buffer = new CircularBuffer(queueOptions.Capacity);
        var dedupIndex = new DeduplicationIndex();
        var persister = new FilePersister(persistenceOptions);
        var queueManager = new QueueManager(buffer, dedupIndex, queueOptions, persister);

        // Register message handlers
        services.AddScoped<IMessageHandler<WorkMessage>, WorkMessageHandler>();
        services.Configure<HandlerOptions<WorkMessage>>(opt =>
        {
            opt.MaxParallelism = 10;
            opt.MinParallelism = 5;
            opt.Timeout = TimeSpan.FromSeconds(30);
            opt.MaxRetries = 5;
        });

        var serviceProvider = services.BuildServiceProvider();

        var registry = new HandlerRegistry(serviceProvider);
        registry.RegisterHandler<WorkMessage, WorkMessageHandler>(
            serviceProvider.GetRequiredService<HandlerOptions<WorkMessage>>());

        var dispatcher = new HandlerDispatcher(
            queueManager,
            registry,
            serviceProvider.GetRequiredService<IServiceScopeFactory>());

        var deadLetterQueue = new DeadLetterQueue(queueManager, queueOptions, persister);

        // Start dispatcher
        await dispatcher.StartAsync();

        var stopwatch = Stopwatch.StartNew();
        var cts = new CancellationTokenSource(duration);

        // Start stats reporter
        var statsTask = Task.Run(() => StatsReporter(stopwatch, cts.Token));

        // Start producer tasks
        var producerTasks = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            int producerId = i;
            producerTasks.Add(Task.Run(async () => await ProducerLoop(queueManager, dispatcher, producerId, cts.Token)));
        }

        // Wait for test duration
        try
        {
            await Task.WhenAll(producerTasks);
        }
        catch (OperationCanceledException)
        {
            // Expected when test duration expires
        }

        // Stop dispatcher
        await dispatcher.StopAsync();

        // Final stats
        Log.Information("=== Final Statistics ===");
        Log.Information("Total Enqueued: {TotalEnqueued}", TotalEnqueued);
        Log.Information("Total Processed: {TotalProcessed}", TotalProcessed);
        Log.Information("Total Failed: {TotalFailed}", TotalFailed);
        Log.Information("Success Rate: {SuccessRate:P2}", (double)TotalProcessed / TotalEnqueued);
        Log.Information("Duration: {Duration}", stopwatch.Elapsed);
        Log.Information("Throughput: {Throughput:F2} msg/sec", TotalProcessed / stopwatch.Elapsed.TotalSeconds);

        // Cleanup
        serviceProvider.Dispose();
        if (persister is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static async Task ProducerLoop(IQueueManager queueManager, IHandlerDispatcher dispatcher, int producerId, CancellationToken cancellationToken)
    {
        var random = new Random(producerId);
        long messageId = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var message = new WorkMessage
                {
                    Id = $"producer-{producerId}-{messageId++}",
                    ProducerId = producerId,
                    Data = $"Work data {messageId}",
                    ProcessingTimeMs = random.Next(10, 100) // Random processing time
                };

                await queueManager.EnqueueAsync(message);
                dispatcher.SignalMessageReady(typeof(WorkMessage));

                Interlocked.Increment(ref TotalEnqueued);

                // Throttle production rate
                await Task.Delay(random.Next(50, 200), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Producer {ProducerId} error", producerId);
                await Task.Delay(1000, cancellationToken);
            }
        }

        Log.Information("Producer {ProducerId} completed", producerId);
    }

    private static async Task StatsReporter(Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);

                var enqueued = Interlocked.Read(ref TotalEnqueued);
                var processed = Interlocked.Read(ref TotalProcessed);
                var failed = Interlocked.Read(ref TotalFailed);

                Log.Information(
                    "[{Elapsed}] Stats: Enqueued={Enqueued}, Processed={Processed}, Failed={Failed}, Rate={Rate:F2} msg/s",
                    stopwatch.Elapsed.ToString(@"hh\:mm\:ss"),
                    enqueued,
                    processed,
                    failed,
                    processed / stopwatch.Elapsed.TotalSeconds);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Stats reporter error");
            }
        }
    }

    private class WorkMessage
    {
        public string Id { get; set; } = string.Empty;
        public int ProducerId { get; set; }
        public string Data { get; set; } = string.Empty;
        public int ProcessingTimeMs { get; set; }
    }

    private class WorkMessageHandler : IMessageHandler<WorkMessage>
    {
        private static readonly Random Random = new Random();

        public async Task HandleAsync(WorkMessage message, CancellationToken cancellationToken)
        {
            // Simulate work
            await Task.Delay(message.ProcessingTimeMs, cancellationToken);

            Interlocked.Increment(ref TotalProcessed);

            // Simulate occasional failures (5% failure rate)
            if (Random.Next(100) < 5)
            {
                Interlocked.Increment(ref TotalFailed);
                throw new Exception("Simulated handler failure");
            }
        }
    }
}
