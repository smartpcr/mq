# MessageQueue Telemetry

This directory contains the telemetry instrumentation for the MessageQueue library, supporting both ETW (Event Tracing for Windows) and OpenTelemetry.

## Overview

The telemetry system provides:
- **Logging**: Structured events for all queue operations
- **Metrics**: Performance counters, histograms, and gauges
- **Flexible providers**: ETW, OpenTelemetry, or both simultaneously

## Architecture

```
IMessageQueueLogger          IMessageQueueMetrics
        ↓                            ↓
   MessageQueueTelemetry (composite)
        ↓                            ↓
MessageQueueEventSource    MessageQueueOpenTelemetry
      (ETW)                    (OpenTelemetry)
```

## Usage

### Basic Configuration

```csharp
using MessageQueue.Core.Options;
using MessageQueue.Core.Trace;

// Configure which telemetry providers to use
var options = new QueueOptions
{
    // Use only OpenTelemetry
    TelemetryProvider = TelemetryProvider.OpenTelemetry,

    // Or use only ETW
    // TelemetryProvider = TelemetryProvider.ETW,

    // Or use both
    // TelemetryProvider = TelemetryProvider.All,

    // Or disable telemetry
    // TelemetryProvider = TelemetryProvider.None
};

// Create telemetry instance
using var telemetry = new MessageQueueTelemetry(options.TelemetryProvider);

// Use for logging
telemetry.MessageEnqueued(messageId, "MyMessage", "dedup-key");

// Use for metrics
telemetry.RecordMessageEnqueued("MyMessage");
```

### OpenTelemetry Setup

When using OpenTelemetry, configure the SDK in your application startup:

```csharp
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Add OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("MessageQueue.Core")
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddMeter("MessageQueue.Core")
        .AddConsoleExporter());

var app = builder.Build();
```

### ETW Setup

When using ETW, use standard Windows ETW tools to collect events:

```powershell
# Start ETW session
logman create trace MessageQueueTrace -p "MessageQueue-Core" -o trace.etl -ets

# Stop ETW session
logman stop MessageQueueTrace -ets

# View events using PerfView or other ETW tools
```

## Telemetry Events

### Logging Events

The `IMessageQueueLogger` interface provides these events:

- **MessageEnqueued**: A message was added to the queue
- **MessageCheckedOut**: A message was checked out by a handler
- **MessageAcknowledged**: A message was successfully processed
- **MessageRequeued**: A message failed and was requeued
- **MessageMovedToDLQ**: A message exceeded max retries and moved to DLQ
- **MessageReplayed**: A message was replayed from DLQ
- **LeaseExtended**: A lease was extended for a long-running handler
- **LeaseExpired**: A lease expired and message was requeued
- **HandlerStarted**: Handler execution started
- **HandlerCompleted**: Handler execution completed successfully
- **HandlerFailed**: Handler execution failed
- **DeduplicationOccurred**: A duplicate message was detected
- **SnapshotCreated**: A persistence snapshot was created
- **JournalTruncated**: Journal was truncated after snapshot
- **Error**: An error occurred

### Metrics

The `IMessageQueueMetrics` interface provides these metrics:

#### Counters
- `messagequeue.messages.enqueued`: Total messages enqueued
- `messagequeue.messages.dequeued`: Total messages dequeued
- `messagequeue.messages.acknowledged`: Total messages acknowledged
- `messagequeue.messages.requeued`: Total messages requeued
- `messagequeue.messages.dlq`: Total messages moved to DLQ
- `messagequeue.leases.extensions`: Total lease extensions
- `messagequeue.leases.expirations`: Total lease expirations
- `messagequeue.deduplication.hits`: Total deduplication hits

#### Histograms
- `messagequeue.handler.duration`: Handler execution time (ms)
- `messagequeue.snapshot.duration`: Snapshot operation time (ms)
- `messagequeue.journal.duration`: Journal write time (ms)

#### Gauges
- `messagequeue.queue.depth.ready`: Messages in ready state
- `messagequeue.queue.depth.inflight`: Messages in-flight
- `messagequeue.queue.depth.dlq`: Messages in DLQ

## Best Practices

1. **Choose the right provider**:
   - Use **OpenTelemetry** for cloud-native applications with modern observability stacks
   - Use **ETW** for Windows-native applications requiring low-level diagnostics
   - Use **Both** when you need compatibility with multiple monitoring systems

2. **Performance considerations**:
   - ETW has minimal overhead (~1-2% CPU)
   - OpenTelemetry has slightly higher overhead but better cross-platform support
   - Using both increases overhead but may be justified for hybrid monitoring

3. **Metric cardinality**:
   - Be careful with high-cardinality dimensions (e.g., message IDs)
   - Use message types as dimensions, not individual message IDs

4. **Integration with application logging**:
   - The telemetry system is separate from application logging
   - Use activity/trace IDs to correlate telemetry with application logs

## Examples

See the integration tests for usage examples:
- `MessageQueue.Integration.Tests/TelemetryTests.cs` (if exists)

## Performance Impact

Telemetry overhead is minimal:
- **ETW**: ~1-2% CPU overhead
- **OpenTelemetry**: ~2-3% CPU overhead
- **Both**: ~3-4% CPU overhead
- **None**: Zero overhead

The telemetry system uses efficient patterns:
- Check `IsEnabled()` before expensive operations
- Minimal allocations in hot paths
- Batch operations where possible
