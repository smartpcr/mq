namespace MessageQueue.Core.Interfaces;

using MessageQueue.Core.Enums;
using MessageQueue.Core.Models;

/// <summary>
/// Manages queue operations including enqueue, checkout, acknowledge, and requeue.
/// Coordinates deduplication, retry tracking, and DLQ routing.
/// </summary>
public interface IQueueManager
{
    /// <summary>
    /// Enqueues a message with optional deduplication key.
    /// If a message with the same key exists, it will be replaced based on deduplication semantics.
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="message">Message to enqueue</param>
    /// <param name="deduplicationKey">Optional deduplication key (defaults to message ID if null)</param>
    /// <param name="correlationId">Optional correlation ID for message tracing</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Message ID</returns>
    Task<Guid> EnqueueAsync<T>(T message, string deduplicationKey = null, string correlationId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks out the next ready message of the specified type.
    /// Transitions message to InFlight with a lease.
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="handlerId">Handler identifier for lease tracking</param>
    /// <param name="leaseDuration">Lease duration (defaults to handler-specific timeout)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Message envelope or null if no messages available</returns>
    Task<MessageEnvelope<T>> CheckoutAsync<T>(string handlerId, TimeSpan leaseDuration = default(TimeSpan), CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledges successful message processing.
    /// Transitions message to Completed state.
    /// </summary>
    /// <param name="messageId">Message ID to acknowledge</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AcknowledgeAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requeues a message after failure.
    /// Increments retry count and respects max retry limits.
    /// </summary>
    /// <param name="messageId">Message ID to requeue</param>
    /// <param name="exception">Exception that caused the failure</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RequeueAsync(Guid messageId, Exception exception = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extends the lease for a message being processed.
    /// Used by long-running handlers to prevent timeout.
    /// </summary>
    /// <param name="messageId">Message ID</param>
    /// <param name="extensionDuration">Additional lease duration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ExtendLeaseAsync(Guid messageId, TimeSpan extensionDuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets queue metrics (ready count, in-flight count, etc.)
    /// </summary>
    Task<QueueMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Queue metrics for monitoring
/// </summary>
public class QueueMetrics
{
    public int ReadyCount { get; set; }
    public int InFlightCount { get; set; }
    public int CompletedCount { get; set; }
    public int DeadLetterCount { get; set; }
    public int TotalCapacity { get; set; }
}

/// <summary>
/// Generic message envelope wrapper
/// </summary>
public class MessageEnvelope<T>
{
    public Guid MessageId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public T Message { get; set; } = default!;
    public string? DeduplicationKey { get; set; }
    public MessageStatus Status { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; }
    public LeaseInfo? Lease { get; set; }
    public MessageMetadata Metadata { get; set; } = new MessageMetadata();
    public DateTime EnqueuedAt { get; set; }
}
