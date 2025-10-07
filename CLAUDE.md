# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a **Message Queue** implementation in C#/.NET that provides an in-memory message queue built on a concurrent circular buffer with periodic disk persistence. The system is designed for high-throughput scenarios with multiple concurrent producers and consumers.

### Key Features
- **Lock-free concurrent circular buffer** with deduplication support
- **Persistence layer** using append-only journal + periodic snapshots for crash recovery
- **DI-based handler system** supporting dynamic scaling, timeouts, and retry logic
- **Dead-letter queue (DLQ)** for failed messages
- **Immediate dispatch** via channel-based signaling (no polling)
- **Long-running handler support** with lease extension and heartbeat mechanisms
- **Multi-framework targeting**: .NET Framework 4.6.2 and .NET 8.0

## Architecture

The system consists of six core components (see `docs/design.md` for full design):

1. **CircularBuffer** - Lock-free array of message slots with head/tail indices and optimistic concurrency control
2. **QueueManager** - Coordinates enqueue, checkout, acknowledge, requeue operations with deduplication
3. **HandlerDispatcher** - Dispatches messages to DI-resolved handlers using channel-based workers
4. **Persister** - Snapshots buffer state and DLQ to disk; replays journal on startup
5. **LeaseMonitor** - Background task that scans for expired leases and requeues messages
6. **DeadLetterQueue** - Secondary circular buffer for messages exceeding max retries

### Message Flow
```
Producer → QueueManager.Enqueue → CircularBuffer (dedup check) → Channel signal
                                         ↓
Consumer ← HandlerDispatcher ← Worker loops (awaiting channel)
                                         ↓
                                 Handler execution (DI scope)
                                         ↓
                              Success: Acknowledge | Failure: Retry or DLQ
```

## Implementation Phases

Implementation is divided into 7 phases (see `docs/plan.md`):

1. **Foundations & Contracts** - Interfaces, models, DI setup
2. **Concurrent Circular Buffer & Deduplication** - Core buffer with CAS operations and dedup index
3. **Persistence Layer** - Journal/snapshot serialization and recovery
4. **Handler Dispatcher & Workers** - DI integration, worker loops, channel signaling
5. **Retry, Lease Monitoring, DLQ** - Failure handling pipeline
6. **Handler Chaining & Admin APIs** - Message chaining, long-running support, monitoring
7. **Hardening & QA** - Soak tests, security review, performance benchmarking

## Project Structure

All code is organized under the `src/` directory with corresponding tests in `tests/`:

- **MessageQueue.Core** - Core interfaces, models, enums, and options (foundation library)
- **MessageQueue.CircularBuffer** - Lock-free circular buffer and deduplication index
- **MessageQueue.Persistence** - Journal, snapshot, and recovery mechanisms
- **MessageQueue.Dispatcher** - Handler registry, worker loops, and lease management
- **MessageQueue.DeadLetter** - Dead-letter queue and management APIs
- **MessageQueue.Admin** - Admin APIs and OpenTelemetry metrics
- **MessageQueue.Host** - Integrated queue host with all services

See `docs/project-structure.md` for detailed organization and parallel development guidelines.

## Build and Test Commands

### Building
```bash
# Build entire solution
dotnet build MessageQueue.sln

# Build specific project
dotnet build src/MessageQueue.Core/MessageQueue.Core.csproj

# Build in Release mode
dotnet build MessageQueue.sln -c Release
```

### Testing
```bash
# Run all tests
dotnet test MessageQueue.sln

# Run specific test project
dotnet test tests/MessageQueue.Core.Tests/MessageQueue.Core.Tests.csproj

# Run tests with coverage
dotnet test /p:CollectCoverage=true

# Run single test
dotnet test --filter "FullyQualifiedName~Namespace.Class.TestMethod"
```

### Running Samples
```bash
# Basic sample
dotnet run --project samples/MessageQueue.Samples.Basic/MessageQueue.Samples.Basic.csproj

# Advanced sample
dotnet run --project samples/MessageQueue.Samples.Advanced/MessageQueue.Samples.Advanced.csproj

# Performance benchmarks
dotnet run --project tests/MessageQueue.Performance.Tests/MessageQueue.Performance.Tests.csproj -c Release
```

### Code Analysis
```bash
# Run code analysis (NetAnalyzers enabled)
dotnet build MessageQueue.sln /p:TreatWarningsAsErrors=true
```

