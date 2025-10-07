// -----------------------------------------------------------------------
// <copyright file="MessageStatus.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Enums;

/// <summary>
/// Message status in the queue lifecycle.
/// </summary>
public enum MessageStatus
{
    /// <summary>
    /// Message is empty/deleted
    /// </summary>
    Empty = 0,

    /// <summary>
    /// Message is ready for processing
    /// </summary>
    Ready = 1,

    /// <summary>
    /// Message is currently being processed (has active lease)
    /// </summary>
    InFlight = 2,

    /// <summary>
    /// Message processing completed successfully
    /// </summary>
    Completed = 3,

    /// <summary>
    /// Message exceeded max retries and moved to dead-letter queue
    /// </summary>
    DeadLetter = 4,

    /// <summary>
    /// Message was replaced by a newer message with same deduplication key
    /// </summary>
    Superseded = 5
}
