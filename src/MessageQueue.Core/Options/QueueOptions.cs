// -----------------------------------------------------------------------
// <copyright file="QueueOptions.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Options;

using System.ComponentModel.DataAnnotations;
using MessageQueue.Core.Trace;

/// <summary>
/// Global queue configuration options.
/// </summary>
public class QueueOptions
{
    /// <summary>
    /// Circular buffer capacity (number of message slots)
    /// </summary>
    [Range(100, 1000000)]
    public int Capacity { get; set; } = 10000;

    /// <summary>
    /// Persistence file path
    /// </summary>
    [Required]
    public string PersistencePath { get; set; } = "./queue-data";

    /// <summary>
    /// Snapshot interval in seconds (time-based trigger)
    /// </summary>
    [Range(1, 3600)]
    public int SnapshotIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Snapshot threshold (operation count-based trigger)
    /// </summary>
    [Range(1, 100000)]
    public int SnapshotThreshold { get; set; } = 1000;

    /// <summary>
    /// Default timeout for handlers (if not specified per-handler)
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Default max retries (if not specified per-handler)
    /// </summary>
    [Range(0, 100)]
    public int DefaultMaxRetries { get; set; } = 5;

    /// <summary>
    /// Lease monitor check interval
    /// </summary>
    public TimeSpan LeaseMonitorInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Enable persistence (set to false for in-memory only mode)
    /// </summary>
    public bool EnablePersistence { get; set; } = true;

    /// <summary>
    /// Enable deduplication
    /// </summary>
    public bool EnableDeduplication { get; set; } = true;

    /// <summary>
    /// Dead-letter queue capacity
    /// </summary>
    [Range(100, 100000)]
    public int DeadLetterQueueCapacity { get; set; } = 10000;

    /// <summary>
    /// Default retry backoff strategy
    /// </summary>
    public RetryBackoffStrategy DefaultBackoffStrategy { get; set; } = RetryBackoffStrategy.Exponential;

    /// <summary>
    /// Default initial backoff delay for retries
    /// </summary>
    public TimeSpan DefaultInitialBackoff { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Default maximum backoff delay
    /// </summary>
    public TimeSpan DefaultMaxBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Telemetry provider (ETW, OpenTelemetry, or Both)
    /// </summary>
    public TelemetryProvider TelemetryProvider { get; set; } = TelemetryProvider.OpenTelemetry;

    /// <summary>
    /// OpenTelemetry OTLP exporter endpoint
    /// </summary>
    public string OtlpEndpoint { get; set; } = "http://localhost:4320";

    /// <summary>
    /// Enable OpenTelemetry OTLP export
    /// </summary>
    public bool EnableOtlpExport { get; set; } = true;
}
