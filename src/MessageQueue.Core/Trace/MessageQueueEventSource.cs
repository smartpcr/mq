// -----------------------------------------------------------------------
// <copyright file="MessageQueueEventSource.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Trace;

using System;
using System.Diagnostics.Tracing;

/// <summary>
/// ETW EventSource for message queue telemetry.
/// </summary>
[EventSource(Name = "MessageQueue-Core")]
public sealed class MessageQueueEventSource : EventSource, IMessageQueueLogger, IMessageQueueMetrics
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly MessageQueueEventSource Instance = new MessageQueueEventSource();

    private MessageQueueEventSource()
    {
    }

    #region IMessageQueueLogger Implementation

    [Event(MessageQueueEventIds.MessageEnqueued, Level = EventLevel.Informational, Message = "Message enqueued: {0}, Type: {1}, DedupKey: {2}")]
    public void MessageEnqueued(Guid messageId, string messageType, string? deduplicationKey)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.MessageEnqueued, messageId, messageType, deduplicationKey ?? string.Empty);
        }
    }

    [Event(MessageQueueEventIds.MessageCheckedOut, Level = EventLevel.Informational, Message = "Message checked out: {0}, Handler: {1}, Lease: {2}ms")]
    public void MessageCheckedOut(Guid messageId, string handlerId, TimeSpan leaseDuration)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.MessageCheckedOut, messageId, handlerId, leaseDuration.TotalMilliseconds);
        }
    }

    [Event(MessageQueueEventIds.MessageAcknowledged, Level = EventLevel.Informational, Message = "Message acknowledged: {0}")]
    public void MessageAcknowledged(Guid messageId)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.MessageAcknowledged, messageId);
        }
    }

    [Event(MessageQueueEventIds.MessageRequeued, Level = EventLevel.Warning, Message = "Message requeued: {0}, RetryCount: {1}, Reason: {2}")]
    public void MessageRequeued(Guid messageId, int retryCount, string? reason)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.MessageRequeued, messageId, retryCount, reason ?? string.Empty);
        }
    }

    [Event(MessageQueueEventIds.MessageMovedToDLQ, Level = EventLevel.Error, Message = "Message moved to DLQ: {0}, Reason: {1}, RetryCount: {2}")]
    public void MessageMovedToDLQ(Guid messageId, string reason, int retryCount)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.MessageMovedToDLQ, messageId, reason, retryCount);
        }
    }

    [Event(MessageQueueEventIds.MessageReplayed, Level = EventLevel.Informational, Message = "Message replayed from DLQ: {0}, ResetRetryCount: {1}")]
    public void MessageReplayed(Guid messageId, bool resetRetryCount)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.MessageReplayed, messageId, resetRetryCount);
        }
    }

    [Event(MessageQueueEventIds.LeaseExtended, Level = EventLevel.Informational, Message = "Lease extended: {0}, Extension: {1}ms")]
    public void LeaseExtended(Guid messageId, TimeSpan extensionDuration)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.LeaseExtended, messageId, extensionDuration.TotalMilliseconds);
        }
    }

    [Event(MessageQueueEventIds.LeaseExpired, Level = EventLevel.Warning, Message = "Lease expired: {0}, Handler: {1}")]
    public void LeaseExpired(Guid messageId, string handlerId)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.LeaseExpired, messageId, handlerId);
        }
    }

    [Event(MessageQueueEventIds.HandlerStarted, Level = EventLevel.Informational, Message = "Handler started: {0}, Type: {1}, Handler: {2}")]
    public void HandlerStarted(Guid messageId, string messageType, string handlerId)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.HandlerStarted, messageId, messageType, handlerId);
        }
    }

    [Event(MessageQueueEventIds.HandlerCompleted, Level = EventLevel.Informational, Message = "Handler completed: {0}, Type: {1}, Duration: {2}ms")]
    public void HandlerCompleted(Guid messageId, string messageType, TimeSpan duration)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.HandlerCompleted, messageId, messageType, duration.TotalMilliseconds);
        }
    }

    [Event(MessageQueueEventIds.HandlerFailed, Level = EventLevel.Error, Message = "Handler failed: {0}, Type: {1}, Error: {2}, Duration: {3}ms")]
    public void HandlerFailed(Guid messageId, string messageType, string error, TimeSpan duration)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.HandlerFailed, messageId, messageType, error, duration.TotalMilliseconds);
        }
    }

    [Event(MessageQueueEventIds.DeduplicationOccurred, Level = EventLevel.Informational, Message = "Deduplication occurred: Key={0}, Existing={1}, New={2}")]
    public void DeduplicationOccurred(string deduplicationKey, Guid existingMessageId, Guid newMessageId)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.DeduplicationOccurred, deduplicationKey, existingMessageId, newMessageId);
        }
    }

    [Event(MessageQueueEventIds.MessageDropped, Level = EventLevel.Warning, Message = "Message dropped: {0}, Type: {1}, Reason: {2}")]
    public void MessageDropped(Guid messageId, string messageType, string reason)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.MessageDropped, messageId, messageType, reason);
        }
    }

    [Event(MessageQueueEventIds.SnapshotCreated, Level = EventLevel.Informational, Message = "Snapshot created: MessageCount={0}, OperationsSinceLastSnapshot={1}")]
    public void SnapshotCreated(int messageCount, int operationsSinceLastSnapshot)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.SnapshotCreated, messageCount, operationsSinceLastSnapshot);
        }
    }

    [Event(MessageQueueEventIds.JournalTruncated, Level = EventLevel.Informational, Message = "Journal truncated: OperationsRemoved={0}")]
    public void JournalTruncated(int operationsRemoved)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.JournalTruncated, operationsRemoved);
        }
    }

    [Event(MessageQueueEventIds.Error, Level = EventLevel.Error, Message = "Error in {0}: {1}")]
    public void Error(string operation, string error, Exception? exception)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.Error, operation, error);
        }
    }

    #endregion

    #region IMessageQueueMetrics Implementation

    [Event(MessageQueueEventIds.RecordMessageEnqueued, Level = EventLevel.Informational)]
    public void RecordMessageEnqueued(string messageType)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.RecordMessageEnqueued, messageType);
        }
    }

    [Event(MessageQueueEventIds.RecordMessageDequeued, Level = EventLevel.Informational)]
    public void RecordMessageDequeued(string messageType)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.RecordMessageDequeued, messageType);
        }
    }

    [Event(MessageQueueEventIds.RecordMessageAcknowledged, Level = EventLevel.Informational)]
    public void RecordMessageAcknowledged(string messageType)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.RecordMessageAcknowledged, messageType);
        }
    }

    [Event(MessageQueueEventIds.RecordMessageRequeued, Level = EventLevel.Informational)]
    public void RecordMessageRequeued(string messageType, int retryCount)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.RecordMessageRequeued, messageType, retryCount);
        }
    }

    [Event(MessageQueueEventIds.RecordMessageMovedToDLQ, Level = EventLevel.Informational)]
    public void RecordMessageMovedToDLQ(string messageType, string reason)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.RecordMessageMovedToDLQ, messageType, reason);
        }
    }

    [Event(MessageQueueEventIds.RecordHandlerDuration, Level = EventLevel.Informational)]
    public void RecordHandlerDuration(string messageType, TimeSpan duration, bool success)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.RecordHandlerDuration, messageType, duration.TotalMilliseconds, success);
        }
    }

    [Event(MessageQueueEventIds.RecordQueueDepth, Level = EventLevel.Informational)]
    public void RecordQueueDepth(int readyCount, int inFlightCount, int dlqCount)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.RecordQueueDepth, readyCount, inFlightCount, dlqCount);
        }
    }

    [Event(MessageQueueEventIds.RecordLeaseExtension, Level = EventLevel.Informational)]
    public void RecordLeaseExtension(string messageType)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.RecordLeaseExtension, messageType);
        }
    }

    [Event(MessageQueueEventIds.RecordLeaseExpiration, Level = EventLevel.Informational)]
    public void RecordLeaseExpiration(string messageType)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.RecordLeaseExpiration, messageType);
        }
    }

    [Event(MessageQueueEventIds.RecordDeduplicationHit, Level = EventLevel.Informational)]
    public void RecordDeduplicationHit(string messageType)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.RecordDeduplicationHit, messageType);
        }
    }

    [Event(MessageQueueEventIds.RecordMessageDropped, Level = EventLevel.Informational)]
    public void RecordMessageDropped(string messageType, string reason)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.RecordMessageDropped, messageType, reason);
        }
    }

    [Event(MessageQueueEventIds.RecordSnapshot, Level = EventLevel.Informational)]
    public void RecordSnapshot(int messageCount, TimeSpan duration)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.RecordSnapshot, messageCount, duration.TotalMilliseconds);
        }
    }

    [Event(MessageQueueEventIds.RecordJournalWrite, Level = EventLevel.Informational)]
    public void RecordJournalWrite(TimeSpan duration)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(MessageQueueEventIds.RecordJournalWrite, duration.TotalMilliseconds);
        }
    }

    #endregion
}
