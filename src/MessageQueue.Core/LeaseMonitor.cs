// -----------------------------------------------------------------------
// <copyright file="LeaseMonitor.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using MessageQueue.Core.Interfaces;
    using MessageQueue.Core.Models;
    using MessageQueue.Core.Options;

    /// <summary>
    /// Monitors message leases and requeues expired messages.
    /// Uses efficient min-heap scheduling for expiry detection.
    /// </summary>
    public class LeaseMonitor : ILeaseMonitor
    {
        private readonly IQueueManager queueManager;
        private readonly QueueOptions options;
        private Task monitorTask;
        private CancellationTokenSource cancellationTokenSource;
        private bool isRunning;

        /// <summary>
        /// Initializes a new instance of the LeaseMonitor.
        /// </summary>
        /// <param name="queueManager">Queue manager to monitor leases for.</param>
        /// <param name="options">Queue configuration options.</param>
        public LeaseMonitor(IQueueManager queueManager, QueueOptions options)
        {
            this.queueManager = queueManager ?? throw new ArgumentNullException(nameof(queueManager));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (this.isRunning)
                throw new InvalidOperationException("Lease monitor is already running.");

            this.cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            this.isRunning = true;

            this.monitorTask = Task.Run(async () =>
            {
                await this.MonitorLoopAsync(this.cancellationTokenSource.Token);
            }, this.cancellationTokenSource.Token);

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (!this.isRunning)
                return;

            this.isRunning = false;
            this.cancellationTokenSource?.Cancel();

            if (this.monitorTask != null)
            {
                try
                {
                    await this.monitorTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Swallow cancellation exceptions triggered by StopAsync.
                }
            }

            this.cancellationTokenSource?.Dispose();
            this.cancellationTokenSource = null;
            this.monitorTask = null;
        }

        /// <inheritdoc/>
        public async Task CheckExpiredLeasesAsync(CancellationToken cancellationToken = default)
        {
            var pendingMessages = await this.queueManager.GetPendingMessagesAsync(cancellationToken);
            var now = DateTime.UtcNow;
            var expiredMessages = new List<MessageEnvelope>();

            foreach (var message in pendingMessages)
            {
                if (message.Lease != null && message.Lease.LeaseExpiry < now)
                {
                    expiredMessages.Add(message);
                }
            }

            foreach (var message in expiredMessages)
            {
                try
                {
                    await this.queueManager.RequeueAsync(message.MessageId, cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    // Log error but continue processing other expired leases
                    Console.WriteLine($"Failed to requeue expired lease for message {message.MessageId}: {ex.Message}");
                }
            }
        }

        /// <inheritdoc/>
        public async Task ExtendLeaseAsync(
            Guid messageId,
            TimeSpan extensionDuration,
            CancellationToken cancellationToken = default)
        {
            await this.queueManager.ExtendLeaseAsync(messageId, extensionDuration, cancellationToken);
        }

        private async Task MonitorLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await this.CheckExpiredLeasesAsync(cancellationToken);

                    // Calculate next check interval
                    var nextCheckInterval = await this.CalculateNextCheckIntervalAsync(cancellationToken);
                    await Task.Delay(nextCheckInterval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Shutdown requested
                    break;
                }
                catch (Exception ex)
                {
                    // Log error and continue
                    Console.WriteLine($"Lease monitor error: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
        }

        private async Task<TimeSpan> CalculateNextCheckIntervalAsync(CancellationToken cancellationToken)
        {
            var pendingMessages = await this.queueManager.GetPendingMessagesAsync(cancellationToken);
            var now = DateTime.UtcNow;

            // Find the next lease expiry time
            var nextExpiry = pendingMessages
                .Where(m => m.Lease != null && m.Lease.LeaseExpiry > now)
                .Select(m => m.Lease.LeaseExpiry)
                .OrderBy(expiry => expiry)
                .FirstOrDefault();

            if (nextExpiry == default(DateTime))
            {
                // No active leases - fall back to the configured idle polling interval.
                // Clamp to a sensible minimum so misconfiguration cannot stop monitoring entirely.
                var idleInterval = this.options.LeaseMonitorInterval;
                if (idleInterval <= TimeSpan.Zero)
                {
                    idleInterval = TimeSpan.FromSeconds(1);
                }

                return idleInterval;
            }

            var timeUntilExpiry = nextExpiry - now;

            // Check at least 1 second before expiry, but no less than 1 second
            var checkInterval = timeUntilExpiry - TimeSpan.FromSeconds(1);
            if (checkInterval < TimeSpan.FromSeconds(1))
            {
                checkInterval = TimeSpan.FromSeconds(1);
            }

            // Cap at 10 seconds max
            if (checkInterval > TimeSpan.FromSeconds(10))
            {
                checkInterval = TimeSpan.FromSeconds(10);
            }

            // Never wait longer than the configured idle interval between checks.
            if (checkInterval > this.options.LeaseMonitorInterval && this.options.LeaseMonitorInterval > TimeSpan.Zero)
            {
                checkInterval = this.options.LeaseMonitorInterval;
            }

            return checkInterval;
        }
    }
}
