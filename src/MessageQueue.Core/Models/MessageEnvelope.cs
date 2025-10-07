// -----------------------------------------------------------------------
// <copyright file="MessageEnvelope.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Models;

using System.Text.Json.Serialization;
using MessageQueue.Core.Enums;

/// <summary>
/// Message envelope containing message data and metadata.
/// Supports polymorphic serialization for different message types.
/// </summary>
public class MessageEnvelope
{
    /// <summary>
    /// Unique message identifier
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// Message type token for handler resolution
    /// </summary>
    public string MessageType { get; set; } = null!;

    /// <summary>
    /// Serialized message payload (JSON)
    /// </summary>
    public string Payload { get; set; } = null!;

    /// <summary>
    /// Deduplication key (hash-based)
    /// </summary>
    public string? DeduplicationKey { get; set; }

    /// <summary>
    /// Current message status
    /// </summary>
    public MessageStatus Status { get; set; }

    /// <summary>
    /// Retry attempt count
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Maximum allowed retries
    /// </summary>
    public int MaxRetries { get; set; }

    /// <summary>
    /// Lease information (when in InFlight status)
    /// </summary>
    public LeaseInfo? Lease { get; set; }

    /// <summary>
    /// Last persisted version number
    /// </summary>
    public long LastPersistedVersion { get; set; }

    /// <summary>
    /// Message metadata (headers, correlation IDs)
    /// </summary>
    public MessageMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Timestamp when message was enqueued
    /// </summary>
    public DateTime EnqueuedAt { get; set; }

    /// <summary>
    /// Superseded flag for deduplication
    /// </summary>
    public bool IsSuperseded { get; set; }

    /// <summary>
    /// Timestamp indicating when the message should be available for retry (for backoff enforcement)
    /// </summary>
    public DateTime? NotBefore { get; set; }
}

/// <summary>
/// Lease information for in-flight messages
/// </summary>
public class LeaseInfo
{
    /// <summary>
    /// Handler ID holding the lease
    /// </summary>
    public string HandlerId { get; set; } = null!;

    /// <summary>
    /// When the message was checked out
    /// </summary>
    public DateTime CheckoutTimestamp { get; set; }

    /// <summary>
    /// When the lease expires
    /// </summary>
    public DateTime LeaseExpiry { get; set; }

    /// <summary>
    /// Number of times lease has been extended
    /// </summary>
    public int ExtensionCount { get; set; }
}

/// <summary>
/// Message metadata for headers and correlation
/// </summary>
public class MessageMetadata
{
    /// <summary>
    /// Correlation ID for message tracing
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Additional headers (custom key-value pairs)
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// Source that enqueued the message
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Message version for schema evolution
    /// </summary>
    public int Version { get; set; } = 1;
}
