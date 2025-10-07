# Project Structure

This document outlines the organization of the Message Queue solution, designed to enable parallel development across multiple teams.

## Solution Layout

```
MessageQueue/
├── src/                                    # All source code
│   ├── MessageQueue.Core/                  # Core contracts and models
│   ├── MessageQueue.CircularBuffer/        # Lock-free buffer implementation
│   ├── MessageQueue.Persistence/           # Journal and snapshot persistence
│   ├── MessageQueue.Dispatcher/            # Handler dispatcher and workers
│   ├── MessageQueue.DeadLetter/            # Dead-letter queue implementation
│   ├── MessageQueue.Admin/                 # Admin APIs and monitoring
│   ├── MessageQueue.Host/                  # Queue host integration
│   ├── MessageQueue.Core.Tests/            # Core unit tests
│   ├── MessageQueue.CircularBuffer.Tests/  # Buffer tests
│   ├── MessageQueue.Persistence.Tests/     # Persistence tests
│   ├── MessageQueue.Dispatcher.Tests/      # Dispatcher tests
│   ├── MessageQueue.Integration.Tests/     # Integration tests
│   ├── MessageQueue.Performance.Tests/     # Performance benchmarks
│   └── samples/                            # Sample applications
│       ├── MessageQueue.Samples.Basic/
│       └── MessageQueue.Samples.Advanced/
├── docs/                                   # Documentation
│   ├── design.md                           # Architecture design
│   ├── plan.md                             # Implementation plan
│   ├── project-structure.md                # This file
│   └── getting-started.md                  # Quick start guide
├── Directory.Build.props                   # Shared MSBuild properties
├── Directory.Packages.props                # Central package management
└── MessageQueue.sln                        # Solution file (15 projects)
```

## Source Projects

### MessageQueue.Core
**Purpose**: Foundation library with all contracts, models, and shared types
**Dependencies**: None (base library)
**Key Components**:
- `Interfaces/` - Core interfaces (IQueueManager, ICircularBuffer, IPersister, etc.)
- `Models/` - Message envelopes, metadata structures
- `Enums/` - Message states, operation codes
- `Options/` - Configuration options (QueueOptions, HandlerOptions)

**Owned by**: Phase 1 - All developers (contracts definition)

---

### MessageQueue.CircularBuffer
**Purpose**: Lock-free circular buffer with deduplication
**Dependencies**: MessageQueue.Core
**Key Components**:
- `Implementation/` - ConcurrentCircularBuffer, deduplication index
- `Metadata/` - Buffer metadata, version tracking

**Owned by**: Phase 2 - Stream A & B (Developers A1, A2, B1, B2)

---

### MessageQueue.Persistence
**Purpose**: Persistence layer with journal, snapshot, and recovery
**Dependencies**: MessageQueue.Core
**Key Components**:
- `Journal/` - Append-only journal writer/reader with CRC validation
- `Snapshot/` - Snapshot serializer/deserializer
- `Recovery/` - Startup recovery, journal replay

**Owned by**: Phase 2-3 - Stream C (Developers C1, C2)

---

### MessageQueue.Dispatcher
**Purpose**: Handler execution infrastructure with DI integration
**Dependencies**: MessageQueue.Core, MessageQueue.CircularBuffer
**Key Components**:
- `Registry/` - Handler registry with type-based lookup
- `Workers/` - Worker loops, channel-based signaling, parallelism control
- `Lease/` - Lease acquisition, renewal, timeout enforcement

**Owned by**: Phase 4 - Stream A & B (Developers A1, A2, B1, B2, B3)

---

### MessageQueue.DeadLetter
**Purpose**: Dead-letter queue for failed messages
**Dependencies**: MessageQueue.Core, MessageQueue.CircularBuffer
**Key Components**:
- `Implementation/` - DLQ circular buffer with failure metadata
- `Management/` - Admin APIs (inspect, replay, purge)

**Owned by**: Phase 5 - Stream C (Developers C1, C2)

---

### MessageQueue.Admin
**Purpose**: Administration and observability
**Dependencies**: MessageQueue.Core
**Key Components**:
- `Api/` - Admin operations (scaling, snapshot triggers, DLQ management)
- `Metrics/` - Queue metrics, OpenTelemetry integration

**Owned by**: Phase 6 - Stream C (Developers C1, C2, C3)

---

### MessageQueue.Host
**Purpose**: Integrated queue host with lifecycle management
**Dependencies**: All src projects
**Key Components**:
- `Services/` - PersistentQueueHost, service registrations
- `Configuration/` - Configuration binding, options validation

**Owned by**: Phase 2-6 integration (Developers D1, D2)

---

## Test Projects

### MessageQueue.Core.Tests
**Location**: `src/MessageQueue.Core.Tests/`
**Type**: Unit tests
**Coverage**: Contracts, models, serialization, DI registration
**README**: See `src/MessageQueue.Core.Tests/README.md`
**Owned by**: Phase 1 (Developer D)

### MessageQueue.CircularBuffer.Tests
**Location**: `src/MessageQueue.CircularBuffer.Tests/`
**Type**: Unit + Integration tests
**Structure**:
- `Unit/` - Slot state machine, CAS operations, dedup logic
- `Integration/` - Multi-threaded stress tests, concurrent scenarios

**README**: See `src/MessageQueue.CircularBuffer.Tests/README.md`
**Owned by**: Phase 2 (Developers A3, B3)

### MessageQueue.Persistence.Tests
**Location**: `src/MessageQueue.Persistence.Tests/`
**Type**: Unit + Integration tests
**Structure**:
- `Unit/` - Journal encoding, CRC validation, snapshot serialization
- `Integration/` - Crash recovery, file corruption handling

