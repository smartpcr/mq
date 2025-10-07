// -----------------------------------------------------------------------
// <copyright file="MessageQueueEventIds.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Trace;

/// <summary>
/// Event IDs for MessageQueue telemetry events.
/// </summary>
public static class MessageQueueEventIds
{
    // Logging Events (1-99)

    /// <summary>Message enqueued event.</summary>
    public const int MessageEnqueued = 1;

    /// <summary>Message checked out event.</summary>
    public const int MessageCheckedOut = 2;

    /// <summary>Message acknowledged event.</summary>
    public const int MessageAcknowledged = 3;

    /// <summary>Message requeued event.</summary>
    public const int MessageRequeued = 4;

    /// <summary>Message moved to DLQ event.</summary>
    public const int MessageMovedToDLQ = 5;

    /// <summary>Message replayed from DLQ event.</summary>
    public const int MessageReplayed = 6;

    /// <summary>Lease extended event.</summary>
    public const int LeaseExtended = 7;

    /// <summary>Lease expired event.</summary>
    public const int LeaseExpired = 8;

    /// <summary>Handler started event.</summary>
    public const int HandlerStarted = 9;

    /// <summary>Handler completed event.</summary>
    public const int HandlerCompleted = 10;

    /// <summary>Handler failed event.</summary>
    public const int HandlerFailed = 11;

    /// <summary>Deduplication occurred event.</summary>
    public const int DeduplicationOccurred = 12;

    /// <summary>Message dropped event.</summary>
    public const int MessageDropped = 13;

    /// <summary>Snapshot created event.</summary>
    public const int SnapshotCreated = 14;

    /// <summary>Journal truncated event.</summary>
    public const int JournalTruncated = 15;

    /// <summary>Error event.</summary>
    public const int Error = 16;

    // Metrics Events (100-199)

    /// <summary>Record message enqueued metric.</summary>
    public const int RecordMessageEnqueued = 100;

    /// <summary>Record message dequeued metric.</summary>
    public const int RecordMessageDequeued = 101;

    /// <summary>Record message acknowledged metric.</summary>
    public const int RecordMessageAcknowledged = 102;

    /// <summary>Record message requeued metric.</summary>
    public const int RecordMessageRequeued = 103;

    /// <summary>Record message moved to DLQ metric.</summary>
    public const int RecordMessageMovedToDLQ = 104;

    /// <summary>Record handler duration metric.</summary>
    public const int RecordHandlerDuration = 105;

    /// <summary>Record queue depth metric.</summary>
    public const int RecordQueueDepth = 106;

    /// <summary>Record lease extension metric.</summary>
    public const int RecordLeaseExtension = 107;

    /// <summary>Record lease expiration metric.</summary>
    public const int RecordLeaseExpiration = 108;

    /// <summary>Record deduplication hit metric.</summary>
    public const int RecordDeduplicationHit = 109;

    /// <summary>Record message dropped metric.</summary>
    public const int RecordMessageDropped = 110;

    /// <summary>Record snapshot metric.</summary>
    public const int RecordSnapshot = 111;

    /// <summary>Record journal write metric.</summary>
    public const int RecordJournalWrite = 112;
}
