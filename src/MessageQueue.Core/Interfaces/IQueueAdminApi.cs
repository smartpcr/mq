// -----------------------------------------------------------------------
// <copyright file="IQueueAdminApi.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Administrative API for queue management and monitoring.
    /// </summary>
    public interface IQueueAdminApi
    {
        /// <summary>
        /// Scales handler instances for a specific message type.
        /// </summary>
        /// <typeparam name="TMessage">Message type to scale.</typeparam>
        /// <param name="instanceCount">Desired number of handler instances.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task ScaleHandlerAsync<TMessage>(int instanceCount, CancellationToken cancellationToken = default);

        /// <summary>
        /// Triggers a manual snapshot of the queue state.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task TriggerSnapshotAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets current queue metrics.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Queue metrics including counts, latency, and throughput.</returns>
        Task<QueueMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets detailed handler metrics.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Dictionary of message type to handler metrics.</returns>
        Task<Dictionary<string, HandlerMetricsSnapshot>> GetHandlerMetricsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Replays a message from the dead-letter queue.
        /// </summary>
        /// <param name="messageId">Message ID to replay.</param>
        /// <param name="resetRetryCount">Whether to reset the retry count.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task ReplayDeadLetterAsync(Guid messageId, bool resetRetryCount = true, CancellationToken cancellationToken = default);

        /// <summary>
        /// Purges the dead-letter queue.
        /// </summary>
        /// <param name="olderThan">Optional: only purge messages older than this timespan.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task PurgeDeadLetterQueueAsync(TimeSpan? olderThan = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Snapshot of handler metrics for monitoring.
    /// </summary>
    public class HandlerMetricsSnapshot
    {
        /// <summary>
        /// Gets or sets the message type name.
        /// </summary>
        public string MessageType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current number of active workers.
        /// </summary>
        public int ActiveWorkers { get; set; }

        /// <summary>
        /// Gets or sets the total number of messages processed.
        /// </summary>
        public long TotalProcessed { get; set; }

        /// <summary>
        /// Gets or sets the total number of failed messages.
        /// </summary>
        public long TotalFailed { get; set; }

        /// <summary>
        /// Gets or sets the average processing time in milliseconds.
        /// </summary>
        public double AverageProcessingTimeMs { get; set; }

        /// <summary>
        /// Gets or sets the messages processed per second (throughput).
        /// </summary>
        public double MessagesPerSecond { get; set; }
    }
}
