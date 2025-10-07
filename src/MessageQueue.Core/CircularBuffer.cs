namespace MessageQueue.Core;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MessageQueue.Core.Enums;
using MessageQueue.Core.Interfaces;
using MessageQueue.Core.Models;

/// <summary>
/// Lock-free circular buffer implementation using Compare-And-Swap (CAS) operations.
/// Thread-safe for concurrent producers and consumers.
/// </summary>
public class CircularBuffer : ICircularBuffer
{
    private readonly MessageEnvelope?[] _slots;
    private readonly int _capacity;
    private long _writePosition;
    private long _readPosition;

    /// <summary>
    /// Initializes a new instance of the CircularBuffer.
    /// </summary>
    /// <param name="capacity">The maximum number of messages the buffer can hold.</param>
    public CircularBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");

        _capacity = capacity;
        _slots = new MessageEnvelope?[capacity];
        _writePosition = 0;
        _readPosition = 0;
    }

    /// <inheritdoc/>
    public Task<bool> EnqueueAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope == null)
            throw new ArgumentNullException(nameof(envelope));

        cancellationToken.ThrowIfCancellationRequested();

        // Try to find an empty slot
        for (int attempt = 0; attempt < _capacity; attempt++)
        {
            long currentWrite = Interlocked.Read(ref _writePosition);
            int slotIndex = (int)(currentWrite % _capacity);

            // Try to claim this slot with CAS
            var currentSlot = Interlocked.CompareExchange(ref _slots[slotIndex], envelope, null);

            if (currentSlot == null)
            {
                // Successfully claimed the slot
                envelope.Status = MessageStatus.Ready;
                Interlocked.Increment(ref _writePosition);
                return Task.FromResult(true);
            }

            // If slot is occupied, try next position
            Interlocked.CompareExchange(ref _writePosition, currentWrite + 1, currentWrite);
        }

        // Buffer is full
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public Task<MessageEnvelope?> CheckoutAsync(Type messageType, string handlerId, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        if (messageType == null)
            throw new ArgumentNullException(nameof(messageType));
        if (string.IsNullOrWhiteSpace(handlerId))
            throw new ArgumentException("Handler ID cannot be null or whitespace.", nameof(handlerId));

        cancellationToken.ThrowIfCancellationRequested();

        string targetMessageType = messageType.FullName ?? messageType.Name;

        // Scan for a ready message of the requested type
        for (int attempt = 0; attempt < _capacity; attempt++)
        {
            long currentRead = Interlocked.Read(ref _readPosition);
            int slotIndex = (int)(currentRead % _capacity);

            var envelope = Interlocked.CompareExchange(ref _slots[slotIndex], null, null); // Read current value

            if (envelope != null &&
                envelope.Status == MessageStatus.Ready &&
                envelope.MessageType == targetMessageType &&
                !envelope.IsSuperseded)
            {
                // Try to transition to InFlight using CAS on status
                var leaseInfo = new LeaseInfo
                {
                    HandlerId = handlerId,
                    CheckoutTimestamp = DateTime.UtcNow,
                    LeaseExpiry = DateTime.UtcNow.Add(leaseDuration),
                    ExtensionCount = 0
                };

                // Create a copy with updated status
                var updatedEnvelope = new MessageEnvelope
                {
                    MessageId = envelope.MessageId,
                    MessageType = envelope.MessageType,
                    Payload = envelope.Payload,
                    DeduplicationKey = envelope.DeduplicationKey,
                    Status = MessageStatus.InFlight,
                    RetryCount = envelope.RetryCount,
                    MaxRetries = envelope.MaxRetries,
                    Lease = leaseInfo,
                    LastPersistedVersion = envelope.LastPersistedVersion,
                    Metadata = envelope.Metadata,
                    EnqueuedAt = envelope.EnqueuedAt,
                    IsSuperseded = envelope.IsSuperseded
                };

                // Try to replace with CAS
                var original = Interlocked.CompareExchange(ref _slots[slotIndex], updatedEnvelope, envelope);
                if (ReferenceEquals(original, envelope))
                {
                    // Successfully checked out
                    return Task.FromResult<MessageEnvelope?>(updatedEnvelope);
                }
            }

            // Move to next slot
            Interlocked.CompareExchange(ref _readPosition, currentRead + 1, currentRead);
        }

        // No ready message found
        return Task.FromResult<MessageEnvelope?>(null);
    }

    /// <inheritdoc/>
    public Task<bool> ReplaceAsync(MessageEnvelope envelope, string deduplicationKey, CancellationToken cancellationToken = default)
    {
        if (envelope == null)
            throw new ArgumentNullException(nameof(envelope));
        if (string.IsNullOrWhiteSpace(deduplicationKey))
            throw new ArgumentException("Deduplication key cannot be null or whitespace.", nameof(deduplicationKey));

        cancellationToken.ThrowIfCancellationRequested();

        // Find existing message with matching deduplication key
        for (int i = 0; i < _capacity; i++)
        {
            var existing = Interlocked.CompareExchange(ref _slots[i], null, null); // Read current value

            if (existing != null &&
                existing.DeduplicationKey == deduplicationKey &&
                existing.Status == MessageStatus.InFlight)
            {
                // Mark existing as superseded
                var superseded = new MessageEnvelope
                {
                    MessageId = existing.MessageId,
                    MessageType = existing.MessageType,
                    Payload = existing.Payload,
                    DeduplicationKey = existing.DeduplicationKey,
                    Status = MessageStatus.Superseded,
                    RetryCount = existing.RetryCount,
                    MaxRetries = existing.MaxRetries,
                    Lease = existing.Lease,
                    LastPersistedVersion = existing.LastPersistedVersion,
                    Metadata = existing.Metadata,
                    EnqueuedAt = existing.EnqueuedAt,
                    IsSuperseded = true
                };

                // Try to mark as superseded
                var original = Interlocked.CompareExchange(ref _slots[i], superseded, existing);
                if (ReferenceEquals(original, existing))
                {
                    // Successfully superseded, now enqueue the new message
                    return EnqueueAsync(envelope, cancellationToken);
                }
            }
        }

        // No in-flight message found with this key
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public Task<bool> AcknowledgeAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Find message by ID and mark as completed
        for (int i = 0; i < _capacity; i++)
        {
            var envelope = Interlocked.CompareExchange(ref _slots[i], null, null); // Read current value

            if (envelope != null && envelope.MessageId == messageId)
            {
                // Update status to Completed
                var completed = new MessageEnvelope
                {
                    MessageId = envelope.MessageId,
                    MessageType = envelope.MessageType,
                    Payload = envelope.Payload,
                    DeduplicationKey = envelope.DeduplicationKey,
                    Status = MessageStatus.Completed,
                    RetryCount = envelope.RetryCount,
                    MaxRetries = envelope.MaxRetries,
                    Lease = envelope.Lease,
                    LastPersistedVersion = envelope.LastPersistedVersion,
                    Metadata = envelope.Metadata,
                    EnqueuedAt = envelope.EnqueuedAt,
                    IsSuperseded = envelope.IsSuperseded
                };

                // Try to update with CAS
                var original = Interlocked.CompareExchange(ref _slots[i], completed, envelope);
                if (ReferenceEquals(original, envelope))
                {
                    // Successfully acknowledged, now clear the slot
                    Interlocked.CompareExchange(ref _slots[i], null, completed);
                    return Task.FromResult(true);
                }
            }
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public Task<bool> RequeueAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MessageEnvelope? envelope = null;

        // Find the message
        for (int i = 0; i < _capacity; i++)
        {
            var existing = Interlocked.CompareExchange(ref _slots[i], null, null); // Read current value

            if (existing != null && existing.MessageId == messageId)
            {
                envelope = existing;
                // Clear the slot
                Interlocked.CompareExchange(ref _slots[i], null, existing);
                break;
            }
        }

        if (envelope == null)
            return Task.FromResult(false);

        // Increment retry count and reset status
        var requeued = new MessageEnvelope
        {
            MessageId = envelope.MessageId,
            MessageType = envelope.MessageType,
            Payload = envelope.Payload,
            DeduplicationKey = envelope.DeduplicationKey,
            Status = MessageStatus.Ready,
            RetryCount = envelope.RetryCount + 1,
            MaxRetries = envelope.MaxRetries,
            Lease = null, // Clear lease
            LastPersistedVersion = envelope.LastPersistedVersion,
            Metadata = envelope.Metadata,
            EnqueuedAt = envelope.EnqueuedAt,
            IsSuperseded = envelope.IsSuperseded
        };

        return EnqueueAsync(requeued, cancellationToken);
    }

    /// <inheritdoc/>
    public int Capacity => _capacity;

    /// <inheritdoc/>
    public int Count => _slots.Count(slot => slot != null &&
        (slot.Status == MessageStatus.Ready || slot.Status == MessageStatus.InFlight));

    /// <inheritdoc/>
    public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int count = _slots.Count(slot => slot != null &&
            (slot.Status == MessageStatus.Ready || slot.Status == MessageStatus.InFlight));

        return Task.FromResult(count);
    }

    /// <inheritdoc/>
    public Task<MessageEnvelope[]> GetAllMessagesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messages = _slots
            .Where(slot => slot != null && slot.Status != MessageStatus.Completed)
            .ToArray()!;

        return Task.FromResult(messages);
    }

    /// <inheritdoc/>
    public Task<bool> RemoveAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        for (int i = 0; i < _capacity; i++)
        {
            var envelope = Interlocked.CompareExchange(ref _slots[i], null, null); // Read current value

            if (envelope != null && envelope.MessageId == messageId)
            {
                // Try to remove with CAS
                var original = Interlocked.CompareExchange(ref _slots[i], null, envelope);
                if (ReferenceEquals(original, envelope))
                {
                    return Task.FromResult(true);
                }
            }
        }

        return Task.FromResult(false);
    }
}
