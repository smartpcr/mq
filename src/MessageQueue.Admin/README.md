# MessageQueue.Admin

## Purpose

Administration APIs and observability features for queue management, metrics collection, and OpenTelemetry integration.

## Contents

- **Api/** - Management operations
  - `QueueAdminApi` - Runtime configuration (scaling, snapshot triggers)
  - `HandlerScaling` - Dynamic worker adjustment
  - `SnapshotTrigger` - Manual persistence control

- **Metrics/** - Observability
  - `QueueMetrics` - Length, throughput, latency
  - `HandlerMetrics` - Success/failure counts, execution time
  - `TelemetryExporter` - OpenTelemetry integration

## Key Features

- **Runtime scaling** - Adjust handler parallelism without restart
- **Manual snapshots** - Force persistence on demand
- **Metrics dashboard** - Real-time queue statistics
- **OpenTelemetry** - Distributed tracing and metrics export
- **Health checks** - Queue status, persistence health

## Dependencies

- `MessageQueue.Core` - Interfaces and models
- `OpenTelemetry` - Metrics and tracing

## Usage

```csharp
var adminApi = services.GetRequiredService<IQueueAdminApi>();

// Scale handler instances
await adminApi.ScaleHandlerAsync<OrderMessage>(instanceCount: 8);

// Trigger manual snapshot
await adminApi.TriggerSnapshotAsync();

// Get queue metrics
var metrics = await adminApi.GetMetricsAsync();
Console.WriteLine($"Ready: {metrics.ReadyCount}, InFlight: {metrics.InFlightCount}");
```

## Phase

**Phase 6** - Advanced Features (Weeks 6-7, Days 36-49)
- Stream C: Admin & Monitoring (Developers C1, C2, C3, C4)
