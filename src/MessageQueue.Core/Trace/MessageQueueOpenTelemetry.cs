// -----------------------------------------------------------------------
// <copyright file="MessageQueueOpenTelemetry.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Trace;

using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

/// <summary>
/// OpenTelemetry implementation for message queue telemetry.
/// </summary>
public sealed class MessageQueueOpenTelemetry : IMessageQueueLogger, IMessageQueueMetrics, IDisposable
{
    private const string ActivitySourceName = "MessageQueue.Core";
    private const string MeterName = "MessageQueue.Core";

    private readonly ActivitySource activitySource;
    private readonly Meter meter;

    // Counters
    private readonly Counter<long> messagesEnqueuedCounter;
    private readonly Counter<long> messagesDequeuedCounter;
    private readonly Counter<long> messagesAcknowledgedCounter;
    private readonly Counter<long> messagesRequeuedCounter;
    private readonly Counter<long> messagesMovedToDLQCounter;
    private readonly Counter<long> leaseExtensionsCounter;
    private readonly Counter<long> leaseExpirationsCounter;
    private readonly Counter<long> deduplicationHitsCounter;
    private readonly Counter<long> messagesDroppedCounter;

    // Histograms
    private readonly Histogram<double> handlerDurationHistogram;
    private readonly Histogram<double> snapshotDurationHistogram;
    private readonly Histogram<double> journalWriteDurationHistogram;

    // Observable Gauges
    private readonly ObservableGauge<int> queueDepthReadyGauge;
    private readonly ObservableGauge<int> queueDepthInFlightGauge;
    private readonly ObservableGauge<int> queueDepthDLQGauge;

