// -----------------------------------------------------------------------
// <copyright file="MessageQueueTelemetry.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Trace;

using System;

/// <summary>
/// Composite telemetry implementation supporting ETW, OpenTelemetry, or both.
/// </summary>
public sealed class MessageQueueTelemetry : IMessageQueueLogger, IMessageQueueMetrics, IDisposable
{
    private readonly TelemetryProvider provider;
    private readonly MessageQueueEventSource? etwSource;
    private readonly MessageQueueOpenTelemetry? otelSource;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageQueueTelemetry"/> class.
    /// </summary>
    /// <param name="provider">Telemetry provider to use.</param>
    /// <param name="enableOtlpExport">Enable OTLP export for OpenTelemetry.</param>
    /// <param name="otlpEndpoint">OTLP endpoint URL.</param>
    public MessageQueueTelemetry(
        TelemetryProvider provider,
        bool enableOtlpExport = true,
        string otlpEndpoint = "http://localhost:4320")
    {
        this.provider = provider;

        if (provider.HasFlag(TelemetryProvider.ETW))
        {
            this.etwSource = MessageQueueEventSource.Instance;
        }

        if (provider.HasFlag(TelemetryProvider.OpenTelemetry))
        {
            this.otelSource = new MessageQueueOpenTelemetry(enableOtlpExport, otlpEndpoint);
        }
    }

    #region IMessageQueueLogger Implementation

    /// <inheritdoc/>
    public void MessageEnqueued(Guid messageId, string messageType, string? deduplicationKey)
    {
        this.etwSource?.MessageEnqueued(messageId, messageType, deduplicationKey);
        this.otelSource?.MessageEnqueued(messageId, messageType, deduplicationKey);
    }

    /// <inheritdoc/>
    public void MessageCheckedOut(Guid messageId, string handlerId, TimeSpan leaseDuration)
    {
        this.etwSource?.MessageCheckedOut(messageId, handlerId, leaseDuration);
        this.otelSource?.MessageCheckedOut(messageId, handlerId, leaseDuration);
    }

    /// <inheritdoc/>
    public void MessageAcknowledged(Guid messageId)
    {
        this.etwSource?.MessageAcknowledged(messageId);
        this.otelSource?.MessageAcknowledged(messageId);
    }

    /// <inheritdoc/>
    public void MessageRequeued(Guid messageId, int retryCount, string? reason)
    {
        this.etwSource?.MessageRequeued(messageId, retryCount, reason);
        this.otelSource?.MessageRequeued(messageId, retryCount, reason);
    }

    /// <inheritdoc/>
    public void MessageMovedToDLQ(Guid messageId, string reason, int retryCount)
    {
        this.etwSource?.MessageMovedToDLQ(messageId, reason, retryCount);
        this.otelSource?.MessageMovedToDLQ(messageId, reason, retryCount);
    }

    /// <inheritdoc/>
    public void MessageReplayed(Guid messageId, bool resetRetryCount)
    {
        this.etwSource?.MessageReplayed(messageId, resetRetryCount);
        this.otelSource?.MessageReplayed(messageId, resetRetryCount);
    }

    /// <inheritdoc/>
    public void LeaseExtended(Guid messageId, TimeSpan extensionDuration)
    {
        this.etwSource?.LeaseExtended(messageId, extensionDuration);
        this.otelSource?.LeaseExtended(messageId, extensionDuration);
    }

    /// <inheritdoc/>
    public void LeaseExpired(Guid messageId, string handlerId)
    {
        this.etwSource?.LeaseExpired(messageId, handlerId);
        this.otelSource?.LeaseExpired(messageId, handlerId);
    }

    /// <inheritdoc/>
    public void HandlerStarted(Guid messageId, string messageType, string handlerId)
    {
        this.etwSource?.HandlerStarted(messageId, messageType, handlerId);
        this.otelSource?.HandlerStarted(messageId, messageType, handlerId);
    }

    /// <inheritdoc/>
    public void HandlerCompleted(Guid messageId, string messageType, TimeSpan duration)
    {
        this.etwSource?.HandlerCompleted(messageId, messageType, duration);
        this.otelSource?.HandlerCompleted(messageId, messageType, duration);
    }

    /// <inheritdoc/>
    public void HandlerFailed(Guid messageId, string messageType, string error, TimeSpan duration)
    {
        this.etwSource?.HandlerFailed(messageId, messageType, error, duration);
        this.otelSource?.HandlerFailed(messageId, messageType, error, duration);
    }

