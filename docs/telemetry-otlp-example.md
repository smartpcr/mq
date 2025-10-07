# OpenTelemetry OTLP Export Configuration

MessageQueue.Core includes built-in support for exporting telemetry data using OpenTelemetry Protocol (OTLP).

## Quick Start

### Default Configuration

By default, telemetry is exported to `http://localhost:4320` using OTLP over HTTP with Protobuf encoding:

```csharp
using MessageQueue.Core.Options;
using MessageQueue.Core.Trace;

var options = new QueueOptions
{
    TelemetryProvider = TelemetryProvider.OpenTelemetry,
    EnableOtlpExport = true,  // Default
    OtlpEndpoint = "http://localhost:4320"  // Default
};

using var telemetry = new MessageQueueTelemetry(
    options.TelemetryProvider,
    options.EnableOtlpExport,
    options.OtlpEndpoint);
```

### Custom Endpoint

To export to a different endpoint:

```csharp
var options = new QueueOptions
{
    TelemetryProvider = TelemetryProvider.OpenTelemetry,
    EnableOtlpExport = true,
    OtlpEndpoint = "http://otel-collector:4317"
};

using var telemetry = new MessageQueueTelemetry(
    options.TelemetryProvider,
    options.EnableOtlpExport,
    options.OtlpEndpoint);
```

### Disable Built-in Export

If you want to configure OpenTelemetry externally (e.g., in ASP.NET Core startup):

```csharp
var options = new QueueOptions
{
    TelemetryProvider = TelemetryProvider.OpenTelemetry,
    EnableOtlpExport = false  // Disable built-in export
};

using var telemetry = new MessageQueueTelemetry(
    options.TelemetryProvider,
    options.EnableOtlpExport,
    options.OtlpEndpoint);
```

Then configure OpenTelemetry in your application:

```csharp
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("MessageQueue.Core")
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://your-collector:4317");
        }))
    .WithMetrics(metrics => metrics
        .AddMeter("MessageQueue.Core")
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://your-collector:4317");
        }));
```

## OpenTelemetry Collector Setup

To receive telemetry data, run an OpenTelemetry Collector:

### Docker Compose Example

```yaml
version: '3.8'

services:
  otel-collector:
    image: otel/opentelemetry-collector:latest
    command: ["--config=/etc/otel-collector-config.yaml"]
    volumes:
      - ./otel-collector-config.yaml:/etc/otel-collector-config.yaml
    ports:
      - "4317:4317"   # OTLP gRPC
      - "4318:4318"   # OTLP HTTP
      - "4320:4320"   # Custom HTTP endpoint
```

### Collector Configuration (otel-collector-config.yaml)

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
      http:
        endpoint: 0.0.0.0:4318
      http/custom:
        endpoint: 0.0.0.0:4320

processors:
  batch:

exporters:
  logging:
    loglevel: debug

  # Export to Jaeger for traces
  jaeger:
    endpoint: jaeger:14250
    tls:
      insecure: true

  # Export to Prometheus for metrics
  prometheus:
    endpoint: "0.0.0.0:8889"

service:
  pipelines:
    traces:
      receivers: [otlp]
      processors: [batch]
      exporters: [logging, jaeger]

    metrics:
      receivers: [otlp]
      processors: [batch]
      exporters: [logging, prometheus]
```

## Configuration Options

### QueueOptions Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `TelemetryProvider` | `TelemetryProvider` | `OpenTelemetry` | Which telemetry provider to use (None, ETW, OpenTelemetry, All) |
| `EnableOtlpExport` | `bool` | `true` | Enable automatic OTLP export |
| `OtlpEndpoint` | `string` | `"http://localhost:4320"` | OTLP collector endpoint URL |

### Export Details

- **Protocol**: OTLP over HTTP with Protobuf encoding
- **Traces**: Exported immediately as activities complete
- **Metrics**: Exported every 10 seconds (configurable in code)

## Telemetry Data

### Traces (Activities)

All queue operations generate activities with tags:
- Message enqueued/dequeued
- Handler execution (start, complete, failed)
- Lease operations (extended, expired)
- Deduplication events
- Message drops

### Metrics

**Counters:**
- `messagequeue.messages.enqueued` - Total messages enqueued
- `messagequeue.messages.dequeued` - Total messages dequeued
- `messagequeue.messages.acknowledged` - Total messages acknowledged
- `messagequeue.messages.requeued` - Total messages requeued
- `messagequeue.messages.dlq` - Total messages moved to DLQ
- `messagequeue.messages.dropped` - Total messages dropped (buffer full)
- `messagequeue.leases.extensions` - Total lease extensions
- `messagequeue.leases.expirations` - Total lease expirations
- `messagequeue.deduplication.hits` - Total deduplication hits

**Histograms:**
- `messagequeue.handler.duration` - Handler execution time (ms)
- `messagequeue.snapshot.duration` - Snapshot operation time (ms)
- `messagequeue.journal.duration` - Journal write time (ms)

**Gauges:**
- `messagequeue.queue.depth.ready` - Messages in ready state
- `messagequeue.queue.depth.inflight` - Messages in-flight
- `messagequeue.queue.depth.dlq` - Messages in DLQ

## Troubleshooting

### Connection Refused

If you see connection errors, ensure the OTLP collector is running and accessible:

```bash
curl http://localhost:4320
```

### No Data in Collector

1. Check that `EnableOtlpExport = true`
2. Verify the `OtlpEndpoint` URL is correct
3. Check collector logs for errors
4. Ensure the collector is configured to receive OTLP over HTTP

### Performance Impact

Built-in OTLP export has minimal overhead:
- Traces: ~2-3% CPU overhead
- Metrics: ~1-2% CPU overhead (exported every 10s)
- Network: Small HTTP requests every 10 seconds for metrics

To reduce overhead:
- Increase metric export interval (requires code change)
- Use sampling for traces (requires code change)
- Disable export when not needed (`EnableOtlpExport = false`)