**README**: See `src/MessageQueue.Persistence.Tests/README.md`
**Owned by**: Phase 3 (Developers C3, B3)

### MessageQueue.Dispatcher.Tests
**Location**: `src/MessageQueue.Dispatcher.Tests/`
**Type**: Unit + Integration tests
**Structure**:
- `Unit/` - Registry lookup, channel signaling, timeout logic
- `Integration/` - End-to-end dispatch, parallelism enforcement

**README**: See `src/MessageQueue.Dispatcher.Tests/README.md`
**Owned by**: Phase 4 (Developer B4)

### MessageQueue.Integration.Tests
**Location**: `src/MessageQueue.Integration.Tests/`
**Type**: End-to-end integration tests
**Structure**:
- `Scenarios/` - Complete workflows (enqueue → dispatch → ack)
- `Stress/` - Soak tests, chaos testing
- `Recovery/` - Startup restoration, DLQ persistence

**README**: See `src/MessageQueue.Integration.Tests/README.md`
**Owned by**: Phase 5-7 (Developer teams)

### MessageQueue.Performance.Tests
**Location**: `src/MessageQueue.Performance.Tests/`
**Type**: Performance benchmarks (BenchmarkDotNet)
**Structure**:
- `Benchmarks/` - Throughput, latency, memory benchmarks

**README**: See `src/MessageQueue.Performance.Tests/README.md`
**Owned by**: Phase 7 (Developer Team C)

---

## Sample Projects

### MessageQueue.Samples.Basic
**Purpose**: Demonstrate basic usage (enqueue, handler, acknowledge)
**Target Audience**: New users learning the API

### MessageQueue.Samples.Advanced
**Purpose**: Showcase advanced features (chaining, DLQ, long-running, admin)
**Target Audience**: Production use cases

---

## Build Configuration

### Directory.Build.props
Defines shared properties for all projects:
- Target frameworks: `net462` and `net8.0`
- Assembly metadata (version, company, copyright)
- Compiler settings (warnings as errors, implicit usings)
- Test project dependencies (auto-includes MSTest, FluentAssertions, Moq)

### Directory.Packages.props
Central package version management:
- All package versions defined once
- Projects reference packages without versions
- Ensures consistency across solution

---

## Dependency Graph

```
MessageQueue.Host
    ├── MessageQueue.Core
    ├── MessageQueue.CircularBuffer → MessageQueue.Core
    ├── MessageQueue.Persistence → MessageQueue.Core
    ├── MessageQueue.Dispatcher → MessageQueue.Core, MessageQueue.CircularBuffer
    ├── MessageQueue.DeadLetter → MessageQueue.Core, MessageQueue.CircularBuffer
    └── MessageQueue.Admin → MessageQueue.Core

Samples → MessageQueue.Host (and transitive dependencies)
Tests → Corresponding src projects
```

---

## Parallel Development Guidelines

### Phase 1 (Week 1): Foundation
**All developers collaborate** on interface definitions and shared models to establish contracts.

### Phase 2-6: Parallel Streams
Teams work independently on separate projects:
- **Core Stream** (A): CircularBuffer → Dispatcher workers
- **Persistence Stream** (C): Journal/Snapshot → Recovery
- **Infrastructure Stream** (C): Admin → Monitoring

### Integration Points
Projects with dependencies coordinate at these milestones:
- **Day 15**: CircularBuffer + Persistence foundation ready
- **Day 28**: Dispatcher can integrate with buffer
- **Day 35**: DLQ integrates with buffer and dispatcher
- **Day 42**: Admin APIs integrate with all components

### Merge Strategy
Each project maintains its own feature branch:
- `feature/circular-buffer`
- `feature/persistence`
- `feature/dispatcher`
- `feature/dead-letter`
- `feature/admin`

Integration branch `develop` merges completed projects after integration tests pass.

---

## Build Commands

```bash
# Build entire solution
dotnet build MessageQueue.sln

# Build specific project
dotnet build src/MessageQueue.Core/MessageQueue.Core.csproj

# Run all tests
dotnet test MessageQueue.sln

# Run specific test project
dotnet test tests/MessageQueue.Core.Tests/MessageQueue.Core.Tests.csproj

# Run performance benchmarks
dotnet run --project tests/MessageQueue.Performance.Tests/MessageQueue.Performance.Tests.csproj -c Release

# Run samples
dotnet run --project samples/MessageQueue.Samples.Basic/MessageQueue.Samples.Basic.csproj
```

---

## File Organization Within Projects

### Standard Layout
```
ProjectName/
├── Interfaces/         # Public contracts (if applicable)
├── Implementation/     # Internal implementations
├── Models/            # Data structures
├── Services/          # Business logic
├── Extensions/        # Extension methods
└── ProjectName.csproj
```

### Test Project Layout
```
ProjectName.Tests/
├── Unit/              # Isolated unit tests
├── Integration/       # Integration test scenarios
├── Fixtures/          # Test data and helpers
└── ProjectName.Tests.csproj
```

---

## Naming Conventions

- **Namespaces**: Match folder structure (e.g., `MessageQueue.CircularBuffer.Implementation`)
- **Interfaces**: Prefix with `I` (e.g., `IQueueManager`)
- **Test classes**: Suffix with `Tests` (e.g., `CircularBufferTests`)
- **Test methods**: Descriptive names (e.g., `Enqueue_WithDuplicateKey_ReplacesExistingMessage`)

---

## Next Steps

1. Phase 1 developers define all interfaces in `MessageQueue.Core/Interfaces/`
2. Create shared models in `MessageQueue.Core/Models/`
3. Implement DI registration helpers in `MessageQueue.Core/Extensions/`
4. Phase 2+ teams begin parallel implementation following the implementation plan

See [Implementation Plan](plan.md) for detailed task breakdown and timeline.
