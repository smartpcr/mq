// -----------------------------------------------------------------------
// <copyright file="QueuePublisher.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using MessageQueue.Core.Interfaces;

    /// <summary>
    /// Publisher implementation for handlers to enqueue additional messages.
    /// Enables message chaining and workflow orchestration.
    /// </summary>
    public class QueuePublisher : IQueuePublisher
    {
        private readonly IQueueManager queueManager;

        /// <summary>
        /// Initializes a new instance of the QueuePublisher class.
        /// </summary>
        /// <param name="queueManager">Queue manager for message operations.</param>
        public QueuePublisher(IQueueManager queueManager)
        {
            this.queueManager = queueManager ?? throw new ArgumentNullException(nameof(queueManager));
        }

        /// <inheritdoc/>
        public async Task<Guid> EnqueueAsync<T>(
            T message,
            string deduplicationKey = null,
            string correlationId = null,
            CancellationToken cancellationToken = default)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            return await this.queueManager.EnqueueAsync(
                message,
                deduplicationKey,
                correlationId,
                cancellationToken);
        }
    }
}
