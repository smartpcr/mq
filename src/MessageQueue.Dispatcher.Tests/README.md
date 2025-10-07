# MessageQueue.Dispatcher.Tests

## Purpose

Unit and integration tests for handler registry, worker infrastructure, and lease management.

## Test Categories

- **Unit/** - Component tests
  - Handler registry lookup logic
  - Configuration override behavior
  - Channel signaling correctness
  - Lease renewal decision logic
  - Timeout cancellation pathways

- **Integration/** - End-to-end flows
  - Full dispatch: enqueue → dispatch → acknowledge
  - Parallelism enforcement (MaxParallelism)
  - Timeout scenarios with cancellation
  - Worker scaling (add/remove workers)
  - DI scope lifecycle

## Key Test Scenarios

- Handler resolution from DI container
- Channel awakening on ready messages
- Concurrent handler execution limits
- Timeout-triggered message requeue
- Lease extension for long-running tasks

## Running Tests

```bash
# All tests
dotnet test src/MessageQueue.Dispatcher.Tests/MessageQueue.Dispatcher.Tests.csproj

# Integration flows
dotnet test --filter "FullyQualifiedName~Integration"
```

## Phase

**Phase 4** - Handler Dispatcher (Weeks 4-5, Days 22-35)
