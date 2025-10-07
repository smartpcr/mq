# MessageQueue.Core.Tests

## Purpose

Unit tests for core contracts, models, serialization, dependency injection configuration, handler registry, worker infrastructure, and lease management.

## Test Categories

- **Models/** - Data structure tests
  - Serialization round-trips (JSON, binary)
  - Validation logic
  - Polymorphic type handling

- **Serialization/** - Format tests
  - MessageEnvelope serialization
  - DeadLetterEnvelope serialization
  - Type token mapping

- **DI/** - Registration tests
  - Service lifetime verification
  - Configuration binding
  - Options validation

- **Unit/** - Component tests
  - Handler registry lookup logic
  - Configuration override behavior
  - Channel signaling correctness
  - Lease renewal decision logic
  - Timeout cancellation pathways

## Key Test Scenarios
- Handler resolution from DI container
- Channel awakening on ready messages
- Concurrent handler execution limits
- Timeout-triggered message requeue
- Lease extension for long-running tasks

## Test Framework

- MSTest
- FluentAssertions
- Moq (for interface mocking)

## Running Tests

```bash
dotnet test src/MessageQueue.Core.Tests/MessageQueue.Core.Tests.csproj
```

# MessageQueue.Dispatcher.Tests






## Running Tests

```bash
# All tests
dotnet test src/MessageQueue.Dispatcher.Tests/MessageQueue.Dispatcher.Tests.csproj

# Integration flows
dotnet test --filter "FullyQualifiedName~Integration"
```