    private int lastReadyCount;
    private int lastInFlightCount;
    private int lastDlqCount;
    private bool disposed;
    private TracerProvider? tracerProvider;
    private MeterProvider? meterProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageQueueOpenTelemetry"/> class.
    /// </summary>
    /// <param name="enableOtlpExport">Enable OTLP export.</param>
    /// <param name="otlpEndpoint">OTLP endpoint URL.</param>
    public MessageQueueOpenTelemetry(bool enableOtlpExport = true, string otlpEndpoint = "http://localhost:4320")
    {
        this.activitySource = new ActivitySource(ActivitySourceName, "1.0.0");
        this.meter = new Meter(MeterName, "1.0.0");

        // Create counters
        this.messagesEnqueuedCounter = this.meter.CreateCounter<long>(
            "messagequeue.messages.enqueued",
            description: "Total number of messages enqueued");

        this.messagesDequeuedCounter = this.meter.CreateCounter<long>(
            "messagequeue.messages.dequeued",
            description: "Total number of messages dequeued");

        this.messagesAcknowledgedCounter = this.meter.CreateCounter<long>(
            "messagequeue.messages.acknowledged",
            description: "Total number of messages acknowledged");

        this.messagesRequeuedCounter = this.meter.CreateCounter<long>(
            "messagequeue.messages.requeued",
            description: "Total number of messages requeued");

        this.messagesMovedToDLQCounter = this.meter.CreateCounter<long>(
            "messagequeue.messages.dlq",
            description: "Total number of messages moved to DLQ");

        this.leaseExtensionsCounter = this.meter.CreateCounter<long>(
            "messagequeue.leases.extensions",
            description: "Total number of lease extensions");

        this.leaseExpirationsCounter = this.meter.CreateCounter<long>(
            "messagequeue.leases.expirations",
            description: "Total number of lease expirations");

        this.deduplicationHitsCounter = this.meter.CreateCounter<long>(
            "messagequeue.deduplication.hits",
            description: "Total number of deduplication hits");

        this.messagesDroppedCounter = this.meter.CreateCounter<long>(
            "messagequeue.messages.dropped",
            description: "Total number of messages dropped due to full buffer");

        // Create histograms
        this.handlerDurationHistogram = this.meter.CreateHistogram<double>(
            "messagequeue.handler.duration",
            unit: "ms",
            description: "Handler execution duration in milliseconds");

        this.snapshotDurationHistogram = this.meter.CreateHistogram<double>(
            "messagequeue.snapshot.duration",
            unit: "ms",
            description: "Snapshot operation duration in milliseconds");

        this.journalWriteDurationHistogram = this.meter.CreateHistogram<double>(
            "messagequeue.journal.duration",
            unit: "ms",
            description: "Journal write duration in milliseconds");

        // Create observable gauges
        this.queueDepthReadyGauge = this.meter.CreateObservableGauge(
            "messagequeue.queue.depth.ready",
            () => this.lastReadyCount,
            description: "Number of messages in ready state");

        this.queueDepthInFlightGauge = this.meter.CreateObservableGauge(
            "messagequeue.queue.depth.inflight",
            () => this.lastInFlightCount,
            description: "Number of messages in-flight");

        this.queueDepthDLQGauge = this.meter.CreateObservableGauge(
            "messagequeue.queue.depth.dlq",
            () => this.lastDlqCount,
            description: "Number of messages in dead letter queue");

        // Configure OTLP export if enabled
        if (enableOtlpExport)
        {
            this.tracerProvider = Sdk.CreateTracerProviderBuilder()
                .AddSource(ActivitySourceName)
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otlpEndpoint);
                    options.Protocol = OtlpExportProtocol.HttpProtobuf;
                })
                .Build();

            this.meterProvider = Sdk.CreateMeterProviderBuilder()
                .AddMeter(MeterName)
                .AddOtlpExporter((options, readerOptions) =>
                {
                    options.Endpoint = new Uri(otlpEndpoint);
                    options.Protocol = OtlpExportProtocol.HttpProtobuf;
                    readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10000;
                })
                .Build();
        }
    }

    #region IMessageQueueLogger Implementation

    public void MessageEnqueued(Guid messageId, string messageType, string? deduplicationKey)
    {
        using var activity = this.activitySource.StartActivity("MessageEnqueued");
        activity?.SetTag("message.id", messageId);
        activity?.SetTag("message.type", messageType);
        activity?.SetTag("message.dedup_key", deduplicationKey);
    }

    public void MessageCheckedOut(Guid messageId, string handlerId, TimeSpan leaseDuration)
    {
        using var activity = this.activitySource.StartActivity("MessageCheckedOut");
        activity?.SetTag("message.id", messageId);
        activity?.SetTag("handler.id", handlerId);
        activity?.SetTag("lease.duration_ms", leaseDuration.TotalMilliseconds);
    }

    public void MessageAcknowledged(Guid messageId)
    {
        using var activity = this.activitySource.StartActivity("MessageAcknowledged");
        activity?.SetTag("message.id", messageId);
    }

    public void MessageRequeued(Guid messageId, int retryCount, string? reason)
    {
        using var activity = this.activitySource.StartActivity("MessageRequeued");
        activity?.SetTag("message.id", messageId);
        activity?.SetTag("message.retry_count", retryCount);
        activity?.SetTag("message.requeue_reason", reason);
    }

    public void MessageMovedToDLQ(Guid messageId, string reason, int retryCount)
    {
        using var activity = this.activitySource.StartActivity("MessageMovedToDLQ");
        activity?.SetTag("message.id", messageId);
        activity?.SetTag("message.failure_reason", reason);
        activity?.SetTag("message.retry_count", retryCount);
        activity?.SetStatus(ActivityStatusCode.Error, reason);
    }

    public void MessageReplayed(Guid messageId, bool resetRetryCount)
    {
        using var activity = this.activitySource.StartActivity("MessageReplayed");
        activity?.SetTag("message.id", messageId);
        activity?.SetTag("message.reset_retry_count", resetRetryCount);
    }

    public void LeaseExtended(Guid messageId, TimeSpan extensionDuration)
    {
        using var activity = this.activitySource.StartActivity("LeaseExtended");
        activity?.SetTag("message.id", messageId);
        activity?.SetTag("lease.extension_ms", extensionDuration.TotalMilliseconds);
    }

    public void LeaseExpired(Guid messageId, string handlerId)
    {
        using var activity = this.activitySource.StartActivity("LeaseExpired");
        activity?.SetTag("message.id", messageId);
        activity?.SetTag("handler.id", handlerId);
        activity?.SetStatus(ActivityStatusCode.Error, "Lease expired");
    }

    public void HandlerStarted(Guid messageId, string messageType, string handlerId)
    {
        using var activity = this.activitySource.StartActivity("HandlerStarted");
        activity?.SetTag("message.id", messageId);
        activity?.SetTag("message.type", messageType);
        activity?.SetTag("handler.id", handlerId);
    }

    public void HandlerCompleted(Guid messageId, string messageType, TimeSpan duration)
    {
        using var activity = this.activitySource.StartActivity("HandlerCompleted");
        activity?.SetTag("message.id", messageId);
        activity?.SetTag("message.type", messageType);
        activity?.SetTag("handler.duration_ms", duration.TotalMilliseconds);
    }

    public void HandlerFailed(Guid messageId, string messageType, string error, TimeSpan duration)
    {
        using var activity = this.activitySource.StartActivity("HandlerFailed");
        activity?.SetTag("message.id", messageId);
        activity?.SetTag("message.type", messageType);
        activity?.SetTag("handler.duration_ms", duration.TotalMilliseconds);
        activity?.SetTag("handler.error", error);
        activity?.SetStatus(ActivityStatusCode.Error, error);
    }

    public void DeduplicationOccurred(string deduplicationKey, Guid existingMessageId, Guid newMessageId)
    {
        using var activity = this.activitySource.StartActivity("DeduplicationOccurred");
        activity?.SetTag("message.dedup_key", deduplicationKey);
        activity?.SetTag("message.existing_id", existingMessageId);
        activity?.SetTag("message.new_id", newMessageId);
    }

    public void MessageDropped(Guid messageId, string messageType, string reason)
    {
        using var activity = this.activitySource.StartActivity("MessageDropped");
        activity?.SetTag("message.id", messageId);
        activity?.SetTag("message.type", messageType);
        activity?.SetTag("drop.reason", reason);
        activity?.SetStatus(ActivityStatusCode.Error, reason);
    }

    public void SnapshotCreated(int messageCount, int operationsSinceLastSnapshot)
    {
        using var activity = this.activitySource.StartActivity("SnapshotCreated");
        activity?.SetTag("snapshot.message_count", messageCount);
        activity?.SetTag("snapshot.operations_since_last", operationsSinceLastSnapshot);
    }

    public void JournalTruncated(int operationsRemoved)
    {
        using var activity = this.activitySource.StartActivity("JournalTruncated");
        activity?.SetTag("journal.operations_removed", operationsRemoved);
    }

    public void Error(string operation, string error, Exception? exception)
    {
        using var activity = this.activitySource.StartActivity("Error");
        activity?.SetTag("operation", operation);
        activity?.SetTag("error", error);
        activity?.SetStatus(ActivityStatusCode.Error, error);
        if (exception != null)
        {
            activity?.SetTag("exception.type", exception.GetType().FullName);
            activity?.SetTag("exception.message", exception.Message);
            activity?.SetTag("exception.stacktrace", exception.StackTrace);
        }
    }

    #endregion

    #region IMessageQueueMetrics Implementation

    public void RecordMessageEnqueued(string messageType)
    {
        this.messagesEnqueuedCounter.Add(1, new KeyValuePair<string, object?>("message.type", messageType));
    }

    public void RecordMessageDequeued(string messageType)
    {
        this.messagesDequeuedCounter.Add(1, new KeyValuePair<string, object?>("message.type", messageType));
    }

    public void RecordMessageAcknowledged(string messageType)
    {
        this.messagesAcknowledgedCounter.Add(1, new KeyValuePair<string, object?>("message.type", messageType));
    }

    public void RecordMessageRequeued(string messageType, int retryCount)
    {
        this.messagesRequeuedCounter.Add(
            1,
            new KeyValuePair<string, object?>("message.type", messageType),
            new KeyValuePair<string, object?>("retry.count", retryCount));
    }

    public void RecordMessageMovedToDLQ(string messageType, string reason)
    {
        this.messagesMovedToDLQCounter.Add(
            1,
            new KeyValuePair<string, object?>("message.type", messageType),
            new KeyValuePair<string, object?>("failure.reason", reason));
    }

    public void RecordHandlerDuration(string messageType, TimeSpan duration, bool success)
    {
        this.handlerDurationHistogram.Record(
            duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("message.type", messageType),
            new KeyValuePair<string, object?>("handler.success", success));
    }

    public void RecordQueueDepth(int readyCount, int inFlightCount, int dlqCount)
    {
        this.lastReadyCount = readyCount;
        this.lastInFlightCount = inFlightCount;
        this.lastDlqCount = dlqCount;
    }

    public void RecordLeaseExtension(string messageType)
    {
        this.leaseExtensionsCounter.Add(1, new KeyValuePair<string, object?>("message.type", messageType));
    }

    public void RecordLeaseExpiration(string messageType)
    {
        this.leaseExpirationsCounter.Add(1, new KeyValuePair<string, object?>("message.type", messageType));
    }

    public void RecordDeduplicationHit(string messageType)
    {
        this.deduplicationHitsCounter.Add(1, new KeyValuePair<string, object?>("message.type", messageType));
    }

    public void RecordMessageDropped(string messageType, string reason)
    {
        this.messagesDroppedCounter.Add(
            1,
            new KeyValuePair<string, object?>("message.type", messageType),
            new KeyValuePair<string, object?>("drop.reason", reason));
    }

    public void RecordSnapshot(int messageCount, TimeSpan duration)
    {
        this.snapshotDurationHistogram.Record(
            duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("snapshot.message_count", messageCount));
    }

    public void RecordJournalWrite(TimeSpan duration)
    {
        this.journalWriteDurationHistogram.Record(duration.TotalMilliseconds);
    }

    #endregion

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        if (!this.disposed)
        {
            this.tracerProvider?.Dispose();
            this.meterProvider?.Dispose();
            this.activitySource.Dispose();
            this.meter.Dispose();
            this.disposed = true;
        }
    }
}
