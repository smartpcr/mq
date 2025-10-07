// -----------------------------------------------------------------------
// <copyright file="HandlerOptions.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Options;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Per-handler configuration options.
/// </summary>
/// <typeparam name="T">Message type</typeparam>
public class HandlerOptions<T>
{
    /// <summary>
    /// Maximum number of concurrent handler instances
    /// </summary>
    [Range(1, 100)]
    public int MaxParallelism { get; set; } = 1;

    /// <summary>
    /// Minimum number of handler instances (initial workers)
    /// </summary>
    [Range(1, 100)]
    public int MinParallelism { get; set; } = 1;

    /// <summary>
    /// Handler execution timeout
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum retry attempts for this handler
    /// </summary>
    [Range(0, 100)]
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Lease duration (how long handler can hold a message)
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Enable lease extension (heartbeat support)
    /// </summary>
    public bool EnableLeaseExtension { get; set; } = false;

    /// <summary>
    /// Deduplication mode
    /// </summary>
    public DeduplicationMode DeduplicationMode { get; set; } = DeduplicationMode.Strict;

    /// <summary>
    /// Retry backoff strategy
    /// </summary>
    public RetryBackoffStrategy BackoffStrategy { get; set; } = RetryBackoffStrategy.Exponential;

    /// <summary>
    /// Initial backoff delay for retries
    /// </summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum backoff delay
    /// </summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Deduplication mode
/// </summary>
public enum DeduplicationMode
{
    /// <summary>
    /// Strict deduplication - always replace duplicates
    /// </summary>
    Strict,

    /// <summary>
    /// Append mode - allow duplicates (no deduplication)
    /// </summary>
    Append
}

/// <summary>
/// Retry backoff strategy
/// </summary>
public enum RetryBackoffStrategy
{
    /// <summary>
    /// No backoff - immediate retry
    /// </summary>
    None,

    /// <summary>
    /// Fixed delay between retries
    /// </summary>
    Fixed,

    /// <summary>
    /// Exponential backoff (delay doubles each retry)
    /// </summary>
    Exponential,

    /// <summary>
    /// Linear backoff (delay increases linearly)
    /// </summary>
    Linear
}
