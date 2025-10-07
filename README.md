# Message Queue

A high-performance, persistent message queue for .NET built on a lock-free circular buffer with disk persistence, designed for concurrent producer-consumer scenarios with guaranteed message delivery and crash recovery.

## Features

- **🚀 High Performance**: Lock-free circular buffer with optimistic concurrency control
- **💾 Persistent Storage**: Append-only journal + periodic snapshots for crash recovery
- **🔄 Message Deduplication**: Key-based deduplication with replace semantics
- **⚡ Immediate Dispatch**: Channel-based signaling eliminates polling overhead
- **🎯 DI Integration**: Handler resolution via Microsoft.Extensions.DependencyInjection or Unity
- **🔁 Retry Logic**: Configurable retry limits with automatic dead-letter queue routing
- **⏱️ Timeout Support**: Per-handler timeouts with cancellation token propagation
- **📈 Dynamic Scaling**: Configure handler parallelism and scale at runtime
- **🔗 Message Chaining**: Handlers can enqueue additional messages for workflow orchestration
- **⏳ Long-Running Tasks**: Lease extension and heartbeat support for extended operations
- **📊 Observability**: Built-in metrics and OpenTelemetry integration
- **🎯 Multi-Framework**: Supports .NET Framework 4.6.2 and .NET 8.0

## Architecture Overview

```
┌─────────────────────────┐
│  Application Services   │
│  (Handlers, Producers)  │
└───────────┬─────────────┘
            │
            ▼
┌───────────────────────────┐         ┌──────────────────┐
│  PersistentQueueHost      │◄───────►│ Persistence      │
│  ├─ QueueManager          │         │ Store (File)     │
│  ├─ HandlerDispatcher     │         └──────────────────┘
│  ├─ Persister             │
│  └─ LeaseMonitor          │         ┌──────────────────┐
└───────────┬───────────────┘         │ Dead Letter Queue│
            │                         └──────────────────┘
            ▼
    ┌───────────────┐
    │ CircularBuffer│
    │  ├─ Metadata  │
    │  └─ Slots     │
    └───────────────┘
```

### Core Components

- **CircularBuffer**: Lock-free array of message slots with CAS-based head/tail advancement
- **QueueManager**: Coordinates enqueue, checkout, acknowledge, and requeue operations
- **HandlerDispatcher**: Dispatches messages to DI-resolved handlers using worker pools
- **Persister**: Snapshots queue state to disk with WAL semantics
- **LeaseMonitor**: Background service that requeues messages with expired leases
- **DeadLetterQueue**: Stores failed messages exceeding retry limits

## Quick Start

### Installation

```bash
# Clone the repository
git clone <repository-url>
cd mq

# Build the solution
dotnet build

# Run tests
dotnet test
```

### Basic Usage

#### 1. Define Your Message

```csharp
public class OrderProcessingMessage
{
    public string OrderId { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

#### 2. Implement a Handler

```csharp
public class OrderProcessingHandler : IMessageHandler<OrderProcessingMessage>
{
    private readonly ILogger<OrderProcessingHandler> _logger;

    public OrderProcessingHandler(ILogger<OrderProcessingHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(OrderProcessingMessage message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing order {OrderId}", message.OrderId);

        // Your business logic here
        await ProcessOrderAsync(message, cancellationToken);

        _logger.LogInformation("Order {OrderId} processed successfully", message.OrderId);
    }
}
```

#### 3. Register Services

```csharp
var services = new ServiceCollection();

// Register the queue host
services.AddSingleton<IPersistentQueueHost, PersistentQueueHost>();

// Configure handler options
services.Configure<HandlerOptions<OrderProcessingMessage>>(opt =>
{
    opt.MaxParallelism = 4;              // Run up to 4 handlers concurrently
    opt.Timeout = TimeSpan.FromMinutes(5); // 5-minute timeout per message
    opt.MaxRetries = 3;                   // Retry failed messages up to 3 times
});

// Register your handler
services.AddScoped<IMessageHandler<OrderProcessingMessage>, OrderProcessingHandler>();

var serviceProvider = services.BuildServiceProvider();
```

#### 4. Start the Queue and Enqueue Messages

```csharp
// Start the queue host
var queueHost = serviceProvider.GetRequiredService<IPersistentQueueHost>();
await queueHost.StartAsync(cancellationToken);

// Enqueue messages
var queueManager = serviceProvider.GetRequiredService<IQueueManager>();

await queueManager.EnqueueAsync(new OrderProcessingMessage
{
    OrderId = "ORD-001",
    Amount = 99.99m,
    CreatedAt = DateTime.UtcNow
},
deduplicationKey: "ORD-001"); // Messages with same key will be deduplicated
```

## Configuration

### Queue Configuration

```csharp
services.Configure<QueueOptions>(opt =>
{
    opt.Capacity = 10000;                          // Buffer capacity
    opt.PersistencePath = "./queue-data";          // Persistence directory
    opt.SnapshotIntervalSeconds = 30;              // Snapshot every 30 seconds
    opt.SnapshotThreshold = 1000;                  // Or after 1000 operations
    opt.DefaultTimeout = TimeSpan.FromMinutes(2);  // Default handler timeout
    opt.DefaultMaxRetries = 5;                     // Default retry limit
});
```

### Handler Configuration

```csharp
services.Configure<HandlerOptions<TMessage>>(opt =>
{
    opt.MaxParallelism = 2;                    // Concurrent handler instances
    opt.MinParallelism = 1;                    // Initial workers
    opt.Timeout = TimeSpan.FromMinutes(10);    // Handler-specific timeout
    opt.MaxRetries = 5;                        // Handler-specific retry limit
    opt.LeaseExtensionEnabled = true;          // Allow lease extension
});
```

## Advanced Features

### Message Chaining

Handlers can enqueue additional messages to create workflows:

```csharp
public class OrderHandler : IMessageHandler<OrderMessage>
{
    private readonly IQueuePublisher _publisher;

