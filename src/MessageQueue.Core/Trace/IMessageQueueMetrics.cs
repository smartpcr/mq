// -----------------------------------------------------------------------
// <copyright file="IMessageQueueMetrics.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Trace;

using System;

/// <summary>
/// Common metrics interface for message queue operations.
/// </summary>
public interface IMessageQueueMetrics
{
    /// <summary>
    /// Records a message enqueued.
    /// </summary>
    void RecordMessageEnqueued(string messageType);

    /// <summary>
    /// Records a message dequeued.
    /// </summary>
    void RecordMessageDequeued(string messageType);

    /// <summary>
    /// Records a message acknowledged.
    /// </summary>
    void RecordMessageAcknowledged(string messageType);

    /// <summary>
    /// Records a message requeued.
    /// </summary>
    void RecordMessageRequeued(string messageType, int retryCount);

    /// <summary>
    /// Records a message moved to DLQ.
    /// </summary>
    void RecordMessageMovedToDLQ(string messageType, string reason);

    /// <summary>
    /// Records handler execution time.
    /// </summary>
    void RecordHandlerDuration(string messageType, TimeSpan duration, bool success);

    /// <summary>
    /// Records queue depth.
    /// </summary>
    void RecordQueueDepth(int readyCount, int inFlightCount, int dlqCount);

    /// <summary>
    /// Records lease extension.
    /// </summary>
    void RecordLeaseExtension(string messageType);

    /// <summary>
    /// Records lease expiration.
    /// </summary>
    void RecordLeaseExpiration(string messageType);

    /// <summary>
    /// Records deduplication hit.
    /// </summary>
    void RecordDeduplicationHit(string messageType);

    /// <summary>
    /// Records message dropped due to full buffer.
    /// </summary>
    void RecordMessageDropped(string messageType, string reason);

    /// <summary>
    /// Records snapshot operation.
    /// </summary>
    void RecordSnapshot(int messageCount, TimeSpan duration);

    /// <summary>
    /// Records journal operation.
    /// </summary>
    void RecordJournalWrite(TimeSpan duration);
}
