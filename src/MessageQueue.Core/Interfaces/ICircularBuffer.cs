namespace MessageQueue.Core.Interfaces;

using MessageQueue.Core.Models;

/// <summary>
/// Lock-free circular buffer for message storage.
/// Supports enqueue, checkout, acknowledge, and replace operations.
/// </summary>
public interface ICircularBuffer
{
    /// <summary>
    /// Enqueues a message envelope into the buffer.
    /// </summary>
    /// <param name="envelope">Message envelope to enqueue</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if enqueued successfully, false if buffer is full</returns>
    Task<bool> EnqueueAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to checkout the next ready message of specified type.
    /// Atomically transitions message to InFlight state.
    /// </summary>
    /// <param name="messageType">Type token for message filtering</param>
    /// <param name="handlerId">Handler ID for lease tracking</param>
    /// <param name="leaseDuration">Lease duration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Message envelope or null if none available</returns>
    Task<MessageEnvelope?> CheckoutAsync(Type messageType, string handlerId, TimeSpan leaseDuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledges a message, transitioning it to Completed state.
    /// </summary>
    /// <param name="messageId">Message ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if acknowledged, false if message not found</returns>
    Task<bool> AcknowledgeAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces an existing message with the same deduplication key.
    /// Implements supersede semantics for in-flight messages.
    /// </summary>
    /// <param name="envelope">New message envelope</param>
    /// <param name="deduplicationKey">Key for finding existing message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if replaced, false if not found</returns>
    Task<bool> ReplaceAsync(MessageEnvelope envelope, string deduplicationKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requeues a message back to Ready state.
    /// Increments retry count.
    /// </summary>
    /// <param name="messageId">Message ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if requeued, false if message not found or exceeds max retries</returns>
    Task<bool> RequeueAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current buffer capacity.
    /// </summary>
    int Capacity { get; }

    /// <summary>
    /// Gets the current number of messages in the buffer.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets the count of messages in the buffer asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of messages</returns>
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all non-completed messages in the buffer.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of message envelopes</returns>
    Task<MessageEnvelope[]> GetAllMessagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a message from the buffer by ID.
    /// </summary>
    /// <param name="messageId">Message ID to remove</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if removed, false if not found</returns>
    Task<bool> RemoveAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a message envelope to the buffer, preserving its exact state (including status and lease).
    /// Used during recovery to restore snapshots without modifying message state.
    /// </summary>
    /// <param name="envelope">Message envelope to restore with its original state</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if restored successfully</returns>
    Task<bool> RestoreAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default);
}