    /// <inheritdoc/>
    public void DeduplicationOccurred(string deduplicationKey, Guid existingMessageId, Guid newMessageId)
    {
        this.etwSource?.DeduplicationOccurred(deduplicationKey, existingMessageId, newMessageId);
        this.otelSource?.DeduplicationOccurred(deduplicationKey, existingMessageId, newMessageId);
    }

    /// <inheritdoc/>
    public void MessageDropped(Guid messageId, string messageType, string reason)
    {
        this.etwSource?.MessageDropped(messageId, messageType, reason);
        this.otelSource?.MessageDropped(messageId, messageType, reason);
    }

    /// <inheritdoc/>
    public void SnapshotCreated(int messageCount, int operationsSinceLastSnapshot)
    {
        this.etwSource?.SnapshotCreated(messageCount, operationsSinceLastSnapshot);
        this.otelSource?.SnapshotCreated(messageCount, operationsSinceLastSnapshot);
    }

    /// <inheritdoc/>
    public void JournalTruncated(int operationsRemoved)
    {
        this.etwSource?.JournalTruncated(operationsRemoved);
        this.otelSource?.JournalTruncated(operationsRemoved);
    }

    /// <inheritdoc/>
    public void Error(string operation, string error, Exception? exception)
    {
        this.etwSource?.Error(operation, error, exception);
        this.otelSource?.Error(operation, error, exception);
    }

    #endregion

    #region IMessageQueueMetrics Implementation

    /// <inheritdoc/>
    public void RecordMessageEnqueued(string messageType)
    {
        this.etwSource?.RecordMessageEnqueued(messageType);
        this.otelSource?.RecordMessageEnqueued(messageType);
    }

    /// <inheritdoc/>
    public void RecordMessageDequeued(string messageType)
    {
        this.etwSource?.RecordMessageDequeued(messageType);
        this.otelSource?.RecordMessageDequeued(messageType);
    }

    /// <inheritdoc/>
    public void RecordMessageAcknowledged(string messageType)
    {
        this.etwSource?.RecordMessageAcknowledged(messageType);
        this.otelSource?.RecordMessageAcknowledged(messageType);
    }

    /// <inheritdoc/>
    public void RecordMessageRequeued(string messageType, int retryCount)
    {
        this.etwSource?.RecordMessageRequeued(messageType, retryCount);
        this.otelSource?.RecordMessageRequeued(messageType, retryCount);
    }

    /// <inheritdoc/>
    public void RecordMessageMovedToDLQ(string messageType, string reason)
    {
        this.etwSource?.RecordMessageMovedToDLQ(messageType, reason);
        this.otelSource?.RecordMessageMovedToDLQ(messageType, reason);
    }

    /// <inheritdoc/>
    public void RecordHandlerDuration(string messageType, TimeSpan duration, bool success)
    {
        this.etwSource?.RecordHandlerDuration(messageType, duration, success);
        this.otelSource?.RecordHandlerDuration(messageType, duration, success);
    }

    /// <inheritdoc/>
    public void RecordQueueDepth(int readyCount, int inFlightCount, int dlqCount)
    {
        this.etwSource?.RecordQueueDepth(readyCount, inFlightCount, dlqCount);
        this.otelSource?.RecordQueueDepth(readyCount, inFlightCount, dlqCount);
    }

    /// <inheritdoc/>
    public void RecordLeaseExtension(string messageType)
    {
        this.etwSource?.RecordLeaseExtension(messageType);
        this.otelSource?.RecordLeaseExtension(messageType);
    }

    /// <inheritdoc/>
    public void RecordLeaseExpiration(string messageType)
    {
        this.etwSource?.RecordLeaseExpiration(messageType);
        this.otelSource?.RecordLeaseExpiration(messageType);
    }

    /// <inheritdoc/>
    public void RecordDeduplicationHit(string messageType)
    {
        this.etwSource?.RecordDeduplicationHit(messageType);
        this.otelSource?.RecordDeduplicationHit(messageType);
    }

    /// <inheritdoc/>
    public void RecordMessageDropped(string messageType, string reason)
    {
        this.etwSource?.RecordMessageDropped(messageType, reason);
        this.otelSource?.RecordMessageDropped(messageType, reason);
    }

    /// <inheritdoc/>
    public void RecordSnapshot(int messageCount, TimeSpan duration)
    {
        this.etwSource?.RecordSnapshot(messageCount, duration);
        this.otelSource?.RecordSnapshot(messageCount, duration);
    }

    /// <inheritdoc/>
    public void RecordJournalWrite(TimeSpan duration)
    {
        this.etwSource?.RecordJournalWrite(duration);
        this.otelSource?.RecordJournalWrite(duration);
    }

    #endregion

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        if (!this.disposed)
        {
            this.otelSource?.Dispose();
            this.disposed = true;
        }
    }
}