## Project Configuration

- **Central Package Management**: All package versions are managed in `Directory.Packages.props`
- **Shared Build Properties**: `Directory.Build.props` defines common settings (target frameworks, versioning, compiler options)
- **Test Projects**: Automatically reference MSTest, FluentAssertions, Moq, and coverlet via `IsTestProject` property

## Key Dependencies

- **DI**: Microsoft.Extensions.DependencyInjection, Unity
- **Serialization**: System.Text.Json (primary), Newtonsoft.Json
- **Concurrency**: System.Threading.Channels, Interlocked, SemaphoreSlim
- **Persistence**: FileStream with async I/O, optional memory-mapped files
- **Testing**: MSTest, FluentAssertions, Moq
- **Logging**: Serilog (console and file sinks)
- **Observability**: OpenTelemetry

## Concurrency Patterns

- **Producer synchronization**: CAS-based slot acquisition with striped locks for dedup index updates
- **Consumer synchronization**: Channel-based worker signaling (no busy-wait)
- **Lease management**: Atomic checkout with expiry timestamps; background monitor uses min-heap for efficient scanning
- **Deduplication**: Hash-based index mapping keys to slot indices; supersede semantics for in-flight messages

## Persistence Details

- **Journal format**: Append-only log with operation codes (Enqueue, Replace, Checkout, Ack, Fail, DeadLetter) + CRC checksums
- **Snapshot format**: Serialized buffer state (envelopes, dedup index, metadata) + DLQ records
- **Triggers**: Time-based (e.g., every 30s), threshold-based (e.g., every 1000 ops), or on graceful shutdown
- **Recovery**: Load latest snapshot → replay journal entries → restore DLQ

## Testing Strategy

Each phase includes:
- **Unit tests**: Component-level validation (state machines, serialization, dedup logic, lease renewal)
- **Integration tests**: Multi-threaded stress harnesses, crash-recovery scenarios, end-to-end handler flows
- **Scenario coverage matrix** (see implementation plan) ensures all design scenarios are validated

### Critical Test Scenarios
- Concurrent enqueue/checkout under contention
- Deduplication with message replacement during in-flight execution
- Crash recovery with partial journal replay
- Lease expiry causing message requeue and retry increment
- Handler timeout triggering cancellation and DLQ routing
- Handler chaining with correlation ID propagation
- DLQ persistence and admin replay operations

## Design Documentation

- **Design Document**: `docs/design.md` - Complete architectural specification
- **Implementation Plan**: `docs/plan.md` - Phase breakdown with timelines and test coverage matrix

## Message Envelope Structure

```csharp
MessageEnvelope
├── MessageId (GUID)
├── MessageType (Type token for DI resolution)
├── Key (deduplication key - hash-based)
├── Payload (serialized binary/JSON)
├── RetryCount / MaxRetries
├── Lease { HandlerId, CheckoutTimestamp, LeaseExpiry }
├── Status (Ready, InFlight, Completed, DeadLetter, Superseded)
├── LastPersistedVersion
└── Metadata (correlation IDs, headers)
```

## Handler Registration Pattern

```csharp
services.AddSingleton<IPersistentQueueHost, PersistentQueueHost>();
services.Configure<HandlerOptions<TMessage>>(opt =>
{
    opt.MaxParallelism = 2;
    opt.Timeout = TimeSpan.FromMinutes(5);
    opt.MaxRetries = 5;
});
services.AddScoped<IMessageHandler<TMessage>, ConcreteHandler>();
```

Handlers receive `CancellationToken` for timeout enforcement and can call `IQueuePublisher.Enqueue` to chain messages.

## Important Constraints

- **Deduplication**: Messages with the same key replace older messages; in-flight messages are marked `Superseded` rather than replaced
- **Lease semantics**: Handlers hold exclusive leases; expired leases trigger automatic requeue with retry increment
- **Retry limits**: Messages exceeding `MaxRetries` move to DLQ with failure metadata
- **Persistence guarantees**: WAL semantics ensure dedup index and envelope updates are atomic; CRC validation ensures file integrity

## Open Questions Tracked in Design

- Should deduped messages retain history or be entirely replaced? (Currently: replace-only)
- Transactional persistence across queue and DLQ? (May be required)
- Schema evolution for message payloads (version fields + converters)
