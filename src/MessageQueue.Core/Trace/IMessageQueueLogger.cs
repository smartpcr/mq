// -----------------------------------------------------------------------
// <copyright file="IMessageQueueLogger.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Trace;

using System;

/// <summary>
/// Common logging interface for message queue operations.
/// </summary>
public interface IMessageQueueLogger
{
    /// <summary>
    /// Logs message enqueued event.
    /// </summary>
    void MessageEnqueued(Guid messageId, string messageType, string? deduplicationKey);

    /// <summary>
    /// Logs message checked out event.
    /// </summary>
    void MessageCheckedOut(Guid messageId, string handlerId, TimeSpan leaseDuration);

    /// <summary>
    /// Logs message acknowledged event.
    /// </summary>
    void MessageAcknowledged(Guid messageId);

    /// <summary>
    /// Logs message requeued event.
    /// </summary>
    void MessageRequeued(Guid messageId, int retryCount, string? reason);

    /// <summary>
    /// Logs message moved to dead letter queue.
    /// </summary>
    void MessageMovedToDLQ(Guid messageId, string reason, int retryCount);

    /// <summary>
    /// Logs message replayed from dead letter queue.
    /// </summary>
    void MessageReplayed(Guid messageId, bool resetRetryCount);

    /// <summary>
    /// Logs lease extended event.
    /// </summary>
    void LeaseExtended(Guid messageId, TimeSpan extensionDuration);

    /// <summary>
    /// Logs lease expired event.
    /// </summary>
    void LeaseExpired(Guid messageId, string handlerId);

    /// <summary>
    /// Logs handler started event.
    /// </summary>
    void HandlerStarted(Guid messageId, string messageType, string handlerId);

    /// <summary>
    /// Logs handler completed event.
    /// </summary>
    void HandlerCompleted(Guid messageId, string messageType, TimeSpan duration);

    /// <summary>
    /// Logs handler failed event.
    /// </summary>
    void HandlerFailed(Guid messageId, string messageType, string error, TimeSpan duration);

    /// <summary>
    /// Logs deduplication occurred event.
    /// </summary>
    void DeduplicationOccurred(string deduplicationKey, Guid existingMessageId, Guid newMessageId);

    /// <summary>
    /// Logs message dropped due to full buffer.
    /// </summary>
    void MessageDropped(Guid messageId, string messageType, string reason);

    /// <summary>
    /// Logs snapshot created event.
    /// </summary>
    void SnapshotCreated(int messageCount, int operationsSinceLastSnapshot);

    /// <summary>
    /// Logs journal truncated event.
    /// </summary>
    void JournalTruncated(int operationsRemoved);

    /// <summary>
    /// Logs error event.
    /// </summary>
    void Error(string operation, string error, Exception? exception);
}
