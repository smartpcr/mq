// -----------------------------------------------------------------------
// <copyright file="DeadLetterQueue.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using MessageQueue.Core.Enums;
    using MessageQueue.Core.Interfaces;
    using MessageQueue.Core.Models;
    using MessageQueue.Core.Options;

    /// <summary>
    /// Dead-letter queue implementation with circular buffer and failure metadata tracking.
    /// </summary>
    public class DeadLetterQueue : IDeadLetterQueue
    {
        private readonly ConcurrentQueue<DeadLetterEnvelope> deadLetterMessages;
        private readonly IQueueManager queueManager;
        private readonly IPersister persister;
        private readonly QueueOptions options;
        private readonly object lockObject = new object();
        private int count;

        /// <summary>
        /// Initializes a new instance of the DeadLetterQueue.
        /// </summary>
        /// <param name="queueManager">Main queue manager for replay operations.</param>
        /// <param name="options">Queue configuration options.</param>
        /// <param name="persister">Optional persister for DLQ persistence.</param>
        public DeadLetterQueue(
            IQueueManager queueManager,
            QueueOptions options,
            IPersister persister = null)
        {
            this.queueManager = queueManager ?? throw new ArgumentNullException(nameof(queueManager));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.persister = persister;
            this.deadLetterMessages = new ConcurrentQueue<DeadLetterEnvelope>();
            this.count = 0;
        }

        /// <inheritdoc/>
        public async Task AddAsync(
            MessageEnvelope envelope,
            string failureReason,
            Exception exception = null,
            CancellationToken cancellationToken = default)
        {
            if (envelope == null)
                throw new ArgumentNullException(nameof(envelope));

            if (string.IsNullOrWhiteSpace(failureReason))
                throw new ArgumentException("Failure reason cannot be null or empty.", nameof(failureReason));

            var deadLetterEnvelope = DeadLetterEnvelope.FromMessageEnvelope(envelope, failureReason, exception);

            this.deadLetterMessages.Enqueue(deadLetterEnvelope);
            Interlocked.Increment(ref this.count);

            // Persist to DLQ if persister is available
            if (this.persister != null)
            {
                var record = new OperationRecord
                {
                    SequenceNumber = 0, // Will be set by persister
                    OperationCode = OperationCode.DeadLetter,
                    MessageId = deadLetterEnvelope.MessageId,
                    Payload = System.Text.Json.JsonSerializer.Serialize(deadLetterEnvelope),
                    Timestamp = DateTime.UtcNow,
                    Checksum = 0
                };

                await this.persister.WriteOperationAsync(record, cancellationToken);
            }

            await Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<IEnumerable<DeadLetterEnvelope>> GetMessagesAsync(
            Type messageType = null,
            int limit = 100,
            CancellationToken cancellationToken = default)
        {
            var messages = this.deadLetterMessages.ToArray();

            if (messageType != null)
            {
                var messageTypeName = messageType.FullName ?? messageType.Name;
                messages = messages.Where(m => m.MessageType == messageTypeName).ToArray();
            }

            var result = messages.Take(limit);
            return Task.FromResult(result.AsEnumerable());
        }

        /// <inheritdoc/>
        public async Task ReplayAsync(
            Guid messageId,
            bool resetRetryCount = true,
            CancellationToken cancellationToken = default)
        {
            // Find the message in DLQ
            var messages = this.deadLetterMessages.ToArray();
            var dlqMessage = messages.FirstOrDefault(m => m.MessageId == messageId);

            if (dlqMessage == null)
                throw new InvalidOperationException($"Message {messageId} not found in dead-letter queue.");

            // Create a new message envelope for replay
            var replayEnvelope = new MessageEnvelope
            {
                MessageId = resetRetryCount ? Guid.NewGuid() : dlqMessage.MessageId,
                MessageType = dlqMessage.MessageType,
                Payload = dlqMessage.Payload,
                DeduplicationKey = resetRetryCount ? null : dlqMessage.DeduplicationKey,
                Status = MessageStatus.Ready,
                RetryCount = resetRetryCount ? 0 : dlqMessage.RetryCount,
                MaxRetries = dlqMessage.MaxRetries,
                Lease = null,
                LastPersistedVersion = 0,
                Metadata = dlqMessage.Metadata,
                EnqueuedAt = DateTime.UtcNow,
                IsSuperseded = false
            };

            // Re-enqueue to main queue using generic method via reflection
            var enqueueMethod = typeof(IQueueManager).GetMethod(nameof(IQueueManager.EnqueueAsync));
            var messageType = Type.GetType(dlqMessage.MessageType);
            if (messageType == null)
                throw new InvalidOperationException($"Could not resolve message type: {dlqMessage.MessageType}");

            var genericMethod = enqueueMethod.MakeGenericMethod(messageType);
            var message = System.Text.Json.JsonSerializer.Deserialize(dlqMessage.Payload, messageType);

            var task = (Task<Guid>)genericMethod.Invoke(
                this.queueManager,
                new object[]
                {
                    message,
                    replayEnvelope.DeduplicationKey,
                    null, // correlationId
                    cancellationToken
                });

            await task.ConfigureAwait(false);

            // Remove from DLQ (rebuild queue without this message)
            this.RemoveMessage(messageId);

            // Persist replay operation
            if (this.persister != null)
            {
                var record = new OperationRecord
                {
                    SequenceNumber = 0,
                    OperationCode = OperationCode.DeadLetterReplay,
                    MessageId = messageId,
                    Payload = System.Text.Json.JsonSerializer.Serialize(replayEnvelope),
                    Timestamp = DateTime.UtcNow,
                    Checksum = 0
                };

                await this.persister.WriteOperationAsync(record, cancellationToken);
            }
        }

        /// <inheritdoc/>
        public async Task PurgeAsync(
            TimeSpan olderThan = default(TimeSpan),
            CancellationToken cancellationToken = default)
        {
            if (olderThan == default(TimeSpan))
            {
                // Purge all
                while (this.deadLetterMessages.TryDequeue(out _))
                {
                    Interlocked.Decrement(ref this.count);
                }
            }
            else
            {
                // Purge messages older than specified timespan
                var cutoffTime = DateTime.UtcNow - olderThan;
                var messages = this.deadLetterMessages.ToArray();
                var toKeep = messages.Where(m => m.FailureTimestamp > cutoffTime).ToList();

                // Rebuild queue
                lock (this.lockObject)
                {
                    while (this.deadLetterMessages.TryDequeue(out _))
                    {
                        Interlocked.Decrement(ref this.count);
                    }

                    foreach (var message in toKeep)
                    {
                        this.deadLetterMessages.Enqueue(message);
                        Interlocked.Increment(ref this.count);
                    }
                }
            }

            // Persist purge operation
            if (this.persister != null)
            {
                var record = new OperationRecord
                {
                    SequenceNumber = 0,
                    OperationCode = OperationCode.DeadLetterPurge,
                    MessageId = Guid.Empty,
                    Payload = System.Text.Json.JsonSerializer.Serialize(new { OlderThan = olderThan }),
                    Timestamp = DateTime.UtcNow,
                    Checksum = 0
                };

                await this.persister.WriteOperationAsync(record, cancellationToken);
            }
        }

        /// <inheritdoc/>
        public Task<DeadLetterQueueMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
        {
            var messages = this.deadLetterMessages.ToArray();

            var metrics = new DeadLetterQueueMetrics
            {
                TotalCount = this.count,
                OldestMessageTime = messages.Any() ? messages.Min(m => m.FailureTimestamp) : (DateTime?)null,
                CountByMessageType = messages.GroupBy(m => m.MessageType)
                    .ToDictionary(g => g.Key, g => g.Count()),
                CountByFailureReason = messages.GroupBy(m => m.FailureReason)
                    .ToDictionary(g => g.Key, g => g.Count())
            };

            return Task.FromResult(metrics);
        }

        private void RemoveMessage(Guid messageId)
        {
            lock (this.lockObject)
            {
                var messages = this.deadLetterMessages.ToArray();
                var toKeep = messages.Where(m => m.MessageId != messageId).ToList();

                while (this.deadLetterMessages.TryDequeue(out _))
                {
                    Interlocked.Decrement(ref this.count);
                }

                foreach (var message in toKeep)
                {
                    this.deadLetterMessages.Enqueue(message);
                    Interlocked.Increment(ref this.count);
                }
            }
        }
    }
}
