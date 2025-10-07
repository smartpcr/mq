// -----------------------------------------------------------------------
// <copyright file="QueueManager.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MessageQueue.Core.Enums;
using MessageQueue.Core.Interfaces;
using MessageQueue.Core.Models;
using MessageQueue.Core.Options;

/// <summary>
/// Main queue manager that coordinates circular buffer, deduplication, and persistence.
/// </summary>
public class QueueManager : IQueueManager
{
    private readonly ICircularBuffer buffer;
    private readonly DeduplicationIndex deduplicationIndex;
    private readonly IPersister? persister;
    private readonly IDeadLetterQueue? deadLetterQueue;
    private readonly QueueOptions options;
    private long sequenceNumber;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, string> messageIdToDeduplicationKey;

    /// <summary>
    /// Initializes a new instance of the QueueManager.
    /// </summary>
    /// <param name="buffer">The circular buffer implementation.</param>
    /// <param name="deduplicationIndex">The deduplication index.</param>
    /// <param name="options">Queue configuration options.</param>
    /// <param name="persister">Optional persister for WAL and snapshots.</param>
    /// <param name="deadLetterQueue">Optional dead-letter queue for failed messages.</param>
    public QueueManager(
        ICircularBuffer buffer,
        DeduplicationIndex deduplicationIndex,
        QueueOptions options,
        IPersister persister = null,
        IDeadLetterQueue deadLetterQueue = null)
    {
        this.buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        this.deduplicationIndex = deduplicationIndex ?? throw new ArgumentNullException(nameof(deduplicationIndex));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.persister = persister;
        this.deadLetterQueue = deadLetterQueue;
        this.sequenceNumber = 0;
        this.messageIdToDeduplicationKey = new System.Collections.Concurrent.ConcurrentDictionary<Guid, string>();
    }

    /// <inheritdoc/>
    public async Task<Guid> EnqueueAsync<T>(
        T message,
        string deduplicationKey = null,
        string correlationId = null,
        CancellationToken cancellationToken = default)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        // Check for deduplication
        if (!string.IsNullOrWhiteSpace(deduplicationKey))
        {
            var existingMessageId = await this.deduplicationIndex.TryGetAsync(deduplicationKey, cancellationToken);
            if (existingMessageId.HasValue)
            {
                // Supersede existing in-flight message
                var envelope = this.CreateEnvelope(message, deduplicationKey, correlationId);
                bool replaced = await this.buffer.ReplaceAsync(envelope, deduplicationKey, cancellationToken);

                if (replaced)
                {
                    // Update deduplication index and reverse mapping
                    await this.deduplicationIndex.UpdateAsync(deduplicationKey, envelope.MessageId, cancellationToken);
                    this.messageIdToDeduplicationKey.TryRemove(existingMessageId.Value, out _);
                    this.messageIdToDeduplicationKey.TryAdd(envelope.MessageId, deduplicationKey);

                    // Persist replace operation
                    if (this.persister != null)
                    {
                        await this.PersistOperationAsync(OperationCode.Replace, envelope, cancellationToken);
                    }

                    return envelope.MessageId;
                }
                else
                {
                    // Existing message was not in-flight, return existing ID
                    return existingMessageId.Value;
                }
            }
        }

        // No deduplication or new message
        var newEnvelope = this.CreateEnvelope(message, deduplicationKey, correlationId);
        await this.buffer.EnqueueAsync(newEnvelope, cancellationToken);

        // Add to deduplication index and track reverse mapping
        if (!string.IsNullOrWhiteSpace(deduplicationKey))
        {
            await this.deduplicationIndex.TryAddAsync(deduplicationKey, newEnvelope.MessageId, cancellationToken);
            this.messageIdToDeduplicationKey.TryAdd(newEnvelope.MessageId, deduplicationKey);
        }

        // Persist enqueue operation
        if (this.persister != null)
        {
            await this.PersistOperationAsync(OperationCode.Enqueue, newEnvelope, cancellationToken);
        }

