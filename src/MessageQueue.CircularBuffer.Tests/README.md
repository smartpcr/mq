# MessageQueue.CircularBuffer.Tests

## Purpose

Unit and integration tests for circular buffer operations, deduplication logic, and concurrent scenarios.

## Test Categories

- **Unit/** - Isolated component tests
  - Slot state machine transitions
  - CAS head/tail advancement
  - Dedup index collision handling
  - Version comparison logic
  - Lease metadata updates

- **Integration/** - Multi-threaded scenarios
  - Concurrent enqueue/checkout stress tests
  - Dedup replacement during in-flight processing
  - Buffer persistence stub integration
  - 100+ concurrent producer/consumer tests

## Key Test Scenarios

- Lock-free operations under contention
- Message replacement with supersede semantics
- Boundary conditions (full buffer, empty buffer)
- Version conflict detection

## Running Tests

```bash
# All tests
dotnet test src/MessageQueue.CircularBuffer.Tests/MessageQueue.CircularBuffer.Tests.csproj

# Unit tests only
dotnet test --filter "FullyQualifiedName~Unit"

# Integration tests only
dotnet test --filter "FullyQualifiedName~Integration"
```

## Phase

**Phase 2** - Core Components (Weeks 2-3, Days 8-20)
