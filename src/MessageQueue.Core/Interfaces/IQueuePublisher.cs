namespace MessageQueue.Core.Interfaces;

/// <summary>
/// Publisher interface for handlers to enqueue additional messages.
/// Enables message chaining and workflow orchestration.
/// </summary>
public interface IQueuePublisher
{
    /// <summary>
    /// Enqueues a message from within a handler.
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="message">Message to enqueue</param>
    /// <param name="deduplicationKey">Optional deduplication key</param>
    /// <param name="correlationId">Optional correlation ID (propagated from parent message)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Message ID</returns>
    Task<Guid> EnqueueAsync<T>(T message, string deduplicationKey = null, string correlationId = null, CancellationToken cancellationToken = default);
}
