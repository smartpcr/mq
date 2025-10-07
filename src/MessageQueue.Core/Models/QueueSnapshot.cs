// -----------------------------------------------------------------------
// <copyright file="QueueSnapshot.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Models;

/// <summary>
/// Queue snapshot for persistence.
/// Contains complete state for recovery.
/// </summary>
public class QueueSnapshot
{
    /// <summary>
    /// Snapshot version
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// When the snapshot was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Buffer capacity
    /// </summary>
    public int Capacity { get; set; }

    /// <summary>
    /// Number of messages in snapshot
    /// </summary>
    public int MessageCount { get; set; }

    /// <summary>
    /// All message envelopes
    /// </summary>
    public List<MessageEnvelope> Messages { get; set; } = new();

    /// <summary>
    /// Deduplication index (key → message ID mapping)
    /// </summary>
    public Dictionary<string, Guid> DeduplicationIndex { get; set; } = new();

    /// <summary>
    /// Dead-letter queue messages
    /// </summary>
    public List<DeadLetterEnvelope> DeadLetterMessages { get; set; } = new();

    /// <summary>
    /// Buffer metadata (head, tail indices if needed)
    /// </summary>
    public Dictionary<string, string>? BufferMetadata { get; set; }
}
