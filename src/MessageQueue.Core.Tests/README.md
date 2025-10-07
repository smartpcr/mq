# MessageQueue.Core.Tests

## Purpose

Unit tests for core contracts, models, serialization, and dependency injection configuration.

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

## Test Framework

- MSTest
- FluentAssertions
- Moq (for interface mocking)

## Running Tests

```bash
dotnet test src/MessageQueue.Core.Tests/MessageQueue.Core.Tests.csproj
```

## Phase

**Phase 1** - Foundations & Contracts (Week 1, Days 4-7)
