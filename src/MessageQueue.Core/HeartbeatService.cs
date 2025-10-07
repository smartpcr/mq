// -----------------------------------------------------------------------
// <copyright file="HeartbeatService.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;
    using MessageQueue.Core.Interfaces;
    using MessageQueue.Core.Options;

    /// <summary>
    /// Service for tracking handler progress through heartbeats.
    /// Automatically extends lease when heartbeat is received.
    /// </summary>
    public class HeartbeatService : IHeartbeatService
    {
        private readonly IQueueManager queueManager;
        private readonly ILeaseMonitor leaseMonitor;
        private readonly QueueOptions options;
        private readonly ConcurrentDictionary<Guid, HeartbeatProgress> heartbeats;
        private readonly TimeSpan leaseExtensionDuration;

        /// <summary>
        /// Initializes a new instance of the HeartbeatService class.
        /// </summary>
        /// <param name="queueManager">Queue manager for lease operations.</param>
        /// <param name="leaseMonitor">Lease monitor for extending leases.</param>
        /// <param name="options">Queue configuration options.</param>
        public HeartbeatService(
            IQueueManager queueManager,
            ILeaseMonitor leaseMonitor,
            QueueOptions options)
        {
            this.queueManager = queueManager ?? throw new ArgumentNullException(nameof(queueManager));
            this.leaseMonitor = leaseMonitor ?? throw new ArgumentNullException(nameof(leaseMonitor));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.heartbeats = new ConcurrentDictionary<Guid, HeartbeatProgress>();
            this.leaseExtensionDuration = options.DefaultTimeout;
        }

        /// <inheritdoc/>
        public async Task HeartbeatAsync(
            Guid messageId,
            int? progressPercentage = null,
            string progressMessage = null,
            CancellationToken cancellationToken = default)
        {
            if (progressPercentage.HasValue && (progressPercentage.Value < 0 || progressPercentage.Value > 100))
            {
                throw new ArgumentOutOfRangeException(nameof(progressPercentage), "Progress percentage must be between 0 and 100.");
            }

            // Update or create heartbeat record
            var progress = this.heartbeats.AddOrUpdate(
                messageId,
                _ => new HeartbeatProgress
                {
                    MessageId = messageId,
                    LastHeartbeat = DateTime.UtcNow,
                    ProgressPercentage = progressPercentage,
                    ProgressMessage = progressMessage,
                    HeartbeatCount = 1
                },
                (_, existing) =>
                {
                    existing.LastHeartbeat = DateTime.UtcNow;
                    existing.ProgressPercentage = progressPercentage ?? existing.ProgressPercentage;
                    existing.ProgressMessage = progressMessage ?? existing.ProgressMessage;
                    existing.HeartbeatCount++;
                    return existing;
                });

            // Extend the lease to prevent timeout
            try
            {
                await this.leaseMonitor.ExtendLeaseAsync(messageId, this.leaseExtensionDuration, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Message may have been completed or is no longer active - remove from tracking
                this.heartbeats.TryRemove(messageId, out _);
                throw;
            }
        }

        /// <inheritdoc/>
        public Task<DateTime?> GetLastHeartbeatAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            if (this.heartbeats.TryGetValue(messageId, out var progress))
            {
                return Task.FromResult<DateTime?>(progress.LastHeartbeat);
            }

            return Task.FromResult<DateTime?>(null);
        }

        /// <inheritdoc/>
        public Task<HeartbeatProgress?> GetProgressAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            if (this.heartbeats.TryGetValue(messageId, out var progress))
            {
                // Return a copy to prevent external modification
                return Task.FromResult<HeartbeatProgress?>(new HeartbeatProgress
                {
                    MessageId = progress.MessageId,
                    LastHeartbeat = progress.LastHeartbeat,
                    ProgressPercentage = progress.ProgressPercentage,
                    ProgressMessage = progress.ProgressMessage,
                    HeartbeatCount = progress.HeartbeatCount
                });
            }

            return Task.FromResult<HeartbeatProgress?>(null);
        }

        /// <summary>
        /// Removes heartbeat tracking for a completed message.
        /// </summary>
        /// <param name="messageId">Message ID.</param>
        public void RemoveHeartbeat(Guid messageId)
        {
            this.heartbeats.TryRemove(messageId, out _);
        }
    }
}
