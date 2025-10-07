namespace MessageQueue.Core.Interfaces;

using MessageQueue.Core.Models;

/// <summary>
/// Dead-letter queue for messages exceeding retry limits.
/// Provides storage and management APIs for failed messages.
/// </summary>
public interface IDeadLetterQueue
{
    /// <summary>
    /// Adds a message to the dead-letter queue.
    /// </summary>
    /// <param name="envelope">Failed message envelope</param>
    /// <param name="failureReason">Reason for failure</param>
    /// <param name="exception">Exception that caused failure (if any)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AddAsync(MessageEnvelope envelope, string failureReason, Exception exception = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves failed messages from DLQ with optional filtering.
    /// </summary>
    /// <param name="messageType">Optional message type filter</param>
    /// <param name="limit">Maximum number of messages to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of dead-letter envelopes</returns>
    Task<IEnumerable<DeadLetterEnvelope>> GetMessagesAsync(Type messageType = null, int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replays a message from DLQ back to the main queue.
    /// </summary>
    /// <param name="messageId">Message ID to replay</param>
    /// <param name="resetRetryCount">Whether to reset retry count</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ReplayAsync(Guid messageId, bool resetRetryCount = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Purges messages from DLQ based on age or other criteria.
    /// </summary>
    /// <param name="olderThan">Purge messages older than this timespan</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PurgeAsync(TimeSpan olderThan = default(TimeSpan), CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets DLQ metrics.
    /// </summary>
    Task<DeadLetterQueueMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// DLQ metrics
/// </summary>
public class DeadLetterQueueMetrics
{
    public int TotalCount { get; set; }
    public DateTime? OldestMessageTime { get; set; }
    public Dictionary<string, int> CountByMessageType { get; set; } = new();
    public Dictionary<string, int> CountByFailureReason { get; set; } = new();
}
