// -----------------------------------------------------------------------
// <copyright file="QueueAdminApi.cs" company="Microsoft Corp.">
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

    /// <summary>
    /// Administrative API for queue management and monitoring.
    /// </summary>
    public class QueueAdminApi : IQueueAdminApi
    {
        private readonly IQueueManager queueManager;
        private readonly IHandlerDispatcher handlerDispatcher;
        private readonly IDeadLetterQueue deadLetterQueue;
        private readonly IPersister persister;

        /// <summary>
        /// Initializes a new instance of the QueueAdminApi class.
        /// </summary>
        /// <param name="queueManager">Queue manager instance.</param>
        /// <param name="handlerDispatcher">Handler dispatcher instance.</param>
        /// <param name="deadLetterQueue">Dead-letter queue instance.</param>
        /// <param name="persister">Persistence layer instance.</param>
        public QueueAdminApi(
            IQueueManager queueManager,
            IHandlerDispatcher handlerDispatcher,
            IDeadLetterQueue deadLetterQueue = null,
            IPersister persister = null)
        {
            this.queueManager = queueManager ?? throw new ArgumentNullException(nameof(queueManager));
            this.handlerDispatcher = handlerDispatcher ?? throw new ArgumentNullException(nameof(handlerDispatcher));
            this.deadLetterQueue = deadLetterQueue;
            this.persister = persister;
        }

        /// <inheritdoc/>
        public async Task ScaleHandlerAsync<TMessage>(int instanceCount, CancellationToken cancellationToken = default)
        {
            if (instanceCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(instanceCount), "Instance count cannot be negative.");
            }

            var messageType = typeof(TMessage);
            await this.handlerDispatcher.ScaleHandlerAsync(messageType, instanceCount, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task TriggerSnapshotAsync(CancellationToken cancellationToken = default)
        {
            if (this.persister == null)
            {
                throw new InvalidOperationException("Persistence is not configured.");
            }

            await this.queueManager.CheckAndCreateSnapshotAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<QueueMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
        {
            return await this.queueManager.GetMetricsAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<Dictionary<string, HandlerMetricsSnapshot>> GetHandlerMetricsAsync(CancellationToken cancellationToken = default)
        {
            return await this.handlerDispatcher.GetMetricsAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public async Task ReplayDeadLetterAsync(Guid messageId, bool resetRetryCount = true, CancellationToken cancellationToken = default)
        {
            if (this.deadLetterQueue == null)
            {
                throw new InvalidOperationException("Dead-letter queue is not configured.");
            }

            await this.deadLetterQueue.ReplayAsync(messageId, resetRetryCount, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task PurgeDeadLetterQueueAsync(TimeSpan? olderThan = null, CancellationToken cancellationToken = default)
        {
            if (this.deadLetterQueue == null)
            {
                throw new InvalidOperationException("Dead-letter queue is not configured.");
            }

            await this.deadLetterQueue.PurgeAsync(olderThan ?? default(TimeSpan), cancellationToken);
        }
    }
}
