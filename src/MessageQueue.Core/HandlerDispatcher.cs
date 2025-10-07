// -----------------------------------------------------------------------
// <copyright file="HandlerDispatcher.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Microsoft.Extensions.DependencyInjection;
    using MessageQueue.Core.Interfaces;
    using MessageQueue.Core.Options;

    /// <summary>
    /// Dispatches messages to registered handlers with parallelism control, timeout enforcement, and channel-based signaling.
    /// </summary>
    public class HandlerDispatcher : IHandlerDispatcher
    {
        private readonly IQueueManager queueManager;
        private readonly HandlerRegistry handlerRegistry;
        private readonly IServiceScopeFactory scopeFactory;
        private readonly ConcurrentDictionary<Type, Channel<bool>> signalChannels;
        private readonly ConcurrentDictionary<Type, WorkerPool> workerPools;
        private readonly ConcurrentDictionary<Type, HandlerMetrics> metrics;
        private CancellationTokenSource globalCancellation;
        private bool isRunning;

        /// <summary>
        /// Initializes a new instance of the HandlerDispatcher.
        /// </summary>
        /// <param name="queueManager">Queue manager for message checkout/acknowledgement.</param>
        /// <param name="handlerRegistry">Handler registry for type lookup.</param>
        /// <param name="scopeFactory">Service scope factory for DI.</param>
        public HandlerDispatcher(
            IQueueManager queueManager,
            HandlerRegistry handlerRegistry,
            IServiceScopeFactory scopeFactory)
        {
            this.queueManager = queueManager ?? throw new ArgumentNullException(nameof(queueManager));
            this.handlerRegistry = handlerRegistry ?? throw new ArgumentNullException(nameof(handlerRegistry));
            this.scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            this.signalChannels = new ConcurrentDictionary<Type, Channel<bool>>();
            this.workerPools = new ConcurrentDictionary<Type, WorkerPool>();
            this.metrics = new ConcurrentDictionary<Type, HandlerMetrics>();
        }

        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (this.isRunning)
                throw new InvalidOperationException("Dispatcher is already running.");

            this.globalCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            this.isRunning = true;

            // Start worker pools for all registered handlers
            foreach (var messageType in this.handlerRegistry.GetRegisteredMessageTypes())
            {
                var registration = this.handlerRegistry.GetRegistration(messageType);
                this.StartWorkerPool(messageType, registration);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (!this.isRunning)
                return;

            this.isRunning = false;

            // Signal shutdown to all workers
            this.globalCancellation?.Cancel();

            // Wait for all workers to complete
            var stopTasks = this.workerPools.Values.SelectMany(pool => pool.Workers).ToList();
            await Task.WhenAll(stopTasks);

            this.globalCancellation?.Dispose();
            this.globalCancellation = null;
        }

        /// <inheritdoc/>
        public void SignalMessageReady(Type messageType)
        {
            if (!this.signalChannels.TryGetValue(messageType, out var channel))
                return;

            // Non-blocking signal via channel
            channel.Writer.TryWrite(true);
        }

        /// <inheritdoc/>
        public async Task ScaleHandlerAsync(Type messageType, int instanceCount, CancellationToken cancellationToken = default)
        {
            if (!this.workerPools.TryGetValue(messageType, out var pool))
                throw new InvalidOperationException($"No worker pool found for message type: {messageType.FullName}");

            var registration = this.handlerRegistry.GetRegistration(messageType);
            var minParallelism = this.GetMinParallelism(registration);
            var maxParallelism = this.GetMaxParallelism(registration);

            instanceCount = Math.Max(minParallelism, Math.Min(instanceCount, maxParallelism));

            var currentCount = pool.Workers.Count;
            if (instanceCount > currentCount)
            {
                // Scale up
                for (int i = currentCount; i < instanceCount; i++)
                {
                    var worker = this.CreateWorkerTask(messageType, registration, i);
                    pool.Workers.Add(worker);
                }
            }
            else if (instanceCount < currentCount)
            {
                // Scale down - mark excess workers for termination
                // Workers will exit naturally when they check pool.DesiredCount
                pool.DesiredCount = instanceCount;
            }

            await Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<HandlerStatistics> GetStatisticsAsync(Type messageType, CancellationToken cancellationToken = default)
        {
            if (!this.metrics.TryGetValue(messageType, out var handlerMetrics))
            {
                return Task.FromResult(new HandlerStatistics
                {
                    MessageType = messageType,
                    ActiveWorkers = 0,
                    TotalProcessed = 0,
                    TotalFailed = 0,
                    AverageExecutionTime = TimeSpan.Zero
                });
            }

            var activeWorkers = this.workerPools.TryGetValue(messageType, out var pool) ? pool.Workers.Count : 0;

            return Task.FromResult(new HandlerStatistics
            {
                MessageType = messageType,
                ActiveWorkers = activeWorkers,
                TotalProcessed = handlerMetrics.TotalProcessed,
                TotalFailed = handlerMetrics.TotalFailed,
                AverageExecutionTime = handlerMetrics.GetAverageExecutionTime()
            });
        }

        private void StartWorkerPool(Type messageType, HandlerRegistration registration)
        {
            var minParallelism = this.GetMinParallelism(registration);

            // Create signal channel for this message type
            var channel = Channel.CreateUnbounded<bool>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false
            });
            this.signalChannels[messageType] = channel;

            // Create metrics tracker
            this.metrics[messageType] = new HandlerMetrics();

            // Create worker pool
            var pool = new WorkerPool
            {
                MessageType = messageType,
                DesiredCount = minParallelism,
                Workers = new List<Task>()
            };

            // Start initial workers
            for (int i = 0; i < minParallelism; i++)
            {
                var worker = this.CreateWorkerTask(messageType, registration, i);
                pool.Workers.Add(worker);
            }

            this.workerPools[messageType] = pool;
        }

        private Task CreateWorkerTask(Type messageType, HandlerRegistration registration, int workerId)
        {
            return Task.Run(async () =>
            {
                await this.WorkerLoop(messageType, registration, workerId);
            });
        }

        private async Task WorkerLoop(Type messageType, HandlerRegistration registration, int workerId)
        {
            var channel = this.signalChannels[messageType];

            while (!this.globalCancellation.IsCancellationRequested)
            {
                try
                {
                    // Check if this worker should terminate (scale down)
                    if (this.workerPools.TryGetValue(messageType, out var pool) && workerId >= pool.DesiredCount)
                    {
                        break;
                    }

                    // Wait for signal with timeout
                    var signalReceived = await channel.Reader.WaitToReadAsync(this.globalCancellation.Token);

                    if (signalReceived)
                    {
                        // Consume signal
                        channel.Reader.TryRead(out _);

                        // Try to checkout a message
                        var envelope = await this.CheckoutMessageAsync(messageType, registration, workerId);

                        if (envelope != null)
                        {
                            await this.ProcessMessageAsync(messageType, envelope, registration);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Shutdown requested
                    break;
                }
                catch (Exception ex)
                {
                    // Log error and continue
                    Console.WriteLine($"Worker {workerId} for {messageType.Name} encountered error: {ex.Message}");
                }
            }
        }

        private async Task<object> CheckoutMessageAsync(Type messageType, HandlerRegistration registration, int workerId)
        {
            var checkoutMethod = typeof(IQueueManager).GetMethod(nameof(IQueueManager.CheckoutAsync));
            var genericMethod = checkoutMethod.MakeGenericMethod(messageType);

            var handlerId = $"worker-{messageType.Name}-{workerId}";
            var leaseDuration = this.GetLeaseDuration(registration);

            var task = (Task)genericMethod.Invoke(this.queueManager, new object[]
            {
                handlerId,
                leaseDuration,
                CancellationToken.None
            });

            await task.ConfigureAwait(false);

            var resultProperty = task.GetType().GetProperty("Result");
            return resultProperty?.GetValue(task);
        }

        private async Task ProcessMessageAsync(
            Type messageType,
            object envelope,
            HandlerRegistration registration)
        {
            if (envelope == null)
                return;

            var metrics = this.metrics[messageType];
            var startTime = DateTime.UtcNow;

            // Get envelope properties using reflection
            var envelopeType = envelope.GetType();
            var messageIdProperty = envelopeType.GetProperty("MessageId");
            var messageProperty = envelopeType.GetProperty("Message");

            var messageId = (Guid)messageIdProperty.GetValue(envelope);
            var message = messageProperty.GetValue(envelope);

            try
            {
                using (var scope = this.scopeFactory.CreateScope())
                {
                    // Create handler instance
                    var handler = this.handlerRegistry.CreateScopedHandler(messageType, scope);

                    // Create timeout cancellation token
                    var timeout = this.GetTimeout(registration);
                    using (var cts = new CancellationTokenSource(timeout))
                    {
                        // Invoke handler
                        var handleMethod = handler.GetType().GetMethod("HandleAsync");
                        var handleTask = (Task)handleMethod.Invoke(handler, new[] { message, cts.Token });

                        await handleTask.ConfigureAwait(false);
                    }

                    // Acknowledge successful processing
                    await this.queueManager.AcknowledgeAsync(messageId);

                    // Update metrics
                    metrics.RecordSuccess(DateTime.UtcNow - startTime);
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout - requeue message
                await this.queueManager.RequeueAsync(messageId);
                metrics.RecordFailure();
            }
            catch (Exception ex)
            {
                // Handler error - requeue message
                Console.WriteLine($"Handler error for {messageType.Name}: {ex.Message}");
                await this.queueManager.RequeueAsync(messageId);
                metrics.RecordFailure();
            }
        }

        private int GetMinParallelism(HandlerRegistration registration)
        {
            var optionsType = registration.Options.GetType();
            var property = optionsType.GetProperty("MinParallelism");
            return (int)property.GetValue(registration.Options);
        }

        private int GetMaxParallelism(HandlerRegistration registration)
        {
            var optionsType = registration.Options.GetType();
            var property = optionsType.GetProperty("MaxParallelism");
            return (int)property.GetValue(registration.Options);
        }

        private TimeSpan GetLeaseDuration(HandlerRegistration registration)
        {
            var optionsType = registration.Options.GetType();
            var property = optionsType.GetProperty("LeaseDuration");
            return (TimeSpan)property.GetValue(registration.Options);
        }

        private TimeSpan GetTimeout(HandlerRegistration registration)
        {
            var optionsType = registration.Options.GetType();
            var property = optionsType.GetProperty("Timeout");
            return (TimeSpan)property.GetValue(registration.Options);
        }

        private class WorkerPool
        {
            public Type MessageType { get; set; }
            public int DesiredCount { get; set; }
            public List<Task> Workers { get; set; }
        }

        private class HandlerMetrics
        {
            private long totalProcessed;
            private long totalFailed;
            private readonly ConcurrentBag<TimeSpan> executionTimes = new ConcurrentBag<TimeSpan>();

            public int TotalProcessed => (int)Interlocked.Read(ref this.totalProcessed);
            public int TotalFailed => (int)Interlocked.Read(ref this.totalFailed);

            public void RecordSuccess(TimeSpan executionTime)
            {
                Interlocked.Increment(ref this.totalProcessed);
                this.executionTimes.Add(executionTime);

                // Keep only last 1000 samples
                while (this.executionTimes.Count > 1000)
                {
                    this.executionTimes.TryTake(out _);
                }
            }

            public void RecordFailure()
            {
                Interlocked.Increment(ref this.totalFailed);
            }

            public TimeSpan GetAverageExecutionTime()
            {
                if (this.executionTimes.IsEmpty)
                    return TimeSpan.Zero;

                var average = this.executionTimes.Average(t => t.TotalMilliseconds);
                return TimeSpan.FromMilliseconds(average);
            }
        }
    }
}
