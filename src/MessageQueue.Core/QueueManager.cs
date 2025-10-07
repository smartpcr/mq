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
    private readonly ICircularBuffer _buffer;
    private readonly DeduplicationIndex _deduplicationIndex;
    private readonly IPersister? _persister;
    private readonly QueueOptions _options;
    private long _sequenceNumber;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, string> _messageIdToDeduplicationKey;

    /// <summary>
    /// Initializes a new instance of the QueueManager.
    /// </summary>
    /// <param name="buffer">The circular buffer implementation.</param>
    /// <param name="deduplicationIndex">The deduplication index.</param>
    /// <param name="options">Queue configuration options.</param>
    /// <param name="persister">Optional persister for WAL and snapshots.</param>
    public QueueManager(
        ICircularBuffer buffer,
        DeduplicationIndex deduplicationIndex,
        QueueOptions options,
        IPersister persister = null)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _deduplicationIndex = deduplicationIndex ?? throw new ArgumentNullException(nameof(deduplicationIndex));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _persister = persister;
        _sequenceNumber = 0;
        _messageIdToDeduplicationKey = new System.Collections.Concurrent.ConcurrentDictionary<Guid, string>();
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
            var existingMessageId = await _deduplicationIndex.TryGetAsync(deduplicationKey, cancellationToken);
            if (existingMessageId.HasValue)
            {
                // Supersede existing in-flight message
                var envelope = CreateEnvelope(message, deduplicationKey, correlationId);
                bool replaced = await _buffer.ReplaceAsync(envelope, deduplicationKey, cancellationToken);

                if (replaced)
                {
                    // Update deduplication index and reverse mapping
                    await _deduplicationIndex.UpdateAsync(deduplicationKey, envelope.MessageId, cancellationToken);
                    _messageIdToDeduplicationKey.TryRemove(existingMessageId.Value, out _);
                    _messageIdToDeduplicationKey.TryAdd(envelope.MessageId, deduplicationKey);

                    // Persist replace operation
                    if (_persister != null)
                    {
                        await PersistOperationAsync(OperationCode.Replace, envelope, cancellationToken);
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
        var newEnvelope = CreateEnvelope(message, deduplicationKey, correlationId);
        await _buffer.EnqueueAsync(newEnvelope, cancellationToken);

        // Add to deduplication index and track reverse mapping
        if (!string.IsNullOrWhiteSpace(deduplicationKey))
        {
            await _deduplicationIndex.TryAddAsync(deduplicationKey, newEnvelope.MessageId, cancellationToken);
            _messageIdToDeduplicationKey.TryAdd(newEnvelope.MessageId, deduplicationKey);
        }

        // Persist enqueue operation
        if (_persister != null)
        {
            await PersistOperationAsync(OperationCode.Enqueue, newEnvelope, cancellationToken);
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

        var lease = leaseDuration ?? _options.DefaultTimeout;
        var messageType = typeof(T);

        var envelope = await _buffer.CheckoutAsync(messageType, handlerId, lease, cancellationToken);

        if (envelope == null)
            return null;

        // Persist checkout operation
        if (_persister != null)
        {
            await PersistOperationAsync(OperationCode.Checkout, envelope, cancellationToken);
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
        bool acknowledged = await _buffer.AcknowledgeAsync(messageId, cancellationToken);

        if (!acknowledged)
            throw new InvalidOperationException($"Message {messageId} not found or already completed.");

        // Persist acknowledge operation
        if (_persister != null)
        {
            var record = new OperationRecord
            {
                SequenceNumber = Interlocked.Increment(ref _sequenceNumber),
                OperationCode = OperationCode.Acknowledge,
                MessageId = messageId,
                Payload = "{}",
                Timestamp = DateTime.UtcNow,
                Checksum = 0
            };

            await _persister.WriteOperationAsync(record, cancellationToken);
        }

        // Remove from deduplication index if it has a dedup key
        if (_messageIdToDeduplicationKey.TryRemove(messageId, out string? dedupKey))
        {
            await _deduplicationIndex.RemoveAsync(dedupKey, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task RequeueAsync(Guid messageId, Exception exception = null, CancellationToken cancellationToken = default)
    {
        // Get all messages to find the one to requeue
        var allMessages = await _buffer.GetAllMessagesAsync(cancellationToken);
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
            // This will be handled in Phase 5
            throw new InvalidOperationException($"Message {messageId} has exceeded max retries.");
        }

        bool requeued = await _buffer.RequeueAsync(messageId, cancellationToken);

        if (!requeued)
            throw new InvalidOperationException($"Failed to requeue message {messageId}.");

        // Persist requeue operation
        if (_persister != null)
        {
            await PersistOperationAsync(OperationCode.Requeue, target, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task ExtendLeaseAsync(Guid messageId, TimeSpan extensionDuration, CancellationToken cancellationToken = default)
    {
        var allMessages = await _buffer.GetAllMessagesAsync(cancellationToken);

        foreach (var msg in allMessages)
        {
            if (msg.MessageId == messageId && msg.Lease != null)
            {
                msg.Lease.LeaseExpiry = msg.Lease.LeaseExpiry.Add(extensionDuration);
                msg.Lease.ExtensionCount++;

                // Persist lease extension
                if (_persister != null)
                {
                    await PersistOperationAsync(OperationCode.LeaseRenew, msg, cancellationToken);
                }

                return;
            }
        }

        throw new InvalidOperationException($"Message {messageId} not found or has no active lease.");
    }

    /// <inheritdoc/>
    public async Task<QueueMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var allMessages = await _buffer.GetAllMessagesAsync(cancellationToken);

        var metrics = new QueueMetrics
        {
            TotalCapacity = _buffer.Capacity,
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
        return _buffer.GetCountAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<MessageEnvelope>> GetPendingMessagesAsync(CancellationToken cancellationToken = default)
    {
        var allMessages = await _buffer.GetAllMessagesAsync(cancellationToken);
        var pending = new List<MessageEnvelope>();

        foreach (var msg in allMessages)
        {
            if (msg.Status == MessageStatus.Ready)
            {
                pending.Add(msg);
            }
        }

        return pending;
    }

    /// <inheritdoc/>
    public async Task<MessageEnvelope?> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var allMessages = await _buffer.GetAllMessagesAsync(cancellationToken);

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
        var allMessages = await _buffer.GetAllMessagesAsync(cancellationToken);
        var deduplicationSnapshot = await _deduplicationIndex.GetSnapshotAsync(cancellationToken);

        var snapshot = new QueueSnapshot
        {
            Version = Interlocked.Read(ref _sequenceNumber),
            CreatedAt = DateTime.UtcNow,
            Capacity = _buffer.Capacity,
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
        if (_persister != null && _persister.ShouldSnapshot())
        {
            var snapshot = await CreateSnapshotAsync(cancellationToken);
            await _persister.CreateSnapshotAsync(snapshot, cancellationToken);

            // Truncate journal after successful snapshot
            await _persister.TruncateJournalAsync(snapshot.Version, cancellationToken);
        }
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
            MaxRetries = 3, // Default, should come from handler options
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
    /// Persists an operation to the write-ahead log.
    /// </summary>
    private async Task PersistOperationAsync(OperationCode opCode, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        var record = new OperationRecord
        {
            SequenceNumber = Interlocked.Increment(ref _sequenceNumber),
            OperationCode = opCode,
            MessageId = envelope.MessageId,
            Payload = envelope.Payload,
            Timestamp = DateTime.UtcNow,
            Checksum = 0 // Will be calculated by persister
        };

        await _persister!.WriteOperationAsync(record, cancellationToken);
    }
}