        return newEnvelope.MessageId;
    }

    /// <inheritdoc/>
    public async Task<MessageEnvelope<T>> CheckoutAsync<T>(
        string handlerId,
        TimeSpan leaseDuration = default(TimeSpan),
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(handlerId))
            throw new ArgumentException("Handler ID cannot be null or whitespace.", nameof(handlerId));

        var lease = leaseDuration == default(TimeSpan) ? this.options.DefaultTimeout : leaseDuration;
        var messageType = typeof(T);

        var envelope = await this.buffer.CheckoutAsync(messageType, handlerId, lease, cancellationToken);

        if (envelope == null)
            return null;

        // Persist checkout operation
        if (this.persister != null)
        {
            await this.PersistOperationAsync(OperationCode.Checkout, envelope, cancellationToken);
        }

        // Deserialize payload
        var message = JsonSerializer.Deserialize<T>(envelope.Payload);

        return new MessageEnvelope<T>
        {
            MessageId = envelope.MessageId,
            MessageType = envelope.MessageType,
            Message = message!,
            DeduplicationKey = envelope.DeduplicationKey,
            Status = envelope.Status,
            RetryCount = envelope.RetryCount,
            MaxRetries = envelope.MaxRetries,
            Lease = envelope.Lease,
            Metadata = envelope.Metadata,
            EnqueuedAt = envelope.EnqueuedAt
        };
    }

    /// <inheritdoc/>
    public async Task AcknowledgeAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        bool acknowledged = await this.buffer.AcknowledgeAsync(messageId, cancellationToken);

        if (!acknowledged)
            throw new InvalidOperationException($"Message {messageId} not found or already completed.");

        // Persist acknowledge operation
        if (this.persister != null)
        {
            var record = new OperationRecord
            {
                SequenceNumber = Interlocked.Increment(ref this.sequenceNumber),
                OperationCode = OperationCode.Acknowledge,
                MessageId = messageId,
                Payload = "{}",
                Timestamp = DateTime.UtcNow,
                Checksum = 0
            };

            await this.persister.WriteOperationAsync(record, cancellationToken);
        }

        // Remove from deduplication index if it has a dedup key
        if (this.messageIdToDeduplicationKey.TryRemove(messageId, out string? dedupKey))
        {
            await this.deduplicationIndex.RemoveAsync(dedupKey, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task RequeueAsync(Guid messageId, Exception exception = null, CancellationToken cancellationToken = default)
    {
        // Get all messages to find the one to requeue
        var allMessages = await this.buffer.GetAllMessagesAsync(cancellationToken);
        MessageEnvelope target = null;

        foreach (var msg in allMessages)
        {
            if (msg.MessageId == messageId)
            {
                target = msg;
                break;
            }
        }

        if (target == null)
            throw new InvalidOperationException($"Message {messageId} not found.");

        // Check retry count
        if (target.RetryCount >= target.MaxRetries)
        {
            // Move to dead letter queue
            if (this.deadLetterQueue != null)
            {
                var failureReason = exception != null
                    ? $"Max retries exceeded: {exception.Message}"
                    : "Max retries exceeded";

                await this.deadLetterQueue.AddAsync(target, failureReason, exception, cancellationToken);

                // Remove from main queue by acknowledging
                await this.buffer.AcknowledgeAsync(messageId, cancellationToken);

                // Remove from deduplication index
                if (this.messageIdToDeduplicationKey.TryRemove(messageId, out string? dedupKey))
                {
                    await this.deduplicationIndex.RemoveAsync(dedupKey, cancellationToken);
                }

                return;
            }
            else
            {
                throw new InvalidOperationException($"Message {messageId} has exceeded max retries.");
            }
        }

        // Calculate backoff delay
        var newRetryCount = target.RetryCount + 1;
        var notBefore = this.CalculateBackoffTime(newRetryCount);

        // Remove and update the message with backoff
        await this.buffer.RemoveAsync(messageId, cancellationToken);

        // Create updated envelope with incremented retry count and backoff
        var requeuedEnvelope = new MessageEnvelope
        {
            MessageId = target.MessageId,
            MessageType = target.MessageType,
            Payload = target.Payload,
            DeduplicationKey = target.DeduplicationKey,
            Status = MessageStatus.Ready,
            RetryCount = newRetryCount,
            MaxRetries = target.MaxRetries,
            Lease = null,
            LastPersistedVersion = target.LastPersistedVersion,
            Metadata = target.Metadata,
            EnqueuedAt = target.EnqueuedAt,
            IsSuperseded = target.IsSuperseded,
            NotBefore = notBefore
        };

        await this.buffer.EnqueueAsync(requeuedEnvelope, cancellationToken);

        // Persist requeue operation
        if (this.persister != null)
        {
            await this.PersistOperationAsync(OperationCode.Requeue, requeuedEnvelope, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task ExtendLeaseAsync(Guid messageId, TimeSpan extensionDuration, CancellationToken cancellationToken = default)
    {
        var allMessages = await this.buffer.GetAllMessagesAsync(cancellationToken);

        foreach (var msg in allMessages)
        {
            if (msg.MessageId == messageId && msg.Lease != null)
            {
                msg.Lease.LeaseExpiry = msg.Lease.LeaseExpiry.Add(extensionDuration);
                msg.Lease.ExtensionCount++;

                // Persist lease extension
                if (this.persister != null)
                {
                    await this.PersistOperationAsync(OperationCode.LeaseRenew, msg, cancellationToken);
                }

                return;
            }
        }

        throw new InvalidOperationException($"Message {messageId} not found or has no active lease.");
    }

    /// <inheritdoc/>
    public async Task<QueueMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var allMessages = await this.buffer.GetAllMessagesAsync(cancellationToken);

        var metrics = new QueueMetrics
        {
            TotalCapacity = this.buffer.Capacity,
            ReadyCount = allMessages.Count(m => m.Status == MessageStatus.Ready),
            InFlightCount = allMessages.Count(m => m.Status == MessageStatus.InFlight),
            CompletedCount = 0, // Completed messages are removed from buffer
            DeadLetterCount = 0 // Will be tracked separately in Phase 5
        };

        return metrics;
    }

    /// <inheritdoc/>
    public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return this.buffer.GetCountAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<MessageEnvelope>> GetPendingMessagesAsync(CancellationToken cancellationToken = default)
    {
        var allMessages = await this.buffer.GetAllMessagesAsync(cancellationToken);
        var pending = new List<MessageEnvelope>();

        foreach (var msg in allMessages)
        {
            if (msg.Status == MessageStatus.Ready || msg.Status == MessageStatus.InFlight)
            {
                pending.Add(msg);
            }
        }

        return pending;
    }

    /// <inheritdoc/>
    public async Task<MessageEnvelope?> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var allMessages = await this.buffer.GetAllMessagesAsync(cancellationToken);

        foreach (var msg in allMessages)
        {
            if (msg.MessageId == messageId)
            {
                return msg;
            }
        }

        return null;
    }

    /// <summary>
    /// Creates a snapshot of the current queue state for persistence.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Queue snapshot containing all state.</returns>
    public async Task<QueueSnapshot> CreateSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var allMessages = await this.buffer.GetAllMessagesAsync(cancellationToken);
        var deduplicationSnapshot = await this.deduplicationIndex.GetSnapshotAsync(cancellationToken);

        var snapshot = new QueueSnapshot
        {
            Version = Interlocked.Read(ref this.sequenceNumber),
            CreatedAt = DateTime.UtcNow,
            Capacity = this.buffer.Capacity,
            MessageCount = allMessages.Length,
            Messages = allMessages.ToList(),
            DeduplicationIndex = deduplicationSnapshot,
            DeadLetterMessages = new List<DeadLetterEnvelope>() // Phase 5
        };

        return snapshot;
    }

    /// <summary>
    /// Triggers a snapshot if persistence conditions are met.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CheckAndCreateSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (this.persister != null && this.persister.ShouldSnapshot())
        {
            var snapshot = await this.CreateSnapshotAsync(cancellationToken);
            await this.persister.CreateSnapshotAsync(snapshot, cancellationToken);

            // Truncate journal after successful snapshot
            await this.persister.TruncateJournalAsync(snapshot.Version, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public void SetSequenceNumber(long sequenceNumber)
    {
        Interlocked.Exchange(ref this.sequenceNumber, sequenceNumber);
    }

    /// <summary>
    /// Creates a message envelope from a message object.
    /// </summary>
    private MessageEnvelope CreateEnvelope<T>(T message, string deduplicationKey, string correlationId)
    {
        var payload = JsonSerializer.Serialize(message);
        var messageType = typeof(T).FullName ?? typeof(T).Name;

        return new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = messageType,
            Payload = payload,
            DeduplicationKey = deduplicationKey,
            Status = MessageStatus.Ready,
            RetryCount = 0,
            MaxRetries = this.options.DefaultMaxRetries,
            Lease = null,
            LastPersistedVersion = 0,
            Metadata = new MessageMetadata
            {
                CorrelationId = correlationId,
                Headers = new Dictionary<string, string>(),
                Source = "QueueManager",
                Version = 1
            },
            EnqueuedAt = DateTime.UtcNow,
            IsSuperseded = false
        };
    }

    /// <summary>
    /// Calculates the backoff time for a message retry based on retry count and configured strategy.
    /// </summary>
    /// <param name="retryCount">The current retry count.</param>
    /// <returns>The timestamp when the message should be available for retry.</returns>
    private DateTime? CalculateBackoffTime(int retryCount)
    {
        if (this.options.DefaultBackoffStrategy == Options.RetryBackoffStrategy.None)
        {
            return null; // No backoff, immediately available
        }

        TimeSpan delay;

        switch (this.options.DefaultBackoffStrategy)
        {
            case Options.RetryBackoffStrategy.Fixed:
                delay = this.options.DefaultInitialBackoff;
                break;

            case Options.RetryBackoffStrategy.Linear:
                delay = TimeSpan.FromMilliseconds(this.options.DefaultInitialBackoff.TotalMilliseconds * retryCount);
                break;

            case Options.RetryBackoffStrategy.Exponential:
                // Exponential: initialDelay * 2^(retryCount - 1)
                var exponentialMs = this.options.DefaultInitialBackoff.TotalMilliseconds * Math.Pow(2, retryCount - 1);
                delay = TimeSpan.FromMilliseconds(exponentialMs);
                break;

            default:
                delay = this.options.DefaultInitialBackoff;
                break;
        }

        // Cap at max backoff
        if (delay > this.options.DefaultMaxBackoff)
        {
            delay = this.options.DefaultMaxBackoff;
        }

        return DateTime.UtcNow.Add(delay);
    }

    /// <summary>
    /// Persists an operation to the write-ahead log.
    /// </summary>
    private async Task PersistOperationAsync(OperationCode opCode, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        // For Enqueue and Replace operations, serialize the full envelope for recovery
        // For other operations, just use the message payload (or empty JSON)
        string payload;
        if (opCode == OperationCode.Enqueue || opCode == OperationCode.Replace)
        {
            payload = JsonSerializer.Serialize(envelope);
        }
        else
        {
            payload = envelope.Payload ?? "{}";
        }

        var record = new OperationRecord
        {
            SequenceNumber = Interlocked.Increment(ref this.sequenceNumber),
            OperationCode = opCode,
            MessageId = envelope.MessageId,
            Payload = payload,
            Timestamp = DateTime.UtcNow,
            Checksum = 0 // Will be calculated by persister
        };

        await this.persister!.WriteOperationAsync(record, cancellationToken);
    }
}