    public async Task HandleAsync(OrderMessage message, CancellationToken ct)
    {
        // Process order
        var result = await ProcessOrderAsync(message);

        // Chain to next step
        await _publisher.EnqueueAsync(new PaymentMessage
        {
            OrderId = message.OrderId,
            Amount = result.TotalAmount
        },
        deduplicationKey: $"PAY-{message.OrderId}",
        correlationId: message.CorrelationId);
    }
}
```

### Long-Running Handlers with Lease Extension

```csharp
public class LongRunningHandler : IMessageHandler<LongRunningMessage>
{
    private readonly ILeaseExtensionService _leaseService;

    public async Task HandleAsync(LongRunningMessage message, CancellationToken ct)
    {
        while (!await IsJobCompleteAsync(message.JobId))
        {
            // Extend lease to prevent timeout
            await _leaseService.ExtendLeaseAsync(message.MessageId);

            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }
}
```

### Dead Letter Queue Management

```csharp
// Access DLQ
var dlqManager = serviceProvider.GetRequiredService<IDeadLetterQueueManager>();

// Inspect failed messages
var failedMessages = await dlqManager.GetMessagesAsync(limit: 100);

foreach (var envelope in failedMessages)
{
    Console.WriteLine($"Message {envelope.MessageId} failed: {envelope.FailureReason}");
}

// Replay messages from DLQ
await dlqManager.ReplayAsync(envelope.MessageId);

// Purge DLQ
await dlqManager.PurgeAsync();
```

### Runtime Scaling

```csharp
// Get admin API
var adminApi = serviceProvider.GetRequiredService<IQueueAdminApi>();

// Scale handler instances
await adminApi.ScaleHandlerAsync<OrderMessage>(instanceCount: 8);

// Trigger manual snapshot
await adminApi.TriggerSnapshotAsync();

// Get queue metrics
var metrics = await adminApi.GetMetricsAsync();
Console.WriteLine($"Ready: {metrics.ReadyCount}, InFlight: {metrics.InFlightCount}");
```

## Persistence and Recovery

### Persistence Strategy

The queue uses a two-tier persistence model:

1. **Append-Only Journal**: Records all operations (enqueue, checkout, acknowledge, fail) with CRC checksums
2. **Periodic Snapshots**: Compacts queue state at configured intervals

### Recovery Process

On startup:
1. Load the latest snapshot (if available)
2. Replay journal entries newer than the snapshot
3. Restore dead-letter queue
4. Requeue messages with expired leases
5. Start handler workers

### Data Integrity

- CRC checksums on all persisted records
- WAL (Write-Ahead Logging) semantics for atomic updates
- Transactional deduplication index updates

## Performance Characteristics

- **Throughput**: Lock-free operations minimize contention
- **Latency**: Channel-based dispatch eliminates polling delay
- **Scalability**: Configurable handler parallelism per message type
- **Memory**: Fixed-size circular buffer prevents unbounded growth
- **Disk I/O**: Batched snapshots reduce write amplification

## Project Structure

```
src/
├── MessageQueue.Core                      # Core interfaces, models, and implementations
│   ├── CircularBuffer                     # Lock-free buffer implementation
│   ├── QueueManager                       # Queue orchestration
│   ├── HandlerDispatcher                  # Handler execution infrastructure
│   ├── DeadLetterQueue                    # Dead-letter queue
│   ├── HeartbeatService                   # Lease monitoring
│   └── LeaseMonitor                       # Lease expiry detection
├── MessageQueue.Persistence               # Journal and snapshot persistence
│   ├── FilePersister                      # File-based persistence
│   └── RecoveryService                    # Crash recovery logic
├── MessageQueue.Core.Tests                # Unit tests for core components
├── MessageQueue.CircularBuffer.Tests      # Buffer concurrency tests
├── MessageQueue.Persistence.Tests         # Persistence and recovery tests
├── MessageQueue.Dispatcher.Tests          # Handler dispatcher tests
├── MessageQueue.Integration.Tests         # End-to-end integration tests
├── MessageQueue.Performance.Tests         # BenchmarkDotNet performance tests
├── MessageQueue.ChaosTests                # Chaos engineering tests
│   ├── PersistenceFailureTests           # Persistence failure scenarios
│   └── HandlerCrashTests                 # Handler crash scenarios
└── MessageQueue.SoakTests                 # 24-hour stability tests
```

See [Project Structure](docs/project-structure.md) for detailed organization and development guidelines.

## Development

### Build

```bash
# Build entire solution
dotnet build MessageQueue.sln

# Build specific project
dotnet build src/MessageQueue.Core/MessageQueue.Core.csproj

# Build for specific framework
dotnet build -f net8.0

# Release build
dotnet build MessageQueue.sln -c Release
```

### Testing

```bash
# Run all tests
dotnet test MessageQueue.sln

# Run specific test project
dotnet test tests/MessageQueue.Core.Tests/MessageQueue.Core.Tests.csproj

# Run with coverage
dotnet test /p:CollectCoverage=true

# Run specific test
dotnet test --filter "FullyQualifiedName~Namespace.TestClass.TestMethod"
```

### Performance Benchmarks

```bash
# Run all BenchmarkDotNet benchmarks
dotnet run --project src/MessageQueue.Performance.Tests/MessageQueue.Performance.Tests.csproj -c Release -- --all

# Run specific benchmark category
dotnet run --project src/MessageQueue.Performance.Tests/MessageQueue.Performance.Tests.csproj -c Release -- enqueue
dotnet run --project src/MessageQueue.Performance.Tests/MessageQueue.Performance.Tests.csproj -c Release -- checkout
dotnet run --project src/MessageQueue.Performance.Tests/MessageQueue.Performance.Tests.csproj -c Release -- persistence
dotnet run --project src/MessageQueue.Performance.Tests/MessageQueue.Performance.Tests.csproj -c Release -- end-to-end
```

### Chaos Tests

```bash
# Run chaos tests for failure scenarios
dotnet test src/MessageQueue.ChaosTests/MessageQueue.ChaosTests.csproj
```

### Soak Tests

```bash
# Run 24-hour soak test (default)
dotnet run --project src/MessageQueue.SoakTests/MessageQueue.SoakTests.csproj

# Run soak test with custom duration (in hours)
dotnet run --project src/MessageQueue.SoakTests/MessageQueue.SoakTests.csproj -- 1
```

### Code Analysis

```bash
# Run code analysis
dotnet build MessageQueue.sln /p:TreatWarningsAsErrors=true
```

## Project Status

This project has **completed all implementation phases**. Implementation followed a phased approach:

- [x] **Phase 1: Foundations & Contracts** - 8/8 tests passing (100%)
- [x] **Phase 2: Concurrent Circular Buffer & Deduplication** - 43/43 tests passing (100%)
- [x] **Phase 3: Persistence Layer (Journal + Snapshot)** - 63/63 tests passing (100%)
- [x] **Phase 4: Handler Dispatcher & Worker Infrastructure** - 31/31 tests passing (100%)
- [x] **Phase 5: Retry, Lease Monitoring, and Dead-Letter Routing** - 30/30 tests passing (100%)
- [x] **Phase 6: Handler Chaining, Long-Running Support, and Admin APIs** - 21/21 tests passing (100%)
- [x] **Phase 7: Hardening, Observability & Final QA** - Complete

**Overall Progress**: 7/7 phases complete (100%) | **Tests**: 196/196 passing (100%)

Phase 7 deliverables:
- ✅ Performance benchmarks (BenchmarkDotNet suite)
- ✅ Soak tests (24-hour stability tests)
- ✅ Chaos tests (failure scenario testing)
- ✅ Operations documentation
- ✅ API documentation
- ✅ Security review

See [Implementation Plan](docs/plan.md) for detailed timeline and test coverage.

## Documentation

- [Design Document](docs/design.md) - Complete architectural specification
- [Implementation Plan](docs/plan.md) - Phase breakdown and testing strategy
- [Operations Guide](docs/OPERATIONS.md) - Deployment, monitoring, and troubleshooting
- [API Reference](docs/API.md) - Complete API documentation with examples
- [Security Guide](docs/SECURITY.md) - Security best practices and threat model
- [CLAUDE.md](CLAUDE.md) - Developer guide for Claude Code

## Technology Stack

- **Language**: C# with latest language features
- **Frameworks**: .NET Framework 4.6.2, .NET 8.0
- **DI**: Microsoft.Extensions.DependencyInjection, Unity
- **Serialization**: System.Text.Json (primary), Newtonsoft.Json
- **Logging**: Serilog with console and file sinks
- **Testing**: MSTest, FluentAssertions, Moq
- **Observability**: OpenTelemetry
- **Benchmarking**: BenchmarkDotNet

## License

Copyright © AzureStack Compute

## Contributing

This is an internal project. Please refer to the implementation plan for current development priorities.

## Release Process

See [docs/release-guide.md](docs/release-guide.md) for step-by-step instructions on cutting a release with the manual GitHub Actions workflow, including version preparation, inputs, and publishing behaviour.
