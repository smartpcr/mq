# MessageQueue.Integration.Tests

## Purpose

End-to-end integration tests covering complete workflows, stress scenarios, and recovery testing across all components.

## Test Categories

- **Scenarios/** - Complete workflows
  - Enqueue → Dispatch → Acknowledge flow
  - Handler chaining with correlation IDs
  - Retry → DLQ failure path
  - Long-running handler with heartbeat

- **Stress/** - Load testing
  - 24-hour soak tests
  - High-throughput concurrent operations
  - Memory leak detection
  - Performance benchmarking

- **Recovery/** - Resilience testing
  - Startup restoration validation
  - DLQ persistence across restarts
  - Chaos testing (persistence failure, handler crash)
  - Network partition simulation

- **Integration/** - End-to-end flows
  - Full dispatch: enqueue → dispatch → acknowledge
  - Parallelism enforcement (MaxParallelism)
  - Timeout scenarios with cancellation
  - Worker scaling (add/remove workers)
  - DI scope lifecycle

## Key Test Scenarios

- Full system integration with all components
- Multi-step message workflows
- Failure injection and recovery
- Performance under sustained load
- Data consistency after crashes

## Running Tests

```bash
# All integration tests
dotnet test src/MessageQueue.Integration.Tests/MessageQueue.Integration.Tests.csproj

# Stress tests (long-running)
dotnet test --filter "FullyQualifiedName~Stress"

# Recovery scenarios
dotnet test --filter "FullyQualifiedName~Recovery"
```
